# SEG-V9-SQL-ARGV-20261109 — proibição de segredos em argumentos

**Autoridade:** Product Owner, Mandato Único 09/11/2026 (não passar senha/token/connection string como argumentos de processos). **Estado:** IMPLEMENTADO EM PR, sujeito aos gates do HEAD e master. GOV-DOC-01.

## Contexto, justificativa e remediação

A correção anterior #1262 gerou senha forte, registrou máscara antes do Docker, usou SQL efêmero e loopback, mas passava `--env MSSQL_SA_PASSWORD=<valor>` ao `docker run`. Mesmo mascarado em logs, `argv` ainda contém o segredo, o que viola a restrição aprovada.

O bootstrap agora produz um `--env-file` temporário modo `0600`, transfere **somente o caminho** como argumento, e usa `trap EXIT` para excluir o arquivo em sucesso ou falha. Permanecem SQL Server 2022 pinado por digest, porta 127.0.0.1:1433, senha randômica, `::add-mask::` anterior ao Docker, `GITHUB_ENV` para passos posteriores, healthcheck e limpeza do contêiner. Regressão fake Docker confirma ausência do segredo na lista de argumentos e uso explícito de `--env-file`.

**Limites de proteção:** o Docker daemon conhece a senha durante a vida do contêiner e exige runner confiável/descartável, impedimento de `docker inspect` público e limpeza. A correção não apaga nem mascara retroativamente logs históricos. O run [41535062165](https://github.com/lucianox777/Jornada/actions/runs/41535062165) continua acessível com oito ocorrências de segredo temporário, verificadas por contagem sem reproduzir valores. #1253 só encerra com avaliação de descarte/reutilização de credenciais e retenção/expurgo autorizado. Frente C V9 100k não liberada neste corte.

**Impacto:** segurança operacional apenas; nenhum ajuste TF, M/U, FDR, `plan.json`, ciência, RAM-first, TEST, Gold, HML ou Produção. **Precedência:** complementa #1262 e [preflight V9](Relatorio_V9_Preflight_Seguranca_20261109.md); não elimina suas pendências nem substitui GOV-DOC-01. Validar na PR e master por SHA exato.
