# Evidência DEV — fronteira explícita Processor → Runner V9 (22/11/2026)

## Contexto e rastreabilidade
Plano aprovado: `Solution/docs/Decisao_GOV_DEV_E2E_Integral_V9_Plano_Aprovado_20261122.md`. Issues #1851 (E2E integral), #1584 (paridade Runner), #1488 (seleção governada). Precedência: proteções de despacho V9 integradas na PR #1873, commit `b5446e34a4e875153a4ad8c8d1ca506d9fefe66d`.

## Evidência de código inspecionada em master
- `ProcessorWorker.ExecuteAsync` executa `processor.ProcessNextAsync(stoppingToken)`; não despacha o Runner probabilístico automaticamente.
- `IdentityResolutionCoordinator.ResolveAsync` mantém CPF ausente admitido como `NAO_RESOLVIDO` / `AGUARDA_LINKAGE_SOB_DEMANDA`.
- `ProbabilisticLinkageBatchRunner.RunAsync` rejeita seleção V9 produtiva não certificada antes de continuar pelo caminho legado.

## Implementação desta PR
- `Solution/tests/scripts/test_e2e_processor_runner_explicit_boundary.py`: três regressões estáticas offline, sem SQL, rede, execução do Runner ou acesso a dados reais.
- `.github/workflows/jornada-pr-required-fast.yml`: invocação obrigatória desses testes em `unit-runtime`.

## Limites e próximos gates
Este contrato negativo impede que alterações futuras transformem a ingestão normal em despacho V9 implícito. **Não implementa** adaptador V9 certificado, integração física Processor → Runner → Gold, paridade de TEST, oráculo independente ou E2E 1k. O marco #1851 depende dessas implementações e de testes posteriores em ambiente descartável, mediante gates aplicáveis. Sem TRAIN/TEST científico, sem 100k físico, sem HML/PROD.

Registro de evidência técnica de execução do plano já aprovado; **nenhuma nova decisão de PO** ou alteração do índice de decisões vigentes.
