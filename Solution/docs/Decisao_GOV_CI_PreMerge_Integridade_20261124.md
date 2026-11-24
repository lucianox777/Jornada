# GOV-CI-PR-PREMERGE-INTEGRIDADE-20261124 — portas de integridade na PR

**Decisão aprovada pelo PO em 24/11/2026. Estado atualizado:** PR #1927 aprovada para merge condicionado a checks verdes no HEAD exato `508cef7d24fd98219a81ae60a9e5051d4b377832`, verificados cinco de cinco SUCCESS no [run #45323871185](https://github.com/lucianox777/Jornada/actions/runs/45323871185), integrada à master `288d49261e2f7eb84e24e5614b7855169e220396`. CI de push master [#45324894149](https://github.com/lucianox777/Jornada/actions/runs/45324894149) concluída SUCCESS; eventuais falsos positivos futuros exigem correção do verificador, nunca desativação. Referência à [PR #1920](https://github.com/lucianox777/Jornada/pull/1920), [correção #1925](https://github.com/lucianox777/Jornada/pull/1925) e [checkpoint #1851](https://github.com/lucianox777/Jornada/issues/1851). Política GOV-DOC-01.

## Contexto, motivação e precedência

A PR #1920 passou nos gates de pull request e, após a integração, o push na master falhou em quatro dimensões que eram verificadas principalmente no `jornada-ci` completo: manifesto NuGet/lock, arquitetura, modo executável do script local e o mock de integração de SQL. A PR #1925 corrigiu os erros reais; o run manual [#45319973091](https://github.com/lucianox777/Jornada/actions/runs/45319973091) validou o HEAD `772f6bb2` e a integração criou a master `00602216b0494f6c77622071488a7d3bcefd52cc`. Evidências de falhas são registradas no checkpoint operacional, **não** como novas decisões de CI no índice.

O PO aprovou **preparar em frente separada** uma PR pequena que antecipe, para revisões de código, as verificações já existentes no push: `dependency-lock`, proveniência do candidato NuGet e matriz exata de arquitetura. Incluir uma verificação fail-closed do modo Git `100755` em scripts `.sh` chamados diretamente, distinguindo chamadas via `bash/sh` (que não exigem modo executável) de execução direta. A iniciativa não modifica o comportamento de nenhum script e não altera os gates da master.

## Contrato proposto e impacto

No workflow já existente `jornada-pr-required-fast`, criar um job `premerge-integrity` para PRs não classificadas estritamente como documentais, após `classify-impact`. Reusar o comando canônico `scripts/nuget-lock-bootstrap.sh` que verifica restore, locks, proveniência e locked mode; reusar `architecture-dependency-gate.py` com autorregressão; e introduzir apenas um verificador estático dos modos Git dos shell scripts invocados diretamente em workflows e scripts. O verificador tem autorregressão negativa com repositório Git temporário (falta de `100755`) e positiva (permissão corrigida); não executa os scripts inspecionados.

**Aceite condicionado:** check obrigatório da PR na revisão futura, CI no HEAD exato e auditoria de que o modo exigido só se aplica às chamadas diretas. Preservar a dispensa documental já decidida (classificador fail-closed), segurança, secrets, artefatos, SDK e integridade de lock. Não interpretar status `skipped` de jobs documentais como aprovação de integridade de alterações de código.

## Limites, rastreabilidade e governança

A PR é **Draft**, não deve ser mesclada sem autorização posterior do PO. Não concede bypass, aprovação de PR #1924, certificação de Runner V9, alteração de modelo e thresholds, treino/TEST/100k, HML/PROD ou ativação. **Precedência:** complementa GOV-DOC-01 e GOV-OPS-CI-DOCUMENTAL-20261122; não revoga os gates de push nem a segregação científica. O risco reduzido é o descompasso entre o verde da PR e os bloqueios na master; o custo é o restore NuGet extra nos PRs de código. Evidências das falhas e correções permanecem em [incidente #1925](Incidente_CI_Master_Nuget_Lock_Provenance_Pos1920_20261123.md), runs e [#1851](https://github.com/lucianox777/Jornada/issues/1851).


## Aceite superveniente do PO — 24/11/2026

A autorização de merge da #1927 foi expressamente condicionada ao verde no SHA exato. O HEAD permaneceu `508cef7d`, o job `premerge-integrity` e os quatro demais checks passaram no [run #45323871185](https://github.com/lucianox777/Jornada/actions/runs/45323871185); PR marcada ready e mesclada no commit `288d49261e2f7eb84e24e5614b7855169e220396`. O gate permanece habilitado. Se PR legítima for bloqueada por falso positivo, corrigir o analisador conservando teste negativo e cobertura de execução direta. [CI master #45324894149](https://github.com/lucianox777/Jornada/actions/runs/45324894149) concluiu SUCCESS; evidência posterior da integração preservada na master.
