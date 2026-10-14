# Jornada V9 — comparação experimental A/B/C/D, E0, composições e busca adaptativa real

**Decisão em 14/10/2026:** não escolher A, B, C ou D nem descartar os
concorrentes até executar a comparação no **mesmo corpus, quadro de
candidatos, projeção C# e partições**, incluindo as composições recentes.

## Problema de nomenclatura, corrigido

A prova antiga `v9-abc-paired-blocking.py` executa apenas proxies de
passes fixos A0,A1,B0,B1,C0,C1,D0,D1. **D0 e D1 desse teste NÃO são
um calibrador nem blocking adaptativo**. O outro planejador
`v9-adaptive-frequency-planner.py` calcula frequências por pessoas-base
TRAIN e retorna por observação chaves de menor frequência; até agora
o teste não executava o resultado em índice real. Também os rótulos
A/B/C não demonstram por si só equivalência das estratégias
funcionais propostas: A SQL histórico, B aproximação, C híbrido e
D adaptação por frequência conjunta/TF. O uso de TF pelo Fellegi–
Sunter e as configurações de decisão são trabalho distinto do
**blocking de candidatos** e requerem calibração estatística própria.

## Experimento unificado desta entrega

`v9-abcd-adaptive-physical-comparison.py` recebe o mesmo snapshot
imutável do C# e o relatório original do SQLite (SHA-256 conferido);
usa TRAIN e VALIDATION, mantendo TEST inacessível ao experimento.

- **16 braços fixos diagnósticos**: A/B/C/D × normalização com ou sem
  fonética e com ou sem o passe E0 exato
  (nome normalizado + mãe normalizada + nascimento válido).
  Fonética e normalização provêm da mesma projeção C#, nunca de
  Python recalculando grafias ou códigos fonéticos.
- **Quatro braços adaptativos executados de verdade**:
  seleção de até 2 ou 3 passes POR CONSULTA de acordo com contagens
  de **identidades distintas em TRAIN**, com/sem E0. Executam
  consultas reais sobre B-tree SQLite indexada, entre gestores.
  Isso é um adaptador experimental que usa frequência TRAIN,
  **ainda não é otimização conjunta M/U nem um modelo FS calibrado**.
- **Quatro composições canônicas** sobre as 19 células de
  nomes/fonéticas/sobrenomes estruturados e nascimento:
  quatro células, quatorze células, união dezenove e plano
  greedy de M≥95%/menor U obtido SÓ em TRAIN, quando viável.
- Cada braço registra M recuperado / M verdadeiro global sem CPF,
  U de candidatos deduplicados, recall, grau médio `2U/N`,
  P50/P95/P99 com observações grau zero, bootstrap unilateral
  95% por identidade-base e status de certificação **falso**
  porque não há scorer validado e critérios externos de seleção.
  Custo SQLite não é tempo SQL Server. Não se publica PII,
  nomes, ids nem CPF.
- Os 19 passes originais têm M/U **obrigatoriamente
  reconciliados** com o relatório SQL indexado sobre o mesmo SHA.
  Nenhuma célula truncada por `LIMIT` quando >cap; ultrapassar
  orçamento faz o job falhar fechado. Um plano não viável em
  TRAIN permanece **não viável**; não selecionar por VALIDATION.
- **Violação normativa preservada como evidência legada:** as
  células históricas C/D que usam somente mãe+nascimento não
  garantem acordo do **nome da pessoa** no par, embora cada
  registro contenha um nome. Podem ser avaliadas como proxy,
  mas **não** são elegíveis para políticas V9 que exigem
  representação nominal compartilhada da pessoa E (mãe OU DOB).
  Não aprovar uma regra por recall se quebrar esse contrato.

## Calibrador adaptativo: o que ainda deve ser entregue

O calibrador deve ser um **orquestrador de modelos**, e não uma
lista rígida de campos ou um duplicado do algoritmo de blocking.
Adaptadores versionados por família de modelo devem aceitar
TRAIN isolado, produzir snapshots m/u/TF e hiperparâmetros
compatíveis com o quadro de blocking observado; validar propostas
somente em VALIDATION, com orçamentos explícitos de FDR/recall,
custo, abstinência e suficiência por estrato. A mesma interface
deve aceitar outros modelos futuramente sem misturar suas
evidências ou versões. A decisão de recalibração, publicação e
ativação permanece **manual**; diagnóstico de suficiência
(DT-23) pode ser automático. TEST não serve para escolher.

A seleção automática entre famílias só poderá descartar A/B/C
se o adaptativo vencer em comparação **pareada, com custo físico
SQL Server, recall global, incerteza por identidade e FDR
medida sobre vínculos aceitos após calibrar o scorer**. U de
blocking não é FP de decisão nem FDR; não declarar vitória
do adaptador usando somente uma proxy D e 30k de uma seed.
O perfil `correlated` é stress test adversarial hipotético,
e o `clean` não equivale à taxa real das Secretarias.

## Critérios de aceite da fase experimental

A CI deve executar todas as variantes no corpus original
IBGE sintético seed42, além de testes unitários de CPF separado,
nome+M/D, mesmos splits, M/U, E0 monotônico, fonte congelada
e cap. Transferência da seed42 para seeds43/44 na PR #1086
permanece evidência distinta, não substitui o confronto
A/B/C/D nem a calibração m/u/TF de #999/#946.

**Proibido:** mudar V8, Gold, V9/DT-15 ativa, FS/m/u/TF
do modelo operacional, HML/PROD, IBGE original,
JornadaLocal, NODE/NAS ou TEST.
