# CI — Reuso binário por HEAD exata (primeira onda)

**Data:** 12/10/2026. **Escopo:** reduzir builds redundantes com critério fail-closed. Decisões e gates de `DT10_Cache_NuGet_Locks_CI.md`, `DT10_CI_Extracao_Reutilizavel.md` e `Runbook_Git_Release.md` permanecem obrigatórios.

## Decisão de implementação

A execução `deterministic-build`, que já faz dois builds limpos consecutivos, torna-se a **única fonte** dos binários consumidos no job `integration-sql`. O job de integração continua criando sua própria base SQL descartável, executando DDL, teste de integração e fault injection. O build `dotnet build Jornada.sln --configuration Release --no-restore` que antes ocorria ali é substituído por consumo de binários **da mesma execução**. Os builds de `unit`, `harness-smoke`, `security-analysis` (CodeQL) e E2E não são alterados nesta primeira onda.

O produtor empacota somente os arquivos regulares de `Solution/src/*/bin/Release` e `Solution/tests/*/bin/Release` após a segunda compilação determinística. O manifesto inclui hashes SHA-256 por arquivo e do pacote, SHA exato do checkout, árvore Git, `GITHUB_RUN_ID`, versão do SDK (fixada em 10.0.112), sistema/arquitetura e hash do conjunto NuGet `packages.lock.json` de `src` e `tests`. O pacote é artefato do **mesmo workflow**, não cache entre commits.

A etapa consumidora mantém download e validação do artefato `nuget-lockfiles`, `dotnet restore Jornada.sln --locked-mode`, e só então verifica SHA, árvore, run ID, locks, SDK, plataforma, cada arquivo e paths antes de extrair. Caminho não previsto, symlink, arquivo faltante, tar truncado, versão divergente ou SHA incompatível bloqueiam o job sem substituição por código não verificado. A extração preserva o bit executável necessário a binários apphost.

## Gates preservados

Os dez gates `impact`, `dependency-lock`, `ddl-upgrade`, `unit`, `security-analysis`, `harness-smoke`, `e2e`, `deterministic-build`, `integration-sql` e `dt10-evidence` continuam definidos. O `integration-sql` depende agora explicitamente da aprovação `deterministic-build` e não executa se o pacote falhar. `DT10` mantém seu build e SQL próprio quando aplicável. O teste de contrato `test-ci-locked-nuget-cache-contract.py` ainda verifica todos os setups, restores e ao menos três builds explícitos no workflow principal.

Os testes isolados de `ci-built-binary-gate.py` cobrem adulteração de SHA, troca de locks, executabilidade, caminhos perigosos, entradas ausentes e ausência de bypass de gates. A **aceitação real** depende de o job SQL consumir o pacote, executar os testes SQL e fault injection **sem skips**, e entregar evidências na HEAD exata do PR. Após merge, também é necessário CI verde na HEAD de `master`; não alegar ganho de tempo antes de comparar perfis de runs equivalentes.

## Riscos, contenção e próximas ondas

A dependência nova pode aumentar o caminho crítico da CI mesmo reduzindo CPU/minutos consumidos. Medir após integração com a rotina da PR #931 e comparar duração em relógio, runner-min e taxa de falhas; se houver regressão séria ou alteração indevida da semântica, reverter. A adoção adicional por `unit` ou `harness-smoke` requer PRs independentes e revalidação específica; evitar remover o build necessário à instrumentação CodeQL ou a prova dos dois builds determinísticos.

O pacote transporta saídas de compilação da PR **sem permissões privilegiadas adicionais**. Não executar código do PR em workflow `workflow_run` com `contents:write`. A recuperação de falhas de runner é tratada pelo watchdog #930; a correção autônoma de código exige controlador separado (ver plano abaixo).

## Arquitetura para reparo autônomo de código (etapas futuras)

Separar quatro identidades/permissões:

1. **Observador read-only:** evento `workflow_run` concluído ou reconciliação horária; indexa `run_id`, `head_sha`, PR, jobs e logs permitidos; classifica falhas em `infra`, `produto`, `permissão`, `desconhecido`.
2. **Decisor persistente:** armazena `incident_id`, `head_sha`, estado, tentativas, janela de retry, próximo horário, `lease` de um único agente por incidente, fingerprint do log e histórico de ações. Recusa trabalho sobre HEAD obsoleta e mantém orçamento para evitar loops.
3. **Executor de correção em ambiente isolado:** credencial de instalação GitHub App de curta duração, restrita a branch de reparo e PR; jamais misturar o token de `actions:write` do watchdog com `contents:write`. Modelos de código só propõem patches verificáveis, sem aceitar instruções vindas dos logs/PRs como comandos privilegiados. Correção deve referenciar causa, teste de regressão e commits; segredos e dados de produção nunca presentes.
4. **Validador independente:** somente após todos os gates exigidos, ausência de skips e HEAD imutável, verifica proveniência, base atual e revisões/branch protections. Apenas ele pode marcar PR pronto para merge. Se o problema for estatístico, security, schema destrutivo ou normativa, falha fechada sem aprovação automática.

**Implantação progressiva:** (a) coleta/diagnóstico somente leitura #929/#931; (b) autorrecuperação infra limitada #930; (c) executor de correções determinísticas estritamente permitidas em PR próprio e avaliação retrospectiva; (d) integração de agente de código com GitHub App scoped, sandbox, testes e limite de custo, nunca permissões de produção. Credenciais devem ser obtidas no momento da tarefa; **nunca** versionar PAT, chave privada ou segredo em logs. O supervisor externo horário do ChatGPT complementa, mas não substitui o agente persistente no GitHub.

**Isolamento:** nenhuma alteração nos bancos `JornadaLocal`, corpus IBGE original, NAS, NODE, HML/PROD ou volumes locais. Somente runners GitHub-hosted e SQL de testes descartável.
