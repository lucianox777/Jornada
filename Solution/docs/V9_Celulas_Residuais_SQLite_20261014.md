# V9 — cinco células direcionadas aos M faltantes (diagnóstico de SQL em memória)

**Meta:** ≥95% de recall de candidatos M nome-elegíveis (P AND (M OR D)),
em TRAIN e VALIDATION **separadamente**, sem ocultar a abstenção
relativamente a todos os M sem CPF. Nenhuma célula altera automaticamente
o ruleset operacional, nem parâmetros TF/m/u, limiar FS ou ativação V9.

## Base empírica anterior

No artefato real da PR #1073, seed 42 e corpus original com projeção
canônica C# congelada, a **UNION_EXTENDED_V1** de 14 células exploratórias
(sem contar a BASE de duas células) tinha:

| Split | M elegíveis | M recuperados | Recall | U candidatos | M faltantes | M para 95% |
|---|---:|---:|---:|---:|---:|---:|
| TRAIN | 20.075 | 18.216 | 90,7397% | 292.181 | 1.859 | 856 |
| VALIDATION | 6.612 | 6.069 | 91,7877% | 32.782 | 543 | 213 |

A decomposição sem vazar identificadores mostrou **1.180/349** faltantes
com sinais compartilhados de pessoa e nascimento, sem mãe comparável;
**426/115** com sinal nominal da pessoa mas nenhum sinal nominal materno
ou de nascimento; **100/26** sem assinatura compartilhada de pessoa
nos recursos testados. O estrato `PERSON_ONLY` e
`NO_PERSON_SIGNATURE` não pode ser forçado a match por um
passe que exige uma combinação de duas evidências.

As hipóteses M **potenciais** (podem sobrepor-se) contaram em TRAIN e
VALIDATION, respectivamente: `P_FIRST_DOB_MONTHDAY` 544/163;
`P_ANY_BIRTH_YEAR` 411/103;
`P_PREFIX3_BIRTH_YEAR` 236/79; `P_LAST_M_FIRST` 90/33.
As contagens são apenas M em falta que satisfazem o predicado no
gabarito, sem U, custos ou medições de consultas físicas adicionais.
**Não somar** contagens como se fossem M exclusivos.

## Experimento físico novo

As cinco células adicionais indexadas SQL SQLite `:memory:` são:

| Célula | Composição AND | Motivação |
|---|---|---|
| P_FIRST_DOB_MONTHDAY | Primeiro nome da pessoa + dia/mês válidos | Ano divergente, nascimento parcial comparável |
| P_ANY_BIRTH_YEAR | Qualquer sobrenome significativo da pessoa + ano válido | Prenome/fonética podem mudar; mesmo sobrenome e ano |
| P_ANY_DOB_MONTHDAY | Qualquer sobrenome significativo da pessoa + dia/mês válidos | Variantes de prenome/ano, sem depender da mãe |
| P_PREFIX3_BIRTH_YEAR | Prefixo 3 da pessoa + ano válido | Divergência fonética/primeiro nome, mesma data anual |
| P_LAST_M_FIRST | Último sobrenome significativo da pessoa + primeiro nome da mãe | Sem DOB; recupera variantes de prenome materno e sobrenome |

`UNION_EXTENDED` continua **exatamente** a mesma união de 14 células
de V1; `UNION_EXPANDED` mede o conjunto das 19 células. A diferença
de pares M/U de suas uniões indica ganho líquido: não comparar
denominadores diferentes nem reutilizar o recall anterior como ganho.
A busca gananciosa continua ajustada **somente em TRAIN**; aplica
o mesmo plano fixo em VALIDATION, sem refit e sem TEST.
Cap máximo de 1 milhão **por célula e split** implica falha total,
nunca `LIMIT` silencioso. Publicar fanout de candidatos (incluindo
zeros), U incremental, e a soma residual dos 19 passes.

A palavra `ANY` ainda designa uma extração conservadora
**proxy Python** sobre nome integral previamente normalizado pelo C#.
Ela não certifica a paridade da projeção
`BrazilianNameComponents` ou os índices SQL Server.
Antes de selecionar/promover, exige-se confrontar os conjuntos
de pares com o SQL Server isolado, confirmar chaves versionadas
e os riscos U/fanout. Um aumento de M que também explode U
não é automaticamente solução satisfatória.

Fonte: ensaio seed42 original do GitHub e artefato agregado da PR #1073.
O documento não afirma previamente o resultado nem os 95% desta
nova união; aguardar o artifact do HEAD efetivamente executado.
