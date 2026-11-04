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

As configurações de exemplo não contêm personagem nem autorização ofensiva. O usuário deve configurar o personagem observado; os módulos iniciam desligados e F12 permanece a parada de emergência. Somente o último e-mail autenticado é lembrado, sem senha ou token persistido.

## Atualizações automáticas

Veja [docs/UPDATES.md](docs/UPDATES.md). Ao abrir uma instalação gerenciada, o aplicativo procura, baixa e aplica novas versões estáveis **sem confirmação**, antes do login. Se a atualização falhar, mantém a versão atual e exige login normalmente. Fechar a janela de atualização cancela a abertura.

Para receber atualizações é necessário instalar o Setup gerado pelo Velopack. Executar uma pasta antiga/portable não a converte em instalação atualizável. O repositório de releases deve ser público; o código-fonte pode permanecer privado. Nenhum token GitHub vai no cliente.

## Dados e segurança

- Instalações gerenciadas mantêm configurações e logs relativos em `%LOCALAPPDATA%\NinOnlineAutoAssist`, fora da pasta substituída nas atualizações.
- Builds comuns mantêm a configuração ao lado do executável.
- Firebase valida permissões por conta; atualizações não substituem essa autorização.
- Contas comuns enviam presença e histórico de troca de personagem ao Firestore; a conta administrativa definida na política do código/regras é isenta. A isenção é política funcional, não uma credencial.
- Logs, capturas, backups, binários e configurações locais não pertencem ao Git.
- Sem certificado de assinatura configurado: o Windows pode exibir avisos de editor desconhecido. Proteja os mantenedores e o repositório de releases com MFA; quem puder publicar atualizações poderá distribuir código aos clientes.

Esta cópia é preparada para publicação; não contém configuração Firebase operacional nem histórico local. Não redistribua componentes ou materiais de terceiros sem autorização. Nenhuma licença de código aberto é concedida implicitamente.
