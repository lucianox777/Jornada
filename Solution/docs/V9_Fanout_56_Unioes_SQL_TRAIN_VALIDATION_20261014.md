# V9 — fan-out por observação, inclusive zero, das 56 uniões SQL em TRAIN/VALIDATION

## Por que este incremento é necessário

A PR #1010 avaliou **56 uniões deduplicadas de um a quatro dos seis passes SQL pré-registrados** sobre o baseline físico C1+ATIVO, medindo M/U e recall no quadro SQL elegível de TRAIN e VALIDATION. Isso **não basta** para escolher uma política: duas combinações com o mesmo número de candidatos U podem concentrá-los em observações comuns de forma muito diferente. A PR #1006 forneceu histogramas de grau por observação **somente para o baseline**, não para cada união alternativa.

## Método e contrato

`V9SqlTrainValidationUnionFanoutAudit` recebe a mesma lista de pares SQL da PR #1010, as seis extensões, as **56 assinaturas congeladas** e a lista completa de observações do corpus, filtrada exclusivamente para TRAIN e VALIDATION. A leitura SQL não recebe novo INSERT em `#v9pairs`, não muda regras publicadas e não usa TEST.

Para cada união e partição:

- Deduplica por par canônico `(a,b)` **antes** de contar graus e retém a classe M/U verdadeira do corpus. Repetição entre passes não duplica incidência. Se o mesmo par receber rótulos ou partições inconsistentes, aborta; idem com duplicatas no baseline/passe ou par fora das partições.
- Inclui no denominador **todas as observações elegíveis** de TRAIN ou VALIDATION, CPF ausente e nascimento presente, **inclusive zero candidatos**. Confirma que ambos os endpoints são elegíveis, do mesmo split e de gestores diferentes. Não pressupõe que a pessoa-base com várias observações seja uma única ponta.
- Incrementa em +1 o grau total das duas pontas por par (M ou U), e o grau falso de ambas apenas nos pares U. Reconcilia exatamente `Σ graus totais=2×(M+U)` e `Σ graus falsos=2×U` e confronta as contagens M/U da configuração #1010.
- Publica P50/P95/P99 e máximo de candidatos totais e P95/P99/máximo de candidatos U; usa **nearest rank** `ceil(p*n/100)` **sem interpolação** sobre o denominador completo de observações elegíveis. Publica histogramas de grau agregado `(DegreeValue,ObservationCount)` para recálculo independente.
- Falha fechado se qualquer união ultrapassar o cap de **1 milhão** de candidatos, se faltarem assinaturas (exatamente 56×2=112 entradas), se houver TEST, observações repetidas, faltantes ou se o fan-out divergir da união. Resultados somente em contagens/histogramas, sem nomes, CPF, IDs, gestores, bases ou arestas exportadas.

A CI Python valida os **112 histogramas por seed**, recomputa percentis e zeros, total de pontas e rótulos M/U, e vincula as assinaturas às 56 combinações já auditadas. O sumário agregado exibe custo das configurações consultivas, **sem repetir histogramas volumosos**. Os testes NUnit verificam overlap, zeros, concentração nas pontas, partições/gestores, registros inelegíveis e `MayPromoteBlockingPolicy=false`.

## O que esta medição NÃO prova

Não foram estabelecidos orçamentos aprovados de P95/P99, tempo real de junção por pass/ruleset, FDR/FP com incerteza por base, suporte das 81 células sob cada nova combinação, migração/reprodução integral de `VERSIONED_ALIAS`, nem validação de prior/m/u/TF condicionada à política escolhida. **Nenhuma combinação é selecionada automaticamente**, mesmo que passe 95% de recall sintético TRAIN/VALIDATION; o gate do Calibrador histórico usa outro quadro e ainda necessita confronto com os MESMOS gabaritos, projeções e fingerprints.

A suficiência de 30 mil pessoas-base não está demonstrada por número de pares, por Kish de incidências ou por CI verde; os parâmetros FS/TF não são recalibrados nesta entrega. `MayPromoteBlockingPolicy=false` sempre, e o status é `DESCRIPTIVE_ONLY_NO_TEST_NO_FANOUT_BUDGET_OR_OPERATIONAL_PROMOTION`. Sem ativar V9 ou DT-15; sem alterar Rank/Resolve, V8 histórica, IBGE original, JornadaLocal, NODE/NAS, Gold ou HML/PROD. Apenas ensaio `JornadaHarness` isolado, `DOTNET_ENVIRONMENT=Development`, sem extrapolação automática para 11 milhões.
