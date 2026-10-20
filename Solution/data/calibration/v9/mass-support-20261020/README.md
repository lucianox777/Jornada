# V9 — suporte por massa e ensaio congelado de 20/10/2026

Evidência sintética **condicional**, sem aprovação/ativação e sem consumo de TEST. O protocolo e as limitações estão na [documentação V9](../../../../docs/V9_Suporte_Massa_Backoff_20261019.md).

| Artefato | Conteúdo |
| --- | --- |
| `plan.json`, `training-split-manifest.json` | Plano pré-declarado e partição TRAIN por identidade: 11.673 ajuste / 5.003 seleção |
| `*.selection.json.gz`, `selection-reference.json` | Envelope completo das 926 avaliações, seis lotes, rejeições e único finalista; SHA dos bytes descomprimidos no nome |
| `*.fitted.json` | Snapshot BLIND vencedor condicional e variante Gold do mesmo fit, SHA no nome; não editar/reformatar |
| `evaluated-fits.zip` | Todos os snapshots avaliados, com bytes e nomes originais, para auditoria integral sem refit |
| `final-study-report.json` | Métricas, fronteira, rejeições e 36 comparações pareadas com a regra anterior no mesmo CSV/split/contagens |
| `gain-by-batch.json`, `gain-by-batch.svg` | Ganho marginal, resolução pareada e parada heurística após dois lotes estagnados |
| `fresh-holdout-sample-plan.json` | Multiplicidade externa efetiva 1, TEST posterior 1; histórico separado |
| `finalist-validation.json` | Avaliação única nova: recall 97,5417%, TP 7.539, FP 2; bound condicional 0,40479%, P95 2.118,3407 ms |
| `fresh-generation-manifest.json`, `fresh-corpus-recipe-verification.json` | Semente 20261020001, 30 mil identidades, cap 4, mesma receita/referências; clean provisório |
| `gold-runner-reference.json`, `gold-runner-study.json` | Mesmo fit; Runner incremental/publicação SQL reais em base descartável: TP 3.382, FP 3, uma referência impura; diagnóstico TRAIN |
| `completion-journal.jsonl` | Ordem de congelamento, verificação, geração, exposição e execução Gold |
| `*-parity.json` | 22 fits e todas as métricas por fonte idênticos antes/depois das otimizações de contexto e score |
| `evidence-manifest.json` | SHA-256 e tamanho dos arquivos de evidência |

O envelope integral tem 950.811.300 bytes; gzip preserva integralmente os bytes e reduz o arquivo para cerca de 28 MB. Para verificá-lo sem reabrir o holdout:

```python
import gzip, hashlib, json
from pathlib import Path
root = Path('Solution/data/calibration/v9/mass-support-20261020')
manifest = json.loads((root / 'evidence-manifest.json').read_bytes())
for name, expected in manifest['files'].items():
    data = (root / name).read_bytes()
    assert len(data) == expected['bytes']
    assert hashlib.sha256(data).hexdigest() == expected['sha256']
archive = next(root.glob('*.selection.json.gz'))
with gzip.open(archive, 'rb') as stream:
    sha = hashlib.file_digest(stream, 'sha256').hexdigest()
assert sha == manifest['selection_sha256']
```

Para a auditoria independente completa, extrair o envelope e `evaluated-fits.zip` **em diretório novo descartável**, copiar `plan.json`, `selection-reference.json` e `training-split-manifest.json` e executar `python Solution/scripts/v9-nested-calibration-verify.py search <diretorio>`. Os arquivos preservam os fits e as observações de evidência necessárias; não é necessário executar novamente a VALIDATION.

Reprodução de uma busca nova: `python Solution/scripts/v9-nested-calibration-study.py <CSV-TRAIN> <plano-novo.json>` e `dotnet <Worker.dll> --v9-calibrate <plano-novo.json> <CSV-TRAIN> <saida-nova>`, sempre em JornadaHarness descartável. O CSV original tem SHA `28bbb9f4c63c97c353e7f29cdbc77091a4773b2f4c596e46103edadd93635d17`, gerado com seed 42, 30 mil pessoas, perfil demographic-primary/clean e referências fixadas no manifesto. Não reutilizar a semente 20261020001 como nova aprovação: a exposição já foi consumida. As sementes 42/43/44 são históricas e 20261019001 é fixture conhecida.

Verificação local da implementação: restore locked e build Release da solução (zero avisos/erros), 1.809 testes C# e sete regressões SQL de publicação/origem aprovados sem ignorados; 729 vetores de paridade do scorer estão na suíte. As regressões SQL locais usaram LocalDB 15; a baseline oficial SQL Server 2022 é verificada pela CI do commit final. Nem esse conjunto de testes nem o merge removem DT-15.
