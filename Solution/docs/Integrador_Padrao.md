# Integrador padrão da Jornada — envio e resultado por arquivo

## Objetivo

A integração padrão do Gestor possui duas operações de linha de comando equivalentes em C# e Python:

```text
--enviar <arquivo.zip>
--resultado <identificador>
```

`--resultado` recebe **um único identificador obrigatório**. São aceitos:

- SHA-256 hexadecimal do ZIP enviado;
- nome exato do ZIP enviado à API;
- caminho de um ZIP local, caso em que o cliente calcula o SHA-256 e consulta por ele.

Não é necessário informar `entregaId` ao operador.

## Arquivo de configuração

As duas implementações usam o mesmo formato `integrador.config.json`:

```json
{
  "gestor": "SEHAB",
  "accessKey": "SEGREDO_FORNECIDO_PELA_JORNADA",
  "endpoints": {
    "envio": "https://jornada.../api/v1/ingestao/entregas",
    "resultado": "https://jornada-resultado.../api/v1/ingestao/resultados/{identificador}"
  },
  "polling": {
    "intervalSeconds": 5,
    "timeoutSeconds": 3600
  },
  "diretorioSaida": "resultados"
}
```

A chave de acesso fica no arquivo de configuração, como solicitado para o executável padrão, e **o arquivo real não deve ser versionado**. O repositório contém apenas `integrador.config.example.json` com placeholder.

O arquivo deve receber ACL/permissões de sistema operacional restritas à conta que executa a integração.

## Envio

### C#

```powershell
dotnet run --project clients/Jornada.Integrador.CSharp -- --enviar C:\cargas\ENTREGA.zip --config C:\Jornada\integrador.config.json
```

### Python

```powershell
python clients/python/jornada_integrador.py --enviar C:\cargas\ENTREGA.zip --config C:\Jornada\integrador.config.json
```

O programa:

1. calcula o SHA-256 dos bytes do ZIP;
2. lê `manifest.json`;
3. monta o nome canônico `ENTREGA_<GESTOR>_<SISTEMA>_v<FORMATO>_<sha256>.zip`;
4. envia o mesmo ZIP, sem alterar seus bytes;
5. usa `Idempotency-Key: sha256:<sha256>`;
6. apresenta o recibo retornado pela Jornada.

`<SISTEMA>` é o `codigoSistemaOrigem` técnico do manifesto e deve obedecer ao contrato canônico `A-Z/0-9/_/-`. O nome de exibição do sistema é metadado separado; por exemplo, a SEHAB usa código técnico `SEHAB` e pode exibir o sistema como `HabitaSampa`.

O CSV da origem nunca é enviado por este programa. O executável recebe o ZIP **já convertido para o envelope JSON padrão da Jornada**.

## Consulta do resultado final

Exemplos equivalentes:

```powershell
Jornada.Integrador --resultado 305d577839dacf7e3cf09046cfa9b4b58c9793a5b862b896e971578326fbd548
Jornada.Integrador --resultado ENTREGA_SEHAB_SEHAB_v2_305d...zip
Jornada.Integrador --resultado C:\cargas\ENTREGA_SEHAB_SEHAB_v2_305d...zip
```

O cliente consulta o endpoint de resultado até o processamento chegar a `PROCESSADA`, `REJEITADA` ou `QUARENTENA`. Quando finalizado, grava `resultado_<identificador>.json`.

## Serviço de resultado

Projeto:

```text
src/Jornada.Resultado.Api
```

Endpoint:

```text
GET /api/v1/ingestao/resultados/{identificador}
```

O endpoint aceita o SHA-256 ou o nome exato do ZIP. O serviço não cria um segundo cadastro de credenciais: ele encaminha `X-Jornada-Gestor` e `X-Jornada-Access-Key` para a API principal para validar a autorização da Entrega antes de retornar o detalhamento.

O resultado contém:

- Entrega localizada e quantidade de Entregas encontradas para os mesmos bytes/nome;
- SHA-256 e nome canônico;
- Gestor, sistema de origem, schema de Pessoa, Natureza, Tipo e versão;
- status e timestamps da Entrega;
- todos os lotes técnicos, tentativas, recuperações e códigos de erro;
- contagens por `PESSOA`/`REGISTRO` e resultado (`INCLUIDO`, `VERSIONADO`, `RETRANSMITIDO`, `EXCLUIDO`, `REABERTO`);
- quantidade ainda disponível na trilha granular;
- quantidade já consolidada pela política de retenção;
- indicador explícito de existência ou não de detalhe item a item integral.

A resposta não expõe CPF nem conteúdo cadastral/factual.

## Retenção

`ingestao.item_processado` pode ser consolidada conforme política de retenção. Quando isso ocorrer, o endpoint soma a trilha granular ainda existente com `ingestao.item_processado_resumo` e informa que o detalhe histórico item a item deixou de ser integral. O resultado agregado não finge possuir dados que já foram expurgados.

## Configuração do serviço de resultado

O serviço necessita:

```json
{
  "ConnectionStrings": {
    "Jornada": "..."
  },
  "JornadaApiBaseUrl": "https://jornada-api-interna..."
}
```

`JornadaApiBaseUrl` é usado exclusivamente para a validação da mesma credencial de integração na API principal.
