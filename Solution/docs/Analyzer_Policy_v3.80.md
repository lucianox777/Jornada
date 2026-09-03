# Política de analyzers — Engenharia v3.80

A v3.80 encerra a fase de adoção gradual dos .NET analyzers. `Directory.Build.props` mantém
`AnalysisLevel=8.0-recommended` e passa a usar `CodeAnalysisTreatWarningsAsErrors=true`.
Todo diagnóstico não explicitamente aceito em `.editorconfig` deve, portanto, bloquear o build.

## Diagnósticos corrigidos no código

- `CA1305`: conversões/formatações SQL e datas usam `CultureInfo.InvariantCulture` quando o resultado não pode depender da cultura da estação.
- `CA1307`: buscas/comparações de caracteres declaram `StringComparison.Ordinal` quando aplicável.
- `CA1512`: validações numéricas simples usam os helpers `ArgumentOutOfRangeException.ThrowIf*`.
- `CA1805`: inicializadores redundantes com o valor default foram removidos.
- `CA1869`: `JsonSerializerOptions` reutilizáveis passaram a ser cacheadas.
- `CA2208`: `paramName` de `ArgumentOutOfRangeException` voltou a apontar para parâmetros reais do método.
- `CA2215`: `DecompressedLimitStream.DisposeAsync()` chama `base.DisposeAsync()` em todos os caminhos.

## Diagnósticos aceitos por política explícita

Os seguintes diagnósticos ficam desabilitados em `.editorconfig` porque a alteração sugerida seria
puramente estilística/micro-otimização ou contrariaria contratos arquiteturais já deliberados:

- `CA1014`: a Solution é aplicação/serviço, não biblioteca pública CLS destinada a consumidores .NET genéricos.
- `CA1707`: valores de enum com `_` são códigos persistidos/serializados; nomes descritivos de testes também são intencionais.
- `CA1848`: migração indiscriminada de todos os logs para `LoggerMessage` não é requisito funcional; será feita somente onde profiling justificar.
- `CA1859`: interfaces e coleções read-only preservam fronteiras arquiteturais e testabilidade.
- `CA1861`: pequenos arrays literais permanecem locais quando isso melhora legibilidade; não há evidência de pressão de alocação relevante.
- `CA1822`: métodos de instância podem permanecer assim quando expressam responsabilidade do componente; torná-los `static` não altera correção.

A política não usa `NoWarn` genérico e não reduz severidade dos diagnósticos de confiabilidade acima.
Qualquer novo warning não coberto por esta decisão deve falhar o build e ser triado explicitamente.
