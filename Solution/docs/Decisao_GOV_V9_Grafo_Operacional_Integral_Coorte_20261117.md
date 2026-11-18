# GOV-V9-GRAFO-OPERACIONAL-INTEGRAL-COORTE-20261117

**Autoridade:** Product Owner, 17/11/2026. **Estado:** DECIDIDO; implementação e validação pendentes. **Política:** GOV-DOC-01.

## Decisão
Integrar ao runtime científico V9 o **grafo completo de candidatos operacionais**, preservando a resolução global por coorte. A execução científica deve **falhar fechada antes de pontuar, persistir decisões ou publicar** quando o grafo estiver incompleto, truncado, inconsistente com a origem operacional ou sem evidência verificável de completude. A etapa subsequente será persistência das decisões e controles de publicação, sem antecipar sua liberação.

## Contexto e justificativa
A ponte opt-in de leitura física SQL, comparação de contagem/hash de arestas e preparação da coorte antes da persistência foi documentada em GOV-V9-GRAFO-FISICO-RUNASYNC-REGRESSAO-20261116. Seus testes estruturais não demonstram paridade integral com o formato produtivo nem ausência de truncamento. A presente decisão exige a integração efetiva e verificável, não apenas regressão estrutural.

## Critérios de aceite da implementação
1. Obter o universo integral de candidatos da fonte operacional, incluindo arestas e identificadores necessários à competição entre observações, sem paginação parcial, limites silenciosos ou amostragem.
2. Preservar os limites e a semântica da coorte e resolver competição globalmente na coorte, não por página ou lote.
3. Comparar a representação consumida com a origem por contagens, identificadores, hash canônico e integridade de referências, detectando omissões, duplicações, arestas órfãs e truncamento; rejeitar qualquer divergência antes de efeitos persistentes.
4. Testar fisicamente SQL com coortes repartidas entre páginas/lotes, grafos densos, limites de volume, falha de leitura e discrepâncias deliberadas; comprovar bloqueio e ausência de decisões/publicação parcial.
5. Registrar evidências reproduzíveis (SHA do código, run, coorte, assinatura do grafo e resultado dos gates), sem expor dados pessoais.
6. Manter publicação desabilitada no runtime científico até decisão posterior expressa do PO sobre persistência e controles de publicação.

## Impactos e precedência
Afeta ingestão do grafo, validação de completude, scoring/resolução e gates de execução do V9. Não altera thresholds, TFs, vencedor, política de TEST one-use nem autoriza TRAIN/TEST ou publicação. Complementa e torna mais exigente GOV-V9-GRAFO-FISICO-RUNASYNC-REGRESSAO-20261116. Respeita as decisões vigentes de isolamento TEST, artefato científico e separação do executor operacional. Implementação e validação só serão declaradas após código, testes e evidências na master.

## Rastreabilidade
PO 17/11/2026; GOV-DOC-01; issue #1584; PR #1586; `Solution/src/Jornada.Linkage.Runner/V9PinnedSyntheticGraphCohortRunScorer.cs`; `Solution/src/Jornada.Linkage.Runner/ProbabilisticLinkageBatchRunner.cs`. Próximas entregas: integração física e provas; depois persistência de decisões e gates de publicação.

## Progresso de implementação na PR #1655 (17/11/2026; ainda não certificado)
A ponte sintética descartável `V9FrozenCohortSqlCandidateGraph` passou a exigir censo SQL independente das assinaturas efetivamente materializadas após `SqlBulkCopy`, censo de pares distintos e reconciliação exata com as arestas lidas, além de ordem estrita e limites fail-closed. Guardas de código-fonte foram ampliados em `test_v9_physical_runasync_graph_guards.py`. Commits: `e486576`, `3ef5789`, `1e87b7d`, `c9bb9fa`. **Não constituem teste físico executado nem certificação de completude operacional**: o adaptador permanece limitado a 512 observações e `JornadaHarness`; ainda faltam integração com o grafo operacional em escala, testes SQL reais, evidência de ausência de persistência parcial e controles de publicação. A autorização científica permanece bloqueada.

### Proteção adicional de integridade SQL (17/11/2026)
A tabela temporária de assinaturas passou a exigir índice SQL único por `(obs_id, pass_id, assinatura)`, impedindo duplicações físicas mesmo quando a contagem total coincidir. Commit `4933039`; guarda estrutural `7f638b5`. A PR está à frente da `master`, mas as verificações de build/SQL físico ainda precisam ser executadas; não há autorização de produção, TEST científico ou publicação.

