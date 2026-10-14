# V9 — união física de onze passes, déficits de 95% e resíduos de M por evidência observada

## Por que esta rodada

A issue P0 #999 ainda não pode ser encerrada com a combinação dos **seis passes históricos** ou dos **cinco novos passes** examinados isoladamente: os dois conjuntos são comparados ao mesmo baseline SQL físico ATIVO+C1, portanto há sobreposição que impede somar seus ganhos. O baseline SQL sintetiza C1 e o ruleset ATIVO do ensaio, mas **não é a V9 operacional nem inclui histórico VERSIONED_ALIAS**.

Este incremento calcula de forma **exata a união dos onze passes já executados no SQL**, a partir das listas físicas de candidatos de TRAIN e VALIDATION, sem repetir ou adivinhar predicados. O conjunto de **onze** é somente um teto dentro desta família explícita, **não** um limite para todo blocking possível. O cálculo ainda mede para onde se concentram os M elegíveis **que continuam perdidos**, distinguindo nome da pessoa e da mãe exatos, diferentes ou ausentes e nascimento exato ou diferente.

## Resultados numéricos confirmados da rodada anterior (#1015)

Fonte: artefato `v9-physical-sql-active-candidate-wrong-link-30k`, CI **38499377715**, arquivos `v9-sql-physical-seed-{42,43,44}.json`. Os números seguintes são os **cinco passes novos isolados + baseline**, e **não** antecipam a união onze desta PR.

| Seed | Partição | M elegível pré-SQL | M baseline | M dos cinco (união) | U dos cinco (união) | Recall cinco |
|---|---|---:|---:|---:|---:|---:|
| 42 | TRAIN | 18.315 | 7.790 | 13.045 | 39.641 | 71,226% |
| 42 | VALIDATION | 6.070 | 2.721 | 4.340 | 4.523 | 71,499% |
| 43 | TRAIN | 18.326 | 7.789 | 12.971 | 38.066 | 70,779% |
| 43 | VALIDATION | 6.122 | 2.699 | 4.395 | 4.435 | 71,790% |
| 44 | TRAIN | 18.230 | 7.996 | 13.145 | 40.843 | 72,106% |
| 44 | VALIDATION | 6.039 | 2.613 | 4.305 | 3.852 | 71,287% |

Na seed 42, o quinto passe **mãe fonética + ano de nascimento** recuperou *isoladamente em relação ao baseline* +292 M e +40 U no TRAIN; +75 M e +5 U em VALIDATION. **Esses 292 não são ganhos adicionais à união dos quatro primeiros:** boa parte pode estar sobreposta e é obrigatório deduplicar. A união de cinco contra a antiga união de quatro ganhou +206 M e +40 U em TRAIN seed42, com base na comparação dos dois artefatos. Nos demais splits, o recall da união de cinco permanece ~70,78%–72,11%. Nenhum desses números equivale a FP aceito ou parâmetro m/u calibrado.

Os P95/P99 de candidatos totais e falsos por observação **não mudaram materialmente** na maioria dos splits após a nova família; isso não elimina riscos na cauda, erro de vínculo, desempenho temporal da consulta ou em escala de 11 milhões.

## Novo contrato SQL TRAIN/VALIDATION

O `V9SqlElevenPassResidualAudit` só recebe fontes já auditadas no **mesmo cursor SQL de cada seed**: baseline (ATIVO+C1), seis listas de extensões das ablações antigas, cinco listas de extensões da família ano de nascimento, censo **independente dos pares M elegíveis antes do blocking** e seus relatórios de teto respectivos. As listas de candidatos **nunca** entram no JSON (somente contagens agregadas). A implementação exige os mesmos IDs, rótulos M/U e splits, sem CPF, DOB presente, gestores distintos e ordinais canônicos. Exclui `TEST` do denominador, uniões e ranking; não usa respostas de TEST como gabarito de seleção.

Para cada split calcula:
- `SixUnionM/U`, `FiveUnionM/U`, `ElevenUnionM/U`; confirma que os dois primeiros coincidem **exatamente** com seus relatórios anteriores e que os onze não duplicam candidatos recuperados pelos dois conjuntos;
- `RecoveredOnlyBySixM/U` e `RecoveredOnlyByFiveM/U` (diferença **de conjuntos**, não de somas) e identidades de inclusão-exclusão `Eleven=Five+OnlySix=Six+OnlyFive`;
- recall `ElevenUnionM / EligibleM`, M ainda não recuperados e M adicionais necessários para piso 95%, com `ceil(19×EligibleM/20)` inteiro;
- graus **inclusivos de zero** por observação elegível em TRAIN/VALIDATION, P50/P95/P99/máximo de grau total e P95/P99/máximo de U. Exige `Σ grau=2×(M+U)` e `Σ grau_U=2×U`;
- **36 células** (2 partições × 3 estados nome × 3 estados mãe × 2 DOB), cobrindo **todos M elegíveis**, seus recuperados no baseline e na união onze e os resíduos ainda perdidos. Uma data ou mãe `MISSING` jamais é convertida em `DIFFERENT`; são diferenças observadas, não atribuições causais ao gerador.

O *gate* de 95% aqui é meramente **diagnóstico no espaço físico SQL TRAIN/VALIDATION**. O "95%" do Calibrador histórico continua associado a **outra amostragem/ruleset** até reconciliar frame e fingerprints no mesmo gabarito. A auditoria sempre marca `MayPromoteBlockingPolicy=false`, mesmo que o teto das onze famílias supere o piso. A CI Python reconcilia os onze com as medições independentes anteriores por seed, todas 36 células e a inclusão-exclusão; NUnit cobre sobreposição U/M, graus zero, vazamento de TEST, rótulos conflitantes e corte de candidatos.

## Decisão de segurança e próximas ações

**Ainda não há evidência para liberar a V9 operacional.** Falta orçamento governado de FP/FDR, análise do fanout em escala, suporte de células m/u no novo SQL, incerteza por clusters de pessoas-base e rede U, prior e TF treinados sem vazar TEST, além de comprovação de que **30 mil identidades-base** conferem precisão estatística apropriada. `TF f(value)` vem da massa-base sintética TRAIN de pessoa única para as unidades conjuntas, **nunca** do produto de marginais IBGE originais, que continuam congeladas.

O resultado concreto dos **onze passes** será publicado pelo artefato CI desta PR (nenhuma estimativa é inventada antecipadamente). Os grupos com maior número de M ainda perdidos orientarão **novas famílias de predicados SQL pré-registradas apenas em TRAIN/VALIDATION**; não se deve selecionar por TEST. Nenhum código operacional, V9/DT-15, Rank/Resolve, V8 histórico, IBGE original, JornadaLocal, Gold, NODE/NAS ou HML/PROD é alterado. Execução exclusiva em banco `JornadaHarness` efêmero, ambiente Development e cap 1 milhão de candidatos, sem truncamento silencioso.
