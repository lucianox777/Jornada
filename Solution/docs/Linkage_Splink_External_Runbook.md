# Conferência nominal externa Jornada × Splink — runbook offline

**Estado em 26/09/2026:** contrato/fixture C# implementados; **nenhum runner externo executado nesta integração**. Norma vigente: [decisão consolidada §2.1](Decisoes_Linkage_Calibracao_IBGE_20260926.md#21-conferência-externa-jornada--splink--decisão-consolidada-de-26092026); a ADR-007 é registro histórico. Não equivale a homologação estatística [#31](https://github.com/lucianox777/Jornada/issues/31), não substitui a conferência governada e não altera modelos.

**Importante:** o exemplo de nove pessoas abaixo é smoke do intercâmbio **e não compara o bootstrap IBGE**. O próximo experimento deve exportar os mesmos pares sorteados pelo estimador C# sobre o snapshot público validado, checar os níveis via Splink real e comparar suporte/probabilidade de cada estado. Instalar/rodar o Splink **fora da árvore da Jornada**; não simular conclusão dessa prova com o modo V1 abaixo. Acompanhamento [#506](https://github.com/lucianox777/Jornada/issues/506).

## Replay do Censo IBGE — alvo principal de conformidade (novo contrato)

O modo acima de **nove pessoas fictícias continua smoke de intercâmbio**. A conferência efetiva deve partir das **marginais públicas já internalizadas** na referência `CENSO2022_NOMES_BRASIL_V1`, no banco isolado **`JornadaSyntheticDev`** com extended property `Jornada.EnvironmentProfile=Development`; o exportador só consulta `ref`, nunca `silver`, `gold` ou cadastro. Faltar fonte, banco DEV, versão/hash da referência ou marginais exigidas aborta antes de criar pacote.

Na raiz `Solution`, após instalar a referência no `JornadaSyntheticDev`:

```bash
# Nunca usar conexão de HML/Produção ou banco de cidadão. Usar conexão local isolada.
export ConnectionStrings__Jornada='<CONEXAO_AO_JornadaSyntheticDev>'
dotnet run --project src/Jornada.Linkage.Evaluation -- \
  --export-splink-ibge-replay ./evidence/ibge-u-todos.json \
  --seed 20260917 --pairs 10000 --first-name-sex TODOS
dotnet run --project src/Jornada.Linkage.Evaluation -- \
  --export-splink-ibge-replay ./evidence/ibge-u-feminino.json \
  --seed 20260918 --pairs 10000 --first-name-sex FEMININO
unset ConnectionStrings__Jornada
```

O JSON `JORNADA_SPLINK_IBGE_U_REPLAY_V1` contém **os mesmos quatro sorteios hash/slot/seed por par que o método C# usa** e o estado `EXACT/HIGH/MEDIUM/LOW` já classificado. O exportador exige que a contagem de cada estado coincida com uma nova execução do `Estimate()` C# sobre o mesmo recorte/seed/quantidade. Inclui código e SHA-256 do snapshot, versão do bootstrap e comparador, marginais `TODOS`/`FEMININO`, `PairCount` e massa analítica de colisão. O envelope novo **não substitui** `JORNADA_SPLINK_EXCHANGE_V1`/`JORNADA_SPLINK_ESTIMATES_V1` do smoke histórico.

No repositório **externo** `jornada-splink-conformance`, instalado **num diretório descartável fora do checkout da Jornada** e com Splink pinado `4.0.17` inicialmente, o runner de replay deve importar `JORNADA_SPLINK_IBGE_U_REPLAY_V1`, verificar metadata e SHA256, usar `splink.comparison_library.JaroWinklerAtThresholds("nome", [0.92, 0.80])` no backend DuckDB sobre exatamente os pares `pair_index` recebidos, sem reamostrar, sem treinar `m/u` e sem depender do estado C# para classificá-los. Retornar os `pair_index` e estados Splink em `JORNADA_SPLINK_IBGE_U_REPLAY_RESULT_V1`, com SHA de entrada e versão efetiva do Splink. A primeira execução pode ser manual local, sem Action, e deve salvar log, versão instalada e hashes antes de apagar o venv. O runner novo não foi executado com Splink nesta PR; não confundir fixture/contrato C# com evidência externa real.

Depois da execução externa, no checkout da Jornada:

```bash
dotnet run --project src/Jornada.Linkage.Evaluation -- \
  --check-splink-ibge-replay ./evidence/ibge-u-todos.json \
  ./evidence/ibge-u-todos-splink.json ./evidence/ibge-u-todos-diagnostico.json
```

A verificação recusa retorno incompleto, duplicado, com versão/hash/seed/comparador divergente e reporta **cada estado, quantidade, TVD e discordâncias por par**, sempre `*DIAGNOSTICO*`. A probabilidade `u` desse relatório é **incondicional** aos passes de blocking; não converte massa m/u do Splink em modelo operacional. Para comparar LLR, é preciso garantir o mesmo `m` fixado e o `u` correspondente, em um experimento separado; não aplicar arbitrariamente o teto governado 0,01 ao Monte Carlo.

Para exibir evidência local sem modificá-la na interface, em **API Development** configure somente os caminhos absolutos dos **dois arquivos brutos**:

```bash
export JORNADA_SPLINK_IBGE_INPUT_PATH=/caminho/ibge-u-todos.json
export JORNADA_SPLINK_IBGE_RESULT_PATH=/caminho/ibge-u-todos-splink.json
```

