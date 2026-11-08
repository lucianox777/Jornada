> **Decisão mais nova do PO — 08/11/2026, PR de lote #1343:** as **51 menções residuais em 8 PDFs** já foram transferidas para a [issue pós-merge #1344](https://github.com/lucianox777/Jornada/issues/1344), com todos os oito caminhos e critérios de QA. Trata-se de **pendência editorial não bloqueante para o merge técnico único** sob [DC-V9-DOC-PDF-ADIAMENTO](Decisao_V9_Diferimento_PDF_20261108.md); **não** é neutralização final, dispensa de auditoria, aprovação de layout divergente nem autorização de deletar outros arquivos. O gate real no HEAD de lote `59856e71` [run 42530135640](https://github.com/lucianox777/Jornada/actions/runs/42530135640) auditou **43/43**, nenhum DOCX com ocorrência, 51 em oito PDFs e 0 não auditados. As seções abaixo preservam dados e bloqueios históricos anteriores à decisão de adiamento.

> **Registro superveniente 08/11/2026:** os commits binários `f5288c96` e `d89db326` foram gravados APÓS o merge da PR #1332 na sua branch original. Não fazem parte da master. Seu conteúdo líquido está sendo transferido para a PR #1333: 9 DOCX e 1 PDF neutralizados; 2 documentos administrativos retirados por decisão do PO; os outros PDFs permanecem com revisão pendente. Os parágrafos abaixo que dizem “binários não integrados” descrevem a fotografia anterior ao recebimento, não um estado permanente. Conferir a master após merge da PR #1333 antes de declarar qualquer conclusão.

# NEUT-DEV-F — inventário de binários e resultados do gate read-only (08/11/2026)

