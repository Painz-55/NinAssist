param(
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][string]$FirebaseConfigPath,
    [string]$SettingsPath,
    [string]$OutputDirectory,
    [string]$BuildArtifactsDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use uma versao estavel: 1.1.0, 1.1.1 etc.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path (Split-Path -Parent $root) "Com Login\$Version" }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $OutputDirectory) -and @(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count -gt 0) { throw 'Essa versao ja existe. Use uma pasta nova; o historico nunca sera sobrescrito.' }
if (-not $BuildArtifactsDirectory) { $BuildArtifactsDirectory = Join-Path ([IO.Path]::GetTempPath()) ('NinAssist-local-' + [Guid]::NewGuid().ToString('N')) }
$config = Get-Content -Raw -LiteralPath $FirebaseConfigPath | ConvertFrom-Json
if (-not $config.ProjectId -or -not $config.ApiKey -or $config.private_key -or $config.type -eq 'service_account') { throw 'Use somente a configuracao publica Firebase de cliente.' }
if ($SettingsPath) { $null = Get-Content -Raw -LiteralPath $SettingsPath | ConvertFrom-Json }
Push-Location $root
try {
    & dotnet test NinOnlineAutoAssist.sln -c Release --artifacts-path $BuildArtifactsDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Testes falharam.' }
    & dotnet publish src/NinOnlineAutoAssist.App/NinOnlineAutoAssist.App.csproj -c Release -r win-x64 --self-contained true --artifacts-path $BuildArtifactsDirectory '-p:LocalArchiveBuild=true' '-p:PublishSingleFile=false' '-p:PublishTrimmed=false' "-p:Version=$Version" -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Falha na publicacao local.' }
    @{ ProjectId=$config.ProjectId; ApiKey=$config.ApiKey; DatabaseId=$(if ($config.DatabaseId) { $config.DatabaseId } else { '(default)' }) } |
        ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $OutputDirectory 'firebase.config.json')
    if ($SettingsPath) { Copy-Item -LiteralPath $SettingsPath -Destination (Join-Path $OutputDirectory 'ninautoassist.config.json') }
    @{ Version=$Version; DistributionChannel='LocalArchive'; FirebaseLoginRequired=$true; AutomaticUpdater=$false; CreatedUtc=[DateTime]::UtcNow.ToString('o') } |
        ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $OutputDirectory 'build-info.json')
    @('BUILD LOCAL COM LOGIN - ARQUIVO DE VERSAO', "Versao: $Version", 'Abra NinOnlineAutoAssist.App.exe. Internet, login e permissoes Firebase continuam obrigatorios.', 'Nao ha atualizacao automatica nesta copia. Nao distribuir como instalador oficial.', 'Configuracoes desta pasta podem conter dados pessoais. Nao enviar ao GitHub.') |
        Set-Content -Encoding UTF8 -LiteralPath (Join-Path $OutputDirectory 'LEIA-ME.txt')
    $hashes = Get-ChildItem -LiteralPath $OutputDirectory -Recurse -File | Sort-Object FullName | ForEach-Object {
        '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash, $_.FullName.Substring($OutputDirectory.Length + 1)
    }
    $hashes | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt')
    Write-Output "Build local com login arquivada em: $OutputDirectory"
} finally { Pop-Location }
