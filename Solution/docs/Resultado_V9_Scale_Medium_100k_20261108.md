# V9 — Resultado físico SCALE MEDIUM 100k (08/11/2026)

**Status do ensaio técnico:** **PASS** — [GitHub Actions #42534643000](https://github.com/lucianox777/Jornada/actions/runs/42534643000), concluída em 08/11/2026 23:36 UTC, **primeira tentativa**, HEAD exato **`8d90306cda1d930291318ca5a4f3e7bf5fc93e4a`** da branch `test/v9-medium-e2e-20261108`. **Artifact:** [13639566469](https://github.com/lucianox777/Jornada/actions/runs/42534643000/artifacts/13639566469), SHA-256 do ZIP fornecido pelo GitHub `945e88102e3f4721a3f35f50bec4554892918ce719bd3830a5e84ff34c72da64`. A versão do documento e o workflow poderão ser integrados em SHA posterior, **mas o ensaio físico pertence exclusivamente ao HEAD acima**. **GOV-DOC-01** e [issue #1345](https://github.com/lucianox777/Jornada/issues/1345).

## Fonte e método, não pressuposições

Arquivos JSON efetivamente descompactados e lidos do artifact: `scale-medium-latest.json`, `scale-medium-validation.json` e `scale-medium-blocking-pass-audit.json`. `gitCommitSha` do primeiro arquivo e `sourceGitCommitSha` do segundo conferem com o HEAD ensaiado. `validation.status=PASS`, `errors=[]`, **`baselineStatus=PENDENTE`**: o teste de evidência comprova coerência e integridade, **não** conformidade a baseline de desempenho homologado para HML/Produção.

**Perfil técnico:** `medium`, seed355, SQL Server descartável LOCAL/DEV, **100.000 pessoas sintéticas** no banco, **20.000 pareadas**, **25.000 pendências sem CPF**, amostra de treinamento 10.000, pool 100.000, blocking/IBGE canônico, Calibrador → conferência → Runner `MODEL_VALIDATION` com **`publish=false`**. A aprovação técnica `VALIDATE/ACTIVATE` ocorre só no sandbox de ensaio; não é autorização de uso operacional ou científico.

**⚠️ Versão do motor deste harness:** `selectedModel.algorithmVersion = FELLEGI_SUNTER_DECISION_EVIDENCE_NEUTRAL_MISSING_V8`; modelo de teste versão 2, ruleset `MODEL_2_BLOCKING_V1`. **Este teste de 100k avalia o caminho operacional V8 de Calibrador + Runner, e NÃO prova o espaço de busca multiparâmetro V9 (V2/V3/TF/misturas) em 100k.** As campanhas V9 seed42 30k e J PRIMARY/SENSITIVITY são evidências diferentes, não substituíveis.

## Resultados de identidade — lote de 25.000 pendências

| Métrica observada | Resultado |
| --- | ---: |
| Elegíveis / avaliadas | **25.000 / 25.000** |
| Resoluções corretas | **21.927** |
| Resoluções incorretas (FP observado) | **0** |
| Conflitos | **31** |
| Não resolvidas | **3.042** |
| Reconciliação completa | **21.927 + 31 + 3.042 = 25.000** |
| Sensibilidade por pendência (`resolvedCorrect / totalScale`) | **87,708%** |
| PPV observado entre resolvidas | **100%** (21.927 de 21.927) |
| Fora do universo sintético de nascimento, por desenho | **2.500**, todos não resolvidos |
| Dentro do universo: resolvidas | **21.927 de 22.500 = 97,4533%** |
| Dentro do universo: conflitos / não resolvidas | **31 / 542** |
| Conflitos com verdade entre dois melhores | **31/31** |
| Registros sem candidato no bloco de data de nascimento | **2.500** |

`zeroFpUpper95PctRuleOfThree=0,0137%` consta do artifact como **limite diagnóstico aproximado sob modelo de ensaio**, não FDR cientificamente certificada. A estrutura dos pares pode induzir dependência; os dados são sintéticos e não representativos. **Não inferir FDR real, validade estatística independente, aceitabilidade de produção ou condição de ativação.**

## Blocking, duração e coordenação

- Auditoria read-only de **1.000 observações rotuladas**: a verdade estava no conjunto de candidatos em **900/1.000 (90%)**; as outras 100 tinham zero candidatos; **18.350 pares candidatos**, média de **18,35 candidatos por observação**, p95 de **100** e máximo de **128**. O ruleset testado tem **somente o passe P088** (`birth_year` + `mother_name_last`), com `ruleSetPassCount=1`. Esse recall de blocking é **amostral**, não recall operacional final.
- Tempo de geração de parâmetros: **475.295 ms ≈ 7min55s**. Tempo do Runner para 25.000 avaliações: **24.009 ms ≈ 24,0s**, equivalente a ~1.041 avaliações/s **apenas na etapa Runner**. São **tempos de fases internas**; não representam a latência HTTP ponta a ponta nem p95 de atendimento.
- Probes de coordenação exclusiva e do corpus confirmaram contenção esperada, com esperas de **2.834ms** e **3.002ms**, respectivamente.
- Contagens e métricas derivadas foram reconciliadas com campos brutos do artifact, sem números estimados. `runner.status=CONCLUIDO_SEM_PUBLICACAO`; o workflow incluiu limpeza de SQL descartável após execução.

## Interpretação, limitação e encaminhamento

**PASS técnico restrito:** código e schema SQL/Runner operam sobre base sintética de 100k e processam 25k pendências, sem falsas vinculações observadas nessa massa, com falhas/pendências explícitas e controles de QA. Entretanto, **2.500 pendências sem candidatos por construção da massa** e **542 sem resolução no universo** permanecem, e o blocking do modelo V8 testado recuperou a verdade para **90% na amostra de auditoria**. Esses são resultados a avaliar, não justificativa para alterar thresholds ou liberar seleção.

**NÃO COBERTO neste ensaio:** (1) **100k pessoas entrando efetivamente via HTTP → Bronze → Processor → Silver → Gold → Serving** (issue [#1348](https://github.com/lucianox777/Jornada/issues/1348)); (2) **100k com todas as opções do calibrador multiparâmetro V9**, uma campanha científica própria com orçamento/gates aprovados; (3) benchmark HML/Produção, dados reais, TEST/Gold científico, estimativa FDR independente, latência API p95 e autorização DT-15. Não usar o passe de QA do artifact como equivalência desses critérios.

**Destino reprodutível do workflow após a prova de stage:** substituir o gatilho automático `push` da branch de teste por **`workflow_dispatch` exclusivamente sob `refs/heads/master`**, preservando seleção operacional humana, SHA exato, ACTIONs fixadas, SQL efêmero, segredos não arquivados e nenhuma execução automática de 100k por atualização de documentação. A política do 30k técnico da master permanece exclusivamente `push`; este **é outro workflow de escala**, não ciência, promoção ou seleção. A integração por PR só poderá ser declarada concluída após seus checks rápidos e a CI pós-merge própria.

## Precedência e rastreabilidade

Sujeito a [protocolo GOV-V9-E2E-MEDIUM-20261108](Decisao_V9_E2E_Medium_20261108.md), [matriz a–m](Decisao_V9_Teste_Geral_E2E_Pos_Merge_20261108.md), [GOV-DOC-01](Indice_Decisoes_Vigentes.md), [issue de validação #1345](https://github.com/lucianox777/Jornada/issues/1345) e [issue de E2E volumétrico HTTP #1348](https://github.com/lucianox777/Jornada/issues/1348). Documentar futuras decisões de seleção científica separadamente, não por sucesso desse teste.
