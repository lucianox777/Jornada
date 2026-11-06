# Jornada V9 — bundle de evidências da V3 com a mesma estrutura da V2

**Decisão:** usar o contrato de diretório e vínculo SHA-256 já implementado na PR #1283 para a V2, **sem Terraform, SQL novo, armazenamento cloud, runtime alternativo ou investigação histórica V2/V3**.

## Layout (idêntico à V2)

A nova rotina `scripts/v9-calibration-v3-evidence-bundle.py` **reutiliza diretamente** `FILE_MAP`, `sha`, `strict`, `valid_sha` e `require` do script existente `v9-calibration-evidence-bundle.py`. Um bundle V3 contém **exatamente**:

| Arquivo | Origem e finalidade |
| --- | --- |
| `plan.json` | Cópia byte a byte do **plano resolvido pelo C#** em `--v9-calibrate`. Declara o único método `CLASSWISE_SUPPORT_BACKOFF` e a grade M/U efetivamente estudada. |
| `result.json` | Cópia byte a byte do envelope `<SHA>.selection.json` gerado pelo C#, com seleção e identidade do plano normalizado. |
| `model.fitted.json` | Cópia byte a byte de **um** `<SHA>.fitted.json` medido, escolhido explicitamente pelo SHA — nunca presumido vencedor. |
| `report.json` | Relatório estatístico/gerencial reconstruído de `mu-statistical-analysis.json` do C#, preservando **todas as linhas** de avaliação e os suportes das **81 células**, as estimativas M/U, TF, prior do fitted, métricas, razões de rejeição, premissas e incertezas. Embute também o recibo de seleção; guarda os hashes dos JSON de origem. |
| `manifest.json` | Mesmo conceito da V2: `Components` com **arquivo + SHA-256 + bytes** dos quatro componentes e `CrossLinks` de SHA de plano normalizado, CSV, manifesto gerador, envelope, análise estatística, seleção, fit e frames de identidade. O SHA-256 do próprio manifesto identifica o bundle sem autorreferência circular. |

O `CalibrationRunId`, o `SourceCommitSha` completo, o `SourceArtifactName` e o SHA do fitted integram o manifesto e o relatório, vinculando o bundle à execução registrada no GitHub Actions. **Não há tabela nova de registro ou mudança no modelo SQL nesta etapa**: o registro de origem é o identificador da execução e a referência ao artefato. Se o objetivo futuro for catálogo operacional SQL, isso exigirá decisão e integração separadas, não Terraform nem esquema presumido.

A ausência de `--references` prova **integridade interna**, não autenticidade da fonte. Um JSON externo previamente pinado (formato `V9_V3_CALIBRATION_SOURCE_PINS_V1`) pode autenticar run, commit, nome do artefato e SHAs exatos dos arquivos do C#; o verificador falha se qualquer um divergir.

## Comandos para uma execução V3 já concluída

Apenas executar após um `--v9-calibrate` que já tenha produzido `plan.json`, `selection-reference.json`, `mu-statistical-analysis.json`, `<SHA>.selection.json` e `<SHA>.fitted.json`. Os exemplos **não** executam SQL, não calibram, não usam TEST, não publicam REF e não abrem o Gold.

~~~bash
cd Solution

python3 scripts/v9-calibration-v3-evidence-bundle.py build \
  --source-dir .local/v9-v3-direct/results \
  --out-dir .local/v9-v3-direct/evidence-bundle \
  --fitted-sha <SHA_64_DO_FITTED_MEDIDO> \
  --margin 0.5 \
  --run-id <ID_NUMERICO_DO_GITHUB_ACTIONS> \
  --source-commit-sha <COMMIT_COMPLETO_40> \
  --source-artifact-name <NOME_EXATO_DO_ARTEFATO> \
  --references .local/v9-v3-direct/source-pins.json

python3 scripts/v9-calibration-v3-evidence-bundle.py verify \
  --bundle-dir .local/v9-v3-direct/evidence-bundle \
  --references .local/v9-v3-direct/source-pins.json
~~~

O JSON de `source-pins.json` deve ser gerado/pinado externamente a partir da execução original, **não** a partir do bundle que se pretende validar:

~~~json
{
  "schema_version": "V9_V3_CALIBRATION_SOURCE_PINS_V1",
  "run_id": "42137399700",
  "head_sha": "<SHA_GIT_40>",
  "artifact_name": "<NOME_ORIGINAL>",
  "files": {
    "plan.json": "<SHA256_ORIGINAL>",
    "<SHA256_ENVELOPE>.selection.json": "<SHA256_ENVELOPE>",
    "<SHA256_FITTED>.fitted.json": "<SHA256_FITTED>",
    "mu-statistical-analysis.json": "<SHA256_ORIGINAL>",
    "selection-reference.json": "<SHA256_ORIGINAL>"
  }
}
~~~

Não inventar valores do manifesto. A compilação é exclusivamente um empacotamento **offline**. O verificador recusa arquivo faltante, duplicidade de chave JSON, arquivo extra no bundle, symlink, troca de fit/opção/margem, V2 introduzida no estudo V3, divergência de SHAs, adulteração de métricas e falsa alegação de teste independente/certificação/DT-15.

## Delimitação

- **Sem Terraform.** Não provisiona infraestrutura.
- **Sem migration SQL.** Registro de origem via GitHub Actions run/commit/artefato com integridade no manifesto.
- **Sem replay V2.** O único código reutilizado da V2 é a infraestrutura de bundle/hash.
- **Sem ativação.** Um fitted marcado `MEASURED_NOT_ELIGIBLE` continua inelegível; seu arquivamento é permitido por transparência e não o promove a finalista.
- O `PlanSha256` do C# é uma identidade normalizada distinta do SHA dos bytes físicos do `plan.json`; ambos ficam registrados, sem equiparação indevida.
- A calibração V3 da PR #1291 continua independente. Este bundle só se aplica quando **artefatos reais de execução** estiverem presentes; testes herméticos não simulam sucesso físico.
