# Calibrador — paridade de blocking dinâmico no SQL Server

Status: implementado tecnicamente; ativação probabilística continua sujeita aos gates explícitos de validação/ativação.

## Contexto

O alvo relacional operacional de HML/Produção da Jornada é Microsoft SQL Server. O `Program.cs` do `Jornada.Linkage.Parameters.Worker` usa `LinkageParametersWorker` quando `Database:Provider` é SQL Server e usa `PostgreSqlLinkageParametersWorker` apenas quando o provider é PostgreSQL.

## Paridade implementada

O caminho SQL Server (`LinkageParametersWorker`) captura as amostras m/u, estima os parâmetros probabilísticos e agora usa as mesmas amostras para criar `BlockingFeatureObservation`, executar `BlockingRuleSetSearch.SearchBest(...)` com `BlockingCandidateFeatureCatalog.RequiredCalibratorCandidates` e criar o `LinkageDynamicRuleSet` correspondente.

Os limites da busca bounded continuam vindo de `BlockingRuleSetSearchConfiguration`: na ausência de configuração explícita são preservados os defaults históricos do algoritmo; overrides inválidos falham fechado pela validação já existente.

`LinkageRuleSetWriter.WriteAsync(...)` persiste o ruleset na mesma transação que grava os parâmetros e promove o modelo de `GERANDO` para `RASCUNHO`. Assim, falha na escrita/validação do ruleset reverte a publicação do draft; modelo e ruleset não podem ser publicados parcialmente.

Desde `FS_DECISION_THRESHOLD_PARETO_V1`, o caminho SQL Server também deixa de aceitar `T_LINKAGE` e margem como constantes operacionais. O split determinístico por pessoa-base ocorre antes das amostras m/u e da busca do ruleset: apenas TRAIN alimenta estimação e blocking. Depois de congelados m/u + ruleset, observações CPF rotuladas das partições VALIDATION/TEST são reexecutadas com CPF oculto usando exatamente o scorer/policy compilado em `Jornada.Linkage.Core`. A grade de threshold/margem nasce somente de VALIDATION; TEST não retroalimenta a escolha e pode apenas bloquear promoção.

Os parâmetros incorporados ao fingerprint são arredondados para a mesma escala `DECIMAL(30,12)` usada na persistência SQL Server, permitindo reconstrução canônica pelo `LinkageRuleSetReader`.

## Fail-closed de promoção

Para modelos produzidos pelo método `M_INTERGESTOR_U_GOLD_SERIALIZED`, `VALIDATE` e `ACTIVATE` exigem um `identidade.linkage_ruleset` com ao menos um passe e campos completos. Ausência ou incompletude do ruleset impede a promoção. `GENERATE_DRAFT` também recusa amostra u vazia.

A implementação não altera a precedência determinística CPF→UUID, não reativa PostgreSQL como banco operacional, não ativa modelos automaticamente e não introduz thresholds institucionais novos.

## Evidência técnica de fechamento

O fechamento é coberto por:

1. `BlockingFeatureObservationFactory.Create(...)` sobre as mesmas amostras m/u do modelo SQL Server;
2. busca canônica via `BlockingRuleSetSearch.SearchBest(...)` com as features elegíveis vigentes;
3. `LinkageDynamicRuleSet` usando o mesmo `algoritmo_versao` do modelo;
4. persistência de parâmetros + ruleset + publicação do RASCUNHO na mesma transação;
5. teste de integração SQL Server `SqlServerLinkageRuleSetRoundTripTests`, que grava modelo/ruleset, reconstrói pelo reader e compara fingerprint/projeção/passes;
6. `VALIDATE`/`ACTIVATE` fail-closed quando o modelo SQL Server exige ruleset e ele está ausente/incompleto;
7. testes de `BlockingRuleSetSearchConfiguration` preservando defaults, overrides válidos e rejeição de valores inválidos.

Os valores finais de configuração do blocking continuam sujeitos à calibração representativa e à governança da issue #31; este fechamento trata somente da paridade técnica de geração/persistência.
