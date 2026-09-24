# Varredura de segredos — preparação pós-RC

**Issue:** #405  
**Estado:** execução local + integração CI implementadas após `v5.00-rc.1`.

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
