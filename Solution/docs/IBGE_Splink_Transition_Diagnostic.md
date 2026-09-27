# Matriz de transições IBGE × Splink — diagnóstico offline V2

**Escopo:** evolução exclusivamente diagnóstica da conferência C# × Splink nos mesmos pares sintéticos derivados das marginais IBGE. Não altera o comparador WHOLE_NAME_JARO_WINKLER_V1, os contratos de entrada/resultado V1, m/u, blocking, threshold, margem ou promoção governada.

## Por que a matriz é necessária

O diagnóstico V1 emitia discordâncias por par, marginais por estado e TVD. Movimentos recíprocos entre dois estados podem produzir **TVD zero com pares discordantes**. O V2 registra a matriz de contagens 4 × 4 após validar SHA-256, metadados e cada índice/estado da evidência externa.

## Contrato de saída e invariantes

Novas exportações de --check-splink-ibge-replay usam JORNADA_SPLINK_IBGE_U_REPLAY_DIAGNOSTIC_V2; a metodologia dos mesmos pares continua IBGE_SAME_PAIR_SPLINK_CONFORMANCE_V1. Documentos V1 arquivados permanecem históricos: não reetiquetar documentos antigos como V2.

O novo campo obrigatório **transitions** contém 16 células, inclusive zeros, com a ordem determinística de linhas e colunas EXACT, HIGH, MEDIUM, LOW. Cada célula contém somente c_sharp_state, splink_state e support. Linhas = C# V1; colunas = Splink 4.0.17. A diagonal representa concordância; fora da diagonal, divergência.

- Soma de todas as células = pair_count; soma das 12 células fora da diagonal = pairwise_disagreements.
- Total de cada linha = states[].c_sharp_support; total de cada coluna = states[].splink_support.
- A matriz é calculada dos mesmos pares indexados e independe da ordem de chegada do arquivo externo.
- Pares omitidos/duplicados, índices fora da faixa, estados desconhecidos ou versões/hashes divergentes são rejeitados **antes** da emissão do diagnóstico.

A TVD continua sendo a distância entre distribuições marginais, não identifica quais pares mudaram de estado. A matriz não mede qualidade de identidade, acerto de vínculo nem estimação u independente.

## Gerar novamente em DEV offline

Na pasta Solution, com os dois JSONs de replay e os dois resultados do runner externo autorizados:

~~~powershell
$d = Join-Path $HOME 'jornada-splink-runner-external\results'
$e = Join-Path (Get-Location) 'evidence\ibge-splink'
foreach ($tipo in @('todos','feminino')) {
  dotnet run --project .\src\Jornada.Linkage.Evaluation -c Release -- --check-splink-ibge-replay (Join-Path $e "ibge-u-$tipo.json") (Join-Path $d "ibge-u-$tipo.splink-result.json") (Join-Path $d "ibge-u-$tipo.diagnostic-v2.json")
  if ($LASTEXITCODE -ne 0) { throw "Replay não conforme: $tipo" }
}
~~~

Os replays históricos de 27/09/2026 registraram **20 divergências em TODOS e 33 em FEMININO**. O inventário anterior das 53 transições contém **21 LOW→MEDIUM, 31 MEDIUM→LOW e 1 MEDIUM→HIGH**. Esses resultados antecedem a exportação V2: não representam uma execução já comprovada dos novos arquivos diagnostic-v2.json. Ao reprocessar os JSONs originais, conferir cada célula e investigar discrepâncias sem editar evidência.

O [registro do replay](IBGE_Splink_External_Replay_20260927.md) preserva os hashes; o [runbook externo](Linkage_Splink_External_Runbook.md) mantém Splink/DuckDB/Python fora do build e runtime da Jornada. A [issue #506](https://github.com/lucianox777/Jornada/issues/506) segue aberta para estimação verdadeiramente independente de u e investigação de semântica; [#31](https://github.com/lucianox777/Jornada/issues/31) exige representatividade separadamente.
