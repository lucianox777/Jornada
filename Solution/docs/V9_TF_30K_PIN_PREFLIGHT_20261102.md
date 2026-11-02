# V9 TF 30k — origem operacional do pinned_ibge_json

O workflow `.github/workflows/v9-physical-seed42-probe.yml` é manual
(`workflow_dispatch`). A execução física **não ocorre** na PR nem no
`preflight`. O operador autoriza o `execute_30k` separadamente.

## Procedimento após merge autorizado

1. Copiar o SHA **completo, de 40 caracteres** da `master` atual.
2. Abrir **Actions → v9-physical-seed42-probe → Run workflow**;
   selecionar `master`, `mode=preflight`, `expected_sha=<SHA>`,
   `pinned_ibge_json` vazio.
3. O preflight prepara `JornadaHarness` descartável, carrega o IBGE do
   manifesto versionado e gera o corpus **clean / demographic-primary /
   seed 42 / 30.000 identidades-base**, sem calibrar. Após aprovação
   dos jobs, baixar o artefato `v9-tf-30k-pin-preflight-<run-id>-<sha>`.
4. Conferir `git-approved.json`, `pinned-ibge.json` e
   `generation-manifest.json`. O SHA do preflight deve ser exatamente o
   SHA autorizado para o experimento. **Não** utilizar hashes de fixtures.
5. Somente mediante autorização operacional específica para executar o
   experimento, disparar `mode=execute_30k` na **mesma branch e SHA**,
   informando `expected_sha` e o conteúdo integral do
   `pinned-ibge.json` como `pinned_ibge_json`.
6. O execute refaz o SQL, a referência e o corpus em outro ambiente
   descartável. Compara integralmente os quatro campos: ID da versão SQL,
   SHA publicado, SHA materializado **pela mesma implementação C# usada
   pelo calibrador** e bootstrap do corpus. Divergência: erro antes
   da calibração. Não há uso de TEST na escolha ou ativação.

A procedência do bootstrap não é inferida pela seed isoladamente:
`CorpusFingerprint` é SHA-256 dos bytes de `pessoas_verdade.csv`,
autenticados contra `generation-manifest.json`.
`GenerationHypothesesFingerprint` é SHA-256 dos bytes completos desse
manifesto, que vincula gerador, dados de origem, hipóteses, parâmetros,
seed e hashes de saída. O script verifica o conteúdo real, a quantidade
de 30.000 IDs-base distintos e as partições.

O ID SQL é lido do banco **novo**. Se não reproduzir o valor do preflight,
a operação falha: não se admite ignorar o ID ou trocar automaticamente
para outro. O procedimento deve ser revisto antes de novo dispatch.

Artefatos não incluem `pessoas_verdade.csv`, `observacoes.csv`,
`gabarito.json`, nem credenciais. O workflow não certifica, publica ou
ativa modelos; seguem exigidas as decisões e autorizações de governança
existentes (incluindo DT-15).
