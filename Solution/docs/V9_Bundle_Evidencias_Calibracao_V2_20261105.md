# Calibrador V9 — bundle de evidências de calibração V2 (05/11/2026)

**Três componentes funcionais:** plano de exploração e tolerâncias, resultados efetivos e relatório estatístico/gerencial. O fitted snapshot é um anexo de bytes originais ao resultado; um manifesto SHA-256 vincula os três, o anexo e a execução.

Este é um **bundle de evidências da calibração**, distinto do bundle operacional do modelo (Base+Blocking+FS) e do bundle da instalação. Criar ou verificar o bundle não calibra, não usa TEST, não abre SQL/ref e não executa DT-15.

## V2 original congelada

Fonte: Solution/data/reference/v9-frozen-artifacts/references.json (master).

| Elemento | SHA-256 / identidade |
| --- | --- |
| Plano original plan-tf.json | b6f5f5197074f7ecedf004745f763df3b66a0496f9355589242062f9f1d33ee8 |
| Resultado original study-tf/study.json | 7499ee635ecb4999106dbee22a5993f9f16b88d75c725f99490f2fccd39cba2b |
| Fitted ONE_MF, margem 0,5 | 8bdf08affbbcd88550cb055e42a063c1bfff1ffb968b55a3707be6179475d29f |
| Workflow / commit exato | 41900357031 / 935f704edd2bceb6a125ecfe532ad5b71f9c89b9 |

Não comparar estudo.PlanSha256 (identidade de plano normalizado pelo C#) com o SHA dos bytes originais do arquivo de plano. Também não equiparar hashes de corpus origem e fingerprint de frame SQL.

## Operação offline, sem novo ensaio

Depois de baixar/descompactar o artefato original, contendo plan-tf.json e study-tf/study.json, na raiz do repositório:

    python Solution/scripts/v9-calibration-evidence-bundle.py build --source-dir /artefato-original --references Solution/data/reference/v9-frozen-artifacts/references.json --model-key V2_ONE_MF_original --out-dir /bundle-v2

    python Solution/scripts/v9-calibration-evidence-bundle.py verify --bundle-dir /bundle-v2

    python Solution/scripts/v9-calibration-evidence-bundle.py compare --left /bundle-v2 --right /bundle-candidato

Saída do build: plan.json (cópia byte a byte), result.json (cópia byte a byte), model.fitted.json (anexo byte a byte), report.json (relatório novo reconstruído do estudo) e manifest.json (vínculos SHA). O próprio manifesto é identificado pelo SHA-256 de seus bytes, evitando autorreferência circular. O bundle nunca sobrescreve outro.

**O relatório não é um dossiê DT-15 original assinado.** O report.json é identificado explicitamente como reconstrução somente-leitura, contém contagens/estatísticas publicadas, lacunas e pressupostos, sem inventar certificação, P95 ou autorização. Um relatório institucional posterior deve ser versionado separadamente.

## Auditoria de atributos implementada

- Plano: fingerprint, discovery, opções, TF por quatro slots, alpha, threshold, margens, blocking, histórico de multiplicidade, políticas de seleção, critérios de risco, custo/execução e pin IBGE/gerador.
- Resultado: versão, hashes TRAIN/VALIDATION, seleção TEST-blind, pareamento integral opção/margem, TP/FP/eligíveis e coerência recall/FDR, recibo do fit selecionado, cegamento do agrupamento.
- Fitted: schema e versões, hashes de corpus/grupos/fit/projeção, blocking idêntico ao plano, upper/margem, 81 células M/U únicas normalizadas, parâmetros FS, TF ativo quando declarado.
- Relatório: identidade e fonte SHA, métricas observadas, limites estatísticos presentes, dependência por identidade-base não validada, ausência de atestado de P95, certificação e DT-15.

A seção AttributeAudit registra campos opcionais do contrato C# presentes ou dependentes de defaults. **Não preencher defaults nem reescrever resultados V2.** O verificador de schemas não prova que todas as constantes e defaults do runtime estejam externalizados; exige auditoria independente do código completo.

## Critério de regressão

Comparações entre bundles só apresentam deltas agregados quando corpus de entrada, partições por identidade, blocking e fingerprint físico dos candidatos coincidirem. A comparação não comprova causalidade ou significância; diferenças de código e plano são exibidas. Teste pareado por identidade/fonte e protocolo predeclarado são gates distintos.

Falha de SHA, arquivo ausente, JSON duplicado, TF incompleta, M/U inconsistente, divergência de relatório/estudo/fit, symlink de componente ou arquivo extra provoca falha fechada.

## Limite desta etapa

Esta PR oferece ferramenta e testes de fixtures locais. A execução E2E contra os bytes originais de ~528 MB do run 41900357031 ainda depende de disponibilizar o artefato original no executor. Até lá, a presença de campos em V2 não está integralmente atestada e nenhum resultado de calibração deve ser alterado ou promovido.
