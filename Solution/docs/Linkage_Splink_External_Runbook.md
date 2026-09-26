# Conferência nominal externa Jornada × Splink — runbook offline

**Estado em 26/09/2026:** contrato/fixture C# implementados; **nenhum runner externo executado nesta integração**. Arquitetura aprovada em [ADR-007](../../Documentos/ADR/ADR-007-conferencia-externa-splink-sem-python-operacional.md). Não equivale a homologação estatística [#31](https://github.com/lucianox777/Jornada/issues/31), não substitui a conferência governada e não altera modelos.

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
