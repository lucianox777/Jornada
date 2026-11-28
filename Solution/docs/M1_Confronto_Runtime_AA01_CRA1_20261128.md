# M1 — confronto dos contratos AA01/CRA1 no validador real

**Data:** 28/11/2026. **Natureza:** incremento técnico de testes sob a continuidade solicitada pelo PO: “Continue a frente M1a, M1b e M1c, era a frente do caminho crítico”. Não redefine esses marcos, não cria política de autorização e não substitui o executor da #1940. **Base:** `250aedc5f71aec2fd54f7af80b404450d800ce08`. Referências: #1194, #1853, #1855 e registro documental M1 na #1946. GOV-DOC-01.

## Objetivo e limites

Converter uma pendência concreta da preparação em evidência executável: exercitar `JsonSchemaValidatorCache` e `JsonSchemaSubsetValidator` reais contra os schemas publicados AA01 v1 e CRA1 v1. A #1946 permanece documental; este incremento de teste não altera sua branch nem a #1940. Não há alteração de produto, schema, dependência, SDK, workflow, modelo ou permissões. A integração continua sujeita aos gates e à coordenação vigente.

O recorte contém **65 casos factuais** (35 AA01 e 30 CRA1; 27 aceitações e 38 rejeições esperadas), **dois negativos de digest de catálogo** e **duas observações diagnósticas de formato**, totalizando 69 casos NUnit previstos. Não é o corpus anterior de 141 casos, cujos bytes não foram recuperados nesta sessão; não se declara reexecução ou substituição desse material. Os 30 cenários HTTP anteriores também não são executados por estes testes.

Cada negativa factual exige `InvalidDataException` no campo e regra declarados. Casos positivos e negativos usam dados sintéticos mínimos e os schemas copiados pelo projeto de testes existente. Os SHA-256 são fixados antes da execução:

- AA01: `8fa7723753cb083a8be6dc1bf083f24dac41dddd55e731be00f113b4313e616d`.
- CRA1: `c6342a5fc5486d1e180b2dd8bd9e5b2e02fc1c8562ed6c84968e1da872612154`.

Não alterar uma versão publicada para fazer o teste passar. Aceitação pelo schema não demonstra existência da pessoa referenciada, autorização, validade de negócio, persistência ou publicação.

## Datas: medir antes de mudar comportamento

As duas observações CRA1 usam data sem hora e data/hora sem deslocamento de fuso. A referência Draft 2020-12 com verificação de formato as rejeita; o produto usa `DateTimeOffset.TryParse`. A caracterização registra o resultado efetivo sem convertê-lo em nova regra de compatibilidade ou aceite.

O teste diagnóstico passa somente após seus controles válido/inválido e a captura, mas **Passed nesse teste não significa conformidade de formato**. O TRX contém a linha `M1_FORMAT_OBSERVATION=` com `actualAccepted`, `expectedByReference=false` e `conforms`. Uma eventual divergência deve permanecer explícita no relatório; não mudar parser, schema ou expectativas neste incremento.

Referência de formato: [JSON Schema — format e datas](https://json-schema.org/understanding-json-schema/reference/type). Não se atribui aqui obrigação universal de validar `format` a todo validador: o componente do Jornada já declara suportar date/date-time, e o contraste mede sua implementação concreta.

## Evidência anterior à CI

Os bytes dos dois schemas foram reconstruídos a partir das fontes consultadas e conferidos pelos blobs Git `c74943986052304b9e36a603152deaf331046fdf` e `dbbb7fba86c70122914b8ed195e8b92d7bfd03d6`, além dos SHA-256 acima. Os 65 casos concordaram com as expectativas em Python jsonschema 4.26.0/FormatChecker; as duas entradas diagnósticas foram rejeitadas pela referência. Isso não executou .NET.

O ambiente do chat não contém .NET ou Docker; a obtenção do SDK fixado não foi possível. Não houve instalação alternativa, acesso ao WSL ou execução em banco do PO. A compilação e a execução C# dependem da CI preexistente, sem workflow novo ou disparo manual. Antes de publicar este incremento, foram consultadas as execuções ativas e a concorrência por ref da required-fast; não houve alteração, cancelamento ou reexecução da #1940. O inventário global permanece parcial até completar todos os estados/páginas.

## Estado de entrega

- Testes: preparados para a suíte unitária existente, execução C# ainda não comprovada por este registro.
- Produto/HTTP/SQL/Gold/Resultado.Api/Apoio: não validados por este incremento.
- #1194: continua pendente; `AllowType=false` preservado e `enviar=false` no Apoio, sem fallback GESTOR.
- V9/escala/campanha: não alterados nem considerados pré-condição desta conferência de contratos.
- M1a/M1b/M1c: nenhum novo aceite final. Não somar os 69 casos previstos a testes executados antes de conferir TRX, SHA e resultados.
- Documentação em branch/PR não equivale a integração na master. A #1946 e os requisitos gerais continuam rastreados separadamente.
