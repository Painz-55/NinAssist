# Nin Online AutoAssist

## Interface moderna (1.2.2)

O painel principal mantém os módulos ON/OFF, a sessão e a conexão; abaixo aparecem cartões individuais para Auto Turning, Cast Turn, SubPunish, AA, Tool, Follow, Stamina e Auto Target. Os diálogos validam e salvam os ajustes, sempre desarmando as macros antes da edição. A página **Logs** reúne o painel completo de status ao vivo e o histórico de eventos; o mapa permanece em sua própria página. Alterar a organização visual não libera entradas nem muda permissões.

`src/Modern/App` contém a UI v4, login Firebase e verificação obrigatória de GitHub Releases antes do login. `src/Modern/Live` inclui a leitura do mapa completo usada na UI; `src/Modern/Diagnostics` recompila o diagnóstico contra esse leitor. Esses projetos referenciam o Core deste repositório, **sem DLLs de `C:\NinAutoAssist` nem pastas `dist`**. O instalador é compilado por `scripts/Build-Release.ps1` e pelo workflow de tags, que agora apontam para a UI moderna. Os testes antigos também rodam contra o leitor moderno antes de empacotar. Os botões ON de SubPunish e AA/Tool são a autorização explícita de suas funções, sem caixas extras; OFF, F12, desconexão, troca de personagem e perda de permissão desarmam os módulos. O perfil assinado e os demais gates de segurança continuam obrigatórios.

Não publique uma versão só com um EXE solto: o updater exige instalação Velopack e uma release pública contendo **todos** os assets. A 1.2.2 corrige o tamanho nativo da estrutura `INPUT` que impedia a conexão na 1.2.1. Após a publicação, confirme login, permissões, atualização da instalação anterior, preservação de configurações e F12 em um teste real. Sem esse teste manual, a compilação local não prova a operação ponta a ponta. Não coloque configurações Firebase privadas, `characters`, logs nem chaves no Git.

Auto Stamina exige `HoldToRun=False` no jogo e uma tecla de Run distinta da tecla de ativação do Follow. Se houver colisão, a UI não liga o módulo e registra o motivo; ajuste a tecla do Follow em **AA + Tool / Follow / Stamina**, salve e reconecte. Auto Cast Turn só gira casts iniciados após uma leitura de cast inativo com o módulo ligado; uma leitura inativa durante bloqueio transitório pode armar o próximo cast, mas nunca autoriza giro enquanto o gate está bloqueado nem recupera um cast iniciado durante o bloqueio.

`scripts/Build-LoginPreview.ps1 -FirebaseConfigPath CAMINHO` gera uma **prévia local com Firebase obrigatório**, em pasta separada. Somente a prévia compilada com `LoginPreviewBuild=true` dispensa a checagem do updater para permitir teste do login; o instalador/release não contém esse desvio. Nunca publique a prévia. A atualização real para 1.2.2 requer teste após a release e não é comprovada pela prévia.

O Auto Target procura o jogador vivo mais próximo e usa um escritor experimental dos campos de seleção do cliente (não usa Tab nem clique). Antes da escrita, verifica a build, o perfil assinado, o personagem, o mapa, a identidade e vida do alvo e os gates de segurança; depois exige confirmação por nova leitura. O endereço do objeto gerenciado é resolvido a cada tentativa, não é estático. Um ensaio isolado na build exata confirmou seleção na UI e aceitação pelo servidor, segundo o teste do usuário; GC concorrente, troca de mapa e uso contínuo ainda não foram validados. O modo `Nearest` do Auto Cast Turn usa a lista de jogadores lida do mapa e não seleciona ninguém no jogo. Ambos exigem `MapPlayers`; Auto Target exige também `AutoTarget` no Firebase e seu botão ON. `TargetMemory` está aprovado apenas no perfil empacotado para o hash exato testado; mantenha a permissão Firebase OFF até o teste da versão instalada.

O Cast Turn oferece o filtro de vila apenas no modo **Jogador mais próximo**; o modo **Selecionado no jogo** respeita a seleção manual sem esse filtro. Com o filtro Próximo ativado, vilas desconhecidas são ignoradas por segurança. Não há sinal confiável de grupo/aliança independente da vila.

Cada módulo possui seu próprio editor de configurações. Auto Turning não edita mais o SubPunish; AA, Tool, Follow e Stamina possuem editores e botões ON independentes. Configurar um módulo desarma todos, mas salvar altera somente as opções daquela feature. Auto Target tem a opção de ignorar jogadores da própria vila (ativada por padrão); vila não identificada é ignorada também, para não escolher um possível aliado.

Antes de conceder `AutoTarget` e `MapPlayers` no Firebase, teste a versão instalada com uma conta autorizada e jogadores consentidos: foco, caixa de texto, F12, perda de permissão, troca de personagem/mapa e comportamento após GC. O botão ON é a única ativação da função. O ensaio isolado validou a seleção e o usuário confirmou aceitação no servidor, mas não substitui esse teste de integração. O modo `Nearest` do Cast Turn também requer teste em casts longos e curtos.

Aplicativo Windows em C#/.NET 8 com autenticação Firebase, permissões individuais e atualização automática via Velopack.

## Compilar e testar

