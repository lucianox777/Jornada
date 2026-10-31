# DC-V9-CALIBRADOR-BASELINE-E-VALIDACAO-20261030

**Estado:** consolidação de decisão e proveniência para a implementação do calibrador adaptativo V9. Não representa novo ensaio nem aprovação operacional.

## Decisão
A implementação adaptativa **já tem execução material e artefatos completos**. Preservar a configuração de 20/10/2026 com ~97,5% de recall como **baseline de regressão**, investigar melhorias incrementais sem refazer o motor ou repetir a seleção no holdout consumido. Parâmetros são referência experimental, não constantes imutáveis nem modelo aprovado. Reconciliar com [DC-V9-BOOT-CORE-20261030](DC-V9-BOOT-CORE-20261030.md), [DC-V9-BOOT-01](V9_Registro_Decisoes_Bootstrap_Gold_20261020.md), [seleção interna TRAIN](V9_Selecao_Interna_TRAIN_Finalistas_20261019.md) e [suporte por massa](V9_Suporte_Massa_Backoff_20261019.md).

## Fontes primárias preservadas no Git
Pasta `Solution/data/calibration/v9/mass-support-20261020/`:
- `plan.json`: **plano integral** de busca, regras, limites e seeds.
- `training-split-manifest.json`: partição por identidade.
- `b98121f9b4854e0db56026df1ddaef30e0f8c32717baa88a68549bd0ad08d75a.selection.json.gz`: envelope completo de **926 avaliações em seis lotes**; SHA descomprimido no nome, 950.811.300 bytes originais.
- `evaluated-fits.zip`: snapshots de todas as configurações avaliadas.
- `0a3459585fcf3d20fbd0854e977b84a769127a9ab01ab0d72417dff42d0b3e5b.fitted.json`: finalista BLIND; `6d93690dccac2c92dc4d3113cbb9169dd19156c4f9b3d849ededfb4c54ae5f3f.fitted.json`: variante Gold.
- `final-study-report.json`, `finalist-validation.json`, `fresh-generation-manifest.json`, `fresh-corpus-recipe-verification.json`, `fresh-holdout-sample-plan.json`, `gain-by-batch.json`, `gold-runner-study.json`, `completion-journal.jsonl`, `evidence-manifest.json`.
- `README.md` contém protocolo de verificação de SHA sem reabrir holdout; `evidence-manifest.json` lista hashes e tamanhos.

**Atenção:** `selection-reference.json` declara `externalValidationUsed=false` para o envelope de **seleção interna**, enquanto `finalist-validation.json` documenta avaliação externa posterior. Não interpretar esses campos como contradição ou como validação independente certificada.

## Configuração exata recuperada do plano
- Corpus: seed 42, 30 mil identidades, perfil `demographic-primary/clean`, CSV SHA `28bbb9f4c63c97c353e7f29cdbc77091a4773b2f4c596e46103edadd93635d17`; TRAIN 16.676 identidades sem CPF, 11.673 ajuste/5.003 seleção.
- `ModelOptions` iniciais: alpha {0.1, 0.5, 1}; threshold {0.9, 0.95, 0.99}; **TfWeight=0 em todas as nove opções**. Margens {0.5, 2}; 36 combinações grossas com dois rule sets.
- Refinamento: até 32 lotes, até 1.000.000 configurações declaradas, 2.000 repetições de bootstrap pareado, seed 20261019; passo inicial de threshold 0.025, margem 0.25; `MinimumRecallImprovement=0.0001`, `MinimumFdrImprovement=0.00001`.
- TRAIN split: `JORNADA_V9_INTERNAL_70_30_20261019_V1`, fração de ajuste 0.7.
- Descoberta: prefixo máximo 4; amostra U 100.000, seed 20261019; cap de pares positivos 2.000.000; tentativas 10.000.000.
- Catálogo declarado: 19 passes de blocking, fingerprint `d0d896068d1356377071b60610fe81a05080103f0ba1276ba6075695893810e8`; baseline três passes `P_FIRST_BIRTH_YEAR`, `P_LAST_M_PHON`, `P_PHON_M_LAST`.
- Restrições: recall mínimo 0.95, FDR pontual máximo 0.01, fanout falso P99 máximo 100; seleção com FDR superior máximo 0.01, mínimo 1.000 grupos independentes, P95 máximo 60.000 ms. `MaximumAcceptedPerBaseIdentity=4`, `MaximumBlindClusterSize=16`.
- Suporte: 99% da massa por classe, piso 5 M/U nas respectivas células do núcleo, cauda conjunta quando suporte >= 5; `V9_CLASSWISE_MASS_SUPPORTED_TAIL_MARGINAL_BACKOFF_V2`.
- Finalista: alpha 0.1, threshold 0.8671875, margem 0.609375, oito passes; snapshot `0a345958...f42d0b3e5b`.
- Resultado: TRAIN_SELECTION TP 6.834, FP 0, recall 97,4754%; avaliação adicional seed 20261020001 TP 7.539, FP 2, recall 97,5417%, FDR 0,02652%, bound condicional superior 0,40479%. `certified=false`, `test_used=false`, `activated=false`.

## Implicações e pendências objetivas
1. **TF do finalista:** o plano original prova `TfWeight=0` nas opções iniciais, e a documentação de suporte diz que TF permanece zero no fluxo padrão. Portanto, **o resultado de 97,5% foi obtido sem TF positiva**. Investigar TF como melhoria isolada futura, sem reclassificar o baseline nem alterar seu snapshot.
2. **Refinamento:** a pesquisa de passes, limiares e margens já foi feita e documentada (926 avaliações). Não reimplementar; proteger contra regressões e verificar equivalência do código atual.
3. **Paridade:** conferir `*-parity.json`, os 729 vetores e o snapshot congelado antes de mudar score, parâmetros ou contrato.
4. **Proveniência do commit produtor:** recuperar commit/Actions exatos por histórico Git antes de afirmar que a master atual reproduz bit a bit o resultado. A presença dos artefatos em master não demonstra reprodução atual.
5. **Última validação:** ainda falta comprovação de validação estatística independente e gates de governança; TEST permanece selado. Não reutilizar seed 20261020001 nem abrir novo holdout para escolher alternativas.
6. **Melhorias:** primeiro comparar código atual com snapshot/fit; depois estudar TF/ExactLast e catálogo nominal em ensaios separados com TRAIN, registrando riscos e ganho incremental, sem comprometer o baseline.

**Proibição de interpretação:** FDR observado muito baixo não equivale a FDR certificada, nem a qualidade representativa de bases brasileiras reais.
