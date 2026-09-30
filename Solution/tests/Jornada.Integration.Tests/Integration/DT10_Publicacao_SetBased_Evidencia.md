# DT-10 — Evidência para aceite integral da publicação set-based

Base inventariada: `master` em `73bc53af0d427239fd6ec84cc820add8b22313b5`.

## Escopo

Frente exclusivamente de teste e medição. Não altera stored procedure, DDL canônico, migrations, manifesto, Runner, scorer, workflows ou contratos. Os testes pesados são opt-in por `JORNADA_DT10_EVIDENCE=1` e recusam qualquer banco cujo `Initial Catalog` não seja exatamente `JornadaSyntheticDev` (case-insensitive). Não há reset nem uso de `JornadaLocal`.

## Etapa 0 — inventário anterior à escrita

`LinkageProgressivePublicationSqlServerTests` já cobre equivalência funcional entre a rotina escalar e a set-based no baseline exercitado, idempotência do replay do mesmo run, preservação do resultado bruto, precedência de identidade determinística por CPF, associação a referência estabelecida, promoção de `initial_uuid` após busca completa sem candidato e fila governada de conflito sem duplicação.

O inventário não encontrou prova específica de dois publicadores simultâneos, falha injetada após escrita do ledger, nem medição relativa escalar × set-based com duração, leituras e plano em volumes crescentes. Esses são os três itens desta frente.

## Concorrência adversarial

Arquivo: `DT10PublicationEvidenceSqlServerTests.cs`.

1. Dois runs simultâneos e equivalentes sobre duas origens sobrepostas: ambos devem concluir sem deadlock 1205; cada origem termina na mesma referência e com somente uma transição semântica efetiva.
2. Dois runs simultâneos sobre a mesma origem com resultados diferentes: o primeiro mantém a transação aberta após a procedure e o segundo deve aguardar o lock transacional global. Após o commit do primeiro, a tentativa divergente deve ser rejeitada pelo guarda de precedência `51807`, sem deadlock e sem segundo evento.

A expectativa deriva do contrato SQL atual: publicação serializada pelo recurso `Jornada.Linkage.Runner.Publish` e proibição de troca probabilística de referência já estabelecida.

## Rollback sob falha parcial

Arquivo: `DT10PublicationEvidenceSqlServerTests.cs`.

O teste cria, dentro da própria transação, um trigger temporário de injeção sobre `identidade.pessoa_origem_progressiva_evento`. O trigger lança `51998` após o INSERT set-based do ledger e antes dos updates posteriores. A injeção é armada por `SESSION_CONTEXT`, apenas na sessão do teste.

Critérios: erro injetado observado; `XACT_STATE() = -1` com `SET XACT_ABORT ON`; rollback do chamador; zero eventos do run; zero `progressiva_versao` parcial; nenhuma origem parcialmente promovida; trigger de injeção removido pelo próprio rollback.

A semântica medida é a atual: a procedure exige transação `SERIALIZABLE` do chamador e não pode ser confirmada parcialmente após falha.

## Plano e volumetria relativa

Arquivo: `DT10PublicationPlanVolumeSqlServerTests.cs`.

O teste usa por padrão volumes crescentes `10,100,500`, configuráveis por `JORNADA_DT10_VOLUMES`. Para cada volume, o mesmo run/snapshot é medido primeiro pelo caminho escalar e depois, após rollback para savepoint, pelo caminho set-based.

Para ambos são registrados duração observada, leituras lógicas via `SET STATISTICS IO ON` e plano XML via `SET STATISTICS XML ON`. O resultado é escrito como JSON e anexado ao resultado do teste.

A comparação é deliberadamente **relativa**. Não define SLA, throughput suportado, capacidade absoluta, sizing de produção nem extrapolação para dados reais.

## Execução opt-in

```powershell
$env:JORNADA_DT10_EVIDENCE = "1"
$env:JORNADA_TEST_SQL_CONNECTION = "<conexão cujo Initial Catalog seja JornadaSyntheticDev>"
# opcional: $env:JORNADA_DT10_VOLUMES = "10,100,500"

dotnet test .\tests\Jornada.Integration.Tests\Jornada.Integration.Tests.csproj `
  --filter "TestCategory=DT10Evidence"
```

Se qualquer cenário revelar comportamento diferente do contrato, parar no caso mínimo reproduzível. A correção pertence à frente integradora; este PR não deve modificar procedure, DDL ou migration.

## Estado da evidência

Este relatório registra desenho, inventário e critérios. Resultados numéricos e afirmações de aprovação dos cenários opt-in só podem ser acrescentados após execução real em `JornadaSyntheticDev`; não são inferidos do código nem dos gates normais de CI.
