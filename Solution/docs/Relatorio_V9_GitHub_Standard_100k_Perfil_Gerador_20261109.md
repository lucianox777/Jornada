# V9 — perfil físico do GERADOR 100k no GitHub Actions padrão (09/11/2026)

**Status:** **PASS restrito ao gerador sintético 100k** no runner `ubuntu-24.04`. **NÃO COBERTO:** calibração física multiparâmetro V9 100k com SQL e 24 hipóteses; segurança #1253 permanece aberta. Este relatório não é certificação FDR, nem aprovação de TEST/Gold/DT-15/ativação. GOV-DOC-01.

## Identidade da execução e proveniência imutável

- **Código exato da master (merge #1379):** `b4902dbe0f0ee815eafd4aa48fa216be23c9c382`.
- **PR de implementação do PO:** [#1379](https://github.com/lucianox777/Jornada/pull/1379), HEAD testado `9aeaade523bb8e642e1be3e772bcce1dbb8fd957`; [fast CI #42663701641](https://github.com/lucianox777/Jornada/actions/runs/42663701641) SUCCESS antes do merge.
- **Run físico do perfil (sem SQL):** [#42664056609](https://github.com/lucianox777/Jornada/actions/runs/42664056609), **SUCCESS** no SHA acima; job `synthetic-generator-only` concluído com sucesso.
- **Artifact agregado publicado pelo GitHub:** ID `13694813389`, `v9-standard-100k-generator-only-b4902dbe0f0ee815eafd4aa48fa216be23c9c382`. Conteúdo inspecionado: apenas `host-capacity.json` e `corpus-resource-profile.json`. **Não** foram publicados CSV, gabarito, dados nominais, CPF, strings de conexão ou segredo.
- **Corpus técnico:** `100000` identidades sintéticas, seed `42`, perfil `clean` e `DEMOGRAPHIC_PRIMARY_V1`, autenticado por verificação de hashes dos três arquivos declarados.
- **SHA-256 do manifesto:** `9ec7666eee35a47ed0f487ead6b13b6f73d30b16470132bdeb17c6a9f99eb20e`.
- **SHA-256 de `observacoes.csv`:** `6a2cb2ffd48d14b705faeb14ec4dd517bef94ced730e20a062e0dd5403c832b1`.

## Resultados medidos, sem extrapolação

| Métrica | Valor do artifact | Interpretação / escopo |
|---|---:|---|
| Requisito de RAM total para admissão do perfil | **PASS** | Piso operacional de 12 GiB no momento do preflight; não prova capacidade SQL+24 fits |
| Requisito de disco para admissão do perfil | **PASS** | Piso operacional de 6 GiB livres no momento da leitura |
| RAM total do host | **16.760.344.576 B** (15,61 GiB) | `/proc/meminfo` na máquina do job |
| RAM disponível inicialmente | **15.016.165.376 B** (13,99 GiB) | Amostra inicial; **não** RAM livre garantida durante calibração |
| Disco livre inicialmente no diretório de trabalho | **90.662.481.920 B** (84,44 GiB) | **Observação real desta VM**, não tamanho mínimo garantido de SSD pela documentação do GitHub |
| Pico RSS **apenas do processo gerador** | **514.830.336 B** (490,98 MiB) | GNU time Linux, não inclui SQL/C# calibrador multiparâmetro |
| Tamanho lógico total dos arquivos gerados | **28.555.263 B** (27,23 MiB) | Soma dos 4 arquivos do corpus e manifesto, não alocação física de todo o host |
| Disco livre após geração | **90.556.649.472 B** (84,34 GiB) | Amostra posterior; diferenças incluem outras operações da VM, não consumo atribuível integralmente ao gerador |
| TEST/Gold/ativação | **FALSE** | Não usados |

**Observação importante:** a [documentação GitHub](https://docs.github.com/en/actions/how-tos/write-workflows/choose-where-workflows-run/choose-the-runner-for-a-job) publica **16 GB de RAM e 14 GB SSD** para `ubuntu-24.04` em repositório público, enquanto a amostra do espaço livre do filesystem de trabalho nesta execução superou 84 GiB. **Não assumir, por causa dessa única amostra, que todos os runners terão esse espaço livre**, nem substituir gates dinâmicos por promessas de hardware nominal.

## Interpretação técnica, limitações e próximos gates

O resultado confirma **somente que o gerador sintético SHA-verificado consegue produzir 100 mil identidades no GitHub Actions padrão com recursos modestos neste ensaio**. Isso é uma evidência física concreta favorável à decisão do PO de evitar uma máquina própria. **Não** mede a memória ou o disco do SQL Server, os 24 encaixes V9 (V2 testemunha/âncora, V3, TF, M/U e misturas), o conjunto de pares de candidatos, resolução, seleção, TP/FP/FN, FDR observado ou p95 de latência da prova física.

A via full V9 100k permanece `workflow_dispatch` manual, restrita ao PO, com `expected_sha` exato, mesma parametrização oficial 24/16/8, cap SQL operacional 4096 MB e contêiner 5 GiB, gates reais de RAM/disco **antes do SQL** e **antes do fit**, nenhum TEST/Gold e nenhum resultado falso por insuficiência de recurso. A execução física V9 multiparâmetro permanece **BLOQUEADA pelo preflight #1253** e não foi disparada.

**Estado #1349:** **ABERTA; NÃO COBERTO o objetivo 24/24 fits 100k**, apesar de PASS do gerador. A prova física completa e seu relatório de consumo/qualidade só podem ser declarados após execução real e artifacts verificados no SHA autorizado.

## Precedência e documentação

Rastreia e confirma a etapa preliminar da [decisão PO GOV-V9-GITHUB-STANDARD-100K-OPT-20261109](Decisao_V9_GitHub_Standard_100k_Otimizacao_20261109.md), substituindo a preferência por self-hosted da #1378. Não altera nem revoga [ausência de teto universal de registros](DC-V9-CALIBRACAO-SEM-EXCLUSIVIDADE-30K-20261105.md), contrato do plano oficial 100k, bloqueio de segurança #1253, release V9.0.0, separação TEST e certificação científica. [Issue de prova completa #1349](https://github.com/lucianox777/Jornada/issues/1349), [índice de decisões](Indice_Decisoes_Vigentes.md). GOV-DOC-01.
