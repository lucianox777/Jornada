# Estado da Engenharia — v3.88

**Base Normativa:** v3.62  
**SolutionSchema:** v3.68  
**Solution Engenharia:** v3.88

## Objetivo

Fechar dois achados da execução real da v3.87: o risco físico do índice Bronze sobre `NVARCHAR(1024)` e a incompatibilidade de API entre Testcontainers 4.14 e Docker Server máximo 1.41.

## Correções

- `objeto_chave` continua `NVARCHAR(1024)`; não há redução/truncamento do contrato persistido;
- novo índice `IX_bronze_entrega_arquivo_payload_sha256` usa `payload_sha256 CHAR(64)` como chave e `objeto_chave` como `INCLUDE`;
- o índice antigo é removido somente após a criação do novo;
- queries de retenção/GC filtram pelo SHA indexado e confirmam a chave completa;
- a fixture Integration consulta a API máxima do Docker Server e define `DOCKER_API_VERSION` apenas quando precisa reduzir o default 1.44.

## Evidência externa conhecida da v3.87

- SQL Server Developer local: bootstrap concluído;
- `dotnet restore Jornada.sln --locked-mode`: concluído;
- build Release com `-warnaserror`: 0 warnings / 0 errors;
- Unit: 153 de 153 aprovados;
- Integration: 58 falhas de setup, todas antes dos casos, causadas por `client version 1.44 is too new. Maximum supported API version is 1.41`.

## Pendente

Reexecutar v3.88 em ambiente real: bootstrap limpo, restore locked, build Release, Unit, Integration e FaultInjection.
