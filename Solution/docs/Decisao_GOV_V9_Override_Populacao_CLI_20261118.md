# GOV-V9-OVERRIDE-POPULACAO-CLI-20261118 — Decisão aprovada

**Product Owner:** solicitação expressa em 18/11/2026, após merge da PR #1721.

## Decisão e precedência
A quantidade total de identidades sintéticas poderá ser informada na linha de comando por `--people N`. Quando fornecido, esse argumento **sobrepõe** `syntheticPopulation` do arquivo `config/calibration/v9-synthetic-sampling.json`. Na ausência do argumento, prevalece a configuração. O workflow manual expõe o campo opcional `synthetic_population`, que é encaminhado como `--people` para os geradores independentes de TRAIN, VALIDATION e TEST. A alteração não muda a divisão probabilística 60/20/20, seeds, isolamento científico, nem permite executar TEST antes do vencedor congelado.

## Justificativa, impactos e controles
Permitir campanhas de diferentes tamanhos sem editar e versionar o arquivo de configuração a cada execução. A validação de entrada exige inteiro decimal positivo no workflow e `SyntheticCorpusOptions.Validate()` rejeita população não positiva. O parâmetro deve ser usado de maneira consistente nos três geradores independentes, sem forçar quotas exatas de cada partição. A capacidade física para valores elevados (por exemplo, 1 milhão) **não está certificada**; a CI de contratos não substitui a execução física. Mantêm-se as proteções de admissão, o SHA fixado e a aprovação manual de preflight.

## Rastreabilidade
Implementação: `.github/workflows/v9-standard-linear-100k.yml`; CLI já implementada em `Solution/src/Jornada.Linkage.SyntheticCorpus/Program.cs` (`GetInt(values, "people", ConfigPeople())`); teste: `Solution/tests/scripts/test_v9_standard_linear_workflow.py`. Esta decisão complementa a decisão 60/20/20 da PR #1721 e a GOV-DOC-01. Integração e evidência CI pendentes nesta branch até aprovação.
