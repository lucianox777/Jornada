# DC-V9-DOC-PDF-ADIAMENTO-20261108 — PDFs residuais em issue sem bloquear merge técnico

**Decisão efetiva do Product Owner:** 08/11/2026 (superveniente à preparação da PR #1343).  
**Estado:** DECIDIDO e REGISTRADO em branch de integração; tratamento dos PDFs **PENDENTE** na [issue #1344](https://github.com/lucianox777/Jornada/issues/1344). A integração com a `master` e seus resultados de CI/30k serão atestados separadamente após o merge.  
**Documentação oficial:** [GOV-DOC-01](Politica_Registro_Decisoes_Discussoes.md) e [índice de decisões](Indice_Decisoes_Vigentes.md). [Histórico e auditoria](Auditoria_Binarios_20261108.md).

## Contexto e justificativa

O PO determinou consolidar todas as frentes relevantes na branch de stage, executar o merge único final **se os gates técnicos e a integração estiverem aprovados**, e **não bloquear a integração por reparos editoriais de PDFs/documentos**, que devem ser registrados em uma issue verificável. Os scripts de auditoria e de regeneração read-only continuam preservados e executam seus testes; **a autorização é de diferimento da correção de conteúdo**, não de desabilitar segurança, guards ou alterar o comportamento do software.

Em `integration/v9-lote-po-20261108` no HEAD `59856e71a920a534253a2184f3d99d5538b8180f`, a auditoria binária [run 42530135640](https://github.com/lucianox777/Jornada/actions/runs/42530135640) comprovou **43 documentos** (21 DOCX + 22 PDF), **0 não auditados**, **0 DOCX com menções**, **51 ocorrências residuais distribuídas por 8 PDFs**. O inventário de origem da `master` tem 45; as **duas exclusões exatas e nominadas** da ficha administrativa de contratação já tinham sido aprovadas no commit `d89db326` e constam do manifesto congelado [Inventario_Binarios_Congelado_20261108.json](Inventario_Binarios_Congelado_20261108.json).

## Escopo estrito do diferimento

1. **Oito PDFs com problema**, nominalmente relacionados na [issue #1344](https://github.com/lucianox777/Jornada/issues/1344), ficam em backlog editorial de remediação pós-merge. Ainda não estão neutralizados; o estado correto é **PARCIAL / PENDENTE**.
2. **Não criar bloqueio artificial** por `45 ≠ 43`: o contrato de integridade exige **exatamente** as 2 exclusões aprovadas e impede exclusões/adicionamentos arbitrários, restaurações ou relatório incompleto. Os checks deste gate seguem obrigatórios.
3. Para merge na `master`, continuam obrigatórios a verificação de conteúdo reunido no SHA consolidado, checks rápidos (`unit-runtime`, `deterministic-build`, `security-guards`), segurança, regressão física técnica específica da J (#1337), CI completa `jornada-ci` e gate físico 30k aplicável no `push` integrado. **Não exigir CI completa manual duplicada pré-merge**.
4. Não dispensar revisão visual antes de substituir qualquer PDF cuja paginação/layout difira, nem reescrever baseline histórico SQL, manifesto selado ou documentos de terceiros. As **30 referências SQL literais históricas** e a migração 3.71 já integrada são assunto documental distinto; não alegar neutralização global concluída ou schema 3.71 promovido a HML/PROD.
5. A transferência de pendência para issue **não é permissão para deixar falhas técnicas vermelhas**, para selecionar parâmetros científicos com base no ensaio TRAIN, executar TEST/Gold, ou ativar política DT-15. Qualquer incidente técnico pós-merge é novo bloqueio para outros merges até correção/reversão.

## Precedência, impactos e rastreabilidade

**Precedência:** decisão específica de 08/11 posterior ao mandato de concluir neutralização antes de L: adia **somente a obrigação editorial dos oito PDFs**, sem desfazer a integração K/J/L nem a política de CI/30k de 06–08/11. **Impacto:** evita um ciclo de engenharia e regeneração gráfica para o merge técnico V9, mas deixa **51 ocorrências nominais publicamente observáveis nos PDFs**, explicitamente visíveis e rastreáveis. Isso não é validação editorial nem autorização para anunciar “neutralização completa”.

**Implementação documental deste ato:** PR de lote [#1343](https://github.com/lucianox777/Jornada/pull/1343), issue [#1344](https://github.com/lucianox777/Jornada/issues/1344), este registro temático, [índice](Indice_Decisoes_Vigentes.md) e [auditoria](Auditoria_Binarios_20261108.md). As exceções deverão ser tratadas em PR própria após o merge, com os 43 caminhos congelados e inspeção página a página quando necessário.

## Provas físicas anteriores ao merge único

A regressão técnica J em `8832850aa11a0d14bbffa9debbf1aa2d80ceb5ff`, [run 42529456510](https://github.com/lucianox777/Jornada/actions/runs/42529456510), completou **SUCCESS nos dois jobs** `PRIMARY` (margem 0,5; artifact 13635944882, ZIP SHA-256 `eadc56230807261141511752ed821afe52a5898038742c35e7c5bd7e53426088`) e `PREDECLARED_SENSITIVITY` (margem 0,25; artifact 13637625606, ZIP SHA-256 `1ae07e6b1d143699e02c68ac21009fca4fdda1739b4d86f37044542e12720f64`). Downloads independentes confirmaram ambos os digests e os relatórios `physical-audit.json` observados: 24 avaliações por role, 16 testemunhas/diagnósticos, 8 candidatas CP, paridade V2 e λ0/V3 no mesmo frame SQL; `historical-analytic.json` registra `IDÊNTICO` nos dois braços V2 3 passes por role. **Evidência sintética/engenharia**, não certificação de risco/FDR, validação independente, TEST, Gold ou autorização de ativação.
