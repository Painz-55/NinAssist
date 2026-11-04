# Login e permissões — SEU-PROJETO

## Ativação pelo administrador

1. No [Firebase Console](https://console.firebase.google.com/project/SEU-PROJETO), abra **Authentication → Sign-in method** e habilite **Email/Password** (não precisa habilitar email-link).
2. Crie o **Cloud Firestore**, banco **(default)**, inicialmente em modo produção. A URL de Realtime Database não é usada por este aplicativo.
3. Publique o conteúdo completo de `firebase/firestore.rules` na aba **Rules** do Firestore. Alternativa, com Firebase CLI instalado e sua conta administrativa autenticada: `firebase deploy --only firestore:rules --project SEU-PROJETO`. Verifique regras existentes antes: este arquivo restringe todo o banco às coleções descritas aqui. O agente não publicou regras nem alterou a infraestrutura.
4. Em **Authentication → Users → Add user**, cadastre e-mail e senha do usuário. Copie seu **UID**.
5. Em **Firestore → Data**, crie `users/{UID}`. O ID deve ser o UID exato do Authentication, não o e-mail. Campos:

```text
enabled: boolean true
features: map
  AutoTurning: boolean true
  AutoCastTurn: boolean true
  SubPunishLegacy: boolean true
  SubPunishNew: boolean true
  AutoAttack: boolean true
  Tool: boolean true
  Follow: boolean true
  Stamina: boolean true
  MapPlayers: boolean true
  ResourceInspection: boolean true
expiresAt: timestamp (opcional; omita o campo para não definir vencimento)
```

Use booleanos, não strings "true". Chaves são sensíveis a maiúsculas. Campo de recurso ausente é **negado**. Documento ausente, `enabled=false`, tipos inválidos ou vencimento expirado bloqueiam o login. Um mapa vazio permite abrir o app, mas sem os recursos. AA inclui Melee/Fan/Hisuy; Tool é independente. Legado e Novo usam permissões separadas; o seletor local não libera um modo negado.

**Auto Cast Turn** tem a chave própria `features.AutoCastTurn`, independente de `AutoTurning`. Para contas já criadas, adicione esse booleano manualmente no Console para liberar o novo botão. Não é preciso ampliar regras: o cliente continua sem poder alterar qualquer permissão. Liberar o recurso não o liga; módulos sempre começam OFF.

6. Distribua a nova build **com** `firebase.config.json` e dependências. O arquivo contém apenas configuração pública; nunca coloque conta de serviço, chave privada ou senha no executável/pasta. Não há opção offline ou bypass se faltar configuração.
7. Execute o app e digite a conta cadastrada na própria tela de login. Não é necessário compartilhar senha com o agente. Ainda é necessário validar login real depois de configurar o projeto.

Após um login e leitura de permissões bem-sucedidos, o app lembra **somente o último e-mail**, em `%LOCALAPPDATA%\NinOnlineAutoAssist\login-preferences.json`. A senha continua obrigatória em cada abertura e tokens não são persistidos. A opção **Esquecer e-mail lembrado** limpa a preferência atual. O arquivo não deve fazer parte da distribuição; não contém nenhuma licença/permissão, e editá-lo não concede acesso. Na primeira entrada após essa atualização ainda será necessário informar o e-mail para registrá-lo.

## Quem está usando

**Conta isenta:** `pain@ninassist.com.br` (comparação sem diferenciar maiúsculas, com o e-mail devolvido pelo Firebase Auth, não o digitado na tela). A nova build não envia criação de sessão de uso, heartbeat, encerramento ou personagem dessa conta. As regras novas também negam essas gravações por essa conta. Isso **não concede** permissões ou papel administrativo no cliente: o usuário ainda precisa do documento `users/{uid}`, passa pelo Auth e renova permissões normalmente. Logs automáticos do próprio serviço Firebase Authentication/IAM não são controlados por essa isenção. Não foram apagados registros anteriores, e versões antigas precisam ser atualizadas; a proteção no servidor depende da publicação das regras.

Para as **outras contas**, `appSessions/{idAleatorio}` contém `uid`, `email`, `version`, `startedAt` e `lastSeenAt`; no encerramento normal, tenta gravar `endedAt`. Depois da primeira observação válida do jogo, acrescenta `characterName`, `characterSequence` e `characterChangedAt` (último personagem confirmado, não necessariamente ainda conectado). Timestamps usam **REQUEST_TIME** do Firestore, não o relógio do computador. O heartbeat é a cada aproximadamente **30 segundos**. Considere em uso quando `lastSeenAt` estiver nos últimos **90 segundos** e não houver `endedAt`; isso é uma estimativa de presença da sessão, não prova de interação humana. Fechamento abrupto/crash não garante `endedAt`. Registros antigos são mantidos; configure retenção administrativa conforme sua necessidade. Não há limite de sessões simultâneas implementado.

### Histórico de personagem

Cada personagem local confirmado e cada troca posterior geram um documento em **`appSessions/{sessao}/events/{sequencia}`**:

```text
uid / email: conta autenticada
kind: "characterChanged"
previousCharacterName: nome anterior ("" na primeira observação da sessão)
characterName: nome confirmado pelo leitor do jogo
sequence: 1, 2, 3… (por sessão)
recordedAt: timestamp do servidor
```

Repetir amostras do mesmo nome não gera spam. A → B → A gera três registros, sem substituir os anteriores. Reconectar com o mesmo nome não é uma troca. Editar o campo de conexão não gera um evento: é preciso obter uma amostra válida do personagem conectado. O registro pertence à conta/sessão da aplicação, não a uma identificação inviolável do jogador no servidor do jogo.

As alterações entram numa fila **em memória**, fora do worker de automação, e são enviadas a cada até aproximadamente **5s** quando há conexão; há tentativa final no logout. Resumo e evento são gravados juntos num commit atômico, com timestamps do servidor, sequência ordenada e IDs determinísticos para confirmar uma gravação cuja resposta tenha se perdido, sem duplicar histórico. Não se iniciam gravações para a conta isenta. Operações de telemetria têm orçamento de 3s por rodada; a checagem de permissões continua separada do sucesso dos logs. Se falhar, a conta mostra alterações pendentes e tenta de novo, sem travar o jogo ou afirmar que o envio funcionou. Crash, queda prolongada de rede ou fechamento antes do envio podem perder eventos ainda em memória. `recordedAt` é o horário de aceitação pelo Firestore, não uma promessa do instante exato da troca local.

**Publique novamente `firebase/firestore.rules` antes de usar o histórico.** As regras antigas recusam os novos campos e a subcoleção; o app pode continuar com heartbeat, mas mostrará falha/pendência para personagens até a atualização. As regras novas mantêm documentos `users` sem escrita pelo cliente, impedem leitura de terceiros, exigem que resumo e evento correspondam no mesmo commit e proíbem atualizar/apagar eventos. O agente não publicou essas regras nem validou o projeto real; os testes C# simulam HTTP e não substituem Emulator/Console. Não distribua credenciais administrativas.

Não são enviados chat, hardware, mapa, vila, posição, inventário ou lista de jogadores. O único dado adicional do jogo nesta atualização é o nome do próprio personagem observado. Tokens e senha não são persistidos, nem incluídos em erros/logs. Senhas são tratadas pelo Firebase Auth. O administrador consulta pelo Console/IAM; este projeto não inclui painel administrativo público.

## Revogação e conexão

O aplicativo renova a autenticação e consulta permissões a cada **30s**. Falha nessa validação encerra a sessão e desarma os módulos. Há também uma licença em memória de no máximo **75s**, medida por relógio monotônico desde o início da requisição; atraso de rede não estende a licença. O vencimento de `expiresAt` usa horário HTTP do servidor com margem conservadora de 2s, não depende de adiantar/atrasar o relógio local. Mudanças de permissões desarmam todos os módulos; novas permissões nunca religam automação automaticamente. F12, foco, perfis de jogo e proteções anteriores continuam valendo.

Para bloquear imediatamente no banco, defina `users/{uid}.enabled=false`: as regras recusam gravações imediatamente e o app bloqueia na próxima checagem. Desabilitar apenas no Auth pode deixar ID tokens existentes válidos por um período; use também `enabled=false`. Revogação no desktop não é instantânea: depende do próximo polling/lease.

Não existe cadastro no app. **A API pública de email/password do Firebase pode permitir cadastro externo**, mesmo sem botão. Uma conta criada externamente não recebe `users/{uid}` e portanto não consegue entrar no aplicativo nem gravar presença. Se precisar impedir também a criação de contas Auth externas, avalie os controles de signup/Identity Platform do seu projeto. Não confie no segredo da Web API Key como controle de acesso.

As regras protegem o banco; um executável local pode ser modificado por alguém com controle do computador. Não se promete proteção anti-tamper ou revogação de uma build antiga sem login. A cópia congelada em `PASTA-DO-BACKUP-LOCAL` permanece deliberadamente sem autenticação: não a distribua como versão restrita.

## Validação antes de distribuir

- Testar login correto/incorreto, usuário sem documento, conta desativada e expirada.
- Testar conta apenas Tool, apenas AA, cada modo SubPunish isolado, Follow/Stamina, lista e inspeção negadas.
- Durante Follow, retirar acesso e conferir liberação das setas; testar queda de rede e F12.
- No Firestore, conferir presença, personagem e timestamps; testar A → B → A, repetição do mesmo nome, isenção de `pain@ninassist.com.br`, queda de rede/retry e logout. Tentar escrever `users/{uid}`, alterar/apagar evento, alterar o resumo sem evento correspondente, criar evento sem resumo ou ler usuário/sessão de terceiros com token do cliente: deve negar.
- Testar logout, novo login e fechar o aplicativo; nenhum módulo pode voltar ON sozinho.
- Os testes HTTP locais não substituem testes das regras no Emulator nem login real no projeto.
