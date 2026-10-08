# T0.1b — Console real até Gold no E2E isolado

## O que foi alterado

O perfil **E2E descartável** da CI agora define `JORNADA_E2E_CONSOLE_ZIP=true`.
O `local-e2e.sh` mantém o isolamento de `JornadaE2E` e o mesmo fluxo funcional de antes;
a diferença é que o ZIP inicial vem da Console real, via HTTP, e **não** diretamente
do `build-ingestion-fixture.py` chamado pelo harness.

A sequência é:

1. CI prepara seu próprio contêiner SQL e banco `JornadaE2E`, sem compartilhar volumes ou
   referência IBGE do `JornadaLocal`; faz build Release uma vez.
2. `console-zip-e2e-source.py` inicia `Jornada.DevConsole.dll` em loopback com HOME e
   XDG temporários e `JORNADA_RUNTIME_MODE=DEV`.
3. A Console recebe `POST /api/zip/manual/start` com o caso `AA01_v2`,
   registra a execução, e disponibiliza `GET /api/runs/{id}/result`.
4. O script confere o recibo `SUCESSO`/exit 0, a resposta ZIP, as três entradas
   `manifest.json`, `pessoas.jsonl`, `registros.jsonl`, os bytes normalizados,
   o CRC e o SHA-256 incorporado ao nome do arquivo.
5. O `local-e2e.sh` envia **esse mesmo ZIP** à API real; o pipeline original
   prossegue com auditoria, Bronze, Processor, Silver, Gold e Serving,
   inclusive os testes preexistentes de idempotência e autenticação.
6. As evidências coexistem em `.local/e2e/console-zip-source.json` (Console → ZIP)
   e `.local/e2e/evidence.json` (API → Bronze → Silver → Gold → Serving).

O gerador de fixture original continua sendo o caminho **padrão em execução local**.
A flag explícita altera somente a origem do ZIP quando requisitada pelo harness.
O E2E da CI já opera em banco descartável, diferente do banco habitual.

## Escopo comprovável e lacunas

Se o job `e2e` passar com `JORNADA_E2E_CONSOLE_ZIP=true`, a evidência cobre
`Console HTTP → ZIP → API → Bronze → Processor → Silver → Gold/Serving` com
**uma Pessoa e um benefício sintéticos, determinísticos por CPF**.
Isso não valida o botão de envio da interface browser em si: a remessa no harness
usa o mesmo endpoint de API diretamente via curl. Tampouco valida o fluxo completo
de Linkage probabilístico sem CPF, os contratos condicionais JSON Schema da Console,
nem a segurança de download de objetos Bronze: cada caso permanece pendente.

Os testes de modo DEV/PROD e casos ZIP *somente Pessoa*, com fatos e JSONL inválido
são cobertos em PR própria de teste HTTP da Console, sem necessidade de SQL.
Ainda são necessários testes browser, credencial, rollback e cenários multi-Entrega.

**Operações proibidas nesta frente:** reset/clean do `JornadaLocal`, migração de
dados reais, HML/PROD, alteração de IBGE histórico e qualquer trabalho da
**Trilha 4**. O script `local-e2e.sh` é destrutivo **somente para o banco
descartável `JornadaE2E`**; não executar manualmente contra ambiente compartilhado.


## T0.1c — remessa somente Pessoa via Console (gate adicional)

No mesmo job `jornada-ci/e2e` com `JORNADA_E2E_CONSOLE_ZIP=true`,
`local-e2e.sh` gera um **segundo ZIP real da Console** a partir da fixture
`AA01_SEM_FATOS_v2`. O helper exige três entradas no ZIP; o membro
`registros.jsonl` deve ter **tamanho físico zero**. O harness confirma
o SHA-256 nominal, envia à API DEV sintética, espera a Entrega terminar
em `PROCESSADA` e consulta o mesmo banco descartável para exigir:

- uma referência em `bronze.entrega_arquivo` para a Entrega;
- uma observação de Pessoa na Silver;
- **zero** registros factuais na Silver para a Entrega;
- uma Pessoa correspondente ao CPF sintético na Gold.

A evidência `.local/e2e/console-person-only-evidence.json` guarda apenas ID
da Entrega, hash do ZIP, contagens e nomes das camadas, sem nome/CPF. O teste
não é executado no harness local sem a flag explícita. A aprovação depende
do job E2E passar, não deste texto nem de teste HTTP isolado.

Ainda faltam a reprodução pelo navegador, o caso sem CPF com Linkage governado,
o fluxo de clique “Enviar arquivo” da própria UI e regras de JSON Schema
no gerador manual. A Trilha 4 permanece proibida.
