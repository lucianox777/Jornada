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