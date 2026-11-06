param(
    [Parameter(Mandatory=$true)][string]$BaselineMetadataPath,
    [Parameter(Mandatory=$true)][string]$PreviousProfileId,
    [Parameter(Mandatory=$true)][string]$GameDirectory,
    [int]$ProcessId = 0,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$game = Join-Path $GameDirectory 'NinOnline.dll'
$common = Join-Path $GameDirectory 'Common.dll'
$manifest = Join-Path $root 'compatibility\profiles.json'
if (-not (Test-Path -LiteralPath $game) -or -not (Test-Path -LiteralPath $common) -or
    -not (Test-Path -LiteralPath $BaselineMetadataPath)) { throw 'DLLs ou metadados de baseline ausentes.' }
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $env:LOCALAPPDATA ('Temp\opencode\NinAssist-game-update\' +
        (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
}
$fullOutput = [IO.Path]::GetFullPath($OutputDirectory)
if ($fullOutput.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $fullOutput.Equals($root, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'O diagnóstico deve ficar fora do repositório source-only.'
}
if (Test-Path -LiteralPath $fullOutput) { throw 'Use uma pasta de saída nova; relatórios anteriores não serão sobrescritos.' }
if ($ProcessId -gt 0) {
    $process = Get-Process -Id $ProcessId -ErrorAction Stop
    if ($process.ProcessName -ne 'NinOnline' -or
        -not [IO.Path]::GetDirectoryName($process.MainModule.FileName).Equals(
            [IO.Path]::GetFullPath($GameDirectory).TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'PID não corresponde ao NinOnline no diretório de DLLs solicitado.'
    }
}
New-Item -ItemType Directory -Path $fullOutput -ErrorAction Stop | Out-Null
$build = Join-Path $fullOutput 'build'
dotnet build (Join-Path $root 'src\NinOnlineAutoAssist.Diagnostics') -c Release --artifacts-path $build --nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o diagnóstico.' }
$exe = Join-Path $build 'bin\NinOnlineAutoAssist.Diagnostics\release\NinOnlineAutoAssist.Diagnostics.exe'
& $exe --profile-schema-diff $BaselineMetadataPath $game $manifest $PreviousProfileId |
    Set-Content -Encoding utf8 (Join-Path $fullOutput 'schema-candidates.json')
if ($LASTEXITCODE -ne 0) { throw 'Falha na comparação estática.' }
if ($ProcessId -gt 0) {
    & $exe --profile-candidates $ProcessId |
        Set-Content -Encoding utf8 (Join-Path $fullOutput 'live-candidates.json')
    if ($LASTEXITCODE -ne 0) { throw 'Falha no diagnóstico passivo.' }
}
Write-Output "Relatórios somente leitura: $fullOutput"
Write-Output 'Nenhum perfil foi aprovado, assinado ou enviado; nenhum input foi enviado ao jogo.'
