# Nin Online AutoAssist

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