O `LinkageConferenceGovernanceStatus` do monitor reabre ambos, recalcula a evidência par a par e exige hash igual à referência IBGE ATIVA. Arquivos grandes demais (>16 MB entrada, >10 MB saída), ausentes, de outro snapshot ou malformados são explicitamente rejeitados. Sem esses caminhos, permanece **`SEM_EVIDENCIA_EXTERNA`**. O resultado é apenas diagnóstico read-only; evidência de modelo `CONFORME`, round-trip e status populacional #31 continuam separados. Não copiar os arquivos de pesquisa nem o venv para o bundle operacional.

## O que este V1 realmente permite

- `JORNADA_SPLINK_EXCHANGE_V1`: envelope nominal histórico `records`, `labels`, `seed`, `partition=0`, gerador e fingerprint, mas **somente nove pessoas sintéticas compiladas**. `ibge_source_version=SYNTHETIC_FIXTURE_NO_IBGE` proíbe interpretar o hash como Censo.
- `JORNADA_SPLINK_ESTIMATES_V1`: `splink_version`, `runner=calibrador-splink/run_calibration.py`, `scope=NOME`, versão semântica `IDENTITY_NAME_STATES_V1`, `name_thresholds=[0.92,0.80]`, suporte, seed, fingerprint, quatro níveis, `m_probability` e `u_probability`.
- O CLI rejeita JSON com conteúdo de fixture diferente, schema/feature/nível desconhecidos, probabilidade fora de (0,1), limiar/seed/fingerprint divergentes. Não existe argumento de caminho para dados de entrada na geração nem conexão com SQL.
- `m` C# diagnóstico: nove labels positivos, suavização 0,5. `u` C# diagnóstico: **36 pares não condicionados distintos** das nove pessoas canônicas; `u` Splink V1: amostragem aleatória entre pessoas canônicas. TVD e delta LLR natural podem refletir diferenças de amostragem/estimador, não são gate de scorer nem calibração operacional.

## Fluxo manual e comandos na raiz `Solution`

```bash
dotnet run --project src/Jornada.Linkage.Evaluation -- --export-splink-synthetic ./fixture-splink.json
# Produz fixture-splink.json e fixture-splink.json.sha256; não pede credencial SQL.
```

Verificar SHA-256 do arquivo **e** ausência de dado real. Copiar somente `fixture-splink.json` e o hash para workspace isolado do **repositório externo ainda a criar**, sem clonar banco, anexar dump ou entregar segredos. Nesse workspace, usar runner Python externo, inicialmente compatível com `splink==4.0.17`, pinado e testado em sua própria CI:

```bash
python3 run_calibration.py --input fixture-splink.json --output estimativas-externas.json --seed 20260926 --max-pairs 1000
```

O runner histórico de 17/09 é ponto de partida **para revisão**, não prova de que a nova execução passou. Repetir a mesma execução para prova de determinismo, verificar `JORNADA_SPLINK_ESTIMATES_V1` e versão instalada, registrar hashes do ambiente, entrada e saída. Transferir explicitamente somente estimativas e manifesto para workspace local aprovado; preferir anexos com retenção controlada, não guardar corpus real no Git.

```bash
dotnet run --project src/Jornada.Linkage.Evaluation -- --check-splink-estimates ./fixture-splink.json ./estimativas-externas.json ./diagnostico.json
```

O relatório `JORNADA_EXTERNAL_SPLINK_DIAGNOSTIC_V1` contém TVD m/u, LLR natural por nível e diferenças absolutas, hashes, versão e suportes, sempre com `DIAGNOSTICO_NAO_GOVERNADO`. Não chama `GENERATE_DRAFT`/`VALIDATE`/`ACTIVATE`, não consulta SQL nem persiste evidência governada.

## Regras para o repositório externo

Criar `jornada-splink-conformance` independente, com README, runner, `requirements.txt` pinado, testes de contrato/determinismo e sua própria Action. **Nenhuma** `ProjectReference`, submodule, build job, checkout cruzado ou venv Splink na Jornada. Não configurar `repository_dispatch` automaticamente; transferência manual enquanto não existir aprovação de segurança do trigger e segregação de permissões. O repositório externo não deve armazenar token/banco operacional nem fazer consultas aos endpoints reais da Jornada.

A fixture **não é** o corpus `JornadaSyntheticDev` amplo; adicionar suporte à exportação desse corpus requer **V2 de contrato e fonte autoritativa** verificada pelo gerador + perfil SQL Development, com recusa de dados mistos, fingerprints/splits congelados e proteção contra deslocamento de coorte/CPF. Não autorizar um `--input qualquer.json` com campo `synthetic=true` como substituto desse gate.

## Critério de fechamento desta fase

1. C# compila e testes de origem/contrato passam nas Actions da Jornada, sem nova instalação Python/Splink.
2. Repositório externo separado criado, runner V1 revisado e testado, smoke executado duas vezes com igualdade de saída e evidência de dependências pinadas.
3. Resultado importado via C# com mesma seed/proveniência e diagnóstico arquivado como evidência **externa não governada**.
4. Etapa seguinte, independente: ensaio amplo com comparadores brutos e scorer com mesmos parâmetros m/u, blocking congelado e TEST intocado, distinguindo estimativa m/u, paridade de implementação e qualidade populacional. DT-01 só encerra com caracterização independente que satisfaça seus critérios próprios.

**Proibições:** publicar resultado externo como `CONFORME` persistente, usar dados de cidadão, mudar thresholds, alterar a tolerância ex ante, preencher missing municipal por UF/Brasil ou tratar o smoke sintético como evidência populacional.