### Reconciliação de conteúdo das assinaturas (17/11/2026)
O censo de linhas e o índice único, isoladamente, não detectam substituições de conteúdo que preservem a contagem. O componente passou a ler e comparar integralmente o conjunto de tuplas `(obs_id, gestor, pass_id, assinatura)` materializado no SQL com o conjunto planejado em memória, recusando registros estranhos, duplicações e ausências antes da consulta de arestas. Commits `f1cad2e` e `b67e0d6`. A comparação é limitada ao harness atual; a solução para escala operacional, testes físicos e evidência de execução continuam pendentes.

### Evidência canônica do manifesto (17/11/2026)
O recibo de leitura SQL agora expõe `CanonicalSignaturesSha256`, calculado a partir das tuplas de assinaturas SQL efetivamente verificadas e ordenadas de forma determinística; complementa `CanonicalEdgesSha256`. Commits `73f93d4` e `886dd07`. O digest identifica o manifesto do harness, mas **não substitui** a prova de completude operacional, testes físicos, vinculação imutável a run/coorte ou certificação de publicação. A assinatura inclui identificador do gestor e hashes, não nomes brutos.

### Revisão de digest e validação (17/11/2026)
Corrigido o delimitador de linhas do manifesto SHA-256 para LF efetivo, evitando codificação literal de barra invertida + n (commit `3caf0db`). Consulta de workflow runs associadas ao commit não retornou execução; logo não há evidência de build ou teste físico. A PR continua sem certificação e o caminho de parâmetros científicos SQL não foi alterado por este commit. A decisão separada GOV-V9-DESATIVACAO-PARAMETROS-CIENTIFICOS-SQL-20261117 exige inventário e migração não destrutiva.

### Compatibilidade dos identificadores com SQL Server (17/11/2026)
O censo de assinaturas rejeita antes do `SqlBulkCopy` gestores e identificadores de passes com mais de 120 caracteres ou espaço U+0020 final, evitando truncamento/igualdade SQL divergente da identidade ordinal C#. Commits `af3a34c` e `1de08ad`. A verificação é estrutural, sem execução física confirmada; não habilita o runtime produtivo e não altera o carregamento de parâmetros V9 por pacote.

### Igualdade SQL e identidade ordinal (17/11/2026)
As colunas `gestor`, `pass_id` e `assinatura` da tabela temporária de assinaturas receberam `COLLATE Latin1_General_100_BIN2`, para evitar que a collation padrão do SQL Server una candidatos por equivalência insensível a maiúsculas/acentos que não corresponde ao comparador ordinal do planejador .NET. Commits `3ce7b0f` e `52b3258`. A alteração vale apenas para a tabela temporária do harness; não certifica paridade com `identidade.blocking_chave`, nem substitui execução física. A integração operacional e a fonte única de parâmetros por pacote continuam como critérios de conclusão.

### Gate automatizado de validação física da PR (17/11/2026)
O workflow `.github/workflows/v9-test-fullschema-runasync-smoke.yml`, anteriormente restrito a `workflow_dispatch`, passou a aceitar `pull_request` com filtros para código do Runner/Core, testes V9 e o próprio workflow (commit `5efba6a`). O job existente compila a solução de testes, prepara SQL Server descartável `JornadaHarness`, executa `RunAsync` físico não publicador com 200 observações e falha injetada no segundo lote, e publica recibo explicitamente **não certificador**. A etapa inicial agora também executa os testes estruturais de grafo e carregamento por pacote (commit `b1a1e20`). Consulta imediata às execuções e status do commit `b1a1e20` não retornou resultados; **não há ainda confirmação de execução ou aprovação**. O job não prova TEST científico independente 100k, nem paridade produtiva, e não autoriza retirar o bloqueio de ativação V9.

### Reconciliação independente das arestas (17/11/2026)
Além de comparar o censo e o stream SQL, o componente agora constrói um conjunto independente de pares a partir das assinaturas C# congeladas, aplicando deduplicação entre passes e exclusão de pares do mesmo gestor. O grafo SQL somente é aceito se o conjunto de arestas for exatamente igual ao oráculo em memória (`expectedPairs.SetEquals(edges)`); contagens iguais isoladas não bastam. Commits `6678808` e `db711c5`. Escopo: até 512 observações no `JornadaHarness`, sem equivalência produtiva certificada. Requer execução de testes físicos antes de concluir a PR.

### Limite do oráculo independente (17/11/2026)
A materialização do conjunto C# de arestas esperado também falha explicitamente se exceder `MaximumPairIncidences`, sem truncamento ou comparação parcial (commits `975b5ae` e `b607635`). O limite continua exclusivo do smoke descartável; não é parâmetro de capacidade produtiva aprovado. A ativação do Runner V9 permanece bloqueada até integração e certificação próprias.

