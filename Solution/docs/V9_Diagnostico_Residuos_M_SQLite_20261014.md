# V9 — diagnóstico agregado dos M elegíveis não recuperados (SQL de memória)

## Objetivo e dependência

Complemento ao laboratório `v9-sqlite-memory-cells.py` da PR #1072.
A meta **≥95% de recall de candidatos M** em TRAIN e VALIDATION continua
prioritária; ampliar o número de passes sem uma decomposição dos M faltantes
multiplica U/fanout e não resolve a calibração FS.

O perfil é calculado para `truth_eligible - UNION_EXTENDED`, antes de
qualquer decisão FS. Nenhuma seleção é feita com TEST. O perfil conta
**apenas vínculos M verdadeiros que faltam**, não candidatos U que um novo
passe produziria. Cada hipótese de célula abaixo recebe portanto
`candidate_hypotheses_potential_true_M_ONLY` — limite superior de possíveis
M incrementais antes de medir U, cap, planos e tempo no SQL.

## Agrupamento exaustivo e mutuamente exclusivo

Para cada par M faltante:

1. `NO_PERSON_SIGNATURE`: nenhuma igualdade entre nome integral, fonética
   oficial, prenome, prefixo3, último sobrenome ou qualquer sobrenome
   (este último ainda é **proxy Python**, não projeção estruturada C#).
2. `PERSON_ONLY`: ao menos uma assinatura da pessoa é compartilhada,
   mas nenhuma assinatura nominal materna ou de nascimento.
3. `PERSON_MOTHER`: assinaturas comparáveis tanto da pessoa quanto da
   mãe, sem nascimento comparável.
4. `PERSON_BIRTH`: pessoa e data comparáveis, sem mãe comparável.
5. `PERSON_MOTHER_BIRTH`: os três grupos possuem ao menos uma
   assinatura comparável.

O relatório publica total, contagens de sinais compartilhados e até
20 assinaturas binárias agregadas; nenhuma pessoa, CPF, ID, nome, lista de
pares ou frequência TF individual é serializada. A soma das categorias
deve ser **exatamente** o total M não recuperado em cada split.

## Testes de hipóteses sem contaminação

As dez células possíveis são `P_PHON_M_PHON`,
`P_PHON_M_PREFIX3`, `P_FIRST_M_ANY`, `P_ANY_M_FIRST`,
`P_LAST_M_FIRST`, `P_PHON_DOB_MONTHDAY`,
`P_FIRST_DOB_MONTHDAY`, `P_PREFIX3_BIRTH_YEAR`,
`P_LAST_BIRTH_YEAR` e `P_ANY_BIRTH_YEAR`.

A contagem por hipótese não é ganho cumulativo: **um mesmo M pode entrar
em várias hipóteses**. Não se deve somá-las, nem usar a existência de M
para aprovar a célula. Na rodada seguinte, medir cada candidato no mesmo
SQLite indexado e na base SQL Server isolada, com U incremental sobre
o **mesmo** conjunto anterior, P95/P99 incluindo grau zero, cap sem
truncamento, latência, paridade canônica e incerteza por identidade.

Assinaturas nominal/fonética partem das projeções canônicas C# já
congeladas. Sobrenome na posição final e qualquer posição usam
heurística Python explicitamente provisória e **não** provam
equivalência com `BrazilianNameComponents`. A comparação
Jaro–Winkler deve ocorrer depois da geração de candidatos, jamais
como substituto declarado de índice de igualdade.

## Fronteiras estatísticas e operacionais

A política é `P AND (M OR D)`; excluir `MD` não pode inflar artificialmente
a métrica: sempre apresentar o teto de elegibilidade contra todos os M
intergestor sem CPF. TF nominal e conjunta precisa ser estimada por
**identidades-base independentes em TRAIN**, e não por pontas de pares
recuperados. Recall de candidatos não mede FDR, posterior, risco de
vínculo indevido ou aceitação final. M sintético raro ou U dependente
pode resultar em `INCONCLUSIVO`; 30 mil não bastam por decreto.

Esta PR acrescenta só diagnósticos agregados, testes e documentação.
Nada modifica políticas ativas, Gold, V8, DT-15, calibração FS, índices
persistentes, referência IBGE original, JornadaLocal, HML/PROD ou NODE/NAS.
