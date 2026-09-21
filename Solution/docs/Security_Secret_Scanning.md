# Varredura de segredos — preparação pós-RC

**Issue:** #405  
**Estado:** execução local + integração CI implementadas após `v5.00-rc.1`.

## Política

A varredura usa as regras padrão mantidas pelo Gitleaks por meio de `[extend] useDefault = true`. A configuração do repositório não começa com allowlist global.

Um achado deve ser classificado antes de qualquer exceção:

1. segredo real ainda válido → revogar/rotacionar primeiro;
2. segredo real histórico → tratar impacto/rotação e registrar decisão; não reescrever histórico automaticamente;
3. credencial sintética/teste → preferir eliminar literal ou injetar por variável quando isso preservar reprodutibilidade;
4. falso positivo comprovado → exceção estreita, direcionada à regra/caminho exatos, com justificativa versionada.

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
