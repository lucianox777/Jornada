# Exportação de marginais públicas IBGE para estimação externa de u

Este comando é **somente leitura**, exclusivamente para a referência pública já internalizada no banco isolado `JornadaSyntheticDev` com `Jornada.EnvironmentProfile=Development`. Reutiliza `IbgeNominalUReferenceReader.ReadBrazilPublishedMarginalsAsync` (BRASIL, período TODOS, UF 00, município 0000000) e exige **uma única** referência ATIVA com código `CENSO2022_NOMES_BRASIL_V1`. Não lê Silver, Gold, observações, dados de cidadãos nem altera modelos.

Na pasta `Solution`, depois de preparar o banco sintético isolado e a referência pública IBGE:

```powershell
$env:ConnectionStrings__Jornada = '<somente conexão JornadaSyntheticDev Development>'
dotnet run --project .\src\Jornada.Linkage.Evaluation -c Release -- --export-ibge-public-marginals .\evidence\ibge-splink\public-marginals-todos.json --first-name-sex TODOS
dotnet run --project .\src\Jornada.Linkage.Evaluation -c Release -- --export-ibge-public-marginals .\evidence\ibge-splink\public-marginals-feminino.json --first-name-sex FEMININO
```

Cada saída JSON segue `JORNADA_IBGE_PUBLIC_MARGINALS_V1`, com código e hash da referência, recorte, `first_names` e `surnames` normalizados, agregados e ordenados. Um sidecar `.sha256` registra o SHA físico do JSON. Conferir que o hash de conteúdo da referência corresponde ao manifesto IBGE aprovado antes de entregar o arquivo ao runner externo; o exportador valida formato e identidade lógica, mas não reconstitui sozinho o manifesto original. Não versionar os JSONs completos de evidência no Git.

No repositório independente [jornada-splink-conformance](https://github.com/lucianox777/jornada-splink-conformance), executar `independent_u.py` separadamente para TODOS e FEMININO, preservando JSONs de entrada, sidecars e relatórios. A execução externa usa sorteio independente do C#, três seeds predefinidas e Splink real. O presente PR entrega **o intercâmbio público**; não executa o SQL local nem comprova a equivalência estatística. Ainda são necessárias a execução sobre marginais públicas efetivamente exportadas e a comparação quantitativa pré-registrada com o bootstrap C#, sob [issue #506](https://github.com/lucianox777/Jornada/issues/506).
