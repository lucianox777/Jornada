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


## Referência C# offline para comparação quantitativa

Depois da exportação pública, gerar a referência C# **sem acesso ao SQL** e sem depender do runner Splink:

```powershell
dotnet run --project .\src\Jornada.Linkage.Evaluation -c Release -- --estimate-ibge-public-marginals .\evidence\ibge-splink\public-marginals-todos.json .\evidence\ibge-splink\csharp-u-todos.json --pairs 10000
dotnet run --project .\src\Jornada.Linkage.Evaluation -c Release -- --estimate-ibge-public-marginals .\evidence\ibge-splink\public-marginals-feminino.json .\evidence\ibge-splink\csharp-u-feminino.json --pairs 10000
```

O relatório `JORNADA_IBGE_CSHARP_OFFLINE_U_V1` registra SHA físico da entrada, SHA de conteúdo da referência, recorte, versões do método e do comparador, probabilidade analítica de colisão e suportes/probabilidades/erros-padrão por estado. Seeds C# fixas **20261011, 20261012, 20261013**, distintas das seeds Python **20261001, 20261002, 20261003**. Cada saída recebe sidecar SHA-256. Os arquivos de teste com ANA/MARIA/SILVA/SANTOS são **fictícios** e não constituem evidência IBGE real.

O teste quantitativo da #506 exige, para **cada recorte**, os mesmos arquivos públicos de entrada (comparar `marginals_sha256`), estimativas externas do Splink e relatórios C#; comparar separadamente EXACT/HIGH/MEDIUM/LOW, intervalos e diferenças de classificador. Uma divergência C# V1 × Splink nas fronteiras não deve ser confundida com erro da distribuição amostral. Este comando **não** realiza comparação automática, não consulta dados de cidadãos e não certifica representatividade #31.


## Conferência quantitativa offline dos dois relatórios — diagnóstico, sem SQL

O comparador C# recebe **a mesma exportação pública física** entregue aos dois estimadores. Antes de calcular diferenças, valida o SHA-256 dos bytes do arquivo, código e hash da referência, recorte, construção conjunta, canal de observação e versões dos estimadores. Exige três seeds distintas e fixadas por implementação, mesmo número de pares por seed, os quatro estados com suporte conservado, probabilidades coerentes e colisão analítica derivada novamente das marginais. Um contrato incompleto ou divergente falha sem gerar relatório.

Execute, após obter `independent-u-report.json` do repositório externo para **cada** arquivo de marginais:

```powershell
dotnet run --project .\src\Jornada.Linkage.Evaluation -c Release -- --compare-ibge-independent-u .\evidence\ibge-splink\public-marginals-todos.json .\evidence\ibge-splink\csharp-u-todos.json .\evidence\ibge-splink\todos\independent-u-report.json .\evidence\ibge-splink\conference-todos.json

dotnet run --project .\src\Jornada.Linkage.Evaluation -c Release -- --compare-ibge-independent-u .\evidence\ibge-splink\public-marginals-feminino.json .\evidence\ibge-splink\csharp-u-feminino.json .\evidence\ibge-splink\feminino\independent-u-report.json .\evidence\ibge-splink\conference-feminino.json
```

Cada resultado `JORNADA_IBGE_INDEPENDENT_U_CONFERENCE_V1` gera sidecar SHA-256, probabilidades médias por estado, diferença Splink − C#, erro-padrão Monte Carlo da diferença, intervalo nominal de 95% **condicional às marginais fixas** e TVD entre distribuições médias. O status fixo `DIAGNOSTIC_ONLY_NOT_GOVERNED` **não** equivale a um parecer de conformidade. As três seeds por implementação são amostragens distintas; **não** se comparam pares por índice neste experimento. Para investigar classificadores, usar a matriz 4×4 do experimento anterior dos **mesmos pares**.

Os intervalos não incorporam incerteza de marginais publicadas, dependência entre prenome/sobrenome no mundo real nem erro administrativo. Não ajustar limites, parâmetros, tolerância de promoção ou comparador para fazer os relatórios coincidirem. A conferência final da #506 exige preservar os dois conjuntos de entradas originais, relatórios e sidecars, e registrar discrepâncias e sua explicação por estado. Os testes unitários utilizam somente fixtures fictícias; **não são prova da execução sobre o IBGE real**.
