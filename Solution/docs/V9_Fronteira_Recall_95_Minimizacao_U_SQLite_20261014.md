# V9 — fronteira empírica ≥95% com menor U sem usar TEST

## Problema resolvido nesta rodada

O laboratório `v9-sqlite-memory-cells.py` chegou a ≥95% de recuperação
de candidatos M no corpus original sintético seed 42,
**mas a UNION_EXPANDED de 19 células gerou U excessivo**:

| Mesma base e gabarito | TRAIN | VALIDATION |
|---|---:|---:|
| M elegíveis sem CPF, sob P AND (M OR D) | 20.075 | 6.612 |
| M recuperados pela UNION_EXTENDED (14 exploratórias) | 18.216 | 6.069 |
| U da UNION_EXTENDED | 292.181 | 32.782 |
| M recuperados pela UNION_EXPANDED (19 exploratórias) | 19.447 | 6.435 |
| U da UNION_EXPANDED | 618.399 | 69.041 |
| M adicionais de 14 para 19 | 1.231 | 366 |
| U adicionais de 14 para 19 | 326.218 | 36.259 |
| Recall 19 células | 96,87% | 97,32% |

Esses 95% **não certificam escala de 11,45 milhões**, índices físicos
SQL Server, FDR, escore FS, nem a população real. Antes de aceitar
qualquer célula, penalizar U e fanout no **mesmo frame** é indispensável.

## Dois métodos de seleção, calculados apenas em TRAIN

1. `exact_targeted`: mantém fixos os 14 passes exploratórios de V1,
   enumera **todas as 32 combinações** de cinco passes novos e seleciona
   exatamente a menor contagem de U observado em TRAIN dentre as uniões
   que conseguem ≥95% de M no TRAIN. Desempates: menos novas células,
   mais M e nome determinístico. **A otimização exata é apenas nesse
   espaço condicionado à base fixa**, não nos 2^19 subconjuntos gerais.
2. `greedy_pruned`: parte dos 19 passes e remove sucessivamente o
   passe com maior redução de U exclusivo enquanto preserva
   o alvo de 95% TRAIN, desempata por menor perda M, e recalcula
   contribuições. Esse resultado pode melhorar o U do baseline fixo,
   mas é **ótimo local, não ótimo global**.

Para ambos, VALIDATION usa a **lista congelada** escolhida em TRAIN,
sem reotimização. Publicar M, U, recall, P95/P99 do grau da união
(incluindo observações com grau zero), perda residual de M contra 95%,
e resultado `recall_target_met` por partição. Se não houver suporte M
em TRAIN ou nenhuma união atingir 95%, marcar
`NO_TRAIN_TRUTH` ou `TRAIN_TARGET_INFEASIBLE`; nunca relaxar o
limiar ou fabricar uma escolha.

**Importante:** a escolha por 95% TRAIN não implica que VALIDATION
atinja 95%; nessa situação a proposta segue **não elegível à promoção**.
O estimador não invoca TEST nem reavalia parâmetros de `m/u`/TF.

## Parâmetros estatísticos e escalabilidade

Os custos U observados em 30 mil pessoas-base são úteis para
comparação interna de representações, mas **não** equivalem a
previsões físicas para 11,45 milhões. O crescimento de colisões por
observação depende das frequências e correlações conjuntas das chaves,
quantidade de observações por identidade, mistura de gestores,
concentração dos nomes e diferenças de idade. Simplesmente multiplicar
**todos** os custos por um mesmo fator não altera a ordem de
`U` da otimização exata. Um gate operacional de orçamento precisa
levar em conta fanout máximo/P95/P99 e concentração por chave/gestor,
com simulação ou expansão estatística e validação SQL Server.

`U` significa candidatos não vinculados recuperados pelo blocking;
`U/M` **não é FDR** dos vínculos aceitos depois do único FS.
O estimador de TF exige **identidades-base independentes em TRAIN**,
não aparições de extremidades de pares candidatos. Os sobrenomes
em qualquer posição continuam sendo **proxy Python** até equivalência
demonstrada com `BrazilianNameComponents`. Nenhum CPF pode entrar
no frame probabilístico de blocking. O TEST permanece fechado.

## Gates de CI e fronteiras

A entrega amplia apenas o relatório agregado e testes herméticos;
reutiliza as mesmas tabelas/indexes SQLite `:memory:` e o mesmo
snapshot canônico C# SHA-256 congelado sem gerar outro corpus.
A CI `v9-memory-batch` roda o corpus original seed 42 e publica
apenas estatísticas agregadas; `jornada-ci` completa continua
obrigatória. Concluir separadamente a prova de paridade de conjuntos
de candidatos no SQL Server isolado com chaves versionadas,
latência e distribuição de custo. Não ativar V9, DT-15, alterar Gold,
V8, FS/TF/m/u/thresholds ativos, HML/PROD, JornadaLocal, NODE/NAS
ou o material IBGE de referência.
