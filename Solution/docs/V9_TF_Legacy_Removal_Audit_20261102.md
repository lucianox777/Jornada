# V9 — auditoria de remoção dos campos TF legados (patch P2)

Base examinada: master `e6f5db507b3a95cec71ab0e573c22ff84f101d79`.

## Constatação de código

A remoção literal de `TfCalibration`, `MotherTfCalibration` e
`NotSelected` **não é uma mudança apenas de serialização**.

- `Solution/src/Jornada.Linkage.Core/V9FittedSnapshot.cs` declara os dois
  campos no construtor posicional do snapshot, executa o caminho legado
  de dois lados em `CreateTf`, valida fontes mutuamente exclusivas e
  preserva os campos ao congelar o payload. O enum
  `V9SnapshotTfFitState.NotSelected` é aceito no validador atual.
- `Solution/src/Jornada.Linkage.Parameters.Worker/V9NestedCalibrationCli.cs`,
  `LinkageParametersWorker.cs` e
  `V9FellegiSunterAdaptiveCalibrationAdapter.cs` ainda referenciam
  `TfCalibration`. O adapter também referencia `NotSelected`.
- `Solution/scripts/v9-snapshot-tf-migration-preflight.py` verifica
  explicitamente a ausência dos campos legados na entrada de migração.
- Testes de snapshot e replay ainda referenciam o contrato anterior.

## Plano de alteração segura

1. Congelar um inventário de fixtures de snapshot V9 antigo e novo.
2. Alterar os produtores para emitir **somente** `UnitTfCalibrations`.
3. Preservar leitura histórica apenas em um adaptador de migração explícito,
   fora do contrato operacional novo; rejeitar ambiguidades e fontes duplas.
4. Remover os membros do payload operacional e o estado `NotSelected`,
   atualizando consumidores, fixtures, serialização e verificações.
5. Rodar build e testes de regressão de snapshots, replay e migração;
   nenhuma migração automática nem reinterpretação silenciosa de hashes.

**Este patch é uma auditoria de dependências, não afirma que a remoção foi
implementada.** Evitar apagar os membros antes de corrigir os consumidores,
pois isso interromperia a compilação e a leitura de snapshots históricos.
