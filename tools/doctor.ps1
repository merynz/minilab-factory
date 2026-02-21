param(
    [string]$ExpectedUnityVersion = "6000.2.6f2",
    [switch]$RequireUploadTooling
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$results = New-Object System.Collections.Generic.List[object]

function Add-Result([string]$Check, [string]$Status, [string]$Details, [string]$Fix = "") {
    $results.Add([pscustomobject]@{
            Check = $Check
            Status = $Status
            Details = $Details
            Fix = $Fix
        })
}

function Resolve-UnityPath {
    if ($env:MINILAB_UNITY_PATH -and (Test-Path $env:MINILAB_UNITY_PATH)) {
        return (Resolve-Path $env:MINILAB_UNITY_PATH).Path
    }

    $roots = @(
        "C:\Program Files\Unity\Hub\Editor",
        "C:\Program Files\Unity"
    )

    foreach ($root in $roots) {
        if (!(Test-Path $root)) { continue }
        $candidate = Get-ChildItem -Path $root -Recurse -Filter "Unity.exe" -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($candidate) { return $candidate.FullName }
    }

    return ""
}

function Resolve-UnityVersion([string]$UnityPath) {
    if ([string]::IsNullOrWhiteSpace($UnityPath)) { return "" }
    $editorDir = Split-Path -Parent $UnityPath
    $versionDir = Split-Path -Parent $editorDir
    return Split-Path -Leaf $versionDir
}

function Resolve-Executable([string]$CommandName, [string[]]$FallbackPaths) {
    $command = Get-Command $CommandName -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    foreach ($path in $FallbackPaths) {
        if ($path -and (Test-Path $path)) {
            return (Resolve-Path $path).Path
        }
    }

    return ""
}

function Test-EnvPath([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return $false }
    return (Test-Path $Value)
}

$unityPath = Resolve-UnityPath
if ([string]::IsNullOrWhiteSpace($unityPath)) {
    Add-Result -Check "Unity Editor" -Status "FAIL" -Details "Unity Editor bulunamadi." -Fix "Unity Hub ile 6000.2.6f2 kurun veya MINILAB_UNITY_PATH ayarlayin."
} else {
    $unityVersion = Resolve-UnityVersion $unityPath
    if ($unityVersion -ne $ExpectedUnityVersion) {
        Add-Result -Check "Unity Editor" -Status "FAIL" -Details "Unity bulundu fakat versiyon farkli: $unityVersion ($unityPath)." -Fix "Beklenen versiyon: $ExpectedUnityVersion. Unity Hub ile kurun veya MINILAB_UNITY_PATH ile override edin."
    } else {
        Add-Result -Check "Unity Editor" -Status "PASS" -Details "$unityVersion ($unityPath)"
    }
}

$androidModulePath = ""
$androidSdkFromModule = ""
$androidNdkFromModule = ""
$androidJdkFromModule = ""
if (-not [string]::IsNullOrWhiteSpace($unityPath)) {
    $unityEditorDir = Split-Path -Parent $unityPath
    $androidModulePath = Join-Path $unityEditorDir "Data/PlaybackEngines/AndroidPlayer"
    $androidSdkFromModule = Join-Path $androidModulePath "SDK"
    $androidNdkFromModule = Join-Path $androidModulePath "NDK"
    $androidJdkFromModule = Join-Path $androidModulePath "OpenJDK"
}

if ([string]::IsNullOrWhiteSpace($unityPath)) {
    Add-Result -Check "Unity Android Build Support" -Status "SKIP" -Details "Unity bulunmadigi icin modul kontrolu yapilmadi."
} elseif (!(Test-Path $androidModulePath)) {
    Add-Result -Check "Unity Android Build Support" -Status "FAIL" -Details "AndroidPlayer modulu yok: $androidModulePath" -Fix "Unity Hub > Installs > Add modules > Android Build Support + SDK & NDK Tools + OpenJDK."
} else {
    Add-Result -Check "Unity Android Build Support" -Status "PASS" -Details $androidModulePath
}

if ([string]::IsNullOrWhiteSpace($unityPath) -or !(Test-Path $androidModulePath)) {
    Add-Result -Check "Android SDK/NDK/OpenJDK" -Status "SKIP" -Details "Unity Android modulu olmadan kontrol atlandi."
} else {
    $sdkPath = if (Test-Path $androidSdkFromModule) { $androidSdkFromModule } elseif ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } else { "" }
    $ndkPath = if (Test-Path $androidNdkFromModule) { $androidNdkFromModule } elseif ($env:ANDROID_NDK_ROOT) { $env:ANDROID_NDK_ROOT } else { "" }
    $jdkPath = if (Test-Path $androidJdkFromModule) { $androidJdkFromModule } elseif ($env:JAVA_HOME) { $env:JAVA_HOME } else { "" }

    $missing = New-Object System.Collections.Generic.List[string]
    if (-not (Test-EnvPath $sdkPath)) { $missing.Add("SDK") }
    if (-not (Test-EnvPath $ndkPath)) { $missing.Add("NDK") }
    if (-not (Test-EnvPath $jdkPath)) { $missing.Add("OpenJDK/JAVA_HOME") }

    if ($missing.Count -gt 0) {
        Add-Result -Check "Android SDK/NDK/OpenJDK" -Status "FAIL" -Details ("Eksik: " + ($missing -join ", ")) -Fix "Unity Hub Android module altinda SDK/NDK/OpenJDK kutularini isaretleyin."
    } else {
        Add-Result -Check "Android SDK/NDK/OpenJDK" -Status "PASS" -Details "SDK=$sdkPath | NDK=$ndkPath | JDK=$jdkPath"
    }
}

