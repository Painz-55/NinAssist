# SubPunish Novo — arquivo de pesquisa (fora da compilação)

O produto usa **somente SubPunish Legado**. Estes arquivos preservam o motor ancorado, a correlação de deslocamento, os testes e os diagnósticos antigos para retomada futura. Não os inclua nos projetos ativos sem uma revisão de segurança e nova validação no jogo.

Em 2026-11-05, a tentativa sem alvo às 14:18:46 registrou Q→A→S via `SendInput`, mas o usuário não viu animações; outras remoções antecipadas não apresentaram salto local correlacionado. Perto da expiração, os sinais de Sub acionada e de término natural são indistinguíveis pelos campos usados. Não inferir aceitação no servidor, não liberar fallback silencioso, e não publicar esse modo por causa de testes unitários.

Configurações antigas com `PunishMode=AnchoredExperimental` são migradas em memória para Legado com autorização OFF; o motor arquivado não pode ser escolhido pela UI nem pela fábrica ativa. O código aqui é intencionalmente apenas histórico, não um build funcional independente.