Requisitos: Windows x64 e SDK .NET 8. O empacotamento também exige o runtime .NET 10 para o CLI vpk fixado no manifesto.

```powershell
dotnet restore NinOnlineAutoAssist.sln
dotnet test NinOnlineAutoAssist.sln
dotnet build NinOnlineAutoAssist.sln
```

Copie `firebase.config.example.json` para `firebase.config.json` e preencha a configuração pública do seu projeto. Ative login e-mail/senha e aplique as regras de `firebase/firestore.rules`; consulte `firebase/README.md`. Sem configuração válida e autorização, o aplicativo não libera uso. Nunca inclua contas de serviço, senhas, tokens ou chaves privadas.

As configurações de exemplo não contêm personagem nem autorização ofensiva. Desde a versão 1.1.1, o nick é detectado automaticamente pelo índice do jogador local em uma build de jogo validada; não há busca por nomes próximos. É possível desmarcar a detecção e informar o nome manualmente. Os módulos iniciam desligados e F12 permanece a parada de emergência. Somente o último e-mail autenticado é lembrado, sem senha ou token persistido.

A identidade é fixada após leituras consistentes. Mudança de personagem exige reconexão e não transfere automação ligada para o novo personagem. Jogo no login/seleção, mais de um cliente, estado inconsistente ou fingerprints desconhecidos não liberam entrada automática. Compatibilidade e validação: [docs/GAME-COMPATIBILITY.md](docs/GAME-COMPATIBILITY.md).

## Atualizações automáticas

Fluxo único: **baixar Setup → instalar → abrir pelo atalho → verificar atualização obrigatória → atualizar/reiniciar se necessário → login Firebase → programa**. A verificação acontece em toda abertura; se já estiver na versão atual, segue para o login sem reinstalar. Se não conseguir consultar o GitHub, baixar, validar ou iniciar a aplicação da atualização, **bloqueia a abertura**, sem modo offline. Fechar a janela de atualização cancela a abertura. Veja [docs/UPDATES.md](docs/UPDATES.md).

Distribua somente o **Setup.exe** gerado pelo Velopack; todos os demais assets precisam permanecer na release para o updater. Builds soltas e o pacote portable são bloqueados neste código. Abrir o EXE instalado diretamente também executa a mesma verificação: não existe outro EXE de interface, parâmetro de pular, preferência local ou acesso offline ao login. O feed é incorporado na compilação; editar/remover `updates.config.json` não desativa nem redireciona o updater. O repositório de releases deve ser público; nenhum token GitHub vai no cliente.

Isso controla o fluxo do executável oficial, não impede engenharia reversa, alterações no binário ou execução de versões antigas sem esse bloqueio por quem controla o PC. Autorização Firebase continua obrigatória em cada abertura. Compilações padrão seguem esse fluxo; há um canal separado de arquivo local para o mantenedor, descrito abaixo.

## Organização local e histórico de versões

Quando este repositório fica em `NinAssist`, os scripts colocam saídas **fora do código-fonte**:

- `../Com Login/<versão>`: executável local com login Firebase e sem updater, para manter versões e testar regressões.
- `../Instalador/<versão>/releases`: Setup oficial e assets do updater obrigatório.
- `../Instalador/<versão>/publish`: arquivos que compõem o instalador, não uma build local para abrir solta.
- A build original sem login permanece separada e congelada; não é gerada por estes scripts.

Para arquivar uma nova build local:

```powershell
./scripts/Build-Local.ps1 -Version 1.1.1 -FirebaseConfigPath 'CAMINHO/firebase.config.json' -SettingsPath 'CAMINHO/ninautoassist.config.json'
```

O script recusa sobrescrever uma pasta de versão existente, inclui identificação e hashes e preserva as builds anteriores. `SettingsPath` é opcional; sem ele, usa padrões limpos. As configurações pessoais de cada cópia ficam naquela pasta; não envie essas builds ao GitHub.

O canal local é definido **na compilação** por `LocalArchiveBuild=true` e aparece como **LOCAL / ARQUIVO** no login. Isso não remove autenticação, permissões ou telemetria. Não existe opção em JSON ou argumento de execução que converta o instalador nesse canal. `Build-Release.ps1` força `LocalArchiveBuild=false` para o instalador. Os canais são binários diferentes, mesmo quando compartilham a mesma versão de funcionalidades.

## Dados e segurança

- Instalações gerenciadas mantêm configurações e logs relativos em `%LOCALAPPDATA%\NinOnlineAutoAssist`, fora da pasta substituída nas atualizações.
- Builds comuns mantêm a configuração ao lado do executável.
- Firebase valida permissões por conta; atualizações não substituem essa autorização.
- Contas comuns enviam presença e histórico de troca de personagem ao Firestore; a conta administrativa definida na política do código/regras é isenta. A isenção é política funcional, não uma credencial.
- Logs, capturas, backups, binários e configurações locais não pertencem ao Git.
- Sem certificado de assinatura configurado: o Windows pode exibir avisos de editor desconhecido. Proteja os mantenedores e o repositório de releases com MFA; quem puder publicar atualizações poderá distribuir código aos clientes.

Esta cópia é preparada para publicação; não contém configuração Firebase operacional nem histórico local. Não redistribua componentes ou materiais de terceiros sem autorização. Nenhuma licença de código aberto é concedida implicitamente.
