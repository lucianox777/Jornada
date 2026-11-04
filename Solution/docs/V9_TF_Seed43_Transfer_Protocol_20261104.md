# V9-TF-002 — transferência física congelada seed 42 → seed 43

**Estado:** protocolo e preflight implementados; **campanha SQL ainda não executada**.
Escopo exclusivamente científico, sem publicação, DT-15, acesso a TEST para seleção ou alteração do histórico TF-001.

## Hipótese prévia e desenho

Objetivo primário: avaliar se o ganho de recall da opção
TF_ONE_MF_W1_M0_A0.5_T0.95 frente ao controle T0.95_TF0,
medido no seed 42, se mantém num corpus **novo** gerado com seed 43,
**sem** otimizar blocking, threshold, TF, pesos, margens ou snapshots.

- Transferência: FROZEN_NO_REFIT. Configurações fixadas pelo estudo seed42
  e snapshots C# imutáveis verificados por SHA-256.
- Corpus: gerador C# original, 30.000 identidades-base,
  DEMOGRAPHIC_PRIMARY_V1, perfil clean, cap 4;
  seed 43 **não** substitui o seed 42 nem seus hashes históricos.
- Referência: mesmo código/versão/hashes IBGE publicados, mas
  PinnedIbgeBootstrap do corpus novo, nunca copiado do seed 42.
- TRAIN e VALIDATION segregados por identidade-base; TEST selado e
  não inspecionado para ajustes. O preflight confere apenas os identificadores
  e os nomes das partições, nunca as etiquetas TEST nem outcomes físicos.
- Margens: 0,5 / 2 / 4. Não alterar o protocolo depois de ver resultados.
- FDR operacional alvo: **1%**, inalterado.
- Campanha física posterior: usar **JornadaHarness descartável novo**
  e configuração explícita. É proibido reutilizar banco/pin sintético
  seed42, alterar JornadaLocal ou rodar workload seed42 por engano.

## Contrato de execução

O responsável deverá criar um plano JSON **antes de inspecionar os resultados
do corpus seed43**, com exatamente os campos:

- schema_version: V9_TF_SEED43_FROZEN_TRANSFER_PREFLIGHT_V1
- source_seed: 42; target_seed: 43; transfer_mode: FROZEN_NO_REFIT
- source_commit_sha: SHA de 40 posições do código do estudo original
- source_study_sha256: hash SHA-256 dos bytes do study.json original
- source_run_id: ID positivo do run físico original
- frozen_models: dois objetos com option_id e snapshot_sha256,
  um para T0.95_TF0 e outro para TF_ONE_MF_W1_M0_A0.5_T0.95;
  cada snapshot deve existir no diretório como SHA256.fitted.json
- target_fdr_max: 0.01; test_sealed: true; margins: [0.5, 2, 4]

Os hashes reais **não** são pré-fabricados neste repositório.
A ausência de artefato original faz o preflight **falhar**.

### Auditoria de preflight (somente leitura)

Executar da raiz Solution depois de gerar o corpus e recuperar os
artefatos originais imutáveis:

~~~bash
python3 scripts/v9-tf-seed43-frozen-transfer-preflight.py \
  --plan /evidence/seed43/frozen-transfer-plan.json \
  --corpus-root /evidence/seed43/people-30k \
  --seed42-registry data/reference/v9-synthetic-seed42-30000/manifest.json \
  --seed43-pin /evidence/seed43/pinned-ibge.json \
  --frozen-snapshots /evidence/seed42/snapshots \
  --source-study /evidence/seed42/study.json \
  --output /evidence/seed43/preflight.json
~~~

A saída preflight.json contém **só hashes, contagens por partição,
identificadores de configurações e marcadores de status**; não inclui
nomes, CSV, truth, resultados TEST, autorização ou certificação.
A ferramenta rejeita saída já existente, hashes divergentes,
referência trocada, dados seed42 reutilizados, snapshots alterados e
planos que reajustem parâmetros.

## Métricas físicas pendentes

Executar depois do preflight em SQL isolado, com os mesmos contratos de
V9AdaptiveCalibrationCoordinator e Resolve físico, sem duplicar pipeline.
Conservar seed42 como referência, e registrar por braço/margem: TP/FP/FN,
recall/FDR e bound, conflitos e pendências, cobertura/fragmentação, P95
medido, SHA de snapshot/corpus/plano e diferença **pareada por
identidade-base** entre ONE_MF e TF0.

Critério **descritivo** de estabilidade: ganho de recall positivo sem
aumento observado de FP. Critério **inferencial** e poder devem ser
pré-declarados antes do ensaio, contabilizando competição entre
identidades, multiplicidade e seleção adaptativa. Caso as hipóteses de
IID ou amostragem representativa não estejam demonstradas, a conclusão
continua NO_ELIGIBLE_CONFIGURATION, sem certificação, mesmo com zero FP.

O TEST de seed43 mencionado no histórico já foi usado em outro estudo
sintético e **não pode ser tratado automaticamente como novo holdout
confirmatório lacrado**. Eventual confirmação requer amostra nova,
protocolo e orçamento alfa próprios.

Referências: issue #1228, V9StatisticalSamplePlan,
V9IdentityClusterFdr, V9RecordedClusterFdrValidation e
V9_Revisao_Independencia_Multiplicidade_Amostra_20261019.md.

**Fronteira da entrega:** esta PR oferece protocolo e verificação
programática da proveniência para a campanha. Não implementa ainda o
executor físico seed43, não atesta resultados nem dispara campanha 30k.
