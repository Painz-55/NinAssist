# Publicação e atualizações

## 1. Repositórios

Feed configurado: **https://github.com/Painz-55/NinAssist**. Este repositório é público e pode hospedar código e releases. Ainda é necessário publicar a primeira release e distribuir o instalador para ativar atualizações nos clientes.

## Fluxo obrigatório do usuário

1. Baixa somente o **Setup.exe** da release oficial e instala.
2. Abre pelo atalho: a primeira tela é a verificação obrigatória do updater.
3. O updater consulta a release estável publicada. Se houver nova versão, baixa, valida o pacote, aplica e reinicia automaticamente. A versão reiniciada verifica novamente antes de prosseguir.
4. Somente uma verificação bem-sucedida sem atualização pendente libera a tela de login Firebase.
5. Somente autenticação e permissões válidas liberam o programa; módulos continuam inicialmente desligados.

Esse fluxo vale na primeira abertura e nas seguintes. Sem internet, feed válido ou conclusão do update, fecha sem abrir login/programa. Um pacote em cache não autoriza acesso offline e seu checksum é revalidado antes de aplicar. Executáveis soltos e o pacote portable são recusados. Executar diretamente o EXE instalado não pula a verificação, pois updater e login estão na mesma entrada do aplicativo.

A URL é incorporada no assembly no empacotamento por `UpdateRepositoryUrl`; `updates.config.json` é apenas informação do pacote, não uma chave para ativar/desativar o fluxo. Não existe flag de execução para pular o updater no instalador. O mantenedor pode gerar um **binário local separado** com `Build-Local.ps1`, que abre direto no login para arquivar versões e testar regressões; ele não deve ser distribuído como cliente oficial. Isso não é garantia anti-tamper contra binários modificados por alguém com controle do computador; código público pode ser recompilado e versões antigas não adquirem o bloqueio retroativamente.

Crie o repositório de código, preferencialmente privado. Se for privado, crie também um repositório **público** somente para releases. A URL do feed deve ser `https://github.com/USUARIO/REPOSITORIO`, sem token, query ou caminho adicional.

Copie o conteúdo da exportação para o repositório. Inclua as pastas ocultas `.github` e `.config`, além de `.gitignore`.

## 2. GitHub Actions

Em Settings → Secrets and variables → Actions configure:

- Variable `UPDATES_REPOSITORY` (opcional): URL de outro repositório público de releases. Sem ela, o workflow usa o próprio repositório e verifica que ele é público. Para a configuração atual, não é necessário criar essa variável.
- Secret `FIREBASE_PUBLIC_CONFIG_JSON`: JSON contendo somente `ProjectId`, `ApiKey` e `DatabaseId`, conforme o exemplo. São campos de cliente, não uma conta de serviço. As regras Firestore são a barreira de autorização.
- Secret `RELEASES_TOKEN`: somente se as releases forem publicadas em outro repositório. Use acesso restrito ao repositório de destino com permissão Contents write. O token só é usado no CI.

Faça push de uma tag estável nova, por exemplo `v1.1.0`, ou execute o workflow de release indicando uma versão. Ele testa, publica o app self-contained x64, empacota e envia todos os assets. Nunca substitua assets de uma versão existente. Para versões seguintes use `1.1.1`, `1.1.2` etc. A configuração `Directory.Build.props` define a versão padrão; o empacotamento informa a versão da release explicitamente.

CI de pull requests não publica releases. Restrinja quem pode criar tags/executar releases. O workflow recusa feed privado e versão já publicada. Publicação entre repositórios não usa notas automáticas baseadas no histórico do código privado.

## 3. Empacotamento local (sem upload)

```powershell
./scripts/Build-Release.ps1 -Version 1.1.0 -RepositoryUrl https://github.com/Painz-55/NinAssist -FirebaseConfigPath ./firebase.config.json
```

SDK .NET 8 e runtime .NET 10 são necessários. Localmente, o script cria `../Instalador/1.1.0/releases`, sem gerar portable. O CI passa `-OutputDirectory artifacts/<versão>` explicitamente para coletar os assets. Publique **todos** os assets da pasta releases na mesma release estável: o usuário baixa o Setup; os pacotes e o índice são consumidos pelo updater. Não inclua builds, caches ou a subpasta publish em Git. Os scripts usam cache externo temporário para não deixar bin/obj no código-fonte.

## 4. Primeira instalação e migração

Feche todas as instâncias do assistente. Para manter os ajustes de uma build de pasta antiga, execute antes da primeira abertura:

```powershell
./scripts/Import-Settings.ps1 -From 'C:\CAMINHO\BUILD-ANTIGA\ninautoassist.config.json'
```

O script recusa sobrescrever configurações existentes. Depois instale o Setup e abra pelo atalho. Configurações pessoais e último e-mail ficam em `%LOCALAPPDATA%\NinOnlineAutoAssist`. Não distribua essa pasta a outros usuários.

## 5. Validação necessária antes de distribuir

Em uma máquina/VM de teste: instale a versão inicial, altere ajustes, publique uma versão superior, abra pelo atalho e confirme download/reinício automáticos, preservação dos ajustes, login obrigatório e módulos desligados. Teste também rede indisponível, fechamento durante download e pacote inválido. Este teste real depende de um feed e de duas releases; testes unitários não o substituem.

As verificações ocorrem na abertura, não durante automação. Não há downgrade automático ou recuperação completa garantida. Falhas de consulta/download/aplicação **não liberam login**. Feed vazio, com pacotes incompatíveis ou somente versões inferiores também bloqueia a abertura. HTTPS e verificações de integridade do Velopack não substituem assinatura digital do editor. URL incorporada ausente/inválida bloqueia a abertura. O GitHub limita consultas anônimas por IP; muitas aberturas podem temporariamente impedir a verificação e, portanto, o acesso ao programa. Indisponibilidade do GitHub também bloqueia novas aberturas.