**Origem reproduzível:** PR [#1332](https://github.com/lucianox777/Jornada/pull/1332), CI read-only [run 42507484490](https://github.com/lucianox777/Jornada/actions/runs/42507484490) no HEAD `3979a6b2855a25f91b1e0b6511a575b6f52fe87b`. Artefato **`neutralizacao-binarios-inventario`** contém JSON por caminho e contagem, sem texto documental nem dados pessoais. **Fotografia anterior à alteração binária; este inventário por si só não atesta neutralização final.**

## Contagens medidas no GitHub (pypdf 5.9.0 + leitura OOXML)

| Grupo | Arquivos com ocorrência nominal | Ocorrências antes | Situação |
|---|---:|---:|---|
| DOCX (total 22 auditados) | 10 | **61** | Exige substituição no DOCX original e conferência renderizada |
| PDF (total 23 auditados) | 10 | **59** | Regenerar exclusivamente da fonte correspondente quando possível |
| **Total (45 auditados)** | **20** | **120** | Zero arquivos sem inspeção; **não** significa zero ocorrências remanescentes |
 
Os 10 DOCX e correspondentes quantidades: `Anexo_Arquitetura_Fase1_Jornada_v1.39.docx` (3), `Anexo_Estrutura_Artefatos_Jornada_v1.43.docx` (1), `Anexo_Modelo_Fisico_DER_Jornada_v1.39.docx` (3), `Anexo_Pendencias_Desenvolvimento_Jornada_v1.47.docx` (6), `Especificacao_Tecnica_Jornada_v3.62.docx` (34), `Projeto_Integracao_Beneficios_Servicos_Sociais/Ficha_Projeto_Integracao_Beneficios_Servicos_Sociais.docx` (7), `Requisitos/00_Indice_Mestre_Requisitos_Jornada_v1.0.docx` (1), `Requisitos/00_Indice_Mestre_Requisitos_Jornada_v1.1.docx` (1), `Requisitos/01_Requisitos_de_Negocio_Jornada_v1.1.docx` (2), `Resumo_Executivo_Especificacao_Tecnica_Jornada_v1.1.docx` (3). Todos sob `Documentos/`.

Dos 10 PDFs com ocorrências, **9 possuem arquivo DOCX com mesmo basename**; a exceção identificável por fonte ausente é `Documentos/Resumo_Executivo_v3.82.pdf` (1 ocorrência), sem `Documentos/Resumo_Executivo_v3.82.docx` no repositório consultado. Isso não autoriza sobrepor texto no PDF, apagar arquivo, atualizar metadados para aparentar neutralização ou gerar PDF visualmente divergente.

## Execução segura da transformação

O workflow da PR `.github/workflows/neutralizacao-binarios-audit.yml` gera **artefato de patch somente leitura**: modifica os DOCX em workspace descartável de CI, tenta regenerar PDF **somente** a partir do DOCX correspondente, verifica páginas/extração, informa exceções e publica ZIP para inspeção/aplicação sob PR. **Não** grava no Git e **não** tem `contents: write`. A comparação visual continua obrigatória; discrepância de contagem de páginas ou extração vira **exceção identificada**, não aprovação fictícia.

O resultado final **no GitHub** requer aplicar e revisar os arquivos realmente modificados na branch da PR #1332 (ou na frente L se houver escopo textual), confirmar ZIP e layout, executar novamente a auditoria e provar residual somente nas exceções explícitas. Até então, a contagem comprovada no repositório é **120 antes / 120 após (nenhum binário integrado)**; distinguir esse número da medição isolada feita dentro da Action.

**Preservação:** não alterar migrações/baselines SQL anteriores, hashes selados, documentos sem ocorrência ou PDFs sem fonte validada. A política de substituição literal e exceções foi aprovada pelo Product Owner em 07–08/11/2026. A neutralização textual da PR #1333 é independente. Decisões P95, LOCK e D2 não são alteradas.

**Estado:** F em execução; **merge não autorizado até evidência positiva dos binários e CI da `master` no SHA exato**, observando fila G→F→campanha U-raro→J→K→L.

## Incremento de processamento em CI read-only — 08/11/2026

**Evidência nova:** [run 42508046393](https://github.com/lucianox777/Jornada/actions/runs/42508046393), artifact `neutralizacao-binarios-patch-readonly` (ID **13627958839**, ZIP com 15 entradas incluindo o recibo). Não há `git push` de binários na Action; nenhuma transformação foi integrada à master. A auditoria em workspace descartável verificou:

- **120 → 49 ocorrências nominais**; redução de **71** ocorrências naquele patch, sem inspeções faltantes.
- **10 DOCX** alterados, **4 PDFs** regenerados de fonte DOCX pareada com paginação conservada, **6 PDFs** mantidos como exceções. Portanto **14 arquivos binários efetivamente modificados** no patch; o ZIP contém também o recibo JSON. O ZIP contém **14 arquivos DOCX/PDF alterados** e **um recibo JSON** (15 entradas); o vetor `changedFiles` do recibo tem somente os 14 caminhos documentais, sem o recibo.
- As **49 ocorrências residuais estão apenas nos seis PDFs excepcionados**. Os dez DOCX processados e quatro PDFs regenerados não apresentaram ocorrência remanescente pela extração verificada.

| Exceção PDF | Motivo objetivo |
|---|---|
| `Anexo_Arquitetura_Fase1_Jornada_v1.39.pdf` | Fonte existente; páginas **10 → 9** no renderizador, necessita verificação visual antes de substituir |
| `Anexo_Modelo_Fisico_DER_Jornada_v1.39.pdf` | Fonte existente; páginas **15 → 14** |
| `Anexo_Pendencias_Desenvolvimento_Jornada_v1.47.pdf` | Fonte existente; páginas **17 → 16** |
| `Especificacao_Tecnica_Jornada_v3.62.pdf` | Fonte existente; páginas **72 → 71**; não reescrever a especificação publicada sem controle editorial |
| `Requisitos/01_Requisitos_de_Negocio_Jornada_v1.1.pdf` | Fonte existente; páginas **10 → 12** |
| `Resumo_Executivo_v3.82.pdf` | `SOURCE_DOCX_NOT_PRESENT`; **sem fonte com mesmo basename** localizada |

Todos os caminhos acima são relativos à pasta `Documentos/`, exceto os subdiretórios explicitamente indicados. **A variação de páginas pode representar apenas renderizador/fonte, mas não pode ser aprovada silenciosamente sem inspeção visual.**

**Estado de GitHub após o run:** a PR #1332 contém tooling, documentação e **artifact disponível**, mas **ainda não contém os 14 arquivos binários modificados**. A contagem real no Git permanece **120 → 120** até que os bytes revisados do patch sejam incorporados à mesma PR, e até nova execução dos checks no SHA final. **AGUARDA revisão/aplicação do patch binário**; esta pendência impede declarar F concluída e avançar para merge em fila única.


## Cinco PDFs de layout divergente — previews disponíveis, NÃO publicados

O [run 42510185175](https://github.com/lucianox777/Jornada/actions/runs/42510185175) (**SUCCESS**) publicou um artifact de QA id **13628548526**, que inclui **cinco PDFs candidatos renderizados em `pdf-previews/`** para os cinco casos de discrepância de páginas e o mesmo pacote de **14 binários** com 10 DOCX + 4 PDF comprovadamente sem alteração de quantidade de páginas. **Os cinco previews não substituem nenhum PDF do repositório**, não entram no ZIP de aplicação de 14 arquivos, não reduzem as 49 ocorrências remanescentes no patch e exigem comparação visual antes de decidir sua publicação. O sexto PDF, `Resumo_Executivo_v3.82.pdf`, não possui fonte editável correspondente.

**Nota de autenticidade:** o pacote de aplicação de 14 arquivos que foi exportado separadamente a partir do run **42508046393** continua com SHA-256 `376bae3e06b34802e736f1b3c7d47e2e7e3b6ecc1b98a1822564b301c8d3f988`; o artifact com previews de run posterior possui bytes de ZIP diferentes e não deve ser tratado como idêntico. O script `apply-neutralized-binary-patch.py` exige o primeiro SHA explicitamente para impedir troca silenciosa de evidência. Nenhum dos dois foi integrado automaticamente à PR.

## Aplicação manual do PO — 08/11/2026

- **Commit `f5288c96` (branch da #1332):** aplicados 10 DOCX e `Requisitos/00_Indice_Mestre_Requisitos_Jornada_v1.1.pdf` do pacote `neutralizacao-binarios-patch-readonly`, após conferência contra a master: a única diferença de texto é PRODAM/PRODAM-SP → DESENVOLVEDOR e a concordância.
- **Não aplicados (AGUARDA PO):** `Anexo_Estrutura_Artefatos_Jornada_v1.43.pdf` (diferenças de texto/layout além da troca) e `Requisitos/00_Indice_Mestre_Requisitos_Jornada_v1.0.pdf` (marcação Markdown literal no PDF regenerado).
- **Retirados do repositório por decisão do PO:** `Projeto_Integracao_Beneficios_Servicos_Sociais/Ficha_Projeto_Integracao_Beneficios_Servicos_Sociais.docx` e `.pdf` — ficha administrativa do processo de contratação, com dados orçamentários; não é especificação do produto. O histórico do Git preserva as versões anteriores.

## Correção do gate após a exclusão administrativa (08/11/2026, nova tentativa técnica)

**Fato verificado:** o run [42517970694](https://github.com/lucianox777/Jornada/actions/runs/42517970694), HEAD anterior `f38a15ee3916804535db68275d84396f25bf2e99`, executou a auditoria read-only com **43/43 binários inspecionados, 51 menções em oito PDFs, zero DOCX com menções**, artifact 13631679692. O job de regeneração recusou a execução por exigir **45 arquivos históricos** apesar da exclusão documental autorizada pelo PO; **não houve alteração de dados nem falha na extração**. A recusa foi determinística e não merece rerun do mesmo SHA.

**Correção pontual da CI, segunda tentativa documentada (máximo três pushes próprios nesta nova tentativa):** o manifesto `Solution/docs/Inventario_Binarios_Congelado_20261108.json` fixa as **45 rotas documentais da master `db75f3a3f1a3ad38ec6d4f8f1ae3b2e254694b54`**, as **duas exclusões administrativas expressamente autorizadas** e as **43 rotas restantes**; o validador `Solution/scripts/neutralizacao_binarios_inventario_gate.py` compara nomes reais do Git, linhas de relatório, totais e ausência de arquivos não auditados. Rejeita caminho adicional, retirada diferente ou reinclusão da ficha administrativa; **não** aceita apenas uma contagem literal de 43. Testes positivos/negativos por unittest cobrem os casos.

O gerador read-only e o regenerador PDF usam o validador antes/depois da transformação, com `filesAudited` calculado do relatório real, e **nenhum** `contents: write`, `git push`, reescrita do histórico, dispensa de QA visual ou alteração de PDF por substituição direta. Isto **não libera os oito PDFs**; redução dessas 51 menções exige avaliação visual e decisão documental separada, assim como as exceções SQL. Executar checks do novo HEAD antes de qualquer merge. A PR #1333 continua posterior a J e K no mandato. As contagens históricas 45/120 desta auditoria permanecem como evidência da origem, não como estado atual.
