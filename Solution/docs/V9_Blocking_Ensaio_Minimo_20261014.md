# V9 — Ensaio mínimo de blocking (diagnóstico, não seleção)

Executadas seis configurações da PR #1035 em quatro registros artificiais, com um único par verdadeiro e data de nascimento completa. Resultado: nome+mãe M=1/U=0; nome+nascimento M=1/U=2; mãe+nascimento M=1/U=0; união de três passes M=1/U=2; tripla restritiva M=1/U=0; união de dois passes com cap 30 M=1/U=2. Recall 1/1 em todos os casos. A união acrescentou dois candidatos falsos sem novo par verdadeiro nesta fixture.

**Limitação decisiva:** corpus mínimo artificial não permite concluir qual blocking é melhor, nem estimar fanout real, FDR ou ausência de campos. Execução local independente, não GitHub Actions. Próximo passo: corpus TRAIN/VALIDATION representativo, com normalização C# versionada, missingness, nomes comuns, sobrenomes e estratos.
