# DT-05 — diagnóstico somente leitura do ledger semântico (#494)

Este diagnóstico é um instrumento de engenharia para verificar **duas provas distintas**: o resultado bruto de cada avaliação por `linkage_run` e a transição operacional registrada apenas quando a assinatura da decisão muda. Ele **não** substitui os testes de três ondas, concorrência, replay ou rollback, nem certifica qualidade estatística.

**Fronteira de execução:** somente CI GitHub efêmera, com banco `JornadaE2E` ou `JornadaSyntheticDev` e dados sintéticos. Não executar em `JornadaLocal`, NODE/Compose do usuário, IBGE original, HML/PROD ou dados reais. O SQL recebe `LinkageRunId` por substituição SQLCMD; a variável deve vir exclusivamente de GUID produzido e validado pelo próprio harness CI, nunca de entrada externa. A verificação do nome do banco é uma defesa adicional, não prova de efemeridade.

O arquivo `Solution/tools/dt05-ledger-consistency-readonly.sql` verifica um resultado por item de run, resultados sem item, transições órfãs ou com run divergente, assinatura `DT05_V1` recomputada contra o resultado bruto e cadeia histórica sem transições repetidas. Retorna somente contagens e status, sem atributos pessoais. Falha fechada se a execução ou o ledger estiverem ausentes.

A suíte `Solution/tools/tests/test_dt05_ledger_diagnostic.py` compara a expressão de assinatura com a **migração canônica do checkout** e exercita mutações negativas; ela é descoberta pelo job E2E atual (`test_dt05*.py`). Trata-se de teste **estático**, não execução SQL. O aceite operacional futuro deve rodar o diagnóstico após as ondas sintéticas na CI efêmera, publicar evidência identificada por SHA/execução e confirmar todos os gates aplicáveis antes de qualquer merge.

Calibração e ativação de modelo permanecem **sempre manuais, sob demanda**; nenhum dos diagnósticos as dispara.
