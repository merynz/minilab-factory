param(
    [string]$PlayJsonPath = "",
    [string]$AscApiKeyId = "",
    [string]$AscIssuerId = "",
    [string]$AscApiKeyContent = "",
    [string]$AscApiKeyFile = "",
    [string]$FastlaneSession = "",
    [ValidateSet("Process", "User")][string]$Scope = "User"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Set-EnvValue([string]$Name, [string]$Value, [string]$TargetScope) {
    if ($TargetScope -eq "Process") {
        Set-Item -Path ("Env:" + $Name) -Value $Value
    } else {
        [Environment]::SetEnvironmentVariable($Name, $Value, "User")
        Set-Item -Path ("Env:" + $Name) -Value $Value
    }
}

if (-not [string]::IsNullOrWhiteSpace($PlayJsonPath)) {
    $resolvedPlayJson = $PlayJsonPath
    if (-not [System.IO.Path]::IsPathRooted($resolvedPlayJson)) {
        $resolvedPlayJson = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $resolvedPlayJson))
    }

    if (!(Test-Path $resolvedPlayJson)) {
        throw "Play JSON file not found: $resolvedPlayJson"
    }

    Set-EnvValue -Name "MINILAB_PLAY_JSON" -Value $resolvedPlayJson -TargetScope $Scope
    Set-EnvValue -Name "GOOGLE_PLAY_JSON_KEY_PATH" -Value $resolvedPlayJson -TargetScope $Scope
    Write-Host "PASS: Play JSON env set."
}

if (-not [string]::IsNullOrWhiteSpace($AscApiKeyFile)) {
    $resolvedAscFile = $AscApiKeyFile
    if (-not [System.IO.Path]::IsPathRooted($resolvedAscFile)) {
        $resolvedAscFile = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $resolvedAscFile))
    }

    if (!(Test-Path $resolvedAscFile)) {
        throw "ASC API key file not found: $resolvedAscFile"
    }

    $AscApiKeyContent = Get-Content -Path $resolvedAscFile -Raw
}

if (-not [string]::IsNullOrWhiteSpace($AscApiKeyId)) {
    Set-EnvValue -Name "APP_STORE_CONNECT_API_KEY_ID" -Value $AscApiKeyId -TargetScope $Scope
    Write-Host "PASS: APP_STORE_CONNECT_API_KEY_ID set."
}

if (-not [string]::IsNullOrWhiteSpace($AscIssuerId)) {
    Set-EnvValue -Name "APP_STORE_CONNECT_ISSUER_ID" -Value $AscIssuerId -TargetScope $Scope
    Write-Host "PASS: APP_STORE_CONNECT_ISSUER_ID set."
}

if (-not [string]::IsNullOrWhiteSpace($AscApiKeyContent)) {
    Set-EnvValue -Name "APP_STORE_CONNECT_API_KEY_CONTENT" -Value $AscApiKeyContent -TargetScope $Scope
    Write-Host "PASS: APP_STORE_CONNECT_API_KEY_CONTENT set."
}

if (-not [string]::IsNullOrWhiteSpace($FastlaneSession)) {
    Set-EnvValue -Name "FASTLANE_SESSION" -Value $FastlaneSession -TargetScope $Scope
    Write-Host "PASS: FASTLANE_SESSION set."
}

Write-Host "SUCCESS: configure-release-secrets completed."
