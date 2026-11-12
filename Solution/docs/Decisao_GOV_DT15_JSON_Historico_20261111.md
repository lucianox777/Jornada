# GOV-DT15-JSON-HIST-20261111 — Comparação histórica ATIVO × RASCUNHO

**Decisão aprovada pelo Product Owner em 11/11/2026.** Política GOV-DOC-01. Precedência: detalha a DT-15 sem substituir suas guardas humanas, estatísticas ou de publicação.

## Contexto e justificativa
A camada Gold cresce por ingestões periódicas. O ATIVO e o último RASCUNHO podem ter sido calibrados em datas e volumes distintos. O operador Master precisa inspecionar as configurações e resultados **originais**, sem refazer a calibração antiga nem impor normalização ou avaliação pareada para a apresentação histórica. A página independe do gatilho manual ou agendado.

## Contrato funcional aprovado
1. Selecionar o modelo ATIVO efetivo e o último RASCUNHO persistido; apresentar lado a lado o **nome original do arquivo JSON** e o **SHA-256 correspondente ao conteúdo original íntegro do próprio arquivo** (identificar explicitamente representação/bytes), sem substituir por SHA do corpus, da referência IBGE, do fit portátil ou do dossiê.
2. Recuperar os JSONs originais associados aos modelos, sem reserializar, modificar, recalcular ou normalizar resultados históricos. Exibir datas, versões, volume Gold original, parâmetros FS/m/u/TF, blocking, thresholds, margens e indicadores efetivamente disponíveis: recall, FDR observado e limite, FP, FN, abstenções, P95, incerteza e elegibilidade. Campos ausentes devem ser marcados como indisponíveis, nunca preenchidos por suposição.
3. Indicadores percentuais são apresentados como registrados; diferenças descritivas podem ser exibidas com unidade explícita. FP/FN absolutos preservam denominadores e contexto de cada execução. A comparação histórica **não prova superioridade causal** entre populações distintas.
4. Recomendação técnica de ativar/manter/não recomendar somente a partir da **mesma política vigente de elegibilidade/seleção do calibrador V9**, sem novo algoritmo, critérios ou reclassificação implícita. Se os artefatos e a aplicação da política entre campanhas não estiverem disponíveis, declarar recomendação indisponível, sem inventá-la.
5. O Master decide; nenhuma consulta ou recomendação promove modelos automaticamente. Manter identidade, autorização, ledger, VALIDATE e ACTIVATE de DT-15 e restrição DEV vigente.

## Verificação técnica e implementação incremental
- `ref.calibracao_inicial_versao` contém `resultados_json` e `resultados_sha256`. A procedure calcula o hash dos bytes SQL `CONVERT(VARBINARY(MAX), NVARCHAR)`; **não equivale necessariamente ao SHA-256 dos bytes UTF-8 do arquivo original**. O inventário V9 distingue explicitamente o digest SQL do fit portátil.
- A API DEV `ModelGovernanceReadOnlyService` retorna histórico agregado FP/FN, amostras, data e SHA de **referência**; ainda não expõe o nome e SHA do arquivo JSON original nem todos os indicadores exigidos. Não confundir `ReferenceSha256` com SHA do JSON.
- A primeira entrega nesta branch adiciona comparação histórica somente dos dados efetivamente disponíveis na API, mantendo a comparação de blocking existente e avisos explícitos de lacunas. **Não constitui conclusão integral do requisito**.
- Próximas etapas: identificar o artefato original por modelo, persistir nome e hash dos bytes originais com integridade verificável, vincular métricas completas sem alteração de valores, integrar recomendação da política V9 e cobrir por testes de integridade, permissão, ausência de dados e regressão. Nenhum gate HML/Produção é liberado por esta decisão.

## Rastreabilidade
Origem: deliberação do PO de 11/11/2026 sobre DT-15; implementação parcial branch `feat/dt15-comparacao-json-historica-20261111`. Referências: `DT15_Governanca_Decisao_Modelo.md`, `V9_Consolidacao_Pareto_Execucao_20261019.md`, `20261009_Ref_Calibracao_Inicial_Sintetica.sql`, `ModelGovernanceReadOnlyService.cs`.

