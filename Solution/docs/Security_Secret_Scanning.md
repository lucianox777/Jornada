# Varredura de segredos — preparação pós-RC

**Issue:** #405  
**Estado:** execução local + integração CI implementadas após `v5.00-rc.1`.

## Wrappers Bash do smoke SQL e diagnóstico sintético DEV (#405)

`local-sql-runtime-smoke.sh` e `local-synthetic-diagnostics.sh` agora repassam `SQLCMDPASSWORD` ao Docker **somente pelo nome** (`-e SQLCMDPASSWORD`). O valor fica no ambiente do processo filho durante a chamada, não em seus argumentos. O teste `test-local-sql-entrypoint-secret-transport.sh` executa os scripts originais em cópias temporárias com Docker simulado, cobrindo sucesso e falha sem tocar em SQL real. O smoke **não é read-only** quando executado de verdade: aplica DDL, seed e validação. O diagnóstico, ao contrário, é somente leitura. A mudança não autoriza executar o smoke no banco compartilhado sem o fluxo DEV previsto.

## Política

A varredura usa as regras padrão mantidas pelo Gitleaks por meio de `[extend] useDefault = true`. A configuração do repositório não começa com allowlist global.

Um achado deve ser classificado antes de qualquer exceção:

1. segredo real ainda válido → revogar/rotacionar primeiro;
2. segredo real histórico → tratar impacto/rotação e registrar decisão; não reescrever histórico automaticamente;
3. credencial sintética/teste → preferir eliminar literal ou injetar por variável quando isso preservar reprodutibilidade;
4. falso positivo comprovado → exceção estreita, direcionada à regra/caminho exatos, com justificativa versionada. A primeira triagem CI classificou apenas `generic-api-key` sobre chaves sintéticas DEV; a exceção corrente é rule-specific e limitada a `test-access-keys.json`, `local-e2e.sh|ps1` e `Jornada.Cluster.Test.json`.

Não criar baseline automaticamente a partir da primeira execução: isso converteria achados desconhecidos em dívida silenciosa.

## Execução

PowerShell:

```powershell
./Solution/scripts/security-secret-scan.ps1
```

Bash:

```bash
./Solution/scripts/security-secret-scan.sh
```

Para apenas a árvore corrente, use `-CurrentTreeOnly` no PowerShell ou `--current-tree-only` no Bash.

Relatórios anteriores são descartados antes de cada varredura. A ausência de qualquer relatório requerido — mesmo quando o scanner retorna sucesso, ou quando a outra etapa tem achados — é erro operacional, nunca zero ocorrências. O CI executa autotestes dessas falhas e da não exposição de `Secret`/`Match`.

A execução completa faz duas provas:
- `gitleaks dir`: conteúdo presente na árvore;
- `gitleaks git --log-opts=--all`: histórico Git alcançável.

Os relatórios ficam em `Solution/.local/gitleaks/`, já coberto pelo `.gitignore`. Eles não devem ser commitados nem publicados como artifact sem revisão, porque a finalidade é triagem de segurança.

## Integração ao CI

O job `security-analysis` instala **Gitleaks 8.30.1** a partir do release oficial e valida o SHA-256 do tarball Linux x64 antes de executar. Não usa tag `latest` nem adiciona uma Action de terceiros ao workflow.

Em todo CI, a prova obrigatória é:

- `security-secret-scan.sh --current-tree-only`;
- configuração `.gitleaks.toml` com as regras padrão mantidas pelo projeto;
- saída/versionamento copiados para o artifact `security-analysis-evidence`.

A varredura histórica completa continua disponível por `security-secret-scan.sh` sem `--current-tree-only`. Ela é uma ação de saneamento/revisão da #405 e **não é repetida em todo commit**, porque achados históricos exigem triagem explícita e não devem virar baseline/allowlist ou rewrite automático.

O `source-sanity-gate.py` continua complementar: ele protege padrões específicos do projeto e não substitui um detector de segredos.

## Auditoria histórica completa controlada (#405)

O workflow `.github/workflows/secret-history-audit.yml` pode ser executado
manualmente em **Actions > jornada-secret-history-audit > Run workflow**. Ele
utiliza `actions/checkout` com `fetch-depth: 0` e a versão/checksum fixos do
Gitleaks para percorrer a árvore e todos os refs Git alcançáveis. Também é
exercitado automaticamente na PR que altera o próprio workflow; não acrescenta
uma varredura pesada a cada CI normal.

O artifact `gitleaks-history-audit-summary` contém **somente contagens por regra
 e caminho** da árvore atual e do histórico. Não publica `Match`, `Secret`,
logs brutos nem os relatórios JSON originais. Achados no histórico resultam em
advertência para triagem, **não** em baseline, exceção automática ou aprovação
de segurança; erro de ferramenta/relatório encerra o job com falha. Os arquivos
brutos redigidos ficam temporariamente no runner e não são incluídos no artifact.
Para triagem detalhada, execute a varredura local no clone completo com acesso
controlado aos relatórios em `Solution/.local/gitleaks/`.

