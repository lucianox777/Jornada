# GOV-V9-DESATIVACAO-PARAMETROS-CIENTIFICOS-SQL-20261117

**Autoridade:** Product Owner, 17/11/2026. **Estado:** decisão aprovada; levantamento de dependências e implementação pendentes. **Política:** GOV-DOC-01.

## Decisão
Concluir a PR #1655 e, na evolução do seletor V9, retirar o uso de propriedades/parâmetros científicos gravados pelo calibrador no banco como fonte de configuração da seleção. A configuração científica do executor V9 deve provir **exclusivamente do pacote/artefato imutável selecionado**, conforme GOV-V9-RUNNER-ARTEFATO-ARQUIVO-FONTE-UNICA-20261117. Não reintroduzir fallback SQL nem sincronização paralela de parâmetros do artefato para o banco.

## Contexto e justificativa
O PO identificou legado em que o calibrador grava propriedades no banco e o seletor as utiliza para decidir. Esse caminho deve deixar de governar a seleção V9, pois a fonte de verdade aprovada é o pacote de configuração. A PR #1655 trata do grafo de candidatos e suas verificações; o grafo é dado operacional de entrada, **não** parâmetro científico de calibração. O objetivo é fechar essa integração sem alterar os parâmetros científicos nem a política de TEST.

## Escopo e sequência segura
1. Inventariar exatamente colunas, tabelas, procedimentos, consultas, gravações do calibrador e leituras do seletor que representem **parâmetros científicos V9**; distinguir metadados de run, dados de blocking, identidades, decisões e referências históricas.
2. Implementar leitura e validação do pacote V9 no início do Runner, com SHA, compatibilidade, congelamento por execução e falha fechada. Preservar REPLAY DT-05 e comportamento legado não-V9 até migração específica.
3. Redirecionar o caminho V9 para o pacote, remover gravações/leituras **de parâmetros científicos V9** no SQL apenas após prova de ausência de dependências e testes E2E/rollback.
4. **Não executar DROP/DELETE ou migração destrutiva** sobre tabelas, colunas ou dados existentes nesta etapa; qualquer remoção física requer inventário, plano de migração, backup/rollback e validação explícita de impacto. Preservar dados e histórico operacionais.
5. Concluir provas do grafo operacional completo por coorte na PR #1655; não confundir testes estruturais com testes SQL físicos nem autorizar publicação.

## Impactos e precedência
Complementa GOV-V9-GRAFO-OPERACIONAL-INTEGRAL-COORTE-20261117 e operacionaliza GOV-V9-RUNNER-ARTEFATO-ARQUIVO-FONTE-UNICA-20261117. Esta última prevalece sobre origem SQL para parâmetros científicos V9. Não altera parâmetros, TFs, modelo vencedor, regras de holdout ou autorização de TRAIN/TEST. Persistência de decisões e publicação são etapas separadas, ainda bloqueadas.

## Rastreabilidade
PO, conversa de 17/11/2026: continuar PR #1655, concluir integração e retirar propriedades científicas SQL anteriormente gravadas pelo calibrador e usadas pelo seletor, substituindo-as pela configuração por pacote. Evidências e testes pendentes; documentação não implica implementação concluída.