### Disparo da CI antes da integração na master (17/11/2026)
Para não depender exclusivamente do evento `pull_request` quando o próprio workflow foi introduzido na branch, foi adicionado o gatilho `push` restrito a `docs/gov-v9-grafo-operacional-coorte-20261117` (commit `dcfceaf`). O disparo mantém o mesmo job descartável, sem publicação e sem certificação científica. Não constitui evidência de sucesso até haver run e recibo verificados. Após integração, o gatilho temporário específico da branch deverá ser retirado ou substituído por política permanente aprovada.

### Falha física confirmada na CI e correção (17/11/2026)
Run GitHub Actions `44171877566`, job `134352842979`: guards, build .NET, bootstrap SQL Server e schema/preflight passaram; a execução real `RunAsync` falhou com SQL Server erro 8156 (`The column 'obs_id' was specified multiple times for 'complete_graph'`) no censo de `V9FrozenCohortSqlCandidateGraph.ReadAsync`. Causa: subconsulta derivada projetava `a.obs_id,b.obs_id` com nomes iguais. Correção: aliases `source_obs_id` e `target_obs_id` somente no subselect do censo (commit `e1ced69`), com guarda estrutural (commit `dfe5f5c`). Reexecução física ainda necessária; não há aprovação ou certificação científica decorrente desta correção.

## Continuidade — validação canônica do digest do pacote (17/11/2026)
No carregador de seleção V9, exigir `WinnerSha256` com exatamente 64 caracteres hexadecimais minúsculos antes de resolver o caminho do artefato. A medida elimina representações ambíguas do identificador pinado e falha fechada com `RUNNER_V9_ARTIFACT_SHA_INVALID`. Teste estrutural cobre o formato e a ordem de validação. Impacto: rejeição de seleções malformadas; nenhum ajuste de TF4, score, SQL, modelo legado ou liberação de execução produtiva. Rastreabilidade: `V9ConfiguredArtifactLoader.cs`, `test_v9_configured_artifact_loader.py`, PR #1655; CI do novo HEAD pendente.

## Continuidade do desacoplamento calibrador → Runner — 17/11/2026

O PO reafirmou que o escopo prioritário é **separar o calibrador científico do Runner processador**. O contrato de fronteira é `<winnerSha>.operational.json` imutável, referenciado pelo envelope do seletor `Linkage:V9SelectionFile`. AUDIT entrega finalistas; VALIDATION escolhe o winner final por critérios científicos; TEST usa somente o winner congelado; essas responsabilidades não serão alteradas. O Runner não pode executar calibração, escolher winner ou buscar parâmetros científicos V9 no SQL. SQL de observações/processamento operacional não é proibido por essa decisão.

**Implementação incremental nesta PR:** `V9ConfiguredArtifactLoader.LoadCohortRuntimeAsync` já constrói runtime em memória a partir do snapshot validado e verifica SHA/contrato, sem modelo científico SQL. A leitura do envelope do seletor foi reforçada com limite de 64 KiB **antes** de `ReadAllBytesAsync` (e conferência posterior), com teste estrutural de regressão. Commits `d603b6e` e `79fef5d`.

**Limite da entrega:** o `Program.cs` permanece com `RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED`; ainda falta conectar o runtime carregado por arquivo ao pipeline produtivo de lotes/coorte e comprovar paridade física, segurança e CI no SHA final. Não remover a trava nem afirmar desacoplamento produtivo completo com base apenas no carregador. A PR #1656 trata da precedência de prioridades, sem substituir esta implementação.

## Contrato de integridade do grafo — entrega incremental (17/11/2026)

A branch `feat/v9-operational-graph-integrity-contract-20261117` introduz `V9OperationalCandidateGraphIntegrity.Verify`: recebe IDs congelados da coorte, arestas consumidas e **censo independente da origem** (quantidade de observações, arestas e SHA-256 canônico). Rejeita IDs duplicados/inválidos, contagens divergentes, arestas órfãs, laços, orientação não canônica, duplicações e hash divergente. Canonização proposta para o contrato: arestas não direcionadas `A < B`, ordenadas por `(A,B)`, UTF-8 por linha `A:B\\n` com números decimais invariantes; SHA-256 minúsculo. Não impõe limite de páginas/lotes, não acessa SQL, não pontua e não publica. **Esta entrega NÃO demonstra independência do censo, paridade SQL, completude operacional, escalabilidade nem certificação**; esses itens dependem de adaptador físico e testes posteriores. Teste incluído é estrutural e não substitui testes de execução. Justificativa: separar o gate de integridade global da futura leitura SQL, tornando explícito o contrato de rejeição antes de efeitos persistentes. Precedência: decisão de grafo integral desta página, isolamento TEST, artefato V9 imutável e GOV-DOC-01. Índice de decisões permanece inalterado porque não há nova decisão do PO.
