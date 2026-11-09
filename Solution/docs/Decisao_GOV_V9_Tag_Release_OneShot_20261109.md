# GOV-V9-TAG-RELEASE-ONCE-20261109 — mecanismo da Frente A

**Autoridade:** Product Owner, mandato único 09/11/2026. **Estado:** mecanismo em PR; publicação da tag/release NÃO COMPROVADA até execução na master e GET. GOV-DOC-01.

## Justificativa
A conexão GitHub do executor permite PR/merge, mas não oferece criação nativa de tags anotadas e releases. O PO determinou congelar `v9.0.0` no commit histórico **e0e98f99c6290ac76162a8ca1852690b897e954a**, com menção a `Relatorio_V9_Teste_Total_SHA_1489585d_20261109.md`, matriz a–m, limitações e pendências, sem anexos. Uma tag apenas leve ou em HEAD documental não satisfaz o mandato.

## Implementação restrita
Workflow único `.github/workflows/jornada-v9-release-once.yml`, executado somente no push protegido da master que **acrescentar esse próprio arquivo**. Sem actions externas, portanto nenhuma dependência sem pin SHA; usa Python stdlib e token somente em variável ambiente do job com `contents: write`. Não escreve código/master, não faz git push, não altera PRs; cria somente tag anotada e release fixas, com checks fail-closed: repo/ref/event corretos, alvo existe, relatório no commit alvo, ancestralidade com master, tag/release ausentes ou idênticos. Ref/tag divergente nunca é sobrescrita. Os limites da matriz reportam a prova SQL 100k como V8, não V9. Em caso de token insuficiente, logar falha e **AGUARDA EXECUÇÃO MANUAL DO PO**, sem contorno.

**Critério real de conclusão:** PR aprovada em HEAD SHA, merge protegido, workflow concluído SUCCESS, consultas posteriores confirmam `refs/tags/v9.0.0` objeto do tipo `tag`, destino e0e98f99c6290ac76162a8ca1852690b897e954a, release publicada e URL. Atualizar índice posteriormente de PENDENTE para PUBLICADO com novo registro de evidência. Sem TEST/Gold/ativação/Produção nem mudança científica. GOV-DOC-01.
