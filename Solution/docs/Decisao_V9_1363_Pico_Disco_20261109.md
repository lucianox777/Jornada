# V9 #1363 — pico de disco no relatório analítico estatístico (execução)

**Decisão PO 09/11/2026; estado: IMPLEMENTAÇÃO EM PR ISOLADA, PENDENTE CI/PROVA FÍSICA.** GOV-DOC-01.

## Contexto e classificação
O PO solicitou duração por etapa (#1362) e **pico de disco junto à RAM** (#1363) no relatório de calibração V9. A leitura do código mostrou que não havia medição de disco. O uso de disco **não pode** ser deduzido de RAM, bytes de I/O ou disponibilidade do volume compartilhado.

## Contrato da primeira medição implementada
- `V9CalibrationDiskSampler` monitora a **soma dos comprimentos lógicos dos arquivos** no **diretório NOVO de saída do Calibrador**, incluindo subdiretórios, desde o início da execução, sem ler conteúdo, com amostras periódicas a cada **500 ms** e nos limites de lote/exportação/relatório. Mantém *high-water mark* mesmo após remoção de um arquivo amostrado. Guarda baseline, amostra final, pico e instante monotônico, falhas e sucesso.
- Sem observar processos, corpus raw, nomes, CPFs, credenciais, caminhos do usuário, métricas de seleção ou conteúdo científico. `mu-statistical-analysis.json` inclui objeto `diskHighWater` e Markdown informa pico, baseline e atual em MiB, amostragem, escopo, estado e ressalvas. `runtime-diagnostics.json` preserva o contrato existente e passa a incluir `diskHighWaterOutputAtRuntime` após as saídas do relatório.
- **Escopo e limites decisivos:** isso mede tamanho **lógico dos arquivos da saída**, **não** bytes de blocos efetivamente alocados no filesystem (compressão/sparse/COW), não mede diretórios temporários externos, disco do contêiner SQL, corpus de entrada, I/O nem ocupação total do host. Essas grandezas ficam expressamente **NÃO MEDIDAS**, para evitar inventar um "pico de uso físico". Arquivos muito breves, criados e removidos entre amostras, podem escapar; o máximo é **pico observado**, nunca prova de um máximo absoluto.
- Falhas de leitura/sistema geram estado `NOT_MEASURED` ou `PARTIAL_SAMPLED` sem lançar erros que possam alterar o Calibrador; scanner impede seguir reparse points, não escreve nem apaga arquivos. Este caminho não amplia o corpus ou injeta seed científica, e não modifica TCP, modelo, parâmetros, FDR ou TEST.

## Evidências e validação requerida
Cobertura unitária: crescimento e remoção temporária, diretórios aninhados, arquivos fora do escopo, acesso negado e medição parcial sem vazamento de caminho; regressão de JSON/Markdown/runtime no SHA exato da PR e master. **Nenhum pico físico real é declarado até execução física.** #1363 permanece aberta até confirmação de que o PO considera a métrica lógica suficiente ou até uma instrumentação segura de blocos físicos atribuíveis; não substituir medição ausente por zero. Registro [issue #1363](https://github.com/lucianox777/Jornada/issues/1363), GOV-DOC-01.

## Precedência
Observabilidade da V9 e #1302; não substitui decisão de **sem teto de registros** da calibração, campanha 100k #1349, isolamento TEST/Gold ou impedimento de segurança #1253. PR #1362 tem escopo e processo de integração separados; não empilhar sobre esse HEAD nem alterar ciência.
