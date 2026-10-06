# Integração externa — envelope, transmissor e serviço de resultado


> **Organização do monorepo (06/10/2026):** a antiga solução embarcada `Solution/ApoioSecretarias/` foi removida do repositório principal. Referências abaixo a esse caminho descrevem evidência/histórico anterior à remoção. O produto não compila nem distribui esse transmissor/preparador; apenas schemas SEHAB estritamente sintéticos necessários à regressão permanecem em `Solution/tests/fixtures/external-contracts/gestores/SEHAB/`.
A Jornada receptora mantém a API e o contrato canônico. O preparador, o transmissor C# e os contratos específicos migrados da SEHAB estão na solução **independente** `Solution/ApoioSecretarias/SolucaoApoioSecretarias.sln`, no mesmo repositório Git. O produto `Solution/Jornada.sln` não compila nem distribui o transmissor; um eventual repositório Git remoto separado é uma etapa distinta da separação de solução.

## Preparação e envio

O preparador recebe um CSV da fonte, mapeamento **explícito e versionado**, manifesto, schema Pessoa e, opcionalmente, `registros.jsonl` já normalizado. Valida SHA-256 do schema, JSON Schema, versão, CPF/ausência declarada e gera um ZIP determinístico contendo exatamente `manifest.json`, `pessoas.jsonl` e `registros.jsonl`. O ZIP é nomeado `ENTREGA_<GESTOR>_<SISTEMA>_v<FORMATO>_<sha256>.zip`. Mapeamento e autorização de **dados reais** são responsabilidade da origem; o exemplo versionado é exclusivamente sintético.

A partir de `Solution/ApoioSecretarias/`:

```bash
python -m pip install -r preparador/requirements.txt
python preparador/preparador.py --csv <pessoas.csv> --mapeamento <mapa.json> --manifest <manifest.json> --schema <pessoa.schema.json> --saida <diretorio>
dotnet restore SolucaoApoioSecretarias.sln --locked-mode
dotnet build SolucaoApoioSecretarias.sln -c Release --no-restore
dotnet run --project clients/Jornada.Integrador.CSharp -- --enviar <entrega.zip> --config <integrador.config.json>
```

`--enviar-todos` envia os ZIPs da pasta do executável e arquiva cada arquivo confirmado em `Enviados/`. A transmissão usa `POST /api/v1/ingestao/entregas`, cabeçalhos `X-Jornada-Gestor` e `X-Jornada-Access-Key`, `Idempotency-Key: sha256:<sha256>` e o nome canônico. A API retorna recibo antes do processamento assíncrono. O mesmo ZIP é transmitido sem reescrever seus bytes.

Configuração externa exemplificativa, **não versionar com a chave verdadeira**:

```json
{
  "gestor": "GESTOR_EXEMPLO",
  "accessKey": "SEGREDO_DO_AMBIENTE",
  "endpoints": {
    "envio": "https://jornada-api.exemplo/api/v1/ingestao/entregas",
    "resultado": "https://jornada-resultado.exemplo/api/v1/ingestao/resultados/{nomeArquivo}"
  },
  "polling": {"intervalSeconds": 5, "timeoutSeconds": 3600},
  "diretorioSaida": "resultados"
}
```

O arquivo efetivo requer ACL/permissões do sistema operacional restritas à conta do integrador. Credenciais sintéticas de DEV não podem ser distribuídas como credenciais HML/Produção.

## Acompanhamento e resultado

```bash
dotnet run --project clients/Jornada.Integrador.CSharp -- --resultado <nome-exato-do-zip.zip> --config <integrador.config.json> --saida <resultado.json>
```

**A CLI C# aceita somente o nome exato do ZIP**, não caminho local nem SHA-256 nesse comando. A rota do serviço `GET /api/v1/ingestao/resultados/{nomeArquivo}` recebe o nome exato do ZIP; o placeholder legado `{identificador}` não integra mais o contrato do cliente. O cliente consulta até `PROCESSADA`, `REJEITADA` ou `QUARENTENA` e salva o resultado. Não confundir recibo HTTP com processamento concluído.

O serviço `Solution/src/Jornada.Resultado.Api` valida `X-Jornada-Gestor` e `X-Jornada-Access-Key` na API principal, sem cadastrar outra credencial. O retorno inclui dados de rastreabilidade da entrega, lotes e tentativas, contagens e erros de ingestão, e separa itens ainda disponíveis de itens consolidados pela política de retenção. Não devolve CPF nem conteúdo cadastral. Após expurgo, os totais permanecem disponíveis via `ingestao.item_processado_resumo`, com aviso quando não existe mais detalhe integral item a item.

## Fonte contratual e testes

Os arquivos do Gestor fornecidos pela Solução de Apoio têm SHA-256 fixados em seu inventário externo. A Jornada receptora conserva **o catálogo de contratos aprovado** e seu próprio processamento genérico; a cópia de schemas no diretório temporário do E2E isolado **não** é implantação real. O teste `Solution/scripts/local-e2e.sh` exercita preparação → envio C# → HTTP → Bronze → Processor → Silver/identidade e regressão de SMADS, SMDET e SMS; sua conclusão deve ser comprovada por Action verde no SHA exato e artefato de evidência. Ver [gate pré-Ensaio](Gate_06_Segregacao_SEHAB_Evidencias.md) e [plano](Plano_Desenvolvimento.md).
