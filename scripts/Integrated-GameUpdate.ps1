param(
    [Parameter(Mandatory=$true)][string]$GameDirectory,
    [Parameter(Mandatory=$true)][int]$ProcessId,
    [Parameter(Mandatory=$true)][string]$Character,
    [Parameter(Mandatory=$true)][string]$CandidateProfilePath,
    [ValidateRange(15,120)][int]$Seconds = 120,
    [string]$DiagnosticExePath,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$gameDirectory = [IO.Path]::GetFullPath($GameDirectory).TrimEnd('\')
$profilePath = (Resolve-Path -LiteralPath $CandidateProfilePath).Path
$profile = Get-Content -Raw -LiteralPath $profilePath | ConvertFrom-Json
if ($profile.ActuationValidated -ne $false -or $profile.ExternalProfile -ne $true -or
    @($profile.ValidatedInputKinds).Count -ne 0 -or @($profile.ValidatedAttackModes).Count -ne 0 -or
    $profile.PunishComboValidated -ne $false) { throw 'Capture only accepts an input-disabled local profile.' }
if ($Character.Length -lt 1 -or $Character.Length -gt 64) { throw 'Explicit character name required.' }
$process = Get-Process -Id $ProcessId -ErrorAction Stop
if ($process.ProcessName -ne 'NinOnline' -or
    -not [IO.Path]::GetDirectoryName($process.MainModule.FileName).Equals($gameDirectory, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Process and game directory do not match.'
}
$game = Join-Path $gameDirectory 'NinOnline.dll'
$common = Join-Path $gameDirectory 'Common.dll'
if ((Get-FileHash -LiteralPath $game).Hash -ne $profile.Sha256 -or
    (Get-FileHash -LiteralPath $common).Hash -ne $profile.CommonSha256) {
    throw 'DLL pair changed; refusing stale candidate profile.'
}
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $env:LOCALAPPDATA ('Temp\opencode\NinAssist-game-update\' +
        (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ($output.Equals($root, [StringComparison]::OrdinalIgnoreCase) -or
    $output.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or
    (Test-Path -LiteralPath $output)) { throw 'Use a new output directory outside the repository.' }
New-Item -ItemType Directory -Path $output | Out-Null
if ($DiagnosticExePath) {
    $exe = (Resolve-Path -LiteralPath $DiagnosticExePath).Path
} else {
    $build = Join-Path $output 'build'
    & dotnet build (Join-Path $root 'src\NinOnlineAutoAssist.Diagnostics') -c Release --artifacts-path $build --nologo -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Diagnostic build failed.' }
    $exe = Join-Path $build 'bin\NinOnlineAutoAssist.Diagnostics\release\NinOnlineAutoAssist.Diagnostics.exe'
}
@{ StartedUtc=[DateTime]::UtcNow.ToString('o'); ProcessId=$ProcessId; Character=$Character;
    GameSha256=$profile.Sha256; CommonSha256=$profile.CommonSha256; InputAuthorized=$false;
    CandidateProfile=$profilePath; Seconds=$Seconds } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'session.json') -Encoding utf8
Write-Output "PASSIVE CAPTURE STARTED: $Seconds seconds. Manual actions only; assistant OFF."
$provider = Start-Job -ArgumentList $exe,$ProcessId,$Character,$profilePath,$Seconds,$output -ScriptBlock {
    param($exe,$pidGame,$character,$profile,$seconds,$output)
    & $exe --calibration-watch $pidGame $character $profile $seconds |
        Set-Content -LiteralPath (Join-Path $output 'provider-events.json') -Encoding utf8
    if ($LASTEXITCODE -ne 0) { throw 'Provider capture failed.' }
}
$raw = Start-Job -ArgumentList $exe,$ProcessId,$Character,$profilePath,$Seconds,$output -ScriptBlock {
    param($exe,$pidGame,$character,$profile,$seconds,$output)
    & $exe --passive-cast-survey $pidGame $character $profile $seconds |
        Set-Content -LiteralPath (Join-Path $output 'raw-transitions.json') -Encoding utf8
    if ($LASTEXITCODE -ne 0) { throw 'Raw transition capture failed.' }
}
$jobs = @($provider,$raw)
try {
    $jobs | Wait-Job | Out-Null
    $jobs | Receive-Job -ErrorAction Stop
    if (@($jobs | Where-Object State -ne 'Completed').Count -ne 0) { throw 'Integrated capture incomplete.' }
    if ((Get-FileHash -LiteralPath $game).Hash -ne $profile.Sha256 -or
        (Get-FileHash -LiteralPath $common).Hash -ne $profile.CommonSha256) {
        throw 'DLL pair changed during capture; observations invalid.'
    }
    $events = Get-Content -Raw -LiteralPath (Join-Path $output 'provider-events.json') | ConvertFrom-Json
    $rawData = Get-Content -Raw -LiteralPath (Join-Path $output 'raw-transitions.json') | ConvertFrom-Json
    $states = @($events.Events | ForEach-Object { $_.State })
    $result = [ordered]@{
        GameSha256=$profile.Sha256; CommonSha256=$profile.CommonSha256; InputAuthorized=$false
        Attempts=$events.Attempts; Frames=$events.Frames; NavigationFrames=$events.NavigationFrames
        MapPlayerFrames=$events.MapPlayerFrames; RawTransitionSamples=@($rawData.Samples).Count
        Maps=@($states.Map | Sort-Object -Unique)
        Facings=@($states.Facing | Sort-Object -Unique)
        RunStates=@($states.Running | Sort-Object -Unique)
        CastIds=@($states.CastId | Where-Object { $_ -gt 0 } | Sort-Object -Unique)
        Targets=@($states.Target | Where-Object { $_ } | Sort-Object -Unique)
        SubstitutionDurations=@($states | Where-Object SubstitutionActive |
            ForEach-Object { $_.SubstitutionDuration } | Sort-Object -Unique)
        NavigationBlockedEvents=@($states | Where-Object { $_.NavigationStatus -match 'bloqueada' }).Count
        HazardEvents=@($states | Where-Object { $_.NavigationStatus -match '\+ [1-9][0-9]* perigos' }).Count
        Warning='Candidates and manual transitions only. No input semantics, hits, or release approved.'
    }
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'summary.json') -Encoding utf8
    Write-Output "PASSIVE CAPTURE COMPLETE: $output"
} finally {
    $jobs | Remove-Job -Force -ErrorAction SilentlyContinue
}
