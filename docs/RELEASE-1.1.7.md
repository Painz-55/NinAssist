# NinAssist 1.1.7

## Mudanças

- SubPunish: somente motor Legado no aplicativo. O código experimental Novo foi preservado em `future/SubPunishNew/`, fora da compilação. Configurações antigas do Novo passam para Legado com autorização ofensiva desligada.
- Auto Cast Turn: volta a girar no fim de casts válidos observados, exceto IDs de jutsus explicitamente excluídos pelo usuário.
- AA + Tool: perfil assinado da build Steam de 2026-11-05 aprova ataque somente em modo Melee e o SubPunish Legado; Fan, Hisuy Fan e Novo não são autorizados. F12, autenticação Firebase e controles por personagem permanecem.
- Ajustes na interface: gráfico ao lado direito da tabela, botão menor de limpar Turn acima da seleção e diálogo ampliado de teclas/temporização.

O usuário informou validação da cópia local. Os testes automatizados passaram (679 testes). Não confundir tecla enviada com acerto ou efeito no servidor. Teste do **instalador/updater** (atualização antes do login e preservação dos ajustes instalados) ainda depende de publicação em um feed de teste ou release; veja `docs/UPDATES.md`.

## Publicação

O código-fonte e o manifesto `compatibility/profiles.json` **com a respectiva assinatura** devem ir juntos ao GitHub. Nunca envie configuração Firebase local, configurações por personagem, logs, chave privada ou o diretório `publish`.

Pacotes locais preparados em `C:\Build AutoBot\Instalador\1.1.7\releases` (fora do Git). Envie **todos os cinco arquivos** dessa pasta para a mesma release `v1.1.7`; não apenas o Setup. Alternativamente, o workflow `.github/workflows/release.yml` produz esses assets ao enviar a tag `v1.1.7`, desde que `FIREBASE_PUBLIC_CONFIG_JSON` esteja configurado. **Não publique os dois conjuntos ao mesmo tempo nem sobrescreva uma release existente.** Somente o Setup é para instalação inicial; os demais assets são exigidos pelo updater.

Sem certificado de assinatura de código, o Windows poderá alertar sobre editor desconhecido. Confirme permissões e acesso público ao repositório de releases antes de publicar a tag.
