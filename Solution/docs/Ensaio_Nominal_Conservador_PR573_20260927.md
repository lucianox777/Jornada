# PR #573 — resultado nominal sintético e limite de implantação

**Data:** 27/09/2026. **Fonte:** artefato `unit-test-evidence` da [execução #36356610409](https://github.com/lucianox777/Jornada/actions/runs/36356610409), `unit.trx`, teste `ConservativeLinkageCounterSyntheticTests`. **Estado:** diagnóstico exploratório; não é gate de ativação nem avaliação do motor operacional.

## Resultado reproduzido

O teste gera 4.096 pares rotulados como positivos (mesma pessoa com perturbação sintética de um token) e 4.096 negativos difíceis (pessoas distintas compartilhando prenome ou sobrenome). O proxy automático é `PtBrContentTokenGuardV2` em `EXACT/HIGH`; o proxy de recuperação para conferência humana é `WholeNameJaroWinklerV1` em `EXACT/HIGH/MEDIUM`.

| Contagem ou métrica | Resultado | Denominador |
|---|---:|---|
| Verdadeiros positivos do proxy automático | 3.944 | 4.096 positivos |
| Falsos positivos do proxy automático | 592 | 4.096 negativos |
| Falsos negativos automáticos | 152 | 4.096 positivos |
| Verdadeiros negativos automáticos | 3.504 | 4.096 negativos |
| Precisão nominal do proxy automático | 86,95% | 3.944 / (3.944 + 592) |
| Recall nominal do proxy automático | 96,29% | 3.944 / 4.096 |
| FPR binária do proxy automático | 14,45% | 592 / 4.096 |
| Positivos adicionais recuperados pelo proxy amplo | 152 | 152 FN automáticos |
| Positivos remanescentes sem recuperação no teste | 0 | 4.096 positivos |

**Não chamar a última linha de recall top-5:** o teste compara pares isolados; não constrói lista de candidatos, não mede posição, bloqueio SQL, concorrentes, limite de cinco, nem ausência da verdade no universo.

**A precisão de 86,95% depende da prevalência artificial de 50% de pares verdadeiros.** Não é estimativa de precisão operacional, de risco populacional ou de falsos vínculos publicados. O gerador contém apenas 32 prenomes × 16 sobrenomes = **512 nomes completos distintos**, repetidos ao longo das 4.096 linhas; as linhas não são 4.096 identidades nominais independentes. Não calcular intervalos de confiança como se fossem amostras independentes. O proxy não usa nome da mãe, nascimento, CPF, FS calibrado, guards nem modelo congelado. O orçamento FP de 100 bp em DEV é outra métrica, com denominador de cenários positivos e inclusão de `LEAVE_TRUTH_OUT`; não comparar diretamente com a FPR de 14,45%.

## Decisão de engenharia para a continuação

1. **Não promover** `V2 HIGH/EXACT` como regra automática com base neste teste. Manter o modelo ATIVO e seus gates inalterados. O PR pode integrar somente o teste e este registro de evidência, sem alterar o pipeline.
2. Antes do Ensaio único, avaliar **o motor FS C# e a busca síncrona real no mesmo corpus rotulado**: blocking elegível D, C e D∪C (união deduplicada), scoring, guards, concorrentes, ausência da verdade, top-5 e `Nenhum destes`; nenhuma seleção no balcão publica vínculo sem o fluxo governado.
3. Registrar TP, FP, FN, conflitos, abstenções, precisão e recall **após a decisão FS**; separadamente recall de blocking, recall top-5 condicionado à verdade presente, falsos candidatos expostos, taxa de `Nenhum destes`, latência P50/P95/P99 e impacto por estrato (CPF ausente/tardio, mãe ausente, homônimos, erros correlacionados, Gestor).
4. Usar corpus com identidades distintas, homônimos reais simulados, mães/nascimentos gerados e rótulos independentes da grafia; medir por pessoa/cenário e por seed, sem pseudorreplicação. Separar TRAIN, VALIDATION e TEST e congelar parâmetros antes de TEST.
5. Respeitar [Decisões de calibração](Decisoes_Linkage_Calibracao_IBGE_20260926.md): selecionar na fronteira Pareto o menor FN sujeito ao orçamento FP **pré-declarado**; medir orçamento e denominadores corretos, sem otimização após TEST. A aprovação de HML/produção e representatividade real permanecem na issue #31.
6. Preservar a [arquitetura de blocking complementar](Decisao_Arquitetural_Blocking_Complementar_IBGE_20260927.md), a [busca semicega](Diretrizes_Identidade_Progressiva_Apoio_Decisao.md) e o [Ensaio único](Ensaio_Unico_Paridade_HML.md). A decisão recente de não usar fonética operacionalmente exige registro formal de escopo/versionamento: a decisão arquitetural ainda menciona fonética como expansão pesquisável, não como equivalência autorizada.
7. Antes de qualquer `VALIDATE/ACTIVATE` novo, produzir o dossiê pareado e aprovação master previstos na [DT-15](DT15_Governanca_Decisao_Modelo.md), além da conferência governada exigida pelo código e do plano de rollback. DT-15 está parcial; não interpretar o sucesso de CI como autorização de ativação.

**Gate de aceite deste PR:** documentação fiel ao TRX e testes de regressão verdes. **Gate de implantação do novo comportamento:** ensaio integrado e revisão governada independentes, ainda pendentes.
