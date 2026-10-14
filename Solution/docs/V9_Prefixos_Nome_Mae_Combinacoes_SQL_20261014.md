# V9 — dois prefixos adicionais de 3 caracteres, recombinação física SQL

## Hipótese e correção do rumo

A PR #1020 já confirmou o ganho **efetivo** da combinação `prefixo3 do nome da pessoa + data de nascimento exata` (`MAR + 11/02/1975`): artefato `v9-physical-sql-active-candidate-wrong-link-30k` do run #38510771527, seeds 42/43/44, somente TRAIN/VALIDATION, mostrou **3.203 novos M** e **579 novos U** em seis splits comparados à união física de 11 passes, com recall final entre **75,1501% e 76,2603%**. Os números não são do IBGE, mas dos registros sintéticos materializados em SQL (nomes distribuídos a partir de referências IBGE congeladas). A CI #1020 foi integralmente aprovada, a PR foi integrada. O recall físico ainda não chega a 95%.

Para completar o triângulo de duas evidências, esta PR acrescenta **apenas dois passes** ao índice prefixo3 já testado, totalizando três passes por dois novos tipos de chaves, pessoa e mãe:

| Passe | Chaves (AND no mesmo passe) | Independência de campos |
|---|---|---|
| Anterior #1020 | `person_prefix3` + nascimento **exato** | pessoa e data |
| Novo A | `mother_prefix3` + nascimento **exato** | mãe e data |
| Novo B | `person_prefix3` + `mother_prefix3` (data observada, mas não igual) | pessoa e mãe |

Exatamente **três caracteres iniciais** de cada nome **normalizado**; comprimento menor, ausência ou espaços não produzem chave coringa, nem passam a ser obrigatórios para toda identidade `NOVO`. Exemplo: `MAR + ANA + data`; as combinações são OR entre passes e AND dentro de cada passe. São sinais de **fontes nominais diferentes** (pessoa/mãe), não três evidências estatisticamente independentes por definição: frequência de pares pessoa-mãe pode estar correlacionada, e não multiplicamos suas marginais.

## Índices realmente existentes e novos

Em produção já existe `identidade.blocking_chave` e seu `IX_blocking_chave_lookup(normalizacao_versao,atributo,valor_normalizado,...)`; **não** existem atualmente `person_prefix3` e `mother_prefix3` como atributos projetados no vocabulário operacional. O ensaio usa somente `#v9obs` e `#v9p3obs` **temporários** no SQL Server `JornadaHarness` DEV. A primeira coluna `nome_p3` foi projetada pelo normalizador canônico da PR #1020; `mae_p3` é calculada a partir de `mae` já normalizada, com comprimento mínimo 3. Criamos dois índices temporários `(part,mae_p3,nascimento)` e `(part,nome_p3,mae_p3)`. Não é necessário criar um índice físico persistido novo antes de estudar o desempenho do Calibrador; nenhum DDL operacional foi alterado.

## Experimento controlado, com gabarito antes do SQL

Mesmas pessoas-base e observações, mesmas seeds, fingerprint, isolamento de TRAIN/VALIDATION, filtros SQL CPF ausente, nascimento em ambas, gestores diferentes e ordem canônica `obs_id` crescente. O `#v9pairs` ATIVO+C1 **não** é alterado. A auditoria lê pares físicos dos 11 passes antigos + `#v9prefix3` da PR #1020; deduplica os 12 passes e **acrescenta** a união das duas consultas indexadas. Publica M/U de cada novo passe e adicionais sobre o baseline de doze, novos M/U da união de 14, recall sobre todos os M elegíveis, déficit para atingir 95%, candidatos por observação (P95/P99/máximo com graus zero), distribuição de U e integridade com truth independente. O SQL tem cap de **1 milhão de pares**, sem truncamento. A CI executa auditoria C# e verificação independente em Python de todas as três seeds, publica `v9-sql-two-prefix3-summary.json` no artefato físico e reprova gabaritos, reconciliações e vazamentos de TEST.

## O que significa usar dados reais aqui

A **distribuição nominal de referência** usada no corpus tem proveniência do IBGE Censo 2022, publicado e congelado no projeto (dados **reais agregados de frequência**). Contudo, **os pares intergestores desta avaliação ainda são sintéticos**: a referência IBGE pública não fornece vínculos com mãe e datas exatas de nascimento nem pares verdadeiros de duas secretarias. Esta PR **não executa** ligação sobre cadastros municipais reais nem acessa JornadaLocal/HML/PROD. Para homologação com pessoas reais seria necessário um conjunto governado, autorizado, minimizado e rotulado independentemente, sem usar CPF como feature quando servisse como rótulo. Não apresentar resultados sintéticos como desempenho empírico em 11 milhões.

## Limites

**Não houve nesta PR escolha de política, ajuste TF, calibração de M/U nem uso de TEST para decidir comprimentos/passes.** O valor fixo inicial de três caracteres é uma hipótese pré-registrada; o comprimento adaptativo por frequência e orçamento de fan-out será papel do Calibrador numa rodada posterior, sempre com seleção TRAIN/VALIDATION e congelamento antes do TEST. Um passe muito amplo deve falhar fechado pelo cap, não limitar silenciosamente resultados. O scorer FS permanece congelado do proxy, então U candidatos **não** equivale a FP entre vínculos aceitos. Sem decisão de ativação V9/DT-15, mudança operacional de índices, histórico V8, Rank/Resolve, Gold, IBGE original ou clientes.
