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
