# CI — Checkpoints independentes de segurança (Gitleaks e CodeQL)

**Data:** 12/10/2026. **Critério:** preservar todas as verificações e suas evidências, mantendo a recuperação seletiva nativa de jobs aprovados no mesmo run/SHA.

## Problema observado

Na execução `jornada-ci` `38208687847` (master após a PR #936), `security-analysis` consumiu 248 segundos, entre eles ~132 segundos de compilação instrumentada pelo CodeQL e ~53 segundos de análise. As verificações de transporte de segredos, Gitleaks e vulnerabilidades NuGet eram executadas **no mesmo job** antes do CodeQL. Uma falha de runner tardia forçaria reexecutar ambas as partes; a CI não podia distinguir os checkpoints.

## Novo grafo

O check estável obrigatório `security-analysis` passa a ter **dois produtores independentes**, disparados simultaneamente após `impact` e `dependency-lock`:

1. `security-guards`: mantém TODOS os scripts Bash/Python de transporte de senhas, contratos de segurança, sanidade de fontes/pinning, Gitleaks instalado por versão + SHA-256 e varredura da árvore corrente, mais auditoria estruturada de vulnerabilidades transitivas NuGet. Sem permissão `security-events: write`. Artefato próprio `security-guards-evidence`.
2. `security-codeql`: inicialização CodeQL C# `security-extended`, compilação completa instrumentada da solução, análise SARIF sem upload automático, `sarif-security-gate.py` fail-closed (inclusive autoteste). Permissão `security-events: write` restrita a esse job; artefato `security-codeql-evidence`.
3. `security-analysis` (nome preservado): `always()`, exige **sucesso dos dois produtores**, baixa os **dois artefatos desta mesma execução**, verifica evidência Gitleaks, pinning, vulnerabilidades, relatório SARIF e resumo de bloqueios, e republica `security-analysis-evidence` com a estrutura original em `Solution/.local/security`. Jamais transforma `failure` ou `skipped` em sucesso.

Os dois produtores seguem usando a mesma árvore Git/HEAD de um run, SDK 10.0.112, `nuget-lockfiles` versionados, `dotnet restore --locked-mode` e cache NuGet com chave dos locks. A contagem contratual de setups SDK aumenta de 12 para 13 na CI principal, sem alterar DT10 ou remover gates. `unit`, `integration-sql`, `harness-smoke`, `deterministic-build` e ambas as aceitações E2E não são modificados.

## Limites e aceite

A principal vantagem é a **recuperação mais granular**, não necessariamente menor tempo total. A paralelização pode reduzir alguns segundos de relógio mas eleva o custo de um runner extra (setup + restores). Medir wall-clock e runner-minutes. Se segurança ou análise de código não estiverem 100% concluídas no **HEAD exato da PR**, não integrar.

Os testes de contrato verificam que nenhuma proteção original foi retirada, que a permissão de `security-events: write` existe **somente no CodeQL**, que o gate obrigatório depende dos dois sucessos e que todas as evidências continuam exportadas no nome antigo. Erro, ausência de SARIF, vulnerabilidades ou vazamento de segredo bloqueiam a CI. Verificar a CI após merge em master.

**Isolamento:** somente runners efêmeros GitHub; nenhuma mudança em JornadaLocal, IBGE original, NODE, NAS, HML/PROD ou volumes locais.
