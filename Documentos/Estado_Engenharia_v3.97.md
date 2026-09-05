# Estado de Engenharia — v3.97

**Base Normativa:** v3.62  
**SolutionSchema:** v3.68  
**Solution Engenharia:** v3.97  
**Data:** 03/09/2026

## Natureza

Release **documental** sobre a v3.96. Não altera DDL corrente, contratos, código de produção, `PackageReference` ou `packages.lock.json`.

## Requisitos Técnicos

A v3.97 introduz o baseline documental **Requisitos Técnicos Jornada v1.0**, em três formatos:

- `Documentos/Requisitos_Tecnicos_Jornada_v1.0.md`
- `Documentos/Requisitos_Tecnicos_Jornada_v1.0.docx`
- `Documentos/Requisitos_Tecnicos_Jornada_v1.0.pdf`

O baseline contém **65 requisitos técnicos** e uma matriz explícita de rastreabilidade para os **36 Requisitos de Negócio Jornada v1.0**.

## Modelo documental

A Jornada passa a tratar formalmente:

1. **Requisitos de Negócio v1.0** — necessidades e resultados esperados, com ciclo institucional próprio;
2. **Requisitos Técnicos v1.0** — obrigações de arquitetura/engenharia e critérios de aceite técnico, com evolução independente;
3. **Especificação Técnica v3.62 / Base Normativa** — autoridade normativa superior em caso de divergência.

A separação evita que uma evolução técnica de segurança, performance, plataforma ou operação force alteração artificial do baseline de negócio. A rastreabilidade RN -> RT preserva análise de impacto.

## Evidência runtime herdada

A v3.97 não altera comportamento funcional. Permanece válida como evidência incorporada a execução real da v3.95:

- restore `--locked-mode` da Solution, Unit e Integration: PASS;
- build Release: PASS, 0 warnings / 0 errors;
- Unit: **153/153 PASS**;
- Docker Engine: OK;
- imagem SQL canônica por digest: OK, cache local reutilizado;
- SQL Server `ProductVersion=16.0.4265.3`;
- `DBCC CHECKDB(master)`: OK;
- Integration: **58/58 PASS**;
- validação local Release: PASS.

## Proveniência NuGet

O ambiente de empacotamento continua declarando `NO_RESTORE_CLAIMED`. A v3.97 não modifica `.csproj` ou `packages.lock.json` em relação à v3.96.

## Status

**RUNTIME VALIDATED EXTERNALLY** para o caminho funcional herdado da v3.95/v3.96. A v3.97 acrescenta somente baseline documental técnico e metadados de release.
