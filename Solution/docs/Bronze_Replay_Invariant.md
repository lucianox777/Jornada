# Invariante de replay a partir do Bronze

## Decisão

O ZIP Bronze é a fonte imutável para reprocessamento do pacote recebido. O replay operacional deve conseguir reconstruir as projeções derivadas da entrega sem exigir reenvio pela fonte finalística.

A identidade canônica não é descartada no replay. `identidade.pessoa`, âncoras e histórico de resolução constituem ledger operacional durável: o `pessoa_uuid` pode ter sido criado com UUID não derivável deterministicamente do CPF e já pode ter sido publicado para sistemas externos. Apagar esse ledger e gerar outro UUID não é replay; é perda de identidade.

Assim, o invariante é:

> Bronze íntegro + contratos/configuração versionados + ledger canônico de identidade preservado devem ser suficientes para reconstruir as projeções correntes derivadas da entrega.

## Prova executável

`BronzeReplayInvariantTests` exerce o caminho real Bronze → parser do Processor → Silver → Gold → Serving. O ensaio:

1. monta o fixture AA01 v2 como ZIP e o grava no `FileSystemBronzeObjectStore` content-addressed;
2. processa o pacote pelo `SqlProcessorRepository` real;
3. registra `objeto_chave`, SHA-256 e `pessoa_uuid` canônico;
4. remove, somente no banco de integração, a observação factual Silver e as projeções Gold/Serving produzidas pelo fixture;
5. remove `gold.pessoa` do UUID do fixture, preservando `identidade.pessoa`, âncora CPF, vínculo e observação Silver de Pessoa;
6. limpa os recibos `ingestao.item_processado` do lote e recoloca o mesmo lote em `PENDENTE`;
7. não modifica nem republica o ZIP Bronze;
8. reserva novamente o mesmo lote e abre o mesmo `objeto_chave`;
9. exige a reconstrução da observação factual Silver, `gold.pessoa`, Gold factual e Serving;
10. exige que o CPF continue resolvendo para o mesmo `pessoa_uuid` canônico e que chave/hash do Bronze permaneçam válidos.

O ensaio deliberadamente não apaga o ledger de identidade nem os eventos append-only que o referenciam.

## Limite

Esta prova cobre reconstrução das projeções correntes da entrega. Histórico governado de fusão/separação, eventos append-only e UUIDs publicados são estado autoritativo e possuem política própria de backup/restore; não devem ser recriados por inferência a partir de Bronze.
