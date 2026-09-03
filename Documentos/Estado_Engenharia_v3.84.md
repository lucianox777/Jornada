# Estado da Engenharia — v3.84

**Base Normativa:** v3.62
**Solution Engenharia:** v3.84
**Data:** 02/09/2026

## Escopo desta release

A v3.84 é uma correção de fronteira de testes e supply chain. Não altera regras funcionais, Base Normativa ou schema persistido.

Principais mudanças:

- `Jornada.Tests` deixa de referenciar `Testcontainers.MsSql`;
- `Jornada.Integration.Tests` mantém Testcontainers e passa de dez para sete `ProjectReference` diretos: Bronze.Storage, Bronze.Maintenance.Worker, Operations.Maintenance.Worker, Pipeline.Coordination, Contracts, Api e Processor.Worker;
- `Jornada.Ingestion` permanece disponível transitivamente onde necessário por Api/Processor, sem referência direta de conveniência;
- Linkage.Parameters.Worker e Linkage.Runner deixam de ser dependências do projeto Integration;
- FaultInjection no CI e nos scripts locais é executado no assembly `Jornada.Integration.Tests`;
- `nuget-lock-gate.py` rejeita pacotes `Direct` excedentes, e `nuget-lock-provenance.json` distingue lock herdado de lock derivado sem restore local.

## Estado de validação

- **Build/Unit:** último resultado registrado no snapshot v3.82: 99/99 Unit aprovados.
- **Validações estáticas v3.84:** executadas no ambiente de empacotamento.
- **Runtime v3.84:** não reexecutado localmente; .NET SDK/Docker indisponíveis.
- **Locks NuGet:** 12 herdados sem alteração da v3.83; 2 derivados por poda de alcançabilidade após as mudanças de dependência. O pacote declara `NO_RESTORE_CLAIMED` e exige confirmação por `dotnet restore Jornada.sln --locked-mode` na promoção.

## Critério de fechamento

A v3.84 somente deve ser considerada runtime-validada após o CI executar restore bloqueado, build, Unit, Integration e FaultInjection sem skips/falhas. A distribuição não transforma validação estática em evidência de restore.
