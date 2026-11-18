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

## Verificações pontuais pré-workflow — orientação atual do PO

O PO revogou a criação de mecanismo permanente em 18/11/2026. A atividade atual consiste em **verificar pontualmente** o carregamento do vencedor SHA-pinned, a projeção congelada, a conversão operacional e o SQL descartável para decidir se o workflow científico pode ser iniciado. Priorizar verificações já existentes e evidência física real; não introduzir automação permanente ou alterar o protocolo científico. Caso o artefato anterior não esteja disponível, registrar bloqueio e escolher outra verificação tecnicamente válida, sem fabricar resultado. Não considerar os testes pontuais, isoladamente, prova de TEST científico concluído.

## Precedência e impacto

Complementa `Decisao_GOV_V9_TEST_Projecao_Congelada_20261118.md`; não modifica nem revoga o protocolo TEST de 100k, a separação TRAIN/VALIDATION/TEST, o bloqueio de publicação ou a exigência de seed inédita. Corrige o planejamento anterior que tratava um TEST científico reduzido como diretamente executável, apesar da restrição do leitor. A aprovação de mecanismo permanente foi **revogada pelo PO em 18/11/2026** e consta do [registro histórico](Decisao_GOV_V9_Preflight_Fisico_Reduzido_Permanente_20261118.md). O objetivo vigente é realizar verificações pontuais antes de liberar o workflow, sem considerar a campanha automaticamente autorizada.
