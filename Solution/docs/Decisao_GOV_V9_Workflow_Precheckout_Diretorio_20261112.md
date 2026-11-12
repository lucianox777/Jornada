# GOV-V9-WORKFLOW-PRECHECKOUT-DIRETORIO-20261112 — bootstrap e limpeza independentes do checkout

**Autoridade:** Product Owner, 12/11/2026. **Status:** correção aprovada; integração por PR pendente.

## Contexto e evidência
A execução manual V9 100k #43227021840 (job #131212700521), sobre a master `f02a0c0e15a28bf38a90d3747b1e2cd38ed21125`, falhou antes do checkout. O job define `defaults.run.working-directory: Solution`, mas o primeiro passo `Announce V9 workflow start` executa antes de `actions/checkout`. O runner não encontrou `/home/runner/work/Jornada/Jornada/Solution`. Os passos de limpeza com `if: always()` também falharam pelo mesmo motivo. Nenhuma calibração física, SQL ou VALIDATION começou.

## Decisão aprovada e implementação
Preservar o anúncio imediato antes do checkout e executar **somente** esse passo com `working-directory: ${{ github.workspace }}`. Preservar o diretório `Solution` para os passos normais posteriores ao checkout. Executar os dois passos de limpeza final em `github.workspace`, com o caminho absoluto `$GITHUB_WORKSPACE/Solution/.local/v9-100k-technical` para os dados temporários; a remoção é tolerante a diretórios ausentes. A remoção do contêiner SQL continua idempotente.

## Impactos, limites e precedência
Correção exclusivamente operacional no workflow `.github/workflows/v9-v3-four-name-100k.yml`: não altera 324 hipóteses, TF4, paralelismo, parâmetros científicos, segurança de admissão, TEST ou critérios de VALIDATION. Não reexecutar automaticamente a campanha; a execução continua manual, com SHA exato e preflight de segurança. A aprovação do PO substitui o comportamento de diretório herdado nesses três passos e não revoga GOV-V9-VALIDATION-ENVELOPE-STREAM-20261112. CI e nova execução manual são necessárias para comprovação operacional.

## Rastreabilidade
Falha: Actions run #43227021840, job #131212700521. Base: PR #1456, commit `f02a0c0e15a28bf38a90d3747b1e2cd38ed21125`. Branch: `fix/v9-early-step-workdir-20261112`. Índice: `Solution/docs/Indice_Decisoes_Vigentes.md`. Política: GOV-DOC-01.
