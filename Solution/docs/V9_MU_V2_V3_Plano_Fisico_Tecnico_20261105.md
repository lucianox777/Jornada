# V9 — próximo ensaio técnico M/U, V2 × V3 (TRAIN seed42)

## Estado e objetivo

A PR #1285 integrou o contrato `MuEstimationMethods`, mas o gate 30k do PR #1285 executava **45 opções TF**, não a comparação `DEFAULT` versus `CLASSWISE_SUPPORT_BACKOFF`. A existência do código, de um JSON ilustrativo e da CI verde **não é evidência física da comparação M/U**. Este protocolo fornece o **plano realmente executável** que faltava, sem consumir nova seed.

O script `scripts/v9-mu-historical-plan.py` transforma um plano C# aninhado já preparado pelo produtor existente em um novo plano distinto e imutável. Somente o método de estimação M/U varia nas alternativas explicitamente declaradas:

- **DEFAULT (V2):** estimador conjunto tradicional em todas as 81 células; sem `JointSupportPolicy`.
- **CLASSWISE_SUPPORT_BACKOFF (V3):** **cobertura 0,99; mínimo M=5; mínimo U=5; preservar cauda=true**, correspondente à regra `RETAIN_CLASS_JOINT_WHEN_COUNT_AT_LEAST_K_V2`.
- **TF e demais dimensões comuns:** `TF_ONE_MF_W1_M0_A0.5_T0.95` (mãe/primeiro nome, peso 1, modo simétrico), limiar 0,95, alfa 0,5, margem 0,5. Os três passes históricos são incluídos sem retuning, além do controle completo de 19 passes.
- Os valores M/U são lidos de `data/reference/v9-mu-estimation-methods-20261105.json` e comparados com `historical-v3-support-policy.json`; não há busca de intervalo ou novos valores.
- Duas políticas × uma opção TF × uma margem × dois conjuntos iniciais de blocking = **quatro comparações iniciais**. Os **três passes históricos** e o controle de **19 passes** são preservados com suas listas exatas de campos do catálogo congelado. Orçamento do **teste técnico inicial: quatro configurações em um único batch, sem refinamento** de threshold, margem ou blocking. A funcionalidade adaptativa completa permanece disponível para planos científicos separados e devidamente solicitados. Orçamento não é resultado medido.

## Preparação (sem executar estudo científico independente)

Com o corpus conhecido seed42 clean e o pin IBGE verificado do mesmo corpus, criar um plano base novo (não usar output histórico):

```bash
cd Solution
python3 scripts/v9-nested-calibration-study.py \
  .local/v9-tf-physical/people-30k/observacoes.csv \
  .local/mu-nested-tf-source.json --tf-search \
  --pinned-ibge .local/v9-tf-physical/pinned-ibge.json
python3 scripts/v9-mu-historical-plan.py \
  .local/mu-nested-tf-source.json .local/mu-historical-v2-v3.json
```

O `v9-mu-historical-plan.py` apenas **prepara** o plano, valida seed42, pin SHA, opção ONE_MF exata e limites; não abre SQL nem consome TEST/VALIDATION.

Para ensaio técnico físico, em um banco `JornadaHarness` **descartável** e novo, explicitamente provisionado com o pin IBGE fixado, executar o Calibrador C# único:

```bash
dotnet run --project src/Jornada.Linkage.Parameters.Worker -- \
  --v9-calibrate .local/mu-historical-v2-v3.json \
  .local/v9-tf-physical/people-30k/observacoes.csv \
  .local/mu-historical-results-new
```

Exigir diretório de resultados **novo**. O Calibrador deve produzir `mu-statistical-analysis.json` e `mu-statistical-analysis.md` com valores por configuração, recall/FDR observados, intervalos condicionais, suporte das 81 células e razões de inelegibilidade. Não inferir resultados de runs TF existentes; quando executado, arquivar run ID, SHA do Git, SHA do plano, SHA do corpus e dos outputs.

**Atenção:** esse ensaio permanece um gate técnico/exploratório **não confirmatório**, no seed42 já conhecido. NÃO disparar novas seeds, Gold ou VALIDATION confirmatória automaticamente. A confirmação de qualquer modelo depende de protocolo LOCKED específico e autorização de uso operacional DT-15 separada.

## Etapas ainda pendentes

Este commit **não realizou** execução SQL nem produziu resultados V2×V3 novos; não pode declarar vencedor. Próxima frente: instrumentar e executar o ensaio físico no corpus histórico conhecido, verificar relatório com testes independentes de reconciliação e comparar os dois métodos no mesmo frame SQL por identidade-base.

## Gate técnico automatizado — somente corpus histórico

O workflow `.github/workflows/v9-mu-seed42-technical-regression.yml` roda em PRs do próprio repositório quando o plano/estimadores M/U mudam. **Não é campanha confirmatória**: usa apenas a seed42 conhecida, SQL Server descartável e o mesmo pipeline de referência IBGE autenticada por SHA. Não expõe TEST nem aciona DT-15.

Em vez de tratar o sucesso do 30k TF de 45 opções como prova M/U, o workflow **executa especificamente** as quatro combinações iniciais C# (`DEFAULT` e `CLASSWISE_SUPPORT_BACKOFF` × blocking histórico de 3 passes e controle de 19 passes), conferindo as 81 células observadas e seus resultados por método. O job falha se qualquer um dos métodos não tiver medição física, se as políticas diferirem do registro V3 ou se faltar rastreabilidade. O output publica somente hashes, planos, relatório estatístico agregado e eventual `interrupted.json`, sem CSV nominal ou gabarito.

Por se tratar de regressão técnica, o plano fixa `MaximumBatches=1`, orçamento total `4` e **não** busca outros thresholds/alphas/margens. Um estudo adaptativo completo é outra campanha, solicitada e controlada separadamente. As métricas não são afirmadas antes da execução da Action nem são inferência independente.
