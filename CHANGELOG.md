# Alterações

## 1.1.1

- Compatibilidade com o par de assemblies do Nin Online atualizado em 2026-11-04, observado com os mesmos hashes nas instalações Steam e standalone.
- Novo perfil completo e independente, preservando os dois perfis anteriores. Fingerprints desconhecidos ou pares misturados continuam bloqueados.
- Leitura do alvo por tipo + índice no registro de alvo atual; NPCs não são tratados como jogadores.
- Identidade do mapa validada pelo campo estático de mapa atual, antes e depois da leitura do terreno.
- Campos de posição, direção, vitais, corrida, buffs, cast, vila, terreno e projéteis remapeados.
- Nick automático pelo índice local, ligado por padrão, com opção manual. Identidade fixada por conexão; mudança exige reconectar com módulos desligados.
- Login Firebase, permissões, isenção administrativa de telemetria, F12 e canais local/instalador preservados.

Validação passiva em jogo e limites dos testes: `docs/GAME-COMPATIBILITY.md`. Não houve envio de teclas de automação durante a calibração. As ações no jogo foram executadas pelo usuário.
