param(
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][string]$RepositoryUrl,
    [string]$FirebaseConfigPath,
    [string]$OutputDirectory,
    [string]$BuildArtifactsDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$packagedProfiles = Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility\package-profiles.json') | ConvertFrom-Json
if (@($packagedProfiles.profiles | Where-Object { $_.ExperimentalTrial -eq $true -or $_.Id -like 'LOCAL TRIAL *' }).Count -ne 0) {
    throw 'Manifesto LOCAL TRIAL não pode entrar em uma release.'
}
if (@($packagedProfiles.profiles | Where-Object {
    $_.Sha256 -eq '349E3190B1D5CB27E10E8CB88AD2402857567989BB069115B80B264060DAB525' -and
    ($_.CommonSha256 -ne '637E4DB9D08CA4C9285B80E36088C15AEF0D2547FA92D11F48980C2B821C3102' -or
     $_.Id -ne 'Nin Online Steam 2026-11-26 (validated core; restricted)' -or
     $_.ActuationValidated -ne $true -or
     (@($_.ValidatedInputKinds) -join ',') -ne 'Turning,Stamina,TargetMemory' -or
     @($_.ValidatedAttackModes).Count -ne 0)
}).Count -ne 0) { throw 'Perfil Steam do pacote excede a validação restrita.' }
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
    $modernArtifacts = Join-Path $BuildArtifactsDirectory 'modern'
    & dotnet test tests/Modern.Tests/Modern.Tests.csproj -c Release --artifacts-path $modernArtifacts
    if ($LASTEXITCODE -ne 0) { throw 'Testes da UI v4 e do leitor moderno falharam.' }
    & dotnet publish src/Modern/App/NinOnlineAutoAssist.App.csproj -c Release -r win-x64 --self-contained true --artifacts-path $modernArtifacts '-p:LoginPreviewBuild=false' '-p:PublishSingleFile=false' '-p:PublishTrimmed=false' "-p:Version=$Version" "-p:UpdateRepositoryUrl=$($RepositoryUrl.TrimEnd('/'))" -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'Falha na publicação.' }
    # Only public client fields; never copy unknown properties or credentials into an installer.
    @{ ProjectId=$config.ProjectId; ApiKey=$config.ApiKey; DatabaseId=$(if ($config.DatabaseId) { $config.DatabaseId } else { '(default)' }) } |
        ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $publish 'firebase.config.json')
    @{ RepositoryUrl=$RepositoryUrl.TrimEnd('/') } | ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $publish 'updates.config.json')
    Copy-Item (Join-Path $root 'ninautoassist.config.example.json') (Join-Path $publish 'ninautoassist.config.json')
    $defaults = Get-Content -Raw -LiteralPath (Join-Path $publish 'ninautoassist.config.json') | ConvertFrom-Json
    if ($defaults.Live.CharacterName) { throw 'Pacote contém configuração pessoal de personagem; distribuição bloqueada.' }
    & dotnet tool run vpk -- pack --packId NinOnlineAutoAssist --packVersion $Version --packDir $publish --mainExe NinOnlineAutoAssist.App.exe --packTitle 'Nin AutoAssist' --icon (Join-Path $root 'src/Modern/App/app.ico') --runtime win-x64 --noPortable --outputDir $releases
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao empacotar Velopack.' }
    Write-Output "Releases prontas em: $releases"
    Write-Output 'Publique todos os assets gerados da versão estável. Não publique apenas o Setup.exe.'
} finally { Pop-Location }
