# Nova Jornada do Cidadão — Resumo Executivo

**Base Normativa:** v3.62
**Solution Engenharia:** v3.90
**Data:** 2 de setembro de 2026

## Síntese

A v3.90 preserva integralmente a Base Normativa v3.62 e o schema persistido Base 3.62 / SolutionSchema 3.68. A execução real da v3.88 confirmou restore/build/Unit e mostrou que a correção Docker API permitiu iniciar a suíte Integration; 53 de 58 casos foram então bloqueados pelo guard de nome porque a fixture criava `JornadaIntegration_<guid>`. Mantém a correção v3.89 do banco `JornadaIntegrationTest_<guid>` e endurece a limpeza local: `.vs` passa a ser best-effort quando caches do Visual Studio/Copilot estão bloqueados, sem afetar a limpeza de build/test.

## Estado técnico

- **Compilação Integration:** corrigido o acesso aos internals por `InternalsVisibleTo("Jornada.Integration.Tests")` nos três assemblies necessários, sem ampliar API pública.
- **Desenvolvimento local:** `local-db.ps1` preserva os argumentos, valida Docker Engine, acompanha o SQL Server por JSON e agora executa `sqlcmd -I` como proteção adicional de sessão.
- **DDL/runtime SQL:** mantém o hardening v3.87 e substitui o índice Bronze de chave larga por índice em `payload_sha256 CHAR(64)`, com `objeto_chave` como `INCLUDE` e confirmação exata nas consultas.
- **Supply chain:** nenhum lock NuGet foi modificado. A execução externa da v3.88 aceitou restores `--locked-mode` da Solution e dos projetos Unit/Integration; o empacotamento v3.90 continua sem alegar restore local próprio.
- **Integration/Docker:** quando necessário, a fixture reduz `DOCKER_API_VERSION` ao máximo anunciado pelo servidor se este for anterior a 1.44; override explícito do operador é preservado.
- **Rastreabilidade:** predecessor Git direto v3.88 e bundle v3.89→v3.90.

## Documentação incluída

Permanecem incluídos:

- `Resumo_Executivo_Especificacao_Tecnica_Jornada_v1.1.docx`;
- o material do **Projeto de Integração de Benefícios e Serviços Sociais** em pasta própria;
- documentos técnicos e históricos anteriores.

## Próximo marco

Executar a v3.90 em ambiente .NET 8.0.424 + Docker usando `scripts/local-clean.ps1` seguido de `scripts/local-validate-release.ps1`. O próximo marco é confirmar os 58 Integration após o ajuste do nome do banco descartável.
