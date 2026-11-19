# GOV-V9-BETA-CAMPANHAS-HISTORICAS-CI-20261119 — Escopo de CI e experimentos encerrados

**Autoridade:** Product Owner, 19/11/2026. **Estado:** decisão aprovada; implementação em PR, CI e integração pendentes. **Política:** GOV-DOC-01.

## Contexto e evidências
O sistema permanece em beta e não exige retrocompatibilidade do gerador sintético com versões experimentais anteriores. As campanhas ONE_MF × TF0 de seeds 44–47 e 48–57 foram fisicamente executadas em 05/11/2026 e consolidadas em `V9_TF_ONE_MF_Reprodutibilidade_Seeds42_47_202611.md` e `V9_TF_ONE_MF_Confirmacao_3Sigma_202611.md`. A issue #1275 permanece aberta por pendências de fechamento administrativo; seu estado não equivale a execução pendente. As execuções ocorreram em SHA de branch #1276, conforme relatórios.

## Decisão e justificativa
Preservar os manifests LOCKED e os resultados históricos, sem reescrever hashes ou simular novas evidências. Retirar da CI obrigatória da master os testes que exigem que o gerador **atual** corresponda ao gerador histórico das seeds 44–57. Não manter compilação dupla nem infraestrutura permanente de retrocompatibilidade do gerador beta. Manter os testes históricos no repositório para auditoria sob contexto/commit histórico e manter controles científicos vigentes em PR. Justificativa: remover bloqueio de evolução causado por identidade de fonte histórica sem reduzir a rastreabilidade das campanhas concluídas.

## Impactos, limites e precedência
Esta decisão afeta exclusivamente o escopo obrigatório de CI do gerador sintético beta; **não revoga** o protocolo 60/20/20 aprovado, independência TRAIN/VALIDATION/TEST, TEST one-use, hashes de novas campanhas, segurança fail-closed, requisitos de 100k históricos ou gates de certificação. Não certifica ONE_MF, FDR, HML nem Produção. Para o executor operacional, a obrigação de retrocompatibilidade permanece regida pela decisão de 17/11; qualquer refinamento do marco HML/Produção exige registro específico e revisão do documento original. Prevalece sobre a aplicação indiscriminada de gates de gerador histórico ao código beta vigente, mas não altera os próprios LOCKs.

## Implementação e verificações pendentes
- `.github/workflows/ci.yml`: retirar da etapa obrigatória `impact` os testes históricos `test_v9_tf_frozen_transfer_lock_gate.py` e `test_v9_tf_campaign_sign_test.py`; manter arquivos e outros testes vigentes.
- `.github/workflows/jornada-pr-required-fast.yml`: reforçar testes offline de coortes TEST/VALIDATION.
- Diagnosticar e corrigir separadamente a validação de proveniência física 30k, distinguindo campos novos de manifesto e conteúdo do corpus; **não** alterar fingerprints históricos nem dispensar integridade de novas campanhas.
- Verificar CI de PR, master, branch protection e fechamento administrativo #1275; não declarar concluído antes das evidências.

## Rastreabilidade
Issue #1734; issue #1275; PR #1721; relatórios de seeds 42–47 e 48–57; GOV-V9-TEST-COHORT-20PCT-20261118; GOV-DOC-01.

## Complemento de implementação (19/11/2026)
O PR fast passa a executar também os testes offline do `impact` atual (sem os dois gates históricos aposentados). O workflow manual de frozen transfer falha explicitamente quando disparado na master beta; execução histórica exige selecionar ref original e SHA exato. A decisão de 20% teve o cabeçalho atualizado para refletir o merge de #1721. A referência física 30k vigente ainda requer geração controlada e versionamento; não substituir hash por valor não verificado.

