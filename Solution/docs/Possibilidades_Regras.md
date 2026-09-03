# Possibilidades — regras versionadas e dry-run

A Solution v3.73 fornece a infraestrutura técnica para regras de Possibilidades sem inventar critérios institucionais.

- `Jornada.Contracts/PossibilityRules.cs`: operadores determinísticos, validação fail-closed, engine, loader de catálogo e adaptador `IPossibilityEvaluator`.
- `config/possibilities/rule-catalog.json`: catálogo distribuído. Enquanto `status=PENDENTE`, `rules` deve permanecer vazio.
- `scripts/possibility-rules-gate.py`: valida catálogo, evidência/sha de aprovação e possui self-test positivo/negativo.
- `PossibilityImpactSimulator`: compara regra corrente x candidata em memória e informa entradas, saídas e não-avaliáveis sem publicar resultados.

Uma regra só pode ser usada por `PossibilityRuleCatalogLoader.Load(..., requireApproved: true)` quando o catálogo estiver `APROVADO`. A aprovação deve ter evidência versionada e SHA-256. A publicação de critérios reais permanece responsabilidade institucional dos Gestores.

Na v3.73, `LoadFromFile(...)` também verifica a evidência de aprovação em runtime: caminho relativo portável, existência do arquivo e SHA-256. Portanto, `status=APROVADO` não é aceito apenas por uma string no JSON; o gate de release e o loader compartilham a postura fail-closed.
