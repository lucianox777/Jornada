# Jornada.Integrador.CSharp

CLI C# para envio de pacotes ZIP da Jornada e consulta do resultado de processamento.

## Enviar um ZIP

```text
Jornada.Integrador.CSharp --enviar <arquivo.zip> [--config integrador.config.json]
```

O envio unitário preserva o arquivo na origem.

## Enviar todos os ZIPs da pasta do CLI

```text
Jornada.Integrador.CSharp --enviar-todos [--config integrador.config.json]
```

O modo em lote usa a pasta do executável (`AppContext.BaseDirectory`) como origem e considera apenas arquivos `*.zip` diretamente nessa pasta, sem percorrer subpastas.

Para cada ZIP:

1. o arquivo é enviado pela mesma rotina do envio unitário;
2. somente depois de um envio bem-sucedido, a subpasta `Enviados` é criada quando necessário;
3. o ZIP original é movido para `Enviados` mantendo o mesmo nome;
4. se o envio falhar, o ZIP permanece na pasta de origem e o lote continua com os demais arquivos;
5. se já existir em `Enviados` um arquivo com o mesmo nome, o ZIP não é enviado nem sobrescrito e é contabilizado como falha.

O comando retorna código `0` quando não há falhas e código `1` quando pelo menos um ZIP falha. Se não houver ZIPs na pasta do CLI, não há erro.

## Consultar resultado

```text
Jornada.Integrador.CSharp --resultado <nome.zip> [--config integrador.config.json] [--saida resultado.json]
```

A consulta aceita somente o nome exato do ZIP enviado, sem caminho e sem SHA-256.
