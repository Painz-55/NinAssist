param(
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][string]$RepositoryUrl,
    [string]$FirebaseConfigPath,
    [string]$OutputDirectory,
    [string]$BuildArtifactsDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use uma versão estável nova: 1.1.0, 1.1.1 etc.' }
if ($RepositoryUrl -notmatch '^https://github\.com/[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9_.-]{1,100}/?$') { throw 'Informe a URL HTTPS do repositório PÚBLICO de releases, sem token.' }
if (-not $FirebaseConfigPath) { $FirebaseConfigPath = Join-Path $root 'firebase.config.json' }
$config = Get-Content -Raw -LiteralPath $FirebaseConfigPath | ConvertFrom-Json
if (-not $config.ProjectId -or -not $config.ApiKey) { throw 'Preencha a configuração PÚBLICA Firebase; nunca use conta de serviço.' }
if ($config.private_key -or $config.type -eq 'service_account') { throw 'Conta de serviço não pode ser distribuída.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path (Split-Path -Parent $root) "Instalador\$Version" }
if (-not $BuildArtifactsDirectory) { $BuildArtifactsDirectory = Join-Path ([IO.Path]::GetTempPath()) ('NinAssist-release-' + [Guid]::NewGuid().ToString('N')) }
if ((Test-Path -LiteralPath $OutputDirectory) -and @(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count -gt 0) { throw 'Saída não está vazia. Use uma pasta nova para não sobrescrever releases.' }
$publish = Join-Path $OutputDirectory 'publish'
$releases = Join-Path $OutputDirectory 'releases'
New-Item -ItemType Directory -Path $publish -Force | Out-Null
Push-Location $root
try {
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao restaurar o CLI Velopack. Confira os runtimes exigidos por vpk.' }
    & dotnet test NinOnlineAutoAssist.sln -c Release --artifacts-path $BuildArtifactsDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Testes falharam; nenhuma release será empacotada.' }
    & dotnet publish src/NinOnlineAutoAssist.App/NinOnlineAutoAssist.App.csproj -c Release -r win-x64 --self-contained true --artifacts-path $BuildArtifactsDirectory '-p:LocalArchiveBuild=false' '-p:PublishSingleFile=false' '-p:PublishTrimmed=false' "-p:Version=$Version" "-p:UpdateRepositoryUrl=$($RepositoryUrl.TrimEnd('/'))" -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'Falha na publicação.' }
    # Only public client fields; never copy unknown properties or credentials into an installer.
    @{ ProjectId=$config.ProjectId; ApiKey=$config.ApiKey; DatabaseId=$(if ($config.DatabaseId) { $config.DatabaseId } else { '(default)' }) } |
        ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $publish 'firebase.config.json')
    @{ RepositoryUrl=$RepositoryUrl.TrimEnd('/') } | ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $publish 'updates.config.json')
    $defaults = Get-Content -Raw -LiteralPath (Join-Path $publish 'ninautoassist.config.json') | ConvertFrom-Json
    if ($defaults.Live.CharacterName) { throw 'Pacote contém configuração pessoal de personagem; distribuição bloqueada.' }
    & dotnet tool run vpk -- pack --packId NinOnlineAutoAssist --packVersion $Version --packDir $publish --mainExe NinOnlineAutoAssist.App.exe --packTitle 'Nin Online AutoAssist' --runtime win-x64 --noPortable --outputDir $releases
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao empacotar Velopack.' }
    Write-Output "Releases prontas em: $releases"
    Write-Output 'Publique todos os assets gerados da versão estável. Não publique apenas o Setup.exe.'
} finally { Pop-Location }
