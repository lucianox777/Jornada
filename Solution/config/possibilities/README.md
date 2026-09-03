# Regras versionadas de Possibilidades

`rule-catalog.json` é o contrato técnico de publicação de regras. O estado inicial é `PENDENTE` e **não contém regra institucional inventada**.

Uma publicação `APROVADO` deve conter regras versionadas, evidência SHA-256, data e responsável. O motor `PossibilityRuleEngine` é determinístico e `PossibilityImpactSimulator` permite comparar versão vigente e candidata em dry-run sem publicar resultados.

O gate `possibility-rules-gate.py` valida estrutura e falha em modo estrito enquanto o catálogo não estiver aprovado.