A primeira execução do workflow deve ter seus agregados registrados na issue
#405. Concluir a issue exige classificar achados históricos, eliminar os
literais executáveis desnecessários e tratar seletivamente fixtures de CPF.

## Transporte das credenciais em scripts IBGE DEV (#405)

`local-check-ibge-reference.ps1`, `local-diagnose-ibge-reference.ps1` e `local-repair-ibge-reference.ps1` passam `SQLCMDPASSWORD` ao `docker compose exec` **somente pelo nome** (`-e SQLCMDPASSWORD`), herdado do ambiente temporário. Cada função SQL restaura a variável original em `finally`, inclusive em erro. O mock `local-test-ibge-sqlcmd-secret-transport.ps1` exercita as rotinas reais extraídas via AST no PowerShell 5.1 e no PowerShell 7, sem Docker/SQL. O reparo permanece operação explícita, com as pré-condições de integridade já existentes; esta mudança não ativa nem recarrega a referência.

**Limite:** outros entrypoints DEV/Bash ainda estão em inventário na issue #405; a conclusão desta fatia não equivale a afirmar que todo o fluxo local está livre de segredos em argumentos de processos.

## Transporte de segredo no provisionamento SQL local (#405)

`local-db.ps1` passou a encaminhar a senha nas duas funções SQL via variável temporária `SQLCMDPASSWORD` e `docker compose exec -e SQLCMDPASSWORD`. Cada função restaura o ambiente original em `finally`, mesmo quando o Docker falha. `local-db.sh` passa o mesmo valor por uma atribuição de ambiente restrita à chamada `compose`, mantendo a interface `up/reset/backfill/down/clean/status` inalterada. O mock PowerShell `local-test-db-sqlcmd-secret-transport.ps1` extrai e exerce as duas funções reais sem executar o entrypoint. O teste Bash `test-local-db-secret-transport.sh` executa uma cópia temporária de `local-db.sh` exclusivamente em `up --no-synthetic-corpus` contra Docker simulado, com credencial sintética, incluindo a falha induzida. Nenhum desses testes executa limpeza de bancos reais, nem altera a referência IBGE.

O escopo da issue #405 continua aberto para outros entrypoints legados e eventual rotação de credenciais existentes; não considerar o teste dos wrappers como prova de saneamento de todo o ambiente.

## Transporte de senha no DDL e no ensaio sintético Bash (#405)

`local-ddl-upgrade.sh` encaminha a senha SQL apenas pelo ambiente da chamada à função `compose`; `local-synthetic-calibration.sh` usa `SQLCMDPASSWORD` temporário nos dois `docker compose exec` de *preflight* e limpeza. Ambos passam somente `-e SQLCMDPASSWORD` como argumento, sem modificar as consultas SQL nem autorizar execuções destrutivas. O teste `test-local-ddl-synthetic-secret-transport.sh` extrai a função real de `sqlcmd` do upgrade e executa uma cópia isolada do ensaio sintético, tudo com Docker e .NET simulados. O teste valida também que uma falha no preflight impede executar a limpeza, sem tocar no banco DEV original. A prova é restrita ao transporte das credenciais e ao sequenciamento do entrypoint; não substitui o upgrade ou a validação estatística reais.

## Transporte de credencial no harness de escala DEV (#405)

`local-scale.ps1` e `local-scale.sh` mantêm suas operações de escala e SQL existentes, mas as funções de consulta passam somente `-e SQLCMDPASSWORD` ao Docker, com valor no ambiente temporário do processo filho. As três funções PowerShell (`SqlCmd`, `Scalar`, `QueryLines`) restauram o estado anterior de `SQLCMDPASSWORD` em `finally`, inclusive em exceções. Os mocks `local-test-scale-sqlcmd-secret-transport.ps1` e `test-local-scale-sqlcmd-secret-transport.sh` exercitam as funções SQL **originais**, extraídas sem executar os entrypoints destrutivos. Nunca invocar `local-scale.*` na base compartilhada para realizar apenas esse teste: os entrypoints completos começam com `local-db reset`. Os testes não comprovam medição real de escala, calibração ou aprovação de modelos.

## Transporte de senha no drill de backup/restauração DEV (#405)

`local-backup-restore-drill.ps1` e `local-backup-restore-drill.sh` preservam os comandos originais de backup e verificação da Bronze, mas as funções SQL enviam a senha ao Docker somente pelo ambiente temporário `SQLCMDPASSWORD` e pelo argumento `-e SQLCMDPASSWORD`. `SqlCmd` e `Scalar` no PowerShell restauram inclusive a ausência original dessa variável em `finally` no sucesso e em falhas. `local-test-backup-sqlcmd-secret-transport.ps1` extrai as funções reais via AST e `test-local-backup-sqlcmd-secret-transport.sh` extrai as funções Bash originais, ambos sem executar `local-db up`, SQL, restore ou escrever na Bronze. Os mocks comprovam exclusivamente o transporte seguro e a propagação de erros — **não** a integridade de backups reais. A prova de restauração real continua reservada ao drill isolado de CI/ambiente próprio, não ao banco compartilhado.
