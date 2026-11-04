# Compatibilidade do jogo — assistente 1.1.1

## Novo perfil (2026-11-04)

Par SHA-256 exato, conferido nos arquivos das instalações Steam e standalone:

```text
NinOnline.dll 3857343C1E5E9146DD5F546D6134D69C057370AF34FA4648484C73E5BB2FF34C
Common.dll    DD8F314B470F344E8A903A7B0C91FD46E6163327F3B85A3CA0A230AF0BACA373
```

O perfil é selecionado somente pelo par, nunca pelo caminho, nome do personagem ou posição dos campos na lista. Os perfis anteriores permanecem separados. Uma atualização futura com outros hashes exige nova inspeção; não basta substituir os hashes.

## Evidências desta calibração

- Metadados inspecionados sem executar os assemblies de terceiros. Leitura do processo sem suspendê-lo, escrever memória, injetar código ou enviar teclas.
- Índice local apontou para um registro de jogador válido. O provider de produção confirmou automaticamente o nome por leitura repetida, sem usar o nome salvo na configuração.
- Posição, direção e vitais lidos pelo provider real. HP/MP/SP confirmados pelo enum de Common; MP e SP variaram, enquanto seus máximos permaneceram constantes.
- Alvo atual usa um objeto de campos inteiros: `tipo=1` identifica jogador; `tipo=2` não é aceito como jogador. A amostra repete tipo/índice e referência ao final para descartar mudanças durante a leitura. Uma leitura transitória/corrompida não recebe significado válido.
- Corrida isolada pelo usuário: apenas o campo `QVgGMJROkU3` mudou de true (ligada) para false (desligada) entre os estados confirmados.
- Substitution observado com duração 7, 6, 5, 4, 3, 2, 1, 0; nome e marcador de atividade identificados. Nenhum buff foi acionado pelo agente.
- Casts observados com concordância entre o ID global e o ID do ator, início global, fim do ator e relógio do jogo. O buffer de magia secundário não é usado como indicador de cast ativo.
- Leitura de navegação do provider real: grade local 25×25 com células abertas/bloqueadas e mapa consistente. Raiz de projéteis reconhecida, sem perigos ativos nas amostras finais.
- Lista do mapa carregada pelo provider real, com leitura independente de vila e stamina.

## Limites

Esta é validação de leitura e testes automatizados dos motores; não é comprovação de efeitos de todas as automações no servidor. Não foram executados ataques pelo agente, teste de resposta ofensiva do SubPunish, teste completo de cada técnica, reconhecimento de todos os perigos em campo, nem instalação/atualização entre releases na máquina do usuário. Estados de cooldown, identidade do atacante e custo/aceitação das técnicas que já eram desconhecidos continuam desconhecidos.

## Nick automático

Depois do login Firebase, a conexão padrão usa o índice do jogador local do perfil validado. O nome precisa ser não vazio, ter até 64 caracteres, não conter controles e se repetir em uma amostra consistente. O primeiro nome válido fica fixado até a reconexão. Alterar o personagem não troca silenciosamente a identidade nem mantém módulos armados. Sem personagem no mapa, a leitura permanece indisponível; nenhum nome é escolhido da lista de outros jogadores. O modo manual continua disponível.

## Distribuição

1. Commit e push das alterações de 1.1.1.
2. No workflow **Automatic updater release**, selecionar o commit/branch atualizado e informar **1.1.1**; ou enviar a tag nova `v1.1.1`.
3. Publicar todos os assets gerados em uma release estável. Não substituir os pacotes de 1.1.0.
4. Testar em uma instalação existente: fechar o assistente, abrir pelo atalho, confirmar atualização/reinício, login obrigatório, nick automático e módulos inicialmente OFF. Só o commit/push do código não entrega atualização aos clientes: é necessária a release com os pacotes.

O repositório não deve conter traces locais, nomes reais usados na calibração, cópias do jogo ou configurações pessoais. Binários ficam fora dele, em `../Com Login/1.1.1` e `../Instalador/1.1.1`.
