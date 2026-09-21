# Varredura de segredos — preparação pós-RC

**Issue:** #405  
**Estado:** execução local preparada; integração ao CI deliberadamente adiada até depois de `v5.00-rc.1`.

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

## Integração futura ao CI

Após o corte da RC, #405 pode adicionar um job de Gitleaks ao `security-analysis` ou workflow equivalente. A versão da ferramenta/Action deve ser pinada de forma consistente com a política de supply chain da Jornada.

A integração futura deve escanear pelo menos a mudança/árvore corrente. A varredura histórica completa é uma ação de saneamento/revisão e não precisa ser repetida a cada commit se existir evidência inicial tratada e política para novos achados.

O `source-sanity-gate.py` continua complementar: ele protege padrões específicos do projeto e não substitui um detector de segredos.
