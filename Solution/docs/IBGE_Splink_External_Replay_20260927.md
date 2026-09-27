# Conferência externa Jornada × Splink — evidência DEV (27/09/2026)

**Escopo:** diagnóstico reproduzível de estados de comparação em 20.000 pares exclusivamente sintéticos derivados da referência IBGE `CENSO2022_NOMES_BRASIL_V1`, no banco isolado `JornadaSyntheticDev`. A issue #506 acompanha a validação independente. Nenhum dado de cidadão foi exportado.

## Execução confirmada

A referência IBGE foi verificada como `ATIVA`; a operação `ENSURE_IBGE_NOMINAL_U_REFERENCE` produziu `NOME` (TODOS) e `NOME_MAE` (FEMININO), seed `20260917`, 1.000.000 de pares de referência. O exportador C# gerou dois replays de 10.000 pares; o runner **externo**, usando Splink 4.0.17/DuckDB, classificou os mesmos pares; por fim, o verificador C# `--check-splink-ibge-replay` aceitou os dois contratos externos e produziu os diagnósticos abaixo.

| Recorte | Pares | Divergências por par | TVD entre estados | Máx. diferença absoluta |
| --- | ---: | ---: | ---: | ---: |
| TODOS | 10.000 | 20 | 0,0002 | 0,0002 |
| FEMININO | 10.000 | 33 | 0,0009 | 0,0009 |
| **Total** | **20.000** | **53** | — | — |

Ambos retornaram `ESTADOS_DIVERGENTES_DIAGNOSTICO`. Não somar TVDs de recortes distintos como se fossem uma distribuição única.

## Proveniência verificável

| Artefato | SHA-256 |
| --- | --- |
| `ibge-u-todos.json` | `a5cea4f00027427190c726f92329724e6f92fc3bcd64c67f4d9f758ca4d2af64` |
| `ibge-u-feminino.json` | `da89b9a7a6594f52065e64284732f032adb3033c347ba969684161cd028f378c` |
| `ibge-u-todos.splink-result.json` (execução validada no Windows) | `f850a00e886933b1b19d8f889d24811804c8ea14c66310c3135d7c5bf8b4b9b6` |
| `ibge-u-feminino.splink-result.json` (execução validada no Windows) | `e0f02d2dd9787f0e70bc4d4fb7298f0908555a1cde2c783f04bf4665ce886827` |

A evidência integral (dois replays, dois resultados Splink e dois diagnósticos C#) foi preservada fora do repositório; não inserir os dados volumosos nem o ambiente Python na árvore da Jornada. O runner externo ainda requer publicação e versionamento em repositório dedicado.

## Interpretação e limites

As 53 divergências não são falhas do verificador: ele as identifica. O comparador `WHOLE_NAME_JARO_WINKLER_V1` do C# aplica bônus de prefixo também em situações de Jaro baixo; a função de DuckDB usada pelo Splink pode ter comportamento diferente. Há divergências adicionais ainda não explicadas; não alterar silenciosamente o comparador V1 persistido. O Splink emitiu aviso de `m`/`u` não treinados e prior padrão: a evidência **não** valida bootstrap, estimação independente de `u`, `m/u`, scorer, população #31 ou aptidão para produção.

## Reproduzir o verificador C# (PowerShell, pasta Solution)

```powershell
$d=Join-Path $HOME 'jornada-splink-runner-external\results'; $e=Join-Path (Get-Location) 'evidence\ibge-splink'; foreach ($tipo in @('todos','feminino')) { dotnet run --project .\src\Jornada.Linkage.Evaluation -c Release -- --check-splink-ibge-replay (Join-Path $e "ibge-u-$tipo.json") (Join-Path $d "ibge-u-$tipo.splink-result.json") (Join-Path $d "ibge-u-$tipo.diagnostic.json"); if ($LASTEXITCODE -ne 0) {throw "Falha: $tipo"} }
```

**Próximas verificações (#506):** fixar fixtures das 53 divergências; isolar diferenças de Jaro-Winkler e normalização; publicar o runner externo; comparar probabilidades `u` por estado e recorte de forma independente. Não tratar este PR documental como encerramento da issue.
