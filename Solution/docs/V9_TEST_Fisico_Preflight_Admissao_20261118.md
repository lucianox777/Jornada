# V9 — admissão do TEST físico após correção da projeção (2026-11-18)

**Estado:** roteiro de admissão, **não** evidência de execução física. **Referência:** GOV-DOC-01; PR #1686, PR #1687 (merge `878d64f91ea4d87ef6773e32978fdb7d81e313c6`); campanha falha #44235715333.

## Constatação que bloqueia o chamado "TEST reduzido"

A entrada científica `--v9-independent-test <sha256.operational.json>` chama `V9IndependentTestCorpusReader.ReadAsync`. Esse leitor exige **exatamente 100.000 identidades-base** (`byIdentity.Count != 100000` causa falha). O avaliador usa **20 repetições end-to-end** e um único vencedor. Portanto, executar 100, 1.000 ou 10.000 identidades **não** exercita esse TEST científico: o próprio contrato rejeita. O parâmetro opcional `--test-database` invoca diagnóstico SQL externo somente leitura, não o TEST científico. Não trocar silenciosamente o protocolo, o denominador, o vencedor, o número de repetições ou o isolamento para fazer um smoke passar.

## Evidência já obtida

PR #1687 integrada com `unit-runtime`, `deterministic-build`, `security-guards`, `one-winner-sql-smoke`, duas execuções `full-schema-runasync-nonpublish` e preflight Release aprovados. Esses checks não equivalem a executar `V9IndependentTestCli.RunAsync` sobre coorte TEST independente.

## Admissão antes de nova campanha

1. Identificar artefato operacional SHA-pinned do vencedor da VALIDATION anterior, verificando a origem, o SHA e a conversão byte-exata SQL. Se indisponível, registrar bloqueio; **não** recriar vencedor por refit nem reutilizar TEST.
2. Prover SQL efêmero com `ConnectionStrings__Jornada` e `InitialCatalog=JornadaHarness`; validar carga do mesmo modelo operacional sem ativação ou publicação.
3. Gerar coorte TEST independente com **seed inédita e oficialmente reservada**, diferente da seed já exposta `897934382746562240`; manifest, SHA de arquivos e parâmetros de geração exigidos pelo leitor. Não usar dados de TRAIN ou VALIDATION.
4. Rodar `--v9-independent-test` com `JORNADA_V9_TEST_CORPUS_DIR`, `JORNADA_V9_TEST_OUTPUT_DIR` novo e `JORNADA_V9_TEST_RESERVED_SEED`. Preservar 100.000 identidades e 20 repetições se for o TEST científico; não chamar de reduzido.
5. Arquivar logs, `independent-test.json`, identidade do commit, SHA do vencedor, hashes da coorte, parâmetros e resultado; confirmar `refitted=false`, `certified=false`, `activated=false`, `configurationsEvaluated=1`, projeção congelada coincidente e nenhuma publicação.
6. Só após evidência física adequada avaliar liberação da terceira campanha. A aprovação da CI, sozinha, não remove esse bloqueio.

## Opção de preflight físico de menor custo — mecanismo permanente aprovado pelo PO — implementação futura

Para um ensaio físico **reduzido** sem alterar o TEST científico, implementar uma entrada de diagnóstico separada (nome, relatório e marcadores distintos), com corpus sintético pequeno, SQL efêmero, mesmo carregamento SHA-pinned e mesmo resolvedor/projeção. Essa entrada deve ser explicitamente **não científica, não certificadora e incapaz de liberar a campanha sozinha**. Exige implementação, testes e CI próprios antes de execução; não existe hoje como execução comprovada.

## Precedência e impacto

Complementa `Decisao_GOV_V9_TEST_Projecao_Congelada_20261118.md`; não modifica nem revoga o protocolo TEST de 100k, a separação TRAIN/VALIDATION/TEST, o bloqueio de publicação ou a exigência de seed inédita. Corrige o planejamento anterior que tratava um TEST científico reduzido como diretamente executável, apesar da restrição do leitor. A criação de uma entrada diagnóstica separada foi **aprovada pelo PO em 18/11/2026** e está registrada em [GOV-V9-PREFLIGHT-FISICO-PERMANENTE](Decisao_GOV_V9_Preflight_Fisico_Reduzido_Permanente_20261118.md); sua implementação e execução permanecem pendentes. Este roteiro não é autorização para a terceira campanha.
