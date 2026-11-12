# V9 — plano de reutilização física do holdout congelado

Data: 2026-11-12. Estado: proposta técnica, **não implementada**, sem autorização para alterar o protocolo científico.

## Contexto e evidência
Na execução V9 100k TF4 (324 hipóteses), o coordenador chama `EvaluateFrozenHoldoutAsync` por snapshot elegível. O método `V9PhysicalSqlFrozenHoldout.cs` reconstrói assinaturas, tabelas temporárias, observações, assinaturas e pares SQL em cada chamada; em seguida executa `V9FrozenSourceResolveReplay.Replay`, `SummarizeFrozenReplay` e compara resultados ao ajuste. As PRs #1462 e #1463 não eliminam esse trabalho SQL. A execução #43295710416 permanece baseline observacional da versão combinada; as diferenças entre runners não demonstram causalidade.

## Hipótese de otimização
Separar o preparo físico da avaliação de snapshots **somente** quando corpus VALIDATION, blocking passes canônicos, esquema de projeção, limites e política de isolamento forem idênticos. Produzir uma representação imutável dos pares físicos ordenados, com identificadores de observações estáveis e contagem conciliada com SQL. Reutilizar essa representação em múltiplos replays congelados; **não** reutilizar métricas, resultados de Resolve, snapshots, parâmetros FS nem FDR.

## Condições para segurança
1. A chave de reutilização deve incluir fingerprint do corpus VALIDATION, blocking, projeção e limites de recursos; nenhuma aproximação ou cache global.
2. SQL `#v9_cal_*` é restrito à conexão; cache só de pares materializados e validados em memória ou por isolamento explicitamente especificado. Não compartilhar tabelas temporárias entre conexões.
3. Toda hipótese deve executar o scorer/Resolve individual e verificar equivalência dos resultados com o ajuste, incluindo TP/FP, conflitos e não resolvidos.
4. O protocolo `V9_FROZEN_HOLDOUT_FULL_SQL_FS_BLIND_SOURCE_RESOLVE_V1` **inclui** reconstrução SQL no cronômetro. Um replay que use pares em cache **não pode** apresentar a latência como se tivesse seguido esse protocolo. Preservar o replay físico completo para medições de latência exigidas ou criar novo protocolo somente mediante decisão explícita do PO, documentação GOV-DOC-01 e validação.
5. O método público também atende a validações independentes. Não modificar seu contrato ou resultados sem testes de regressão para os consumidores externos.
6. Manter tratamento por hipótese/grupo e limites de memória, sem converter falhas locais em abortos globais.

## Sequência de implementação proposta
A. Identificar chamadas por grupo e consumidores do protocolo P95; levantar testes existentes e métricas por fase.
B. Criar API interna de preparo físico imutável, com prova de equivalência de pares e digest; sem ativar no calibrador.
C. Criar testes para bloqueios iguais/diferentes, duplicatas, divergências, falha SQL, cancelamento, concorrência e equivalência de métricas.
D. Benchmark controlado da preparação versus replay, memória e latência. Somente após evidências, propor integração no coordenador, com preservação explícita da semântica P95.

## Impactos, precedência e rastreabilidade
Não muda TF4, 324 hipóteses, AUTO_0, margens, U mixtures, auditoria, políticas de elegibilidade, snapshots ou seleção. Risco principal: confundir custo do replay com latência ponta a ponta, violando o protocolo de certificação. Precedem as decisões científicas vigentes, GOV-DOC-01 e os contratos de replay congelado. A proposta não substitui decisões oficiais e **não cria decisão vigente**; logo não altera o índice `Solution/docs/Indice_Decisoes_Vigentes.md` até aprovação de decisão. Referências: PRs #1462/#1463, run #43295710416, `Solution/src/Jornada.Linkage.Parameters.Worker/V9PhysicalSqlFrozenHoldout.cs` e `V9NestedCalibrationCoordinator.cs`.
