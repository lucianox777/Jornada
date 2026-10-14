# V9 — censo de chaves SQL e pressão de colisões por gestor

## O motivo

Recall pontual ≥95% sobre a massa sintética não garante que o mesmo
blocking seja econômico ou estatisticamente defensável para a população
do município. As colisões U por observação aumentam com a densidade
de nomes/anos/sobrenomes **dentro das chaves indexadas**, e não
necessariamente na mesma proporção para todas as células. Multiplicar
todo U por um fator 380 sem estimar as distribuições condicionais das
chaves não prevê o otimizador nem o fanout P95/P99 do SQL Server.

## Medição física SQL em memória

Dentro do **mesmo** SQLite `:memory:`, com as mesmas tabelas
`cellkeys(cell,part,composite,oid)` e
`observations(oid,part,manager)`, executamos uma consulta SQL
`GROUP BY cell,part,composite,manager`.
Por **cada chave** contamos registros n e registros por gestor
`n_g`; calculamos a quantidade combinatória de junções
intergestores brutas `(n*n - sum(n_g*n_g))/2`, sem usar gabarito de M.
O cálculo é o de pares de registros distintos intergestores que
compartilham aquela chave; a união deduplicada de pares tem
cardinalidade menor ou igual, porque duas pessoas podem compartilhar
várias chaves de sobrenome.

O artifact agrega, **por split e célula**, sem nenhum valor
de chave/CPF/nome/identificador:

- `populated_keys`, `keys_with_cross_manager_pairs`,
  `max_records_per_index_key` e quantis P95/P99 do tamanho
  da chave indexada (não da latência);
- número de chaves com 20+ e 100+ registros, e fração
  das junções brutas em chaves com pelo menos 20 registros;
- `raw_cross_manager_join_hits`, `unique_candidate_pairs`
  e `repeated_join_hits_before_pair_dedup`.

A métrica compara a **concentração das células** e revela
clusters de alto fanout, incluindo quando a cardinalidade
de joins cresce de forma muito desigual entre chaves. Não
confundir raw JOIN hits com U únicos, nem índice-key P95
com p95 de candidatos por observação, CPU, memória ou p95
de latência SQL Server.

## O que falta para prever 11,45 milhões

O corpus de 30 mil identidades-base pode super/subamostrar
idades, nomes raros, sobrenomes, municípios e gestores.
Projeções cidade/gestor dependem de `P(key | gestor,faixa_etaria,
população)`, número de observações por pessoa, variação da
quantidade de gestores e dependência conjunta dos atributos.
Repetir a proporção de colisões não conserva caudas e gera
extrapolações pouco confiáveis. Este censo fornece pontos
de partida para simulação estratificada e confrontos no
SQL Server isolado, não estimador homologado municipal.

Sobrenomes extraídos como ANY ainda são heurística
Python sobre a projeção de nome C# normalizado; falta
paridade exata com `BrazilianNameComponents` e versão
física indexada. Este trabalho não modifica índices
persistentes nem a política P AND (M OR D), o scorer
Fellegi–Sunter, m/u, TF, prior, TEST, Rank/Resolve,
Gold, DT-15/V9 ativo, V8, HML/PROD, JornadaLocal,
NODE/NAS, referências IBGE.
