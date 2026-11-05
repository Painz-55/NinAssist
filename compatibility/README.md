# Perfis de compatibilidade independentes do instalador

O app 1.1.4+ verifica o perfil assinado incluído no pacote e, se não encontrar o par de DLLs, busca `profiles.json` e `profiles.json.sig` em `main/compatibility`. A assinatura ECDSA P-256/SHA-256 é verificada antes de analisar os campos. A chave pública está no aplicativo; a chave privada **nunca** vai para Git, Firebase ou cliente. Para distribuir um perfil já validado, basta enviar esses dois arquivos ao GitHub; não é preciso criar release do instalador. O app 1.1.0/1.1.1 não conhece esse canal e precisa atualizar para 1.1.4 primeiro.

O manifesto contém um perfil validado nesta build: uma assinatura não prova, por si só, que nomes ofuscados representam o que parecem representar. Não copie hashes novos para um perfil antigo. `ActuationValidated: false` e `ValidatedInputKinds: []` permitem apenas leituras passivas. Para `true`, liste em `ValidatedInputKinds` **somente** as ações validadas (`Turning`, `Punish`, `Attack`, `Tool`, `Follow`, `Stamina`, `CastTurning`) e confirme explicitamente ao assinar. Cada ação não listada fica bloqueada. Mudanças estruturais ou sinais ambíguos continuam bloqueadas. Nunca use a lista de jogadores próximos para inferir identidade local.

Diagnóstico somente leitura para um jogo desconhecido:

```powershell
dotnet run --project src/NinOnlineAutoAssist.Diagnostics -- --profile-candidates <PID>
```

O resultado aponta possíveis raízes e campos de nomes/vitais; **não** é um perfil pronto. Valide índice local, mapa, alvo, direção, corrida, buffs, cast, terreno e estados de transição com observações independentes. Só então gere um JSON com todos os campos explícitos, os dois hashes, `schema: 1` e `ActuationValidated: false`. Para promover as ações, compare os estados no jogo e assine explicitamente:

```powershell
dotnet run --project src/NinOnlineAutoAssist.ProfileSigner -- compatibility/profiles.json '<caminho-da-chave-privada.pem>'
# Apenas depois de validar todos os sinais usados por automação:
dotnet run --project src/NinOnlineAutoAssist.ProfileSigner -- compatibility/profiles.json '<caminho-da-chave-privada.pem>' --approve-actuation
```

Não edite JSON ou assinatura isoladamente; envie os dois juntos. A chave desta instalação de manutenção foi gerada fora do repositório em `%LOCALAPPDATA%\NinAssist\signing\profile-signing.pem`. Faça backup seguro: perdê-la exige uma nova versão do aplicativo com outra chave pública. Revogação da chave também exige nova versão do app. Não exponha o arquivo no Git nem nos logs de CI.

O canal não substitui atualizações de código, nem garante compatibilidade automática em toda atualização do jogo. O verificador exige fingerprint exato, esquema completo, assinatura, tipos e leituras consistentes; falha de rede, assinatura ou leitura mantém a automação desligada. O perfil Steam 2026-11-04 passou por leituras passivas e testes ativos controlados com o usuário; os sete tipos de input estão listados, sem alegar acerto ou efeito confirmado no servidor. Consulte `VALIDATION.md`.
