# Estado de Engenharia — v3.91

- Base Normativa: **v3.62**
- SolutionSchema: **v3.68**
- Solution Engenharia: **v3.91**
- Predecessora: **v3.90**
- Estado: **correções runtime aplicadas; reexecução Integration pendente**

## Evidência predecessora

A v3.90 executou limpeza, restore locked, build Release e Unit 153/153 com sucesso. A suíte Integration chegou aos 58 casos: 30 passaram e 28 falharam. A v3.91 corrige os grupos de causa-raiz demonstrados por esse log, sem alterar dependências ou a Base Normativa.

## Correções

- referência correta de `identidade.identity_map` no UPDATE governado;
- leitura do `ReservedBatch` sem `SequentialAccess` incompatível;
- reset de `NOCOUNT`/`XACT_ABORT` no runner de bootstrap de testes;
- seed DEV convergente após retenção/processor;
- fixture territorial coerente com `situacao_geografia NOT NULL`;
- nome de banco isolado `JornadaIntegration_Test_<guid>`.

## Pendência

Executar `scripts/local-clean.ps1` e depois `scripts/local-validate-release.ps1` em ambiente .NET 8.0.424 + Docker. Só uma saída final 58/58 PASS fecha o runtime Integration v3.91.
