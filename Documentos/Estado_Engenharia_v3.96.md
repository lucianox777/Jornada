# Estado de Engenharia — v3.96

**Base Normativa:** v3.62  
**SolutionSchema:** v3.68  
**Solution Engenharia:** v3.96  
**Data:** 03/09/2026

## Natureza

Release de **fechamento documental e incorporação de evidência runtime** sobre a v3.95. Não altera DDL corrente, contratos, código de produção, `PackageReference` ou `packages.lock.json`.

## Evidência externa incorporada

A execução real da v3.95 concluiu com:

- restore `--locked-mode` da Solution, Unit e Integration: PASS;
- build Release: PASS, 0 warnings / 0 errors;
- Unit: **153/153 PASS**;
- Docker Engine: OK;
- imagem SQL canônica por digest: OK, cache local reutilizado;
- SQL Server `ProductVersion=16.0.4265.3`;
- `DBCC CHECKDB(master)`: OK;
- Integration: **58/58 PASS**;
- validação local Release: PASS.

A evidência detalhada está em `Documentos/Evidencia_Runtime_v3.95_2026-09-03.md`.

## Requisitos de Negócio

A v3.96 introduz o baseline documental **Requisitos de Negócio Jornada v1.0**, derivado da Especificação Técnica v3.62 e incorporado em três formatos:

- `Documentos/Requisitos_de_Negocio_Jornada_v1.0.md`
- `Documentos/Requisitos_de_Negocio_Jornada_v1.0.docx`
- `Documentos/Requisitos_de_Negocio_Jornada_v1.0.pdf`

O documento não substitui a Base Normativa. Mudança material em requisito de negócio que afete regra normativa, contrato, dado ou comportamento exige alteração formal da especificação correspondente.

## Proveniência NuGet

O ambiente de empacotamento continua declarando `NO_RESTORE_CLAIMED`, pois não possui SDK .NET. Entretanto, a v3.96 passa a registrar formalmente a evidência externa confiável da v3.95: restore locked de Solution/Unit/Integration passou com os locks atuais, e a v3.96 não altera `.csproj` nem `packages.lock.json` em relação à v3.95.

## Status

**RUNTIME VALIDATED EXTERNALLY** para o caminho local canônico da v3.95: 153/153 Unit e 58/58 Integration. Requisitos de HML/Produção que dependem de governança, dados reais, calibração ou RIPD permanecem externos à presente aprovação.
