# V9-TF-001 — gerador mínimo de dossiê exploratório

Registros normativos e resultado da primeira campanha: [issue #1228](https://github.com/lucianox777/Jornada/issues/1228#issuecomment-6613863873). Esta ferramenta somente lê o `study.json` produzido pelo ensaio físico e cria **agregados** sem conteúdo nominal, dados TEST, publicação ou ativação.

## Uso

```bash
python Solution/scripts/v9-tf-exploration-dossier.py \
  --study /local/study-tf/study.json \
  --out-dir /local/dossier-v9-tf-001 \
  --sha 3f21032e1b308df2d60fbf31dc92454efa3ab0a1 \
  --run-id 41681714081
```

Saídas: `V9-TF-001.md` (descrição com seleção **somente por recall descritivo**) e `V9-TF-001.manifest.json` (SHA-256 do arquivo fonte, hashes semânticos das 135 medições e snapshots, proveniência, estados de seleção). A origem de SHA e run-id é argumento do chamador; conferir ambos na interface GitHub Actions antes de atribuir validade probatória.

Para comparar uma **repetição manual autorizada** com a primeira campanha, gerar primeiro o manifesto de referência e executar o mesmo comando para o segundo `study.json`, trocando run-id e adicionando `--baseline-manifest /local/dossier-v9-tf-001/V9-TF-001.manifest.json`. Diferenças em SHA do código, plano, TRAIN, VALIDATION, contagem, métricas ou conteúdo SHA-256 dos snapshots causam **falha explícita**; ordem das linhas e metadados do ZIP são ignorados. O campo `replication_equal_to_baseline` só é gravado quando essas evidências coincidem.

**Limites:** hash idêntico de resultados não prova validade estatística, representatividade, independência entre clusters, P95 fim a fim, resultado Gold ou autorização operacional. O relatório não altera nenhum parâmetro do calibrador e não substitui a avaliação independente. As fronteiras completas e o histórico de explorações permanecem nos artefatos originais; o relatório contém apenas uma visão agregada curta.

O script não deve ser executado automaticamente durante o trabalho diário de CI com um corpus físico de 30k. O artefato integral (~528 MB ZIP; muito maior após extração) permanece fora do Git; guardar cópia durável e o hash manifestado.
