# GOV-V9-BINDING-TRAIN-POPULACAO-PARAMETRIZADA-20261118

**Estado:** correção técnica em PR, ainda não integrada nem validada fisicamente. **Autoridade:** solicitação do PO para corrigir falha do workflow após execução 10k. **Política:** GOV-DOC-01.

## Evidência
Run #44407931103, SHA `a2362e8028b2d581d81f64330ebbca9529e510c9`, input `synthetic_population=10000`: ADMISSION e PREPARE concluíram; TRAIN falhou no script `Solution/scripts/v9-scale-100k-technical-plan.py`, linha de conferência do `gabarito.json`, por comparação literal `people == 100000`. AUDIT, VALIDATION e TEST foram pulados; nenhuma certificação.

## Correção e justificativa
O binder deve receber `--expected-people` com a população efetiva do workflow, validar estritamente `gabarito.json` autenticado, seed 42, versão e perfil, e incorporar a população real à proposta e ao fingerprint de estudo. A ausência do novo argumento preserva a verificação histórica de 100.000. A correção elimina apenas a suposição fixa do tamanho; não altera hipóteses, parâmetros científicos, hashes, integridade, segurança ou isolamento do TEST. Não relaxar a conferência para aceitar qualquer contagem.

## Rastreabilidade e pendências
Arquivos: `Solution/scripts/v9-scale-100k-technical-plan.py`, `.github/workflows/v9-standard-linear-100k.yml`. A campanha 10k permanece sujeita aos critérios da decisão GOV-V9-CAMPANHA-10K-VALIDADE-CONDICIONAL-20261118. CI, merge e nova execução física pendentes; outras suposições fixas posteriores poderão exigir correções independentes, com evidências próprias.
