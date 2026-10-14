# V9 — sobrenomes canônicos no laboratório SQL em memória

## Contrato diagnóstico, sem mudar V1 nem a política ativa

Até a PR #1075, o SQLite em memória usava nome completo normalizado
e MetaphoneBR **derivados do C#**, mas o cálculo do último sobrenome e
de sobrenomes de qualquer posição era uma **heurística Python provisória**.
Isso poderia distorcer comparações entre 30 mil nomes sintéticos e o
índice SQL Server cuja projeção usa `BrazilianNameComponents`.

Esta alteração acrescenta o modo **opt-in**
`--v9-export-canonical-csv-with-surnames` ao exportador existente,
**sem tocar** `--v9-export-canonical-csv` e o schema congelado V1.
A projeção estruturada V2 produz as mesmas 12 colunas V1, seguidas de:

| Campo novo | Origem | Significado |
|---|---|---|
| `nome_ultimo_sobrenome` | `V9NameRequiredSqlCellAudit.Last` | Último sobrenome de conteúdo da pessoa |
| `nome_sobrenomes_json` | `V9NameRequiredSqlCellAudit.AnySurname` | Lista JSON de sobrenomes significativos da pessoa, último primeiro |
| `nome_mae_ultimo_sobrenome` | `V9NameRequiredSqlCellAudit.Last` | Último sobrenome significativo da mãe |
| `nome_mae_sobrenomes_json` | `V9NameRequiredSqlCellAudit.AnySurname` | Lista JSON de sobrenomes da mãe, último primeiro |

A versão da derivação de componentes é
`PERSON_NAME_BRAZILIAN_COMPONENTS_V1` e o método de ensaio SQL Server
é `V9_NAME_REQUIRED_SQL_CELLS_DIAGNOSTIC_V1`.
O nome integral original é **preservado**, sem apagar partículas ou agnomes.
Sobrenome em qualquer posição continua uma heurística conservadora sobre
nome **não estruturado**: não é uma taxa IBGE medida da posição final,
nem vínculo determinístico. A prioritização do último sobrenome não
elimina outros sobrenomes pesquisáveis. Pessoa e mãe geram **chaves
separadas** para evitar misturar campos.

## Paridade e integridade

No novo experimento do corpus original seed42, o workflow
`v9-memory-batch` executa explicitamente o exportador C# V2,
congela o mesmo tipo de snapshot com hashes SHA-256 das frequências
nominais independentes TRAIN e roda o SQL SQLite `:memory:`.
`v9-sqlite-memory-cells.py` lê as listas JSON já canonizadas e
**não chama a antiga extração Python** quando as quatro colunas V2
estiverem presentes. Se houver colunas parciais, JSON inválido,
sobrenomes repetidos ou último elemento incompatível com a ordem
canônica, aborta; não recua silenciosamente ao proxy.
As projeções antigas V1 continuam permitidas, mas identificadas
explicitamente como PROXY e sem alegação de equivalência.

NUnit cobre multi-sobrenomes, mãe, agnome, serialização CSV de JSON e
contrato V1 inalterado. Python testa consumo sem fallback e falha
fechada. Qualquer mudança numérica dos 19 passes após a troca de
projetor deve ser publicada **como resultado novo da mesma seed**,
com denom. M em TRAIN e VALIDATION fixo, `U` por célula e união,
margem ≥95% contra universo **elegível e total**, e fanout P95/P99,
sem confundir alteração de chaves com benefício causal da seleção.

## Limites

`MetaphoneBR` e Jaro–Winkler têm papeis diferentes: fonética é
chave indexada de recuperação, Jaro–Winkler é comparação sobre
candidatos; uma faixa de similaridade não vira índice B-tree.
A execução em SQLite não mede custo/fanout real de 11,45 milhões,
índices SQL Server, prior, m/u, TF conjunto, FDR, latência física ou
efetividade operacional. Reexecutar os passes escolhidos com o mesmo
projetor **no SQL Server isolado**, mantendo custo U sob controle,
antes de qualquer discussão de promoção.

TEST continua cego e treinamento de frequências usa a massa-base
sintética independente por identidade, nunca as pontas de pares U.
Nenhuma mudança em IBGE original, FS/TF/m/u operacionais, V8, V9/DT-15,
Rank/Resolve, Gold, JornadaLocal, HML/PROD ou NODE/NAS.
