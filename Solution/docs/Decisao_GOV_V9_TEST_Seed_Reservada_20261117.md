# GOV-V9-TEST — Vinculação da seed reservada ao corpus independente

**Data:** 2026-11-17 (diagnóstico; logs UTC de 2026-11-18). **Estado:** correção proposta em PR, sujeita a CI e integração. **Política:** GOV-DOC-01.

## Contexto e evidência
A execução GitHub Actions 44176520670, job 134517085089, SHA 566c15b369afd46fc5969a7cab185c1ad5506271, concluiu ADMISSION, PREPARE, TRAIN (324), AUDIT e VALIDATION, mas falhou antes de medir TEST. O gerador utilizou seed global reservada 9983388601452659356, enquanto `V9IndependentTestCorpusReader` exigia a constante histórica 20261113001, gerando `InvalidDataException: TEST não corresponde ao protocolo da própria camada TEST.`

## Correção técnica proposta
O TEST recebe a seed da reserva de uso único do próprio workflow. Antes da exposição do corpus ao avaliador, o job verifica o recibo local de reserva, sua seed, SHA do código, run ID, tentativa e referência global, comparando-os com o manifesto de geração. O leitor exige a seed fornecida pelo job e rejeita ausência, valores históricos e divergências. Permanecem os checks de SHA-256 do corpus, a independência de TRAIN/VALIDATION, o único vencedor congelado e a ausência de refit, certificação, ativação ou publicação.

## Precedência e impacto
Esta correção substitui **apenas** a constante de seed do leitor TEST na cadeia padrão, sem alterar o protocolo estatístico, o vencedor, o gerador ou o ledger global. O TEST anterior permanece inválido/incompleto e sua seed exposta não pode ser reutilizada. Nova campanha somente após revisão, testes, merge e despacho manual autorizado, com seed inédita. Não há aprovação científica presumida.

## Rastreabilidade e validação pendente
- Run: https://github.com/lucianox777/Jornada/actions/runs/44176520670/job/134517085089
- Código: `Solution/src/Jornada.Linkage.Parameters.Worker/V9IndependentTestCorpusReader.cs`
- Workflow: `.github/workflows/v9-standard-linear-100k.yml`
- Regressão: `Solution/tests/scripts/test_v9_standard_test_seed_binding.py`
- Pendente: CI, revisão e integração da PR; nova execução TEST independente e auditoria de métricas.
