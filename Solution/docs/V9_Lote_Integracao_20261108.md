# V9 — Lote único de integração (08/11/2026) — preparação, NÃO integrado

**Estado:** DRAFT de integração; nenhuma CI completa da master foi disparada por esta branch. **Base master verificada:** `db75f3a3f1a3ad38ec6d4f8f1ae3b2e254694b54`. **Política:** [DC-V9-CI-LOTE-20261108](Decisao_V9_CI_Lote_Merge_Unico_20261108.md), [GOV-DOC-01](Politica_Registro_Decisoes_Discussoes.md).  
**Branch de reunião:** `integration/v9-lote-po-20261108`. Não mesclar à master antes de concluir gates, revisão e rastreabilidade. O primeiro conteúdo deste lote veio da PR [#1342](https://github.com/lucianox777/Jornada/pull/1342) (decisão de processo); a branch de origem não deve ser encerrada como MERGED na master apenas por ter sido copiada para o lote.

## Frentes a reconciliar sem merges individuais na master

| Ordem | Frente | Evidência de partida | Condição para integrar ao lote |
|---|---|---|---|
| 1 | J — [#1337](https://github.com/lucianox777/Jornada/pull/1337), calibrador multiparâmetro V2 testemunha permanente | `69321c45`: run físico técnico [42525820013](https://github.com/lucianox777/Jornada/actions/runs/42525820013) falhou em redirect 302→401 da origem de artifact, sem chegar à comparação; correção `8832850a` iniciada | Verificar ambos os jobs físicos PRIMARY/SENSITIVITY no SHA corrigido, paridade V2/CP8, 24/16/8, hashes de corpus/IBGE, relatório histórico e checks rápidos verdes. **Ainda pendente neste checkpoint.** |
| 2 | K — [#1318](https://github.com/lucianox777/Jornada/pull/1318), especificação e decisões científicas | HEAD `4d000668` | Reconciliar com J concluída, sem reabrir D2 cancelado; P95/LOCK vigentes; copyright do PO preservado. Validar checks no SHA da reconciliação. |
| 3 | L — [#1333](https://github.com/lucianox777/Jornada/pull/1333), neutralização documental | HEAD `547868db`, 12/12 checks verdes no SHA consultado | Manter as **duas exclusões administrativas nominadas** aprovadas pelo PO: inventário **45 histórico → 43 vigente proposto**, manifesto congelado e testes negativos. Não fingir que arquivos já saíram da master. Reconciliar index/spec com J e K; auditar 8 PDFs/51 ocorrências e exceções SQL em vez de afirmar zero global. |
| 4 | Documentação de lote — [#1342](https://github.com/lucianox777/Jornada/pull/1342) | Copiada como ancestral desta branch | Confirmar referências cruzadas, o índice e rastreabilidade dos conteúdos efetivamente incorporados. |

## Regras de conclusão do lote

1. **Não fechar nem marcar como MERGED as PRs originais** antes de comprovar todos os seus commits relevantes na branch consolidada; usar fechamento `superseded by` com links após a incorporação, se essa for a via adotada.
2. O **item 8 de verificação final de issues** ocorre após a CI completa e o gate físico 30k aplicável no único `push` da master. Antes do merge, inspecionar bloqueios críticos e exigir os checks rápidos obrigatórios `unit-runtime`, `deterministic-build`, `security-guards` no SHA da PR de lote.
3. Fazer o **único merge final** somente depois das frentes elegíveis, sem cancelar as provas físicas iniciadas. Classificar conflitos sem descarte silencioso de código, relatórios, manifesto ou histórico.
4. Depois do merge final, conferir `github.sha` da master, recibos `jornada-ci`, classificador e 30k físico quando obrigatório; se houver falha, suspender novos merges e corrigir/reverter. Nenhum resultado TRAIN sintético certifica FDR, TEST, Gold, HML/PROD ou ativação DT-15.
5. Até existirem resultados físicos pós-correção J, comparação documental reconciliada e PR agregadora aprovada, manter **DRAFT / NÃO INTEGRADO**, sem contabilizar trabalho como entregue.

## Rastreabilidade e precedência

O registro complementa a [decisão de lote do PO em 08/11](Decisao_V9_CI_Lote_Merge_Unico_20261108.md) e preserva a topologia PR fast + master CI/30k de 06–07/11. **Novo teste físico de J é uma regressão técnica específica pendente, não duplicação da CI completa `jornada-ci` antes do merge.** Histórico de inventário 45 nunca se transforma em autorização para excluir um terceiro arquivo. A revisão de oito PDFs permanece problema documental distinto da autorização dos dois arquivos administrativos.

## Checkpoint superveniente — stage consolidado e autorização editorial (08/11/2026)

- **Consolidação Git já efetuada em STAGE, não master:** J/#1337 `8832850aa`, K/#1318 `4d000668`, L/#1333 `547868db` e GOV/#1342 `2a32ea49` são todos ancestrais do HEAD de lote `59856e71a920a534253a2184f3d99d5538b8180f` (compare Git `behind=0` em cada caso). As PRs #1318/#1333/#1342 foram encerradas como **substituídas**, não `MERGED` na master; J/#1337 ainda pode ser encerrada após registrar a prova e confirmar ancestralidade.
- **Prova física J fechada:** workflow [42529456510](https://github.com/lucianox777/Jornada/actions/runs/42529456510), HEAD `8832850aa`, **PRIMARY+SENSITIVITY SUCCESS**. Ambos os ZIPs de artifacts foram conferidos por SHA-256; `physical-audit.json` registra 24/16/8 por role, testemunha V2 no mesmo frame, λ0/V3 e CP exclusivamente para 8 candidatas. Comparação histórica é analítica, sem escolha/ativação científica.
- **Docs não impedem o merge:** o PO determinou a [issue #1344](https://github.com/lucianox777/Jornada/issues/1344) para **8 PDFs / 51 ocorrências** e aprovou o [diferimento documental](Decisao_V9_Diferimento_PDF_20261108.md). A correção documental é pós-merge; `43/43` auditados com exatamente duas exclusões administrativas nominadas e nenhuma exclusão adicional. Não alegar zero literal global.
- **Próxima condição de merge único:** revalidar todos os checks no **SHA FINAL do lote após este registro GOV-DOC-01**; assegurar `master` ainda é o base SHA verificado; retirar DRAFT e integrar **uma vez** sob o ruleset. Somente então ocorrerá `jornada-ci` completa e gate técnico 30k automático no SHA integrado. O teste geral a–m e a revisão de issues permanecem atividades separadas não executadas neste checkpoint.

