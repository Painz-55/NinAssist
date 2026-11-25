param(
    [Parameter(Mandatory=$true)][string]$FirebaseConfigPath,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path (Split-Path -Parent $root) 'NinAssist-LoginPreview-1.2.2' }
if ((Test-Path $OutputDirectory) -and @(Get-ChildItem $OutputDirectory -Force).Count -gt 0) {
    throw 'Use uma pasta nova: a prévia anterior não será sobrescrita.'
}
$config = Get-Content -Raw -LiteralPath $FirebaseConfigPath | ConvertFrom-Json
if (-not $config.ProjectId -or -not $config.ApiKey -or $config.private_key -or $config.type -eq 'service_account') {
    throw 'Use apenas a configuração web pública do Firebase, nunca uma chave de serviço.'
}
$artifacts = Join-Path $env:LOCALAPPDATA ('Temp\NinAssist-login-preview-' + [guid]::NewGuid().ToString('N'))
Push-Location $root
try {
    & dotnet publish src/Modern/App/NinOnlineAutoAssist.App.csproj -c Release -r win-x64 --self-contained true --artifacts-path $artifacts '-p:LoginPreviewBuild=true' '-p:PublishSingleFile=false' '-p:PublishTrimmed=false' '-p:Version=1.2.2' -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar a prévia.' }
    @{ ProjectId=$config.ProjectId; ApiKey=$config.ApiKey; DatabaseId=$(if ($config.DatabaseId) { $config.DatabaseId } else { '(default)' }) } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'firebase.config.json') -Encoding UTF8
    Copy-Item (Join-Path $root 'ninautoassist.config.example.json') (Join-Path $OutputDirectory 'ninautoassist.config.json')
    if (Test-Path (Join-Path $OutputDirectory 'characters')) { throw 'Dados pessoais não podem entrar na prévia.' }
    Write-Output "Prévia autenticada pronta em: $OutputDirectory"
    Write-Output 'Não publique essa pasta nem seu EXE: use-a somente para testar login e UI.'
} finally { Pop-Location }
