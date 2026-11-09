# GOV-V9-TAG-RELEASE-ONCE-20261109 — mecanismo da Frente A

**Autoridade:** Product Owner, mandato único 09/11/2026. **Estado:** mecanismo em PR; publicação da tag/release NÃO COMPROVADA até execução na master e GET. GOV-DOC-01.

## Justificativa
A conexão GitHub do executor permite PR/merge, mas não oferece criação nativa de tags anotadas e releases. O PO determinou congelar `v9.0.0` no commit histórico **e0e98f99c6290ac76162a8ca1852690b897e954a**, com menção a `Relatorio_V9_Teste_Total_SHA_1489585d_20261109.md`, matriz a–m, limitações e pendências, sem anexos. Uma tag apenas leve ou em HEAD documental não satisfaz o mandato.

## Implementação restrita
Workflow único `.github/workflows/jornada-v9-release-once.yml`, executado somente no push protegido da master que **acrescentar esse próprio arquivo**. Sem actions externas, portanto nenhuma dependência sem pin SHA; usa Python stdlib e token somente em variável ambiente do job com `contents: write`. Não escreve código/master, não faz git push, não altera PRs; cria somente tag anotada e release fixas, com checks fail-closed: repo/ref/event corretos, alvo existe, relatório no commit alvo, ancestralidade com master, tag/release ausentes ou idênticos. Ref/tag divergente nunca é sobrescrita. Os limites da matriz reportam a prova SQL 100k como V8, não V9. Em caso de token insuficiente, logar falha e **AGUARDA EXECUÇÃO MANUAL DO PO**, sem contorno.

**Critério real de conclusão:** PR aprovada em HEAD SHA, merge protegido, workflow concluído SUCCESS, consultas posteriores confirmam `refs/tags/v9.0.0` objeto do tipo `tag`, destino e0e98f99c6290ac76162a8ca1852690b897e954a, release publicada e URL. Atualizar índice posteriormente de PENDENTE para PUBLICADO com novo registro de evidência. Sem TEST/Gold/ativação/Produção nem mudança científica. GOV-DOC-01.

## Incidente de publicação parcial em 09/11/2026 — run 42613376654

O workflow inicial da master `dca11cf261041054abd4956d29b9efc8ffe22331` criou a **tag anotada correta** `v9.0.0` com objeto Git `c30912dd1e56fb2888ec656e51604485047f66bc`, destino `e0e98f99c6290ac76162a8ca1852690b897e954a`; GET posterior confirmou `object.type=tag` e destino de commit. No entanto a criação da release falhou com HTTP 422 no [run 42613376654](https://github.com/lucianox777/Jornada/actions/runs/42613376654), pois `make_latest` foi passado como booleano `false` e a API exige **string** `"false"`, `"true"` ou `"legacy"`. Verificado na documentação REST GitHub de releases. Correção pontual `'make_latest':'false'` sem reescrever tag, reutilizando o fluxo idempotente. Até run de correção concluído com sucesso, frente A permanece **TAG CRIADA / RELEASE PENDENTE**; nenhum binário anexado, nenhuma nova execução científica autorizada.

## Conclusão verificada da Frente A — 09/11/2026

**ESTADO: TAG ANOTADA CRIADA / RELEASE PUBLICADA / PROVA REST+WORKFLOW SUCCESS**, sem ativação. A consulta GitHub confirmou `refs/tags/v9.0.0` objeto do tipo `tag` SHA `c30912dd1e56fb2888ec656e51604485047f66bc`, apontando exatamente para commit `e0e98f99c6290ac76162a8ca1852690b897e954a`; não é lightweight tag, nem foi reescrita.

Após falha inicial [run 42613376654](https://github.com/lucianox777/Jornada/actions/runs/42613376654) (tag criada, release POST HTTP 422), a [PR #1370](https://github.com/lucianox777/Jornada/pull/1370) corrigiu `make_latest` para `'false'` (string REST); merge master `9cbce697412038f32176d4b920ffe1134c2ef78a`. O [workflow 42614014935](https://github.com/lucianox777/Jornada/actions/runs/42614014935) terminou **COMPLETED/SUCCESS**. GET posterior comprova [release v9.0.0 publicada](https://github.com/lucianox777/Jornada/releases/tag/v9.0.0), `draft=false`, `prerelease=false`, `target_commitish=e0e98f99...`, **0 assets binários**, corpo com matriz a–m, links de prova e limites de FDR, TEST, V8 100k versus V9.

A operação criou apenas a tag anotada/release; não houve push direto à master, retag, histórico reescrito, novo modelo selecionado ou ativação. **Frente A tecnicamente concluída**; integração deste registro documental e verificação do SHA pós-merge permanecem responsabilidades GOV-DOC-01.
