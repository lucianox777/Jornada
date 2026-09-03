# Estado da Engenharia — v3.87

**Base Normativa:** v3.62  
**SolutionSchema:** v3.68  
**Solution Engenharia:** v3.87

## Objetivo

Corrigir duas falhas detectadas em execução real do bootstrap SQL Server da v3.86, sem modificar a semântica da Base Normativa ou o modelo persistido.

## Correções

- opções `SET` obrigatórias declaradas no DDL corrente e nos baselines executáveis;
- `sqlcmd -I` no bootstrap local;
- SQL dinâmico com `QUOTENAME` passa a ser montado antes e executado por `sys.sp_executesql`;
- gate estático impede a reintrodução do padrão inválido.

## Evidência conhecida

A execução externa da v3.86 comprovou criação do ambiente Docker, container SQL Server, health check e conectividade `sqlcmd`. Foram observados `Msg 1934` por `QUOTED_IDENTIFIER` e depois `Msg 102` próximo a `QUOTENAME`.

## Pendente

Executar novamente o ciclo limpo de `local-db.ps1 -Action up`, seguido de restore `--locked-mode`, build Release, Unit, Integration e FaultInjection.