## Evidência de CI do PR (19/11/2026)
Execução `jornada-pr-required-fast` #44534469495, commit `1788bc3e8b7028b643adb1e8f43daef761e8ddaf`: jobs `security-guards`, `unit-runtime` e `deterministic-build` concluídos com **success**. A evidência cobre os checks do PR fast, **não** comprova a suíte completa `jornada-ci` da master nem a execução física 30k. O probe de proveniência 30k permanece bloqueio de integração; o manifesto original `data/reference/v9-synthetic-seed42-30000/manifest.json` continua imutável. Não declarar resolução da issue #1734 sem prova física e revisão das proteções de merge.

## Preparação técnica do registro 30k v2 (19/11/2026)
O gate `Solution/scripts/v9-tf-ref-provenance-gate.py` recebeu a opção `--write-candidate <.../manifest-v2.json>`. Ela exige corpus físico completo, verifica hashes dos três arquivos pelo bootstrap, confere o pin IBGE e valida o candidato contra os dados gerados antes de gravá-lo com modo exclusivo (`x`), sem sobrescrever referências. O candidato nasce com status `CANDIDATE_REQUIRES_INDEPENDENT_PHYSICAL_REVIEW` e commit de origem pendente de verificação; **não é referência aprovada**. A referência histórica v1 permanece intacta. Para promover a v2 será indispensável gerar fisicamente 30k, preencher e verificar commit e proveniência, executar testes negativos, aprovar a referência e atualizar consumidores de forma explícita. Nenhum hash foi inventado, nenhum gate foi relaxado e nenhuma campanha foi executada por este commit.

## Verificação posterior (19/11/2026)
Para o commit `99f919d82ffcf5c1b9ac7be4470aee78accf6cf9`, os workflows PR `jornada-pr-required-fast` (#44543808600) e `v9-experimental-fast-pr` (#44543808602) concluíram com `success`. Isso não demonstra execução física 30k nem validação da master. Endurecimento subsequente do mecanismo v2: `--generator-commit-sha` passou a ser obrigatório com `--write-candidate` e deve conter SHA hexadecimal exato de 40 caracteres; sem esse parâmetro a geração do candidato falha fechada. O SHA fornecido deve ser conferido com a execução física antes da promoção. O novo commit ainda requer CI própria.

## Proteção de regressão da referência 30k (19/11/2026)
Os workflows de PR no commit `2abcebbc8e71e1a4e47622049c1ca0705be22494` terminaram com `success`: fast #44549382003 e experimental #44549382494. Em seguida, foi acrescentado teste estático do contrato de criação do candidato 30k (escrita exclusiva, diretório da referência, SHA exato, validação prévia e estado não aprovado), integrado ao workflow `jornada-pr-required-fast`. Esse teste não substitui teste físico ou testes negativos de execução; novos commits requerem CI. A geração real e a promoção da v2 permanecem pendentes.

## Investigação da recuperação histórica seed42 (19/11/2026)
Após questionamento do PO, a recuperação da referência congelada tem precedência investigativa sobre criar ou promover uma v2. O manifesto V1 permanece em master com hashes dos três arquivos e commit original `945f2e7efbfa217fbf333aa1fb705e15973be9f2`; a restrição `raw_files_in_git=false` impede supor que checkout de branch recupere CSV/gabarito. A API de artefatos confirmou que ainda estão disponíveis o preflight #41577003637 (artefato #13204930194) e a execução física #41577529059 (artefato #13203912431), ambos ligados ao SHA original. Os artefatos têm poucos KB; **não foi comprovado** que incluam os CSVs e o gabarito completos. A execução física histórica registra `FAILURE_BEFORE_135_MEASUREMENTS`, não sucesso científico. Próximo passo: inspecionar conteúdo dos ZIPs e, caso não contenham os bytes brutos, reproduzir o gerador no SHA original em ambiente descartável e exigir coincidência integral dos hashes antes de declarar recuperação. Não alterar o manifesto V1, não certificar sucesso físico e não promover v2 sem nova decisão expressa.
