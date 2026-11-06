param(
    [Parameter(Mandatory=$true)][ValidateSet('Capture','Analyze')][string]$Mode,
    [Parameter(Mandatory=$true)][string]$GameDirectory,
    [string]$BaselineDirectory = (Join-Path $env:LOCALAPPDATA 'NinAssist\calibration'),
    [string]$OutputDirectory,
    [int]$ProcessId = 0
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$gameDirectory = [IO.Path]::GetFullPath($GameDirectory)
$game = Join-Path $gameDirectory 'NinOnline.dll'
$common = Join-Path $gameDirectory 'Common.dll'
if (-not (Test-Path -LiteralPath $game) -or -not (Test-Path -LiteralPath $common)) {
    throw 'NinOnline.dll e Common.dll são necessários no mesmo diretório.'
}
function OutsideSource([string]$path) {
    $full = [IO.Path]::GetFullPath($path)
    if ($full.Equals($root, [StringComparison]::OrdinalIgnoreCase) -or
        $full.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Baseline e relatórios devem ficar fora do repositório source-only.'
    }
    return $full
}
$BaselineDirectory = OutsideSource $BaselineDirectory
$manifest = Join-Path $root 'compatibility\package-profiles.json'
$gameHash = (Get-FileHash -LiteralPath $game -Algorithm SHA256).Hash
$commonHash = (Get-FileHash -LiteralPath $common -Algorithm SHA256).Hash
$baseline = Join-Path $BaselineDirectory 'baseline.json'
if ($Mode -eq 'Capture') {
    if (Test-Path -LiteralPath $baseline) { throw 'Baseline já existe; não sobrescrever histórico.' }
    $null = New-Item -ItemType Directory -Path $BaselineDirectory -Force
} elseif (-not (Test-Path -LiteralPath $baseline)) {
    throw 'Baseline ausente. Capture uma vez a build atual conhecida antes do próximo update.'
}
if ($ProcessId -gt 0) {
    $process = Get-Process -Id $ProcessId -ErrorAction Stop
    if ($process.ProcessName -ne 'NinOnline' -or
        -not [IO.Path]::GetDirectoryName($process.MainModule.FileName).Equals(
            $gameDirectory.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'PID não corresponde ao jogo no diretório informado.'
    }
}
$build = Join-Path $env:LOCALAPPDATA ('Temp\opencode\NinAssist-passive-diagnostics-' + [guid]::NewGuid().ToString('N'))
dotnet build (Join-Path $root 'src\NinOnlineAutoAssist.Diagnostics') -c Release --artifacts-path $build --nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar diagnóstico passivo.' }
$exe = Join-Path $build 'bin\NinOnlineAutoAssist.Diagnostics\release\NinOnlineAutoAssist.Diagnostics.exe'
if ($Mode -eq 'Capture') {
    $json = & $exe --capture-profile-baseline $game $manifest
    if ($LASTEXITCODE -ne 0) { throw 'Par de DLLs sem perfil assinado; baseline não criado.' }
    $captured = $json | ConvertFrom-Json
    if ($captured.GameSha256 -ne $gameHash -or $captured.CommonSha256 -ne $commonHash) {
        throw 'DLLs mudaram durante a captura; baseline não criado.'
    }
    $json | Set-Content -LiteralPath $baseline -Encoding utf8
    Write-Output "Baseline assinado salvo fora do Git: $baseline"
    Write-Output 'Nenhuma ação no jogo; nenhuma função de input foi autorizada.'
    return
}
$previous = Get-Content -LiteralPath $baseline -Raw | ConvertFrom-Json
if (-not $previous.ProfileId -or -not $previous.GameSha256 -or -not $previous.CommonSha256) {
    throw 'Baseline sem perfil ou hashes verificáveis.'
}
if ($previous.GameSha256 -eq $gameHash -and $previous.CommonSha256 -eq $commonHash) {
    Write-Output 'Par de DLLs idêntico ao baseline; não há nova calibração a fazer.'
    return
}
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $env:LOCALAPPDATA ('Temp\opencode\NinAssist-game-update\' +
        (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
}
$OutputDirectory = OutsideSource $OutputDirectory
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Saída existente; não sobrescrever relatórios.' }
$null = New-Item -ItemType Directory -Path $OutputDirectory
$report = & $exe --profile-schema-diff $baseline $game $manifest $previous.ProfileId
if ($LASTEXITCODE -ne 0) { throw 'Comparação falhou; não aprovar candidatos.' }
$report | Set-Content -LiteralPath (Join-Path $OutputDirectory 'schema-candidates.json') -Encoding utf8
if ($ProcessId -gt 0) {
    $live = & $exe --profile-candidates $ProcessId
    if ($LASTEXITCODE -ne 0) { throw 'Leitura passiva falhou; não aprovar candidatos.' }
    $live | Set-Content -LiteralPath (Join-Path $OutputDirectory 'live-candidates.json') -Encoding utf8
}
@{
    GameSha256 = $gameHash; CommonSha256 = $commonHash; Baseline = $previous.ProfileId
    Result = 'CANDIDATES_ONLY'; InputAuthorized = $false
    Next = 'Revisar campos ambíguos; preparar uma única build completa para teste integrado do usuário.'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'summary.json') -Encoding utf8
Write-Output "Calibração passiva: $OutputDirectory"
Write-Output 'Candidatos não são perfil aprovado. Nenhuma tecla enviada, nenhuma assinatura ou release criada.'
