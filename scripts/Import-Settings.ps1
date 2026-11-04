param([Parameter(Mandatory=$true)][string]$From)
$ErrorActionPreference = 'Stop'
$null = Get-Content -Raw -LiteralPath $From | ConvertFrom-Json
$folder = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'NinOnlineAutoAssist'
$target = Join-Path $folder 'ninautoassist.config.json'
if (Test-Path -LiteralPath $target) { throw 'Já existem configurações de usuário. Faça backup/revise-as antes; não serão sobrescritas.' }
New-Item -ItemType Directory -Path $folder -Force | Out-Null
Copy-Item -LiteralPath $From -Destination $target
Write-Output "Configurações importadas em $target. Isso não salva sessão/tokens nem arma os módulos."
