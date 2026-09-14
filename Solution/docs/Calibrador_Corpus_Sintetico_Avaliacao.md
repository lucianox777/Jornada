# Calibrador — avaliação do corpus sintético JORNADA_SYNTH_CORPUS_V1

## Conclusão

O corpus é útil como **benchmark sintético controlado** e complementa o `IBGE_NOMINAL_BENCHMARK_V1`, mas **não deve ser usado no estado atual como oráculo numérico de `m` nem como benchmark de ground truth CPF/CNS**.

A separação conceitual é correta:

- `IBGE_NOMINAL_BENCHMARK_V1`: testa confundibilidade nominal e blocking dentro do vocabulário observado;
- `JORNADA_SYNTH_CORPUS_V1`: injeta erros administrativos declarados para testar robustez do estimador e do pipeline de calibração.

O split por `base_person_id` antes das observações também está correto e evita vazamento entre `TRAIN/VALIDATION/TEST`.

## Achado crítico — `expected_m_exact` não é o `m` realizado

O gerador calcula `expected_m_exact` apenas a partir de `p_common` e da taxa declarada do campo. Porém, o valor efetivamente observável depende também de:

- corruptores que podem não alterar o valor;
- transposição de data inválida que retorna o valor original;
- heaping que ocasionalmente pode coincidir com a data original;
- mojibake;
- missingness, que muda o denominador porque ausência não é discordância;
- sobreposição entre gatilho comum e corrupção específica.

Medição sobre os pares verdadeiros gerados no pacote recebido, excluindo pares com campo ausente:

| perfil | campo | `expected_m_exact` declarado | `m_exact` realizado |
|---|---|---:|---:|
| correlated | NOME | 0,5207 | 0,5235 |
| correlated | NOME_MAE | 0,4744 | 0,4754 |
| correlated | NASCIMENTO | 0,5691 | 0,6462 |
| clean | NOME | 0,8836 | 0,8811 |
| clean | NOME_MAE | 0,8100 | 0,8088 |
| clean | NASCIMENTO | 0,9216 | 0,9311 |
| independent | NOME | 0,6084 | 0,6185 |
| independent | NOME_MAE | 0,4900 | 0,5083 |
| independent | NASCIMENTO | 0,7225 | 0,7713 |
| field | NOME | 0,2570 | 0,4288 |
| field | NOME_MAE | 0,1840 | 0,3402 |
| field | NASCIMENTO | 0,3154 | 0,6116 |

No perfil `field`, a divergência é grande o bastante para fazer um estimador correto parecer incorreto. Portanto, `expected_m_exact` deve deixar de ser um oráculo analítico e o gerador deve produzir um **gabarito empírico após a materialização das observações**, usando a mesma regra de missing adotada pelo estimador.

## Missingness

A decisão registrada no pacote está correta: ausência não é discordância. Para um campo ausente em qualquer lado do par:

- o campo não entra no denominador de estimação de `m` daquele atributo;
- no scorer operacional a contribuição deve ser neutra (`LR=1`, log-BF zero), salvo metodologia futura explicitamente versionada.

O gabarito deve registrar, por campo, o número efetivo de pares elegíveis usados no cálculo.

## CPF e CNS — o corpus ainda não testa o contrato de ground truth

Os CPFs e CNSs são sequências aleatórias de dígitos. Isso é insuficiente para o contrato atual do Calibrador porque a elegibilidade de CNS exige validade estrutural e o CPF é a âncora determinística da Jornada.

Para benchmark de ground truth, os identificadores sintéticos devem ser:

- fictícios, mas estruturalmente válidos;
- únicos por pessoa no cenário limpo;
- capazes de receber corrupção controlada em cenários específicos;
- acompanhados de casos controlados de reutilização anômala e conflito de data de nascimento para testar a política de elegibilidade CNS.

O corpus atual também não cria reutilização de CNS entre pessoas, portanto não exercita a regra de descarte por multiplicidade anômala.

## Semântica das taxas de presença

`p_cpf_present` e `p_cns_present` são taxas de **retenção do identificador por observação**, não prevalência populacional. A prevalência-base está hardcoded em `make_person` aproximadamente como:

- CPF: 22% das pessoas;
- CNS: 55% das pessoas.

No pacote `correlated` recebido, a prevalência realizada foi aproximadamente:

- pessoas com CPF: 22,10%; observações com CPF: 12,34%;
- pessoas com CNS: 54,51%; observações com CNS: 38,07%.

Renomear os parâmetros e registrar ambas as taxas no gabarito evita interpretação errada de cobertura.

## Ground truth e anti-leakage

`base_person_id` é adequado como verdade sintética interna e pode ser usado para construir pares `MATCH/NON_MATCH`. Ele não pode aparecer em qualquer feature, blocking, score ou artefato destinado a simular a superfície operacional.

Ao avaliar um cenário em que CNS é a fonte de rótulo, o benchmark deve aplicar a mesma regra da Jornada:

- CNS e derivados excluídos de candidate generation/blocking;
- CNS e derivados excluídos do score;
- o conjunto de pares avaliados não pode ser pré-selecionado por CNS.

O mesmo princípio vale para CPF quando ele for usado como fonte de rótulo de uma avaliação.

## Pares negativos

O pacote permite gerar `NON_MATCH` de forma inequívoca por `base_person_id` distinto. A estratégia de seleção dos negativos deve ser explicitamente versionada.

Para validar o scorer isoladamente, podem existir negativos de estresse (por exemplo, mesma data de nascimento). Para medir o sistema completo, a avaliação deve partir da candidate generation real; caso contrário, recall de blocking e desempenho do scorer ficam misturados.

## Cauda nominal

A modelagem de `tail_oversample` é conceitualmente boa porque registra `weight_correction`. Contudo, o peso de correção precisa ser materializado no corpus/gabarito ou ser deterministicamente reconstruível por observação/par. Não basta existir apenas como método do script gerador se o artefato de benchmark será executado independentemente dele.

## Critérios para V2

Uma `JORNADA_SYNTH_CORPUS_V2` deve, no mínimo:

1. calcular `m_exact_empirical` depois de gerar as observações e registrar denominadores por campo;
2. manter separadamente as taxas teóricas de corrupção, sem chamá-las de `m` observado;
3. gerar CPF/CNS fictícios estruturalmente válidos;
4. separar prevalência-base de identificador e retenção por observação;
5. gerar cenários versionados de CNS inválido, reutilizado e incompatível com data de nascimento;
6. registrar pesos de reponderação por pessoa/observação quando houver oversample de cauda;
7. registrar explicitamente a política de geração de pares positivos e negativos;
8. incluir fingerprint do vocabulário IBGE de origem;
9. manter `base_person_id` exclusivamente como truth interna, proibida em blocking e score;
10. preservar a separação por indivíduo-base antes de `TRAIN/VALIDATION/TEST`.

## Uso permitido da V1

Até a V2, a V1 pode ser usada para:

- testes de ingestão do benchmark;
- testes de partição por indivíduo;
- comparação relativa entre algoritmos no mesmo corpus;
- testes de missingness neutra;
- testes de robustez por tipo de corrupção;
- regressão reprodutível com seed fixa.

Não deve ser usada para:

- afirmar que um estimador recuperou ou deixou de recuperar o `m` verdadeiro usando `expected_m_exact` atual;
- validar elegibilidade CPF/CNS;
- estimar representatividade real da população sem CPF;
- definir thresholds de Produção.
