# Jornada.Linkage.Evaluation

Ferramenta **DEV/HML somente leitura** para transformar duas limitações metodológicas declaradas em evidência reproduzível:

1. comparar o blocking V1 (data de nascimento exata) com um V2 candidato (janela ±N dias);
2. medir a transportabilidade das distribuições `m` da amostra CPF-ancorada/inter-Gestores para uma amostra rotulada de Pessoas sem CPF.

Entrada mínima CSV:

```csv
pessoa_observacao_id,pessoa_uuid_verdade
12345,00000000-0000-0000-0000-000000000001
```

As probabilidades `m` são estimadas com a mesma família de estados de comparação e suavização Dirichlet/Laplace do Parameters Worker (default `alpha=0.5`, configurável).

A ferramenta não cria `linkage_run`, não escreve `identity_map`/`vinculo_fonte`, não atualiza Gold e não promove V2. O relatório usa `purpose=DEV_HML_ONLY_NO_PUBLICATION`.

Exemplo:

```bash
dotnet run --project src/Jornada.Linkage.Evaluation -- \
  --labels .local/linkage-labels.csv \
  --output .local/linkage-evaluation.json \
  --birth-window-days 7 \
  --smoothing-alpha 0.5
```
