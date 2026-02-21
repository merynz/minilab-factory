param(
    [string]$UnityPath = "",
    [int]$TimeoutMinutes = 20
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-UnityPath([string]$ExplicitPath) {
    if ($env:MINILAB_UNITY_PATH -and (Test-Path $env:MINILAB_UNITY_PATH)) {
        return (Resolve-Path $env:MINILAB_UNITY_PATH).Path
    }

    if ($ExplicitPath -and (Test-Path $ExplicitPath)) {
        return (Resolve-Path $ExplicitPath).Path
    }

    $roots = @(
        "C:\\Program Files\\Unity\\Hub\\Editor",
        "C:\\Program Files\\Unity"
    )

    foreach ($root in $roots) {
        if (!(Test-Path $root)) {
            continue
        }

        $candidate = Get-ChildItem -Path $root -Recurse -Filter "Unity.exe" -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($candidate) {
            return $candidate.FullName
        }
    }

    return ""
}

function Invoke-UnityWithTimeout([string]$ExePath, [string[]]$Arguments, [string]$LogPath, [int]$TimeoutMins) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $LogPath) -Force | Out-Null

    $argsPreview = ($Arguments | ForEach-Object {
            if ($_ -match '\s') { '"' + $_ + '"' } else { $_ }
        }) -join ' '
    Write-Host "Unity command: `"$ExePath`" $argsPreview"

    $process = Start-Process -FilePath $ExePath -ArgumentList $Arguments -PassThru -NoNewWindow

    Start-Sleep -Seconds 8
    $process.Refresh()
    if (-not $process.HasExited -and $process.MainWindowHandle -ne 0) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        if (Test-Path $LogPath) {
            Write-Host "----- LOG TAIL (last 200 lines) -----"
            Get-Content -Path $LogPath -Tail 200 | ForEach-Object { Write-Host $_ }
        }
        throw "FAIL: interactive launch happened (Unity GUI window detected)."
    }

    $completed = $process.WaitForExit([int]([Math]::Max(1, $TimeoutMins) * 60 * 1000))
    if (-not $completed) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        if (Test-Path $LogPath) {
            Write-Host "----- LOG TAIL (last 200 lines) -----"
            Get-Content -Path $LogPath -Tail 200 | ForEach-Object { Write-Host $_ }
        }
        throw "Unity timed out after $TimeoutMins minutes. Log: $LogPath"
    }

    if ($process.ExitCode -ne 0) {
        if (Test-Path $LogPath) {
            Write-Host "----- LOG TAIL (last 200 lines) -----"
            Get-Content -Path $LogPath -Tail 200 | ForEach-Object { Write-Host $_ }
        }
        throw "Unity command failed (exit $($process.ExitCode)). Log: $LogPath"
    }
}

function Write-TemplateSetupScript([string]$SeedProjectPath) {
    $editorDir = Join-Path $SeedProjectPath "Assets/Editor"
    New-Item -ItemType Directory -Path $editorDir -Force | Out-Null

    $scriptPath = Join-Path $editorDir "TemplateSetup.cs"
    @"
using MiniLab.Core.Boot;
using MiniLab.Core.Config;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MiniLab.TemplateBootstrap
{
    public static class TemplateSetup
    {
        public static void Run()
        {
            EnsureFolders();
            var rendererData = EnsureRendererData();
            var pipelineAsset = EnsurePipelineAsset(rendererData);
            ApplyPipelineSettings(pipelineAsset);
            var coreSettings = EnsureCoreSettings();
            EnsureBootstrapScene(coreSettings);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Template setup completed.");
        }

        private static void EnsureFolders()
        {
            System.IO.Directory.CreateDirectory("Assets/Scenes");
            System.IO.Directory.CreateDirectory("Assets/Settings/Core");
            System.IO.Directory.CreateDirectory("Assets/Settings/Rendering");
        }

        private static Renderer2DData EnsureRendererData()
        {
            const string rendererPath = "Assets/Settings/Rendering/Renderer2D.asset";
            var rendererData = AssetDatabase.LoadAssetAtPath<Renderer2DData>(rendererPath);
            if (rendererData != null)
            {
                return rendererData;
            }

            rendererData = ScriptableObject.CreateInstance<Renderer2DData>();
            AssetDatabase.CreateAsset(rendererData, rendererPath);
            return rendererData;
        }

        private static UniversalRenderPipelineAsset EnsurePipelineAsset(Renderer2DData rendererData)
        {
            const string pipelinePath = "Assets/Settings/Rendering/URP2D.asset";
            var pipelineAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (pipelineAsset == null)
            {
                pipelineAsset = ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();
                AssetDatabase.CreateAsset(pipelineAsset, pipelinePath);
            }

            var so = new SerializedObject(pipelineAsset);
            var rendererDataList = so.FindProperty("m_RendererDataList");
            if (rendererDataList != null)
            {
                if (rendererDataList.arraySize == 0)
                {
                    rendererDataList.arraySize = 1;
                }

                rendererDataList.GetArrayElementAtIndex(0).objectReferenceValue = rendererData;
            }

            var defaultRendererIndex = so.FindProperty("m_DefaultRendererIndex");
            if (defaultRendererIndex != null)
            {
                defaultRendererIndex.intValue = 0;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            return pipelineAsset;
        }

        private static void ApplyPipelineSettings(UniversalRenderPipelineAsset pipelineAsset)
        {
            GraphicsSettings.defaultRenderPipeline = pipelineAsset;
            QualitySettings.renderPipeline = pipelineAsset;
        }

        private static CoreSettings EnsureCoreSettings()
        {
            const string settingsPath = "Assets/Settings/Core/CoreSettings.asset";
            var settings = AssetDatabase.LoadAssetAtPath<CoreSettings>(settingsPath);
            if (settings != null)
            {
                return settings;
            }

            settings = ScriptableObject.CreateInstance<CoreSettings>();
            AssetDatabase.CreateAsset(settings, settingsPath);
            return settings;
        }

        private static void EnsureBootstrapScene(CoreSettings settings)
        {
            const string scenePath = "Assets/Scenes/Bootstrap.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var bootstrap = new GameObject("MiniLabBootstrap");
            var bootstrapComponent = bootstrap.AddComponent<MiniLabBootstrap>();
            var so = new SerializedObject(bootstrapComponent);
            var settingsProp = so.FindProperty("settings");
            if (settingsProp != null)
            {
                settingsProp.objectReferenceValue = settings;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.SaveScene(scene, scenePath);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(scenePath, true)
            };
        }
    }
}
"@ | Set-Content -Path $scriptPath
}

function Update-ManifestForSeed([string]$ManifestPath, [string]$CoreDependencyPath) {
    $manifestObj = Get-Content -Path $ManifestPath -Raw | ConvertFrom-Json
    $deps = [ordered]@{}
    $manifestObj.dependencies.PSObject.Properties | ForEach-Object { $deps[$_.Name] = $_.Value }
    $deps["com.unity.render-pipelines.universal"] = "17.2.0"
    $deps["com.zebratank.minilab.core"] = $CoreDependencyPath

    $manifestOut = [ordered]@{
        dependencies = $deps
    }
    if ($manifestObj.PSObject.Properties.Name -contains "scopedRegistries") {
        $manifestOut["scopedRegistries"] = $manifestObj.scopedRegistries
    }

    $manifestOut | ConvertTo-Json -Depth 20 | Set-Content -Path $ManifestPath
}

function Copy-SeedProject([string]$SeedPath, [string]$DestinationPath) {
    if (Test-Path $DestinationPath) {
        Remove-Item -Path $DestinationPath -Recurse -Force
    }

    New-Item -ItemType Directory -Path $DestinationPath -Force | Out-Null
    $excludedDirs = @("Library", "Temp", "Obj", "Build", "Builds", "Logs", "UserSettings")
    $xdArgs = @()
    foreach ($dir in $excludedDirs) {
        $xdArgs += @("/XD", (Join-Path $SeedPath $dir))
    }

    $args = @($SeedPath, $DestinationPath, "/E", "/NFL", "/NDL", "/NJH", "/NJS", "/NC", "/NS", "/NP") + $xdArgs
    $null = & robocopy @args
    if ($LASTEXITCODE -ge 8) {
        throw "Failed to copy seed project to $DestinationPath (robocopy code: $LASTEXITCODE)"
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedUnityPath = Resolve-UnityPath $UnityPath
if ([string]::IsNullOrWhiteSpace($resolvedUnityPath)) {
    throw "Unity executable not found. Provide -UnityPath or MINILAB_UNITY_PATH."
}

$unityVersion = Split-Path -Leaf (Split-Path -Parent (Split-Path -Parent $resolvedUnityPath))
Write-Host "Unity detected: $unityVersion ($resolvedUnityPath)"

$seedPath = Join-Path $repoRoot "tmp/TemplateSeedUnityProject"
if (Test-Path $seedPath) {
    Remove-Item -Path $seedPath -Recurse -Force
}

$createLog = Join-Path $repoRoot "BuildArtifacts/template-create.log"
Invoke-UnityWithTimeout $resolvedUnityPath @(
    "-batchmode",
    "-nographics",
    "-quit",
    "-createProject", $seedPath,
    "-stackTraceLogType", "Full",
    "-logFile", $createLog
) $createLog $TimeoutMinutes

$manifestPath = Join-Path $seedPath "Packages/manifest.json"
Update-ManifestForSeed -ManifestPath $manifestPath -CoreDependencyPath "file:../../../Packages/MiniLab.Core"
Write-TemplateSetupScript -SeedProjectPath $seedPath

$setupLog = Join-Path $repoRoot "BuildArtifacts/template-setup.log"
Invoke-UnityWithTimeout $resolvedUnityPath @(
    "-batchmode",
    "-nographics",
    "-quit",
    "-projectPath", $seedPath,
    "-executeMethod", "MiniLab.TemplateBootstrap.TemplateSetup.Run",
    "-stackTraceLogType", "Full",
    "-logFile", $setupLog
) $setupLog $TimeoutMinutes

$templateTargets = @(
    "Templates/ArcadeTemplate/UnityProject",
    "Templates/PuzzleTemplate/UnityProject",
    "Templates/DefenseTemplate/UnityProject"
)

foreach ($target in $templateTargets) {
    $targetPath = Join-Path $repoRoot $target
    Copy-SeedProject -SeedPath $seedPath -DestinationPath $targetPath

    $targetManifestPath = Join-Path $targetPath "Packages/manifest.json"
    $targetManifest = Get-Content -Path $targetManifestPath -Raw | ConvertFrom-Json
    $targetDeps = [ordered]@{}
    $targetManifest.dependencies.PSObject.Properties | ForEach-Object { $targetDeps[$_.Name] = $_.Value }
    $targetDeps["com.zebratank.minilab.core"] = "file:../../../../Packages/MiniLab.Core"

    $targetManifestOut = [ordered]@{
        dependencies = $targetDeps
    }
    if ($targetManifest.PSObject.Properties.Name -contains "scopedRegistries") {
        $targetManifestOut["scopedRegistries"] = $targetManifest.scopedRegistries
    }
    $targetManifestOut | ConvertTo-Json -Depth 20 | Set-Content -Path $targetManifestPath
}

Write-Host "Template Unity projects created under Templates/*Template/UnityProject."
