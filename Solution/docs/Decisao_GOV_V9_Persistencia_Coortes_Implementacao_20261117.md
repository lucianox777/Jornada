# GOV-V9-PERSISTENCIA-COORTES-IMPLEMENTACAO-20261117

**Decisão aprovada pelo PO (17/11/2026):** persistir os corpora sintéticos descartáveis e separados de VALIDATION e TEST pelo **mesmo mecanismo de PREPARE**, sem criptografia nova, armazenamento permanente ou backup físico SQL. A separação científica de fases continua obrigatória. GOV-DOC-01.

## Contexto e justificativa
PREPARE já gera `train-inputs.tar.gz` com `SHA256SUMS`, `code-sha.txt`, `run-id.txt` e artifact GitHub Actions por 30 dias. VALIDATION e TEST geram coortes próprias e antes descartavam seus arquivos. O PO esclareceu que a retenção temporária de dados **sintéticos** é suficiente, sem requisitos adicionais de criptografia/ACL. Esta decisão prevalece sobre a proposta preliminar da PR #1637 naquilo que a contradiz.

## Implementação
- O mesmo `v9-standard-prepare-handoff.py` aceita agora uma allowlist explícita de diretórios: `people-100k`, `validation-people`, `corpus`; mantém validação de membros, tamanho, symlinks e presença de `observacoes.csv`.
- O workflow `v9-standard-linear-100k.yml` publica `v9-standard-validation-corpus-*` e `v9-standard-test-corpus-*` com arquivo `tar.gz`, SHA256SUMS, commit e run-id, 30 dias, antes da limpeza local. A publicação é posterior ao trabalho científico de cada job; não adiciona I/O ao loop do calibrador.
- TEST continua gerado apenas depois de VALIDATION e da reserva global da seed one-use; nenhum job anterior baixa artifact do TEST.
- Testes unitários verificam empacotamento/restauração das três coortes, rejeição cruzada e allowlist, e contrato estático do workflow.

## Limites e impactos
Artifact GitHub Actions é temporário, sujeito à retenção/evicção e às permissões do repositório; **não é backup SQL nem armazenamento durável garantido**. Os dados são sintéticos, mas o gabarito independente ainda deve ser tratado com disciplina científica: não usar artifacts para retreinar, ajustar pesos ou repetir TEST. Não altera seed, TF4, hipóteses, paridade do Runner, certificação nem autorização de executar TEST. Falha de upload não é certificação científica. A PR técnica requer CI e revisão antes de integração; o TEST real não será executado para validar a mudança.
