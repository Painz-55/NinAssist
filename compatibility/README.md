# Perfis de compatibilidade independentes do instalador

O app 1.1.4+ verifica o perfil assinado incluído no pacote e, se não encontrar o par de DLLs, busca `profiles.json` e `profiles.json.sig` em `main/compatibility`. A assinatura ECDSA P-256/SHA-256 é verificada antes de analisar os campos. A chave pública está no aplicativo; a chave privada **nunca** vai para Git, Firebase ou cliente. Um perfil só pode ir ao feed remoto se **todas as versões antigas ainda instaláveis** entenderem os gates e restrições desse perfil. Caso contrário, publique primeiro um novo executável com o perfil somente no pacote. O app 1.1.0/1.1.1 não conhece esse canal e precisa atualizar para 1.1.4 primeiro.

`profiles.json` é o **feed remoto legado** e permanece sem a build 2026-11-06: o cliente 1.1.7 busca esse arquivo diretamente e não conhece os novos gates do combo. `package-profiles.json` e `.sig` são copiados pelo projeto App como `compatibility/profiles.json` e `.sig` **somente dentro do novo executável/pacote**. Não copie o perfil novo para o feed remoto enquanto clientes antigos puderem usá-lo. Os dois manifestos são assinados separadamente. Uma assinatura não prova, por si só, que nomes ofuscados representam o que parecem representar. Não copie hashes novos para um perfil antigo. `ActuationValidated: false` e `ValidatedInputKinds: []` permitem apenas leituras passivas. Para `true`, liste em `ValidatedInputKinds` **somente** as ações validadas (`Turning`, `Punish`, `Attack`, `Tool`, `Follow`, `Stamina`, `CastTurning`) e confirme explicitamente ao assinar. Cada ação não listada fica bloqueada. Mudanças estruturais ou sinais ambíguos continuam bloqueadas. Nunca use a lista de jogadores próximos para inferir identidade local.

Diagnóstico somente leitura para um jogo desconhecido:

```powershell
dotnet run --project src/NinOnlineAutoAssist.Diagnostics -- --profile-candidates <PID>
```

O resultado aponta possíveis raízes e campos de nomes/vitais; **não** é um perfil pronto. Valide índice local, mapa, alvo, direção, corrida, buffs, cast, terreno e estados de transição com observações independentes. Só então gere um JSON com todos os campos explícitos, os dois hashes, `schema: 1` e `ActuationValidated: false`. Para promover as ações, compare os estados no jogo e assine explicitamente:

```powershell
dotnet run --project src/NinOnlineAutoAssist.ProfileSigner -- compatibility/profiles.json '<caminho-da-chave-privada.pem>'
# Apenas depois de validar todos os sinais usados por automação:
dotnet run --project src/NinOnlineAutoAssist.ProfileSigner -- compatibility/package-profiles.json '<caminho-da-chave-privada.pem>' --approve-actuation
```

### Diagnóstico mais rápido para atualizações futuras

Enquanto a build **já validada** ainda estiver instalada, capture uma vez os metadados associados ao par exato de DLLs e ao perfil assinado, fora do repositório:

```powershell
dotnet run --project src/NinOnlineAutoAssist.Diagnostics -- --capture-profile-baseline 'CAMINHO\NinOnline.dll' compatibility/profiles.json | Set-Content -Encoding utf8 'CAMINHO_FORA_DO_GIT\baseline.json'
```

Depois da atualização do jogo, gere um relatório estático com os dois hashes novos, tipos e campos candidatos:

```powershell
dotnet run --project src/NinOnlineAutoAssist.Diagnostics -- --profile-schema-diff 'CAMINHO_FORA_DO_GIT\baseline.json' 'CAMINHO_NOVO\NinOnline.dll' compatibility/profiles.json 'Id exato do perfil anterior' | Set-Content -Encoding utf8 'CAMINHO_FORA_DO_GIT\candidatos.json'
```

Ou rode uma única chamada para compilar o diagnóstico e salvar os relatórios fora do Git (o PID é opcional e só faz leituras passivas):

```powershell
./scripts/Prepare-GameUpdate.ps1 -BaselineMetadataPath 'CAMINHO_FORA_DO_GIT\baseline.json' -PreviousProfileId 'Id exato do perfil anterior' -GameDirectory 'CAMINHO_NOVO' -ProcessId <PID>
```

O relatório não edita o perfil, não assina, não injeta e não envia input. Mudança de posição, tipo, estrutura ou valor exige conferência passiva com o processo e, para cada ação, teste controlado autorizado pelo usuário. **Nunca** converter um candidato por ordem de campo em perfil aprovado; hash desconhecido mantém tudo OFF. Um dump antigo sem fingerprint pode ser comparado para pesquisa, mas o relatório marca `BaselineFingerprintVerified=false` e não serve de prova de compatibilidade.

Não edite JSON ou assinatura isoladamente; envie os dois juntos. A chave desta instalação de manutenção foi gerada fora do repositório em `%LOCALAPPDATA%\NinAssist\signing\profile-signing.pem`. Faça backup seguro: perdê-la exige uma nova versão do aplicativo com outra chave pública. Revogação da chave também exige nova versão do app. Não exponha o arquivo no Git nem nos logs de CI.

O canal não substitui atualizações de código, nem garante compatibilidade automática em toda atualização do jogo. O verificador exige fingerprint exato, esquema completo, assinatura, tipos e leituras consistentes; falha de rede, assinatura ou leitura mantém a automação desligada. O perfil Steam 2026-11-04 passou por leituras passivas e testes ativos controlados com o usuário; os sete tipos de input estão listados, sem alegar acerto ou efeito confirmado no servidor. Para 2026-11-06, Melee foi testado; Fan comum foi habilitado por solicitação posterior do usuário com **o mesmo filtro reto, alcance e largura da Tool/shuriken, mas tecla e intervalo de AA**, sem giro preparatório. Ainda requer confirmação em jogo nesta build. Hisuy Fan permanece bloqueado. Consulte `VALIDATION.md`.
