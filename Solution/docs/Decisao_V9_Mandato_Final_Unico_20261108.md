> **GOV-V9-FINAL-20261108-B — PRECEDÊNCIA SUPERVENIENTE:** o [mandato de integração final e teste geral de 08/11/2026](Decisao_V9_Mandato_Integracao_Final_Teste_Geral_20261108.md) substitui TODOS os mandatos operacionais anteriores (inclusive GOV-OPS-V9-01/02 e o mandato preliminar de 04h05). Este documento preserva registros de estado do momento da sua redação; os SHAs/erros históricos não descrevem a master atual. Consulte o PAINEL V9 da PR #1318 (comentário 6725665397) antes de agir. A neutralização de binários ainda AGUARDA PO; não declarar V9 concluída.

# GOV-OPS-V9-FINAL-20261108 — Mandato final único e reação à master vermelha

**Decisão do PO:** 08/11/2026, 04h05 (America/Sao_Paulo). **Precedência:** substitui mandatos, checkpoints e propostas operacionais anteriores, preservando acervo histórico e a política GOV-DOC-01. **Status:** execução parcial; não é conclusão das frentes nem certificação estatística.

## Escopo literalmente recebido na conversa

O mandato exige: (0) desativar todas as vigílias, lembretes e rotinas anteriores e criar no máximo UMA rotina de retomada nesta conversa a cada 60 minutos; jamais cancelar runs já em curso na `master`. (1) ponto de partida `master` 33ba4a20 (merge #1326/D); A/#1308, A2/#1338, B/#1310, C/#1304, D/#1326 e #1311, #1316, #1317, #1320 já integradas (D aguardava master CI), ressalvada verificação de evidências finais. Frentes restantes E/#1336, H/#1328, G/#1334, F/#1332, J/#1337, K/#1318, L/#1333, I/campanha; #1242 deve ficar em rascunho intocável.

(2) desenvolvimento em paralelo, **merge em fila única**, uma PR por frente, rebase sobre a master → push → checks rápidos verdes no HEAD exato → merge → `jornada-ci` e `v9-physical-seed42-probe` verdes na master integrada (30k físico completo se impactado), sem próxima integração antes disso. Não abrir PR adicional ou substituta. `AGUARDA PO` não bloqueia: preservar comportamento, registrar pergunta e continuar. Parar fila somente quando master vermelha (**reverter o último merge e corrigir**), alteração indevida do hash de resultado da âncora histórica V2 sem declaração de mudança do motor, ou conflito que altere lógica científica; outras frentes prosseguem. Reexecutar falha comprovadamente transitória de infraestrutura do mesmo run no máximo duas vezes, se a ferramenta permitir. Nunca declarar frente concluída com requisitos pendentes.

**Integridade do mandato:** o texto recebido nesta conversa foi truncado após `3.1 VALIDAR D: aguardar a master 33ba4a20 verde (jornada-ci, 30k físic`. Itens seguintes não devem ser presumidos nem atribuídos ao PO; aplicar somente as regras expressamente recebidas, sem recuperar mandatos revogados por inferência.

## Incidente real após integração D/#1326

Merge D: `33ba4a201b293aaa1053a9c6d52f1d9e40cd481b`. No SHA exato, `v9-physical-seed42-probe` run **42456779210 SUCCESS**, mas `jornada-ci` run **42456779221 FAILURE**; o job `unit-contracts` **128657556186** falhou na etapa `Gate source sanity and pinned workflow actions`. Diagnóstico do gate: `SQL ARGV SAFETY GATE: FAIL` em `.github/workflows/v9-d-local-physical-determinism-1-0-8.yml:62`, pois `docker exec ... -e SQLCMDPASSWORD=valor` transportava um segredo por **argumento de processo**. A falha é **de código de segurança, não infraestrutura transitória**; re-run não é remédio. Não cancelar a campanha independente e outras execuções que restem em andamento na master.

**Reação exigida pelo mandato:** revertê-la por novo commit de reversão, sem reescrever histórico nem abrir nova PR, restaurando código anterior ao merge D. A reversão não declara D concluída. Corrigir o transporte para `SQLCMDPASSWORD="$JORNADA_SQL_SA_PASSWORD" docker exec ... -e SQLCMDPASSWORD` (somente nome, sem valor na linha de comando) ou outro transporte comprovadamente seguro, e então demonstrar novamente gates exatos. **A PR #1326 já foi mergeada**: a exigência de 'mesma PR' para correção pós-merge apresenta uma limitação do GitHub; não criar PR substituta silenciosamente. Documentar o impedimento e adotar medida de restabelecimento que não fure a fila nem permita CI vermelha.

## Rastreabilidade e limites

- `master` antes de D: `31a6c43c9d55b654509356d2c26462068039293d`.
- `master` de D: `33ba4a201b293aaa1053a9c6d52f1d9e40cd481b`.
- Frentes que continuam em PRs preexistentes: E 1336; H 1328; G 1334; F 1332; J 1337; K 1318; L 1333. #1242 não foi tocada.
- Sem TEST/Gold, sem ativação operacional, sem inferir certificação FDR de seed42 ou de gate técnico.
- Este registro é governança de incidente e transição, não autorização para alterar o motor V2, fonte IBGE ou corpus congelado.

## Evidência de bloqueio operacional — observação executada em 08/11/2026

O GitHub **recusou** a atualização de `refs/heads/master` ao tentar integrar o commit de reversão `f7b70b1c6d1664e3333126c31d33360fad098cb7` (o objeto existe mas **não está no histórico da master**): `Branch 'refs/heads/master' is protected: Changes must be made through a pull request. 3 of 3 required status checks are expected.` A master permanece em `33ba4a201b293aaa1053a9c6d52f1d9e40cd481b`, vermelha. **Não alterar a proteção, não forçar push e não inventar PR substituta**, pois isso afrontaria o mandato. A correção de segurança no workflow D (transporte por variável de ambiente, sem segredo no argv) foi commitada na **mesma branch da PR original #1326**, commit `d35276e0d78c2572957960dbbb5b2dcb6a1ab4e4`, porém a PR está **merged**, e o push posterior não a reinsere na master. Fica registrada a incompatibilidade operacional entre a proteção da master e a proibição de nova PR fora da lista; requer fluxo autorizado para integrar a reversão/correção sem violar ambas. Enquanto isso, a fila de merges permanece bloqueada, outras frentes podem avançar somente nas PRs preexistentes, e nenhuma validação já verde deve ser reinterpretada como certificação científica.
