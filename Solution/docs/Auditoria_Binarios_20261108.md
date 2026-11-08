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
