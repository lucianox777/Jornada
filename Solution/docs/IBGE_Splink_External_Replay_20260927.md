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

A regressão congelada das 53 divergências permite decompor a direção das mudanças de estado observadas: **21 LOW→MEDIUM, 31 MEDIUM→LOW e 1 MEDIUM→HIGH**. O diagnóstico C# passa a emitir também uma matriz completa 4×4 `transitions` (incluindo células zero), cuja soma deve ser igual a `pair_count` e cuja soma fora da diagonal deve ser igual a `pairwise_disagreements`. A matriz é descritiva: não escolhe qual implementação está correta nem altera thresholds, comparador ou probabilidades `m/u`.

## Proveniência verificável

| Artefato | SHA-256 |
| --- | --- |
| `ibge-u-todos.json` | `a5cea4f00027427190c726f92329724e6f92fc3bcd64c67f4d9f758ca4d2af64` |
| `ibge-u-feminino.json` | `da89b9a7a6594f52065e64284732f032adb3033c347ba969684161cd028f378c` |
| `ibge-u-todos.splink-result.json` (execução validada no Windows) | `f850a00e886933b1b19d8f889d24811804c8ea14c66310c3135d7c5bf8b4b9b6` |
| `ibge-u-feminino.splink-result.json` (execução validada no Windows) | `e0f02d2dd9787f0e70bc4d4fb7298f0908555a1cde2c783f04bf4665ce886827` |

A evidência integral (dois replays, dois resultados Splink e dois diagnósticos C#) foi preservada fora do repositório; não inserir os dados volumosos nem o ambiente Python na árvore da Jornada. O runner externo foi publicado e versionado em [`lucianox777/jornada-splink-conformance`](https://github.com/lucianox777/jornada-splink-conformance) (PR externo [#1](https://github.com/lucianox777/jornada-splink-conformance/pull/1), merge `47beff376d5e440a6a1a09649d798c5ee9fc07dc`, CI `runner-ci` aprovada). Ele mantém Splink 4.0.17/DuckDB 1.2.2/Pandas 2.3.2 e seus testes no próprio repositório; nada é incorporado ao build ou deploy da Jornada.

## Interpretação e limites

As 53 divergências não são falhas do verificador: ele as identifica. O comparador `WHOLE_NAME_JARO_WINKLER_V1` do C# aplica bônus de prefixo também em situações de Jaro baixo; a função de DuckDB usada pelo Splink pode ter comportamento diferente. Há divergências adicionais ainda não explicadas; não alterar silenciosamente o comparador V1 persistido. O Splink emitiu aviso de `m`/`u` não treinados e prior padrão: a evidência **não** valida bootstrap, estimação independente de `u`, `m/u`, scorer, população #31 ou aptidão para produção.

## Reproduzir o verificador C# (PowerShell, pasta Solution)

```powershell
$d=Join-Path $HOME 'jornada-splink-runner-external\results'; $e=Join-Path (Get-Location) 'evidence\ibge-splink'; foreach ($tipo in @('todos','feminino')) { dotnet run --project .\src\Jornada.Linkage.Evaluation -c Release -- --check-splink-ibge-replay (Join-Path $e "ibge-u-$tipo.json") (Join-Path $d "ibge-u-$tipo.splink-result.json") (Join-Path $d "ibge-u-$tipo.diagnostic.json"); if ($LASTEXITCODE -ne 0) {throw "Falha: $tipo"} }
```

## Exportação auditável das divergências (PowerShell, pasta Solution)

Após o merge de #550, a CLI C# também exporta apenas os pares discordantes em CSV. O comando valida o replay completo e a resposta externa (SHA de origem, versão, todos os índices e estados) **antes** de gravar. O arquivo `.sha256` acompanha o CSV para preservar sua integridade. A operação é offline: não exige conexão SQL, não escreve no modelo e não invoca Python.

```powershell
$d=Join-Path $HOME 'jornada-splink-runner-external\results'; $e=Join-Path (Get-Location) 'evidence\ibge-splink'; foreach ($tipo in @('todos','feminino')) { dotnet run --project .\src\Jornada.Linkage.Evaluation -c Release -- --export-splink-ibge-disagreements (Join-Path $e "ibge-u-$tipo.json") (Join-Path $d "ibge-u-$tipo.splink-result.json") (Join-Path $d "ibge-u-$tipo.divergencias.csv"); if ($LASTEXITCODE -ne 0) {throw "Falha: $tipo"} }; Get-ChildItem $d -Filter '*.divergencias.csv' | ForEach-Object { $n=(Import-Csv $_.FullName | Measure-Object).Count; Write-Host "$($_.Name): $n divergências"; Get-FileHash $_.FullName -Algorithm SHA256 }
```

Com os replays e resultados **exatos** documentados acima, espera-se 20 linhas no CSV TODOS e 33 no FEMININO, além do cabeçalho. Divergência de contagem exige investigação; não corrigir CSV manualmente. Conservar os JSON originais e os `.sha256` junto dos CSVs. Não incluir o pacote de evidência volumoso na árvore da Jornada.

## Estado dos trabalhos (#506)

- **Concluído:** #545, documentação dos 20.000 pares e hashes; #546, 53 fixtures de regressão; #547, testes das fronteiras Jaro-Winkler; #550, exportador CSV com validação estrita.
- **Concluído nesta continuidade:** diagnóstico com matriz 4×4 C#→Splink, preservando contagem total e divergências fora da diagonal; sem alteração do comparador V1.
- **Concluído:** runner Splink 4.0.17 publicado e testado em repositório [externo dedicado](https://github.com/lucianox777/jornada-splink-conformance), sem dependência Python na Jornada (PR externo #1, CI aprovada).
- **Pendente:** obter estimação independente de `u` por estado e recorte e confrontá-la com o bootstrap C#; investigar divergências remanescentes antes de qualquer nova versão do comparador.

Os avisos de `m/u` não treinados e prior padrão do Splink permanecem uma limitação da conferência de estados; **não** constituem validação de calibração. A #506 permanece aberta até a evidência independente exigida.
