# V9 — resultados observados do índice composto `prefixo3 nome + nascimento exato`

**Proveniência:** PR #1020, `jornada-ci` execução **38510771527**, artefato `v9-physical-sql-active-candidate-wrong-link-30k` (id **11869980875**), HEAD de medição `faa79826d9858b787047c571724f294ddb42bb04`. Três corpora sintéticos independentes de 30 mil identidades-base cada (seeds 42/43/44). Novas regras avaliadas **somente em TRAIN/VALIDATION**; TEST não usado para escolher nem avaliar este passe.

O passe executado usa **um índice físico temporário real** `IX_v9_prefix3_birth(part,nome_p3,nascimento)` e igualdade de **três caracteres do nome completo normalizado e data civil completa**, por exemplo `MAR + 11/02/1975`. Nomes com menos de três caracteres não geram esta chave; o índice persistente `IX_blocking_chave_lookup` já é genérico, mas **a feature prefixo3 NÃO está persistida no catálogo operacional**. Nenhuma tabela ou regra operacional foi alterada.

| Seed | Split | M elegíveis pré-SQL | M / U com 11 passes | M / U adicionais prefixo3 | M / U com 12 passes | Recall 11 → 12 | M adicionais para 95% |
|---|---|---:|---|---|---|---|---:|
| 42 | TRAIN | 18.315 | 13.045 / 39.641 | **811 / 174** | 13.856 / 39.815 | 71,23% → 75,65% | 3.544 |
| 42 | VALIDATION | 6.070 | 4.340 / 4.523 | **289 / 25** | 4.629 / 4.548 | 71,50% → 76,26% | 1.138 |
| 43 | TRAIN | 18.326 | 12.971 / 38.066 | **801 / 194** | 13.772 / 38.260 | 70,78% → 75,15% | 3.638 |
| 43 | VALIDATION | 6.122 | 4.395 / 4.435 | **269 / 17** | 4.664 / 4.452 | 71,79% → 76,18% | 1.152 |
| 44 | TRAIN | 18.230 | 13.145 / 40.843 | **738 / 143** | 13.883 / 40.986 | 72,11% → 76,15% | 3.436 |
| 44 | VALIDATION | 6.039 | 4.305 / 3.852 | **295 / 26** | 4.600 / 3.878 | 71,29% → 76,17% | 1.138 |
| **Soma descritiva** | **6 splits** | **73.102** | **52.201 / 131.360** | **3.203 / 579** | **55.404 / 131.939** | **71,41% → 75,79%** | **14.046** |

A união de doze passes é **física e deduplicada por par (a,b)**: 3.203 M recuperados que os onze passes anteriores não recuperavam, contra 579 U adicionados (razão **5,53 M por U novo** *no corpus sintético deste recorte*). O ganho absoluto de recall agrupado é **+4,38 pontos percentuais**, sem alterar FS/threshold/TF. O SQL recuperou **todos os 41.481 M do gabarito** que concordavam em prefixo3 e nascimento exato nos seis splits; `Prefix3TruthMAbsentAfterSql=0`. A parcela desses M já coberta pelos outros passes não foi contada duas vezes.

O fan-out após a união de doze passes teve P99 de candidatos falsos por observação elegível de **26–30** nos splits TRAIN e **9–11** em VALIDATION, com observações sem candidatos incluídas no denominador. Os máximos de candidatos por observação ficaram em **47–50** TRAIN e **16–17** VALIDATION. Estes números são medidos apenas na massa sintética de 30 mil; não são planos de execução/custo confiáveis para 11 milhões.

**Conclusão:** a hipótese concreta funciona como ampliação de recuperação no frame físico observado. Os 95% de cobertura de blocking continuam **não alcançados**, em todos os seis splits: faltam 14.046 M adicionais somados para o piso de 95% em cada split. Ganho em M/U **não é** medição da FDR ou dos vínculos aceitos, pois os parâmetros FS continuam congelados do proxy antigo. Não usar TEST para retunar a regra, nem inferir que prefixos mais curtos, data flexível ou mais índices sejam seguros sem medir incremento U e fan-out. A seleção/calibração do modelo V9, suficiência de 30 mil, confiança de incerteza M por base/U por rede e aprovação operacional permanecem pendentes.

## Continuidade sugerida

Antes de propor outro passe, quantificar em TRAIN quais M residuais concordam em prefixo3 mas discordam de **dia/mês/ano**, e qual o custo incremental U por relaxamento bem delimitado de data; segmentar por prefixos frequentes sem expor nomes no artefato. O IBGE fornece seletividade marginal por prefixo, porém não distribuição conjunta nome-prefixo × nascimento; qualquer escolha adaptativa precisa de calibração e custo mensurado. Preservar todas as barreiras: V8, V9 operacional, DT-15, Rank/Resolve, IBGE original, JornadaLocal, Gold, NODE/NAS, HML e PROD.
