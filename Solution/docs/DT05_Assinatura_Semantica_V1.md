# DT-05 — Contrato da assinatura semântica V1

> **Organização do monorepo (06/10/2026):** a antiga solução embarcada `Solution/ApoioSecretarias/` foi removida do repositório principal. Referências abaixo a esse caminho descrevem evidência/histórico anterior à remoção. O produto não compila nem distribui esse transmissor/preparador; apenas schemas SEHAB estritamente sintéticos necessários à regressão permanecem em `Solution/tests/fixtures/external-contracts/gestores/SEHAB/`.

**Estado:** contrato técnico V1 congelado; **aceite estreito de CPF tardio ponta a ponta CONCLUÍDO** no PR [#540](https://github.com/lucianox777/Jornada/pull/540), CI [#36301124197](https://github.com/lucianox777/Jornada/actions/runs/36301124197) (PASS), HEAD validado [`c687f63f7287fda2f6eefa6470035fdef39aef70`](https://github.com/lucianox777/Jornada/commit/c687f63f7287fda2f6eefa6470035fdef39aef70). Isso **não** constitui aceite de replay histórico/NAS ampliado. **Implementação canônica:** `database/migrations/20260927_Linkage_Transicao_Semantica_DT05.sql`, procedure `identidade.sp_registrar_transicoes_linkage_run`. Mudança nos campos, ordem, sentinelas ou codificação exige nova versão de assinatura e migração/ensaio explícitos; não alterar silenciosamente V1.

## Identidade e conteúdo

A chave lógica da sequência é `pessoa_observacao_id`. Para cada `linkage_resultado` do run em publicação, a procedure calcula `SHA2_256(CONVERT(VARBINARY(MAX), CONCAT(...)))` com prefixo `DT05_V1|`, campos na ordem exata abaixo, separados por `|`. Os campos nulos são representados por `<NULL>`. GUIDs são convertidos em `NVARCHAR(36)` quando indicado. A conversão SQL para `VARBINARY(MAX)` faz parte do contrato (não assumir que o hash de UTF-8 em outra linguagem seja equivalente).

| Ordem | Campo do resultado bruto | Conversão |
|---|---|---|
| 1 | `modelo_id` | `NVARCHAR(36)` |
| 2 | `modelo_versao` | conversão implícita de `CONCAT` |
| 3 | `status` | valor ou sentinela |
| 4 | `motivo` | valor ou sentinela |
| 5 | `resultado_publicacao` | valor ou sentinela |
| 6 | `pessoa_uuid_publicado` | `NVARCHAR(36)` ou sentinela |
| 7 | `status_publicacao` | valor ou sentinela |
| 8 | `motivo_publicacao` | valor ou sentinela |
| 9 | `melhor_candidato_uuid` | `NVARCHAR(36)` ou sentinela |
| 10 | `segundo_candidato_uuid` | `NVARCHAR(36)` ou sentinela |
| 11 | `politica_publicacao_versao` | valor ou sentinela |
| 12 | `pessoa_origem_id_publicado` | `NVARCHAR(36)` ou sentinela, conforme SQL atual |

**Excluídos intencionalmente:** `linkage_run_id`, timestamps, scores numéricos (inclusive arredondamento), `universo_referencia`, `calculado_em`, `publicado_em`. O resultado bruto de **cada run** permanece em `identidade.linkage_resultado`; o ledger não substitui o histórico bruto. Como modelo e motivo fazem parte da assinatura, mudanças nesses campos são transições mesmo que o UUID publicado permaneça igual. Uma alteração de CPF que não altere nenhum dos 12 campos **não** gera evento V1; o CPF bruto não integra a assinatura.

## Emissão e idempotência

Sob a transação SERIALIZABLE de publicação e lock do Runner, compara-se a assinatura do resultado com a **última transição registrada para a mesma observação**. Se não existe anterior: `INICIAL`; se o hash difere: `ALTERACAO_SEMANTICA` com `assinatura_anterior_sha256` igual ao hash da última transição; se é idêntico: não inserir evento. `UQ_linkage_transicao_resultado` impede duplicação do mesmo resultado em retry. A chave de ordenação do ledger é `transicao_id`, não o timestamp do run.

**Compatibilidade do E2E com a migração de 27/09/2026:** `20260927_Remove_Modo_Carga_Inicial.sql` remove deliberadamente `controle.modo_carga_inicial`. O workflow especializado de três ondas revelou uma consulta residual à tabela abolida (run #36351456686, erro SQL 208) antes da primeira onda. Os três scripts de E2E/Runner deixam de consultar essa tabela e preservam a guarda de banco sintético isolado, a exigência de modelo `ATIVO` e a coordenação real do Runner. O teste unitário `RetiredInitialLoadModeScriptTests` protege a regressão de contrato; **a atualização dos scripts não é nova validação estatística nem conclusão do replay NAS**. A primeira reexecução [#36351828002](https://github.com/lucianox777/Jornada/actions/runs/36351828002) avançou além desse preflight, mas a primeira Entrega foi para `QUARENTENA`: o seed sintético ainda referenciava o schema Pessoa SEHAB v4 transferido para `Solution/ApoioSecretarias/`. O `local-e2e.ps1` passou a **copiar temporariamente** os contratos externos para o workspace DEV antes de subir API/Processor, verificar SHA-256 de v4/v5 contra a fonte da Solução de Apoio e remover o staging no `finally`; não restaurar arquivos SEHAB versionados no produto nem distribuir esse staging. O teste `RetiredInitialLoadModeScriptTests` verifica também essas guardas. **Somente a reexecução do workflow corrigido, incluindo as três ondas, comprova o restabelecimento do ensaio.**

## Evidência e pendências

O teste SQL `Dt05SemanticThreeWavesSqlServerTests`, integrado no PR #524, verifica três runs, retry por onda, encadeamento de hashes e preservação dos resultados brutos. Na terceira onda ele altera **`motivo_publicacao`** para um marcador de evidência de CPF tardio: isso valida o ledger, **não** a ingestão real do CPF. O **Marco A — aceite estreito** foi fechado pelo E2E opt-in do PR #540 em `JornadaSyntheticDev`, exclusivamente com `Jornada_Seed_Dev` e colisão controlada de duas referências sintéticas. O teste usa o Runner C# real e duas entregas HTTP/Processor reais, não altera manualmente resultado ou motivo de publicação:

- **Onda 1:** observação Silver v1 sem CPF → Runner ON_DEMAND sobre o `pessoa_observacao_id` original; resultado bruto `CONFLITO`, publicação `INDEFINIDA`, transição `INICIAL`.
- **Onda 2:** reavaliar o **mesmo** `pessoa_observacao_id` sem alteração no corpus; os doze campos da assinatura V1 são iguais aos da onda 1; o terceiro campo de hash do ledger permanece inexistente nesta onda: **zero nova transição**.
- **Onda 3:** nova entrega real para o **mesmo** `pessoa_origem_id` e `codigoPessoaOrigem`, gerando Silver **v2 append-only com CPF sintético válido**, vínculo `CPF_DETERMINISTICO` com `identidade.cpf_ancora` e referência progressiva `REFERENCIA`. O Runner probabilístico, que por contrato só avalia observações **sem CPF**, reavalia a observação v1 original, agora com a referência progressiva alterada pela v2. Emite `ALTERACAO_SEMANTICA` devido à publicação efetiva, **sem marcador artificial**. A observação v1 continua sem CPF.

**Evidência CI conclusiva:** [run #36301124197](https://github.com/lucianox777/Jornada/actions/runs/36301124197), código no SHA [`c687f63f7287fda2f6eefa6470035fdef39aef70`](https://github.com/lucianox777/Jornada/commit/c687f63f7287fda2f6eefa6470035fdef39aef70), [artefato `dt05-cpf-late-e2e-evidence`](https://github.com/lucianox777/Jornada/actions/runs/36301124197/artifacts/10925705817). Três runs `PUBLICADO`, **três resultados brutos distintos e persistidos** em `identidade.linkage_resultado` e **exatamente duas transições** em `identidade.linkage_transicao_semantica`, para uma única observação lógica. A assinatura `INICIAL` foi `A09318EE31888608362096A8ABB8232FE2D975DEAA148E7A0F9F2E35745A2268`; a `ALTERACAO_SEMANTICA`, `92950C1E14F75F56F9D322618FD1CC5FEC8F7B3DAA19E462273C7365DC60547E`. O `assinatura_anterior_sha256` da segunda transição é exatamente a primeira assinatura. A versão Silver evoluiu **1 → 2**; o Processor confirmou âncora CPF e referência progressiva na origem persistente.

**Reexecução controlada:** configure um arquivo privado apontado por `JORNADA_LOCAL_ENV_FILE` com `JORNADA_SQL_DATABASE=JornadaSyntheticDev`; então execute, a partir de `Solution`, `pwsh ./scripts/local-e2e.ps1 -VerifyLinkageRunner -Dt05CpfLate -AllowSyntheticReset`. O preflight recusa reset compartilhado e outras bases; no CI o ambiente é sintético e destruído depois de salvar o artefato.

**Escopo residual (Marco B, independente):** o binding SQL create-once do manifesto ao run foi integrado no PR #700; na PR #702 candidata, o Runner publica/verifica o manifesto referencial e registra esse binding antes do score, com rehash físico dos ZIPs pinados. Ainda permanecem pendentes o replay histórico determinístico completo, congelamento executável suficiente de parser/normalização/scorer/ruleset, estado efetivo de candidatos e governança, concorrência/recuperação de GC/retenção e medição DEV. `LinkageReplay:CaptureBronzeSources` permanece desligado por padrão.

## Gate de encerramento da issue #494 (10/10/2026)

O ledger semântico é append-only por transição, **não por execução**: um replay sem mudança de assinatura deve preservar a quantidade de transições, embora a auditoria bruta de runs continue registrada em `identidade.linkage_resultado`. A comprovação de três ondas sintéticas não certifica, isoladamente, retenção NAS, recuperação de GC ou desempenho de produção. A issue #494 somente poderá ser encerrada após reconciliar o escopo completo e seus critérios de aceite com os testes E2E reais; não confundir merge de documentação com fechamento da DT-05. A calibração não deve ser acionada por replay.