## Emenda aprovada pelo PO — SHA-256 como sufixo do arquivo JSON (11/11/2026)

**Decisão vigente:** novos arquivos JSON de resultado da calibração V9 devem adotar o padrão `calibracao_v9_<data-hora>_<sha256-completo>.json`, com os 64 caracteres hexadecimais do SHA-256 dos **bytes finais originais** do arquivo, calculado antes da nomeação. O conteúdo do JSON não é alterado para inserir seu próprio hash. A identificação do modelo mantém nome original e digest como metadados imutáveis vinculados ao artefato, com conferência do conteúdo na leitura e antes de qualquer ativação. Não basta renomear arquivo adulterado: a referência previamente aprovada deve permanecer protegida.

**Compatibilidade e precedência:** preservar nomes e bytes de arquivos históricos; registrar hash como metadado sem renomeação retroativa. O padrão é aplicado às novas saídas após auditoria de produtores/consumidores. Arquivos que já usam convenção de hash integral, como `<sha>.fitted.json`, não devem ser renomeados automaticamente sem verificar o contrato de consumo. Distinguir hash do arquivo UTF-8 de hash SQL NVARCHAR, hash do corpus, fit e referência. DT-15 e gates existentes permanecem prevalentes.

**Impactos e testes obrigatórios:** verificar todas as rotas de escrita, leitura, upload e publicação; conferir nome, digest e bytes; detectar adulteração e associação obsoleta; assegurar ausência de alteração em parâmetros, métricas, política V9 e seleção. A migração deve ser fail-closed, sem promoção automática. Implementação integral permanece pendente até auditoria de consumidores e CI.


## Auditoria de compatibilidade do calibrador V9 — 11/11/2026

A inspeção de `V9NestedCalibrationCli.cs` confirmou um contrato **já implementado**: o envelope de seleção é escrito em arquivo temporário, tem SHA-256 calculado dos bytes efetivamente gravados e é movido para `<sha256>.selection.json`. O consumidor `--v9-validate-finalists` **exige literalmente** que o nome seja `<sha256>.selection.json` e rejeita divergência. Assim, renomear esse envelope para `calibracao_v9_<data>_<sha>.json` quebraria o consumo existente. O arquivo `<sha>.fitted.json` também já tem convenção própria de conteúdo endereçado.

**Decisão de implementação compatível:** o novo padrão aprovado aplica-se aos **JSONs finais de resultado de calibração destinados à governança**, não substitui nomes técnicos já fixados em contratos de entrada/saída (selection/fitted). Para esses artefatos, preservar nome e digest existentes e registrar a referência no catálogo de governança. A integração do novo nome exige identificar o ponto exato do resultado final, sem criar cópia ambígua do envelope e sem alterar o fluxo VALIDATION. O helper `V9CalibrationJsonArtifactName` está disponível, mas ainda não conectado ao produtor final. Esta auditoria não certifica execução física nem testes.


## Diretriz de manutenção durante o desenvolvimento — 11/11/2026

O PO ressaltou que o produto ainda está em desenvolvimento e que será necessária uma **revisão e limpeza futura de documentação e artefatos legados**. Isso não autoriza exclusão imediata nem substituição silenciosa de decisões. Adotar desde já inventário de obsolescência, distinguindo: (a) decisões vigentes, (b) decisões superadas com referência à sucessora, (c) evidências históricas que devem ser preservadas, (d) documentos operacionais duplicados e (e) artefatos temporários descartáveis. Antes de qualquer remoção, verificar referências cruzadas, contratos e requisitos de auditoria; submeter a lista de exclusões irreversíveis ao PO para aprovação específica. O índice de decisões vigentes deve apontar somente a norma atual e manter trilha de precedência. Esta diretriz não muda a ciência V9 nem o contrato DT-15.
