# GOV-V9-TF4-3-JOBS-360-TEST-RESERVADO-20261113

**Autoridade:** Product Owner, 13/11/2026 (America/Sao_Paulo). **Estado:** APROVADA PELO PO; integrada por squash na master via PR #1476 (SHA 56d44f90eb069a76a7cf98a4398954f64366d54b); CI obrigatória aprovada; teste físico da nova divisão pendente. **Política aplicável:** GOV-DOC-01.

## Contexto e justificativa

Na campanha manual de 100 mil registros sintéticos / **324 hipóteses TF4** (81 opções de quatro unidades × 2 misturas U × 2 margens), o teto GitHub Actions de **360 minutos aplica-se ao job, e não ao workflow completo**. O desenho aprovado anteriormente separou TRAIN e VALIDATION: o job TRAIN possui 360 min totais (etapa física 315 min), enquanto VALIDATION dispunha apenas de 120 min no job e 40 min na etapa física. O PO aprovou agora **uma arquitetura de três jobs em sequência, cada um com reserva independente de até 360 min, sem antecipar o acesso a TEST**.

O objetivo é evitar desperdício das seis horas do TRAIN por uma fase de validação que ocorre depois e manter explícita a fronteira científica de TEST. O resultado histórico da campanha 324/324 não prova que todos os gates científicos passaram; a nova configuração não muda esse fato nem a execução em andamento sob SHA anterior.

## Decisão implementável e topologia

| Job | Ordem e condição | Limite total do job | Limite da etapa científica | Uso permitido |
| --- | --- | ---: | ---: | --- |
| `physical-100k` | início manual, somente proprietário após security preflight e SHA exato da master | **360 min** | **315 min** para física de 324 hipóteses | TRAIN, auditoria fail-closed, congelamento e artefato |
| `validation-100k` | `needs: physical-100k` + TRAIN success e finalistas congelados verificados | **360 min** | **330 min** para gerar holdout e validar os finalistas | VALIDATION independente, SQL descartável próprio, sem refit |
| `test-100k-locked` | dependência explícita de `validation-100k` **mas `if: false` constante** | **360 min declarados, não consumidos enquanto bloqueado** | nenhuma | **RESERVA ESTRUTURAL; TEST NÃO EXECUTADO** |

A margem de **30 minutos** entre o limite da etapa VALIDATION e o total de 360 min fica reservada para checkout/build, transferência do envelope (>2 GiB), admissão do SQL e referência IBGE congelada, coleta de artefatos e limpeza. Os passos preparatórios também consomem tempo do job; se ultrapassarem a margem, o timeout geral ainda poderá encerrar a execução. O limite da etapa física TRAIN permanece 315 min, e o indicador de ETA permanece sincronizado com esse valor. Não elevar o step físico TRAIN para 360 min: isso eliminaria margem para auditoria/transferência e aumentaria o risco de perder evidências.

## TEST permanece hard-locked por desenho (fail-closed)

A presença visual de um terceiro job `test-100k-locked` **não representa autorização, dataset disponível, medição, certificação ou ativação**. O job tem condição estática `if: ${{ false }}`, nenhuma etapa de checkout, download, corpus ou avaliação de TEST; sua única etapa, caso o bloqueio seja removido inadvertidamente, **termina em erro explícito** `V9_TEST_LOCKED`. A entrada histórica `test_required=true` **continua provocando falha fechada logo na admissão do TRAIN** `TEST_REQUIRED_UNSUPPORTED`; não pode ser usada para habilitar TEST.

A eventual implementação efetiva de TEST exige **nova aprovação expressa e novo registro GOV-DOC-01** com LOCK científico pré-registrado, parâmetros/dados congelados, corpus TEST lacrado e fonte autorizada, identidade SHA e integridade, proteção global anti-reexposição, definição da avaliação física, auditoria independente e política de certificados/ativação. Não ligar TEST apenas trocando `false` por `true`, não gerar TEST a partir do holdout, não reutilizar seed nem burlar as portas existentes.

## Preservações, riscos e precedência

Permanecem inalterados: as 324 hipóteses, opções TF PF/PL/MF/ML, M/U, FS, ranking, margens, limites CP/FDR 1%, P95 60 s, custo/fanout, seed42 do TRAIN, seed/isolamento de VALIDATION, reuso de IBGE congelado e proveniência 30k, SQL descartável por job, SHA imutável, `workflow_dispatch` owner-only, security preflight, artefatos endereçados por SHA, validação do manifesto e `run_attempt=1`. TEST não é consultado e `certified=false`, `activated=false` permanecem.

**Riscos:** cada job tem seu próprio timeout, mas carga de dados e upload/download de artefatos grandes ainda podem falhar independentemente. O job TEST bloqueado tende a aparecer como SKIPPED, o que **não é TEST PASS**; um workflow com TRAIN/VALIDATION bem-sucedidos continua sem certificação científica do TEST. Falhas de VALIDATION não autorizam rerun do mesmo holdout. A nova política não retroage a runs em andamento sob SHA anterior.

**Precedência explícita:** esta decisão **substitui somente** os limites antigos **120 min do job VALIDATION e 40 min da etapa VALIDATION**, estabelecidos na [GOV-V9-TF4-VALIDATION-JOB-SEPARADO-20261113](Decisao_GOV_V9_TF4_324_Validation_Job_Separado_20261113.md), passando a **360/330 min**, e formaliza a reserva inerte de TEST. Preserva a separação, o limite 360/315 de TRAIN e demais controles dessa decisão; preserva também a [GOV-V9-100K-IBGE-CONGELADO-SEM-REGERAR-30K-20261113](Decisao_GOV_V9_100K_IBGE_Congelado_Sem_Regenerar_30K_20261113.md), a [GOV-V9-V3-TF4-324-20261111](Decisao_GOV_V9_V3_TF4_324_100k_20261111.md) e as políticas LOCK/TEST. Não revoga o histórico de execuções anteriores.

**Gates e rastreabilidade:** branch dedicada, testes automatizados exigindo exatamente os três limites de job, step 315/330, `if: false`, dependências, SHA master e `test_required=true` fail-closed; CI `security-guards`, `unit-runtime`, `deterministic-build` e suíte V9; squash merge; SHA final verificado na master. Teste físico dos novos 360 min de VALIDATION **ainda pendente de campanha manual**, que requer preflight real do proprietário. [Workflow](../../.github/workflows/v9-v3-four-name-100k.yml) · [Índice de decisões vigentes](Indice_Decisoes_Vigentes.md).

## Recibo de integração da decisão (13/11/2026)

A [PR #1476](https://github.com/lucianox777/Jornada/pull/1476) foi integrada por squash no SHA `56d44f90eb069a76a7cf98a4398954f64366d54b`. A [CI required-fast #43465617461](https://github.com/lucianox777/Jornada/actions/runs/43465617461) aprovou os jobs `security-guards`, `unit-runtime` (incluindo teste de contrato dos 3 jobs e hard-lock TEST) e `deterministic-build`. Verificado no HEAD da master: TRAIN 360/315, VALIDATION 360/330, TEST reservado 360 com `if: ${{ false }}`, necessidade `validation-100k` e rejeição de `test_required=true` antes de treino. Não houve execução física sob este SHA; nenhuma evidência de TEST foi criada.