$adbFallbacks = @()
if ($androidSdkFromModule) { $adbFallbacks += Join-Path $androidSdkFromModule "platform-tools/adb.exe" }
if ($env:ANDROID_SDK_ROOT) { $adbFallbacks += Join-Path $env:ANDROID_SDK_ROOT "platform-tools/adb.exe" }
$adbPath = Resolve-Executable -CommandName "adb" -FallbackPaths $adbFallbacks
if ([string]::IsNullOrWhiteSpace($adbPath)) {
    Add-Result -Check "ADB" -Status "FAIL" -Details "adb bulunamadi." -Fix "Android SDK platform-tools kurun ve PATH'e ekleyin."
} else {
    Add-Result -Check "ADB" -Status "PASS" -Details $adbPath
}

$javaFallbacks = @()
if ($androidJdkFromModule) { $javaFallbacks += Join-Path $androidJdkFromModule "bin/java.exe" }
if ($env:JAVA_HOME) { $javaFallbacks += Join-Path $env:JAVA_HOME "bin/java.exe" }
$javaPath = Resolve-Executable -CommandName "java" -FallbackPaths $javaFallbacks
if ([string]::IsNullOrWhiteSpace($javaPath)) {
    Add-Result -Check "Java" -Status "FAIL" -Details "java bulunamadi." -Fix "OpenJDK kurun (Unity OpenJDK veya JAVA_HOME) ve PATH'e ekleyin."
} else {
    Add-Result -Check "Java" -Status "PASS" -Details $javaPath
}

$rubyPath = Resolve-Executable -CommandName "ruby" -FallbackPaths @()
$bundlePath = Resolve-Executable -CommandName "bundle" -FallbackPaths @()
$fastlanePath = Resolve-Executable -CommandName "fastlane" -FallbackPaths @()

$toolingMissing = @()
if ([string]::IsNullOrWhiteSpace($rubyPath)) { $toolingMissing += "ruby" }
if ([string]::IsNullOrWhiteSpace($bundlePath)) { $toolingMissing += "bundler(bundle)" }
if ([string]::IsNullOrWhiteSpace($fastlanePath)) { $toolingMissing += "fastlane" }

if ($toolingMissing.Count -eq 0) {
    Add-Result -Check "Ruby/Bundler/Fastlane" -Status "PASS" -Details "ruby, bundle, fastlane bulundu."
} elseif ($RequireUploadTooling) {
    Add-Result -Check "Ruby/Bundler/Fastlane" -Status "FAIL" -Details ("Eksik: " + ($toolingMissing -join ", ")) -Fix "Ruby kurun, sonra `gem install bundler fastlane` calistirin."
} else {
    Add-Result -Check "Ruby/Bundler/Fastlane" -Status "SKIP" -Details ("Eksik: " + ($toolingMissing -join ", ") + " (sadece upload adimi icin gerekli).") -Fix "Upload kullanacaksaniz `gem install bundler fastlane`."
}

$playJsonPath = if ($env:MINILAB_PLAY_JSON) { $env:MINILAB_PLAY_JSON } else { $env:GOOGLE_PLAY_JSON_KEY_PATH }
$playTrack = if ($env:MINILAB_ANDROID_TRACK) { $env:MINILAB_ANDROID_TRACK } else { "internal (default)" }
if ([string]::IsNullOrWhiteSpace($playJsonPath)) {
    Add-Result -Check "Play Upload Env" -Status "SKIP" -Details "MINILAB_PLAY_JSON / GOOGLE_PLAY_JSON_KEY_PATH yok (upload opsiyonel)." -Fix "Play service account json path'ini MINILAB_PLAY_JSON olarak ayarlayin."
} elseif (!(Test-Path $playJsonPath)) {
    Add-Result -Check "Play Upload Env" -Status "FAIL" -Details "JSON yolu var ama dosya bulunamadi: $playJsonPath" -Fix "Path'i duzeltin veya secret dosyasini ilgili ortama koyun."
} else {
    Add-Result -Check "Play Upload Env" -Status "PASS" -Details "json key path hazir, track=$playTrack"
}

$hasAscApiKey = -not [string]::IsNullOrWhiteSpace($env:APP_STORE_CONNECT_API_KEY_ID) `
    -and -not [string]::IsNullOrWhiteSpace($env:APP_STORE_CONNECT_ISSUER_ID) `
    -and -not [string]::IsNullOrWhiteSpace($env:APP_STORE_CONNECT_API_KEY_CONTENT)
$hasFastlaneSession = -not [string]::IsNullOrWhiteSpace($env:FASTLANE_SESSION)

if ($hasAscApiKey -or $hasFastlaneSession) {
    Add-Result -Check "TestFlight Upload Env" -Status "PASS" -Details "App Store Connect credential env bulundu."
} elseif ($IsWindows) {
    Add-Result -Check "TestFlight Upload Env" -Status "SKIP" -Details "Windows host: iOS upload zaten macOS gerektirir." -Fix "macOS runner veya remote Mac uzerinde ASC env ayarlayin."
} else {
    Add-Result -Check "TestFlight Upload Env" -Status "SKIP" -Details "ASC credential env yok (upload opsiyonel)." -Fix "APP_STORE_CONNECT_API_KEY_ID/ISSUER_ID/API_KEY_CONTENT tanimlayin."
}

Write-Host "MiniLab Doctor Report"
Write-Host "---------------------"
$results | Format-Table -AutoSize

$failCount = @($results | Where-Object { $_.Status -eq "FAIL" }).Count
$passCount = @($results | Where-Object { $_.Status -eq "PASS" }).Count
$skipCount = @($results | Where-Object { $_.Status -eq "SKIP" }).Count

Write-Host ""
Write-Host "Summary: PASS=$passCount FAIL=$failCount SKIP=$skipCount"

if ($failCount -gt 0) {
    exit 1
}

exit 0
