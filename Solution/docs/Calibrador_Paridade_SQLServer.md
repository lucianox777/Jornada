# Calibrador — paridade de blocking dinâmico no SQL Server

Status: dívida técnica verificada; não autoriza ativação probabilística.

## Contexto

O alvo relacional operacional de HML/Produção da Jornada é Microsoft SQL Server. O `Program.cs` do `Jornada.Linkage.Parameters.Worker` usa `LinkageParametersWorker` quando `Database:Provider` é SQL Server e usa `PostgreSqlLinkageParametersWorker` apenas quando o provider é PostgreSQL.

## Lacuna verificada

No estado corrente, os dois caminhos não têm paridade para geração do plano dinâmico de blocking.

O caminho PostgreSQL (`PostgreSqlLinkageCalibrator`) executa `BlockingRuleSetSearch.SearchBest(...)`, cria `LinkageDynamicRuleSet` e o persiste com `LinkageRuleSetWriter.WriteAsync(...)` dentro da transação do modelo.

O caminho SQL Server (`LinkageParametersWorker`) captura as amostras, estima m/u e publica o modelo RASCUNHO, mas não executa `BlockingRuleSetSearch` e não persiste `identidade.linkage_ruleset` durante `GENERATE_DRAFT`.

`LinkageRuleSetWriter` já é provider-independent (`DbConnection`/`DbTransaction`) e suporta a persistência transacional necessária. Portanto, a dívida não é de schema nem de writer; é de integração do plano de blocking no caminho SQL Server.

## Consequência

Até existir paridade, um modelo gerado pelo caminho operacional SQL Server não deve ser considerado equivalente ao fluxo calibrado que inclui descoberta e congelamento do ruleset dinâmico. Isso não altera a precedência determinística CPF→UUID e não autoriza reativar PostgreSQL como banco operacional.

A correção deve preservar comportamento fail-closed: modelo e ruleset devem ser produzidos e promovidos juntos, sob a mesma identidade/fingerprint, sem inventar thresholds, número final de passes ou política institucional.

## Critério técnico de fechamento

A dívida pode ser considerada fechada quando o caminho SQL Server:

1. cria `BlockingFeatureObservation` a partir das mesmas amostras m/u capturadas para o modelo;
2. executa a busca canônica de ruleset com as features elegíveis vigentes;
3. cria `LinkageDynamicRuleSet` usando a mesma versão de algoritmo/modelo;
4. persiste o ruleset na mesma transação que publica o RASCUNHO;
5. possui teste de integração SQL Server provando round-trip modelo + ruleset;
6. mantém `VALIDATE`/`ACTIVATE` fail-closed quando o modelo exigir ruleset e ele estiver ausente.

Nenhum desses itens define os valores finais de configuração do blocking; isso permanece sujeito à calibração representativa e à issue #31.
