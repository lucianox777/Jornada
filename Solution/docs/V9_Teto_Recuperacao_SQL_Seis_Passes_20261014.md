# V9 — teto exato do recall no universo dos seis passes SQL pré-registrados

## Decisão a partir da evidência existente — P0 #999

A PR #1010 integrou a busca completa e deduplicada de **56 uniões de 1–4 passes SQL pré-registrados** sobre a base física C1+ATIVO, somente para seleção diagnóstica em TRAIN/VALIDATION, sem uso de TEST. Inspecionamos o artefato real `v9-physical-sql-active-candidate-wrong-link-30k` do run **38489946136** (PR #1010, HEAD `6b6b6753cfa49153b66b6f0382fe4fb052284704`), que contém os relatórios por seed e o resumo agregado. O resultado é inequívoco quanto ao **conjunto examinado**: **zero das 56 combinações alcançou 95% de M elegível simultaneamente em TRAIN e VALIDATION**, em qualquer das três seeds.

Os melhores resultados **em cada partição** — não necessariamente a mesma assinatura em TRAIN e VALIDATION — foram:

| Seed | Split | M elegíveis | M baseline SQL | Melhor M recuperado 1–4 passes | Recall máximo 1–4 | M mínimo para 95% | Déficit mínimo (1–4) |
|---:|---|---:|---:|---:|---:|---:|---:|
| 42 | TRAIN | 18.315 | 7.790 | 11.661 | 63,669% | 17.400 | 5.739 |
| 42 | VALIDATION | 6.070 | 2.721 | 3.931 | 64,761% | 5.767 | 1.836 |
| 43 | TRAIN | 18.326 | 7.789 | 11.537 | 62,954% | 17.410 | 5.873 |
| 43 | VALIDATION | 6.122 | 2.699 | 3.983 | 65,060% | 5.816 | 1.833 |
| 44 | TRAIN | 18.230 | 7.996 | 11.742 | 64,410% | 17.319 | 5.577 |
| 44 | VALIDATION | 6.039 | 2.613 | 3.854 | 63,819% | 5.738 | 1.884 |

Esses valores são o **máximo dentre apenas 56 conjuntos de até quatro passes**, **não** o teto do conjunto dos seis passes. A PR atual calcula esse teto adicional sem permitir publicação, e não substitui os relatórios já existentes. Não há evidência de que bastaria elevar a busca para 5 ou 6 passes; isso precisa ser comprovado por união de pares, nunca por soma dos ganhos individuais.

## Instrumentação do teto de toda a família pré-registrada

`V9SqlTrainValidationUnionSearch` **V2** reúne **todos os pares adicionais distintos dos seis passes**, independentemente da restrição de 1–4 passes na busca publicada. A entrada segue exatamente o conjunto SQL físico de TRAIN/VALIDATION e rejeita TEST, duplicatas internas e discordâncias de gabarito ou partição entre passes. Os seis passes não são executados novamente para essa avaliação: reutilizam-se os próprios pares já produzidos pelas seis consultas físicas SQL; mantêm-se os limites de 1 milhão de pares somados ao baseline. Se o teto exceder esse orçamento, o teste **falha fechado** e não afirma que foi mensurado.

Compara `M_baseline + M_extra_UNIAO_DOS_SEIS` com todos os M **elegíveis enumerados independentemente antes do blocking**, por split. Registra total M/U para ambos os splits, recall, M restantes sem recuperação mesmo com seis passes e **quantidade exata de M adicionais necessária para alcançar 95%** (`max(0,ceil(19*M_elegível/20) − M_uniao_seis)`). O limiar é inteiro, sem arredondamento indevido, e a CI Python confere o teto superior de todas as 56 combinações 1–4, a relação com os ganhos individuais e o déficit.

Como a cobertura M de uma união de passes só **aumenta** quando se adicionam passes, o conjunto que usa os seis é um **teto exato de recall sobre esta família pré-registrada**: se nem todos os seis juntos chegam a 95% em TRAIN/VALIDATION, não há razão para aumentar `MaxPasses` para 5–6 usando estes mesmos passes. Isso **NÃO prova** que 95% seja impossível para novas regras, dados mais ricos, nomenclaturas/históricos ou outro frame do Calibrador. Também não significa que uma união maior seja apropriada operacionalmente: seu U e fan-out/custos têm de ser governados independentemente.

As saídas `AllSixPassesMeetBoth95PercentFloors` e `AllSixPassesProve95ImpossibleWithinRegisteredPassFamily` têm alcance **restrito à família dos seis passes e ao SQL TRAIN/VALIDATION testado**. `MayPromoteBlockingPolicy=false` permanece incondicional, independentemente do resultado.

## O que isto muda na prioridade técnica

1. **Não continuar procurando à força as mesmas seis regras:** os melhores recalls 1–4 já são ~63–65%, muito abaixo do piso de 95%. O teto dos seis quantifica se subir o número de passes nesta família poderia mudar isso.
2. **Reconciliar a suposta aprovação de 95% no Calibrador com o SQL físico** exigindo mesmo conjunto M (gabarito), projeções, fingerprints, denominator, taxas de incompletude, regra/candidate pool e `VERSIONED_ALIAS`; comparar 95% de um corpus candidato amostrado com ~43%/65% deste frame NÃO é paridade.
3. **Testar novas famílias em TRAIN/VALIDATION** com desambiguação de nome da mãe ausente, erros triplos realmente observados, DOB divergente, custo físico e fan-out P95/P99. Não escolher por TEST.
4. **Calibrar m/u, prior, TF com suporte robusto por pessoa-base:** as 81 células nominais, sete semânticas e concentração por base (PRs #1005–#1009) continuam inconclusivas para inferência até tratamento explícito de dependência, incerteza por estrato e política de 30 mil. Distribuições conjuntas TF usam uma pessoa-base por pessoa em TRAIN, nunca produto de marginais IBGE. As marginais IBGE originais permanecem congeladas para unidades compatíveis.

A prova da CI é técnica e em corpus sintético, **não** licença para ativar V9 nem DT-15. V8 histórica, Rank/Resolve, JornadaLocal, NODE/NAS, Gold, HML/PROD e IBGE original não são alterados; só `JornadaHarness` efêmero em `DOTNET_ENVIRONMENT=Development` é utilizado.
