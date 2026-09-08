# Identidade progressiva — persistência V1

Estado: persistência e cutover transacional implementados em SQL Server e PostgreSQL. Este documento complementa a decisão arquitetural consolidada em `ADR_Identidade_Progressiva.md`. A implementação não ativa resolução/fusão probabilística e não altera por si só a semântica factual de Gold ou Serving.

## Modelo e semântica

`identidade.pessoa_origem_progressiva` possui uma linha por `silver.pessoa_origem.pessoa_origem_id`, cuja chave estável é `(sistema_origem_id,codigo_pessoa_origem)`. O banco garante unicidade da origem e do UUID inicial, FKs e imutabilidade da origem, `initial_uuid`, instante de criação e referência legada. O UUID é aleatório, não derivado de CPF ou outros dados pessoais, e é reservado em `identidade.pessoa` na mesma transação.

O estado começa em `PROVISORIA`, versão zero, sem referência canônica. Os únicos outros estados públicos são `REFERENCIA` e `INDEFINIDA`. `REFERENCIA` significa que existe uma referência canônica estabelecida segundo a política e as evidências declaradas; não significa certeza absoluta de identidade civil nem valida automaticamente todos os registros associados. Estado técnico de `identidade.pessoa`, estado de `identidade.vinculo_fonte`, situação do CPF e atribuição de cada fato continuam separados.

O termo `RESOLVIDO` permanece em `identidade.vinculo_fonte` porque ali significa que uma observação possui atribuição corrente. Ele não é estado da identidade progressiva. O evento técnico `RESOLUCAO` e os resultados `NOVA_IDENTIDADE`, `ASSOCIACAO_EXISTENTE` e `INDEFINIDA` também permanecem como conceitos do processo de decisão.

A tabela de eventos registra resultado, versão esperada, política, evidência, modelo e universo. O histórico é append-only e o avanço de versão exige recibo correspondente. Nenhum modelo é homologado pela mera existência desse recibo.

`legacy_pessoa_uuid` preserva, quando existir, a atribuição `RESOLVIDO` da última observação da origem no instante da criação progressiva. É fotografia de proveniência para migração, não nova conclusão de identidade. O UUID legado não é reutilizado como `initial_uuid`, pode ser compartilhado por diversas origens e permanece imutável.

## Instalação, backfill e cutover

SQL Server instala `database/Jornada_Identidade_Progressiva.sql` após `database/Jornada_Fase1.sql`. PostgreSQL instala `database/postgresql/Jornada_Identidade_Progressiva.sql` após os cores Resultado, Ingestion Processor e Processor Persistence. Como a solução ainda não foi publicada, ambos os schemas V1 nascem diretamente com `PROVISORIA`, `REFERENCIA` e `INDEFINIDA`; não existe camada pública de compatibilidade `RESOLVIDA→REFERENCIA`.

`ProgressiveIdentityOriginStore.EnsureInitialAsync(sourceId)` exige a origem já persistida. O overload com conexão/transação assegura a referência dentro da transação do chamador. Locks da origem e constraints de unicidade serializam criações concorrentes. Falha da transação não é convertida em UUID persistido fora dela.

`BackfillPageAsync(maxSources)` aceita de 1 a 1000 origens, seleciona apenas as ainda sem referência progressiva e confirma cada uma em transação própria. A operação é limitada, retomável e reentrante. A instalação não faz varredura monolítica da população.

O cutover operacional é deliberadamente em duas fases. Primeiro, o backfill paginado deve chegar a backlog zero. Enquanto houver origem histórica sem `initial_uuid`, a migração de cutover falha fechada. Depois disso, o trigger em `identidade.vinculo_fonte` assegura automaticamente o `initial_uuid` das novas origens no mesmo contexto transacional usado pelo Processor. Em SQL Server o trigger foi colocado em `vinculo_fonte`, e não em `silver.pessoa_origem`, para preservar o `OUTPUT INSERTED` utilizado pelo writer existente.

Rollback de um lote que criou origem, observação, vínculo e referência progressiva desfaz todos esses efeitos; não deixa `identidade.pessoa` nem referência órfã. Nova versão da mesma origem preserva o `initial_uuid` e não duplica o evento de criação.

## Validação e limites

Os workflows dedicados usam bancos descartáveis em SQL Server e PostgreSQL, aplicam instalação repetida e executam harnesses reais. A persistência cobre concorrência, retransmissão, rollback, origem inexistente, referência legada, backfill e imutabilidade. O cutover cobre recusa pré-backfill, convergência do backfill, instalação reentrante, criação automática e rollback transacional nos dois providers.

A criação do UUID inicial no Processor está implementada. Ainda não estão ativados: publicação probabilística de `REFERENCIA`, fusão/separação automática, aliases históricos, recomposição automática decorrente de Linkage, APIs públicas específicas de referência, BI de composição ou thresholds reais. A integração universal da âncora CPF aos escritores e procedimentos de correção permanece etapa necessária.

A issue #31 continua sendo o gate independente de calibração representativa e homologação estatística. Passar CI não autoriza ativação do Linkage probabilístico.
