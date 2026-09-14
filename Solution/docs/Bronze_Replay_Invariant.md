# Invariante de replay a partir do Bronze

## Decisão

O ZIP Bronze é a fonte imutável para reprocessamento do pacote recebido. O replay operacional deve conseguir reconstruir as projeções derivadas da entrega sem exigir reenvio pela fonte finalística.

A identidade canônica não é descartada no replay. `identidade.pessoa`, âncoras e histórico de resolução constituem ledger operacional durável: o `pessoa_uuid` pode ter sido criado com UUID não derivável deterministicamente do CPF e já pode ter sido publicado para sistemas externos. Apagar esse ledger e gerar outro UUID não é replay; é perda de identidade.

Assim, o invariante é:

> Bronze íntegro + contratos/configuração versionados + ledger canônico de identidade preservado devem ser suficientes para reconstruir as projeções correntes derivadas da entrega.

## Prova executável

`Solution/scripts/local-e2e.ps1` exerce o caminho real HTTP → Bronze → Processor → Silver → Gold → Serving. Depois da primeira materialização, o ensaio:

1. registra `objeto_chave` e SHA-256 do Bronze;
2. remove, somente no banco local de teste, a observação factual Silver e as projeções Gold/Serving da entrega;
3. remove `gold.pessoa` da Pessoa do fixture, preservando `identidade.pessoa`, âncora CPF, vínculo e observação Silver de Pessoa;
4. limpa apenas os recibos `ingestao.item_processado` do lote e recoloca o lote em `PENDENTE`;
5. não modifica nem republica o ZIP Bronze;
6. deixa o `ProcessorWorker` reservar novamente o mesmo lote e abrir o mesmo `objeto_chave`;
7. exige a reconstrução da observação factual Silver, `gold.pessoa`, Gold factual e Serving;
8. exige que o CPF continue resolvendo para o mesmo `pessoa_uuid` canônico;
9. exige que `objeto_chave` e SHA-256 do Bronze permaneçam idênticos.

O ensaio deliberadamente não apaga o ledger de identidade nem os eventos append-only que o referenciam.

## Limite

Esta prova cobre reconstrução das projeções correntes da entrega. Histórico governado de fusão/separação, eventos append-only e UUIDs publicados são estado autoritativo e possuem política própria de backup/restore; não devem ser recriados por inferência a partir de Bronze.
