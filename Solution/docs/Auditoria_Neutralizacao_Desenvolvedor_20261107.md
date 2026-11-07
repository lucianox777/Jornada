# NEUT-DEV-20261107 — neutralização literal do fornecedor em toda a árvore

**Decisão expressa do Product Owner em 07/11/2026, confirmada na discussão posterior da mesma data.** Exigência GOV-DOC-01. Objetivo: nenhuma menção nominal ao fornecedor anterior na árvore **corrente** do repositório, exceto baselines/migrações SQL cujo hash histórico precise ser preservado e seja expressamente autorizado. Não há autorização para reescrever o histórico Git.

## Regra única e precedência

Toda grafia do nome anterior (maiúsculas, minúsculas e capitalização mista) passa a **DESENVOLVEDOR**, sempre em maiúsculas. Sufixos do nome institucional anterior não fazem parte da designação nova. Mudar somente artigo e concordância indispensáveis (como `da`→`do`, `pela`→`pelo`, `na`→`no`); não inventar atribuições, autorizações, relações contratuais ou governança. Esta decisão **substitui** a estratégia parcialmente contextual de #1319 e a permissão anterior de manter menções na documentação histórica. O conteúdo de commits antigos continua preservado no histórico do Git.

## Auditoria de 07/11 — base e cobertura limitada

Base: master `65659c681eb02620e49acd96196c5a53db54be9a`; varredura textual indexada do GitHub, com 49 arquivos e 82 ocorrências literais na árvore textual coberta. A mesma metodologia identifica:

| Superfície | Antes | Proposto nesta PR | Restante |
| --- | ---: | ---: | ---: |
| Documentação e diagramas textuais, inclusive Revisoes/archive | 48, em 37 arquivos | 48 substituições nos 37 arquivos | 0 nesses 37 |
| C#, script Python e workflow | 4, em 3 arquivos | PR técnica separada | 4 |
| SQL atual, baselines e migrações históricas | 30, em 9 arquivos | migração e auditoria próprias | 30 |
| **Subtotal indexado** | **82** | **48** | **34** |
| Binários: DOCX (22) | **61 em 10 arquivos** | nenhuma alteração de binários | **61** |
| Binários: PDF (23) | **59 em 10 arquivos** | nenhuma alteração de binários | **59** |

**Nova evidência física de extração (run #42422301963, SHA `73cfd63c4c48cd6537b66b5b288982823a3c0c07`):** 45 documentos binários foram inspecionados por leitura OOXML/pypdf, com **120 ocorrências detectadas** (61 em 10 DOCX, 59 em 10 PDF), sem erro de extração informado; artifact id **13594271681**, digest SHA-256 `6ba2e5fd770b73b7dd825e44048d1745bd11d0f8b5ab4687cad0286cf246ae15`. A contagem combinada identificada na base é **202 ocorrências**: 82 textuais + 120 binárias; após a troca dos 37 documentos textuais, **154** restam nas superfícies auditadas (34 em arquivos textuais técnicos e 120 em binários). Arquivos binários ainda **não foram editados**. O instrumento mede texto extraível; imagens escaneadas continuam sujeitas a verificação humana. Não há alegação de zero absoluto. Conferência textual integral em checkout: `git grep -I -i -o 'p[r]odam' | wc -l`. Complementar obrigatoriamente com o texto extraído dos 45 binários; distinguir referências na árvore atual daquelas no histórico Git. A busca indexada do GitHub pode omitir conteúdos não indexados; o scanner local será a verificação final.

## Sequência de implementação, sem mascarar bloqueios

1. **Documentos textuais:** esta PR incorpora também versões de engenharia, revisões de setembro, releases históricos versionados e fontes/espelhos SVG/DOT/PlantUML. As saídas SVG exigem inspeção visual para confirmar geometria/legibilidade após a troca nominal. Nenhum dado real foi introduzido.
2. **DOCX editáveis:** extrair e contar no ZIP OOXML, inclusive cabeçalho, rodapé, tabelas, notas, caixas de texto, comentários e propriedades; aplicar somente as trocas aprovadas preservando estilos, relações e layout; renderizar e comparar páginas antes de integrar. Nenhum documento binário foi alterado nesta PR.
3. **PDFs:** extrair texto, contar e listar os documentos afetados; não editar diretamente arquivos PDF. Regenerar a partir das fontes, quando comprovadas, preservando original legal fora da árvore atual caso necessário e autorizado; sem fonte, relatar ao PO e não afirmar neutralização concluída.
4. **C# / scripts / workflow:** PR própria restrita aos comentários e textos visíveis, sem alterar execução. Não inserir nomes antigos em novos registros.
5. **SQL:** confirmar que o novo valor de domínio é `ENRIQUECIMENTO_DESENVOLVEDOR`. Criar **nova migração** idempotente que inclua o valor novo nos CHECKs, converta linhas existentes e remova a autorização do valor antigo, com testes do upgrade e rollback controlado. Não alterar silenciosamente semântica de dados, nem tocar no legado hash-selado.
6. **Baselines e migrações anteriores:** verificar hashes de manifesto/fixture por arquivo. Se hash fixo, **não editar** e listar o SHA e a razão da exceção para aprovação já prevista pelo PO; se não, somente o comentário pode ser neutralizado, sem mudar o comportamento nem quebrar replays.
7. **Exclusão de itens exclusivamente contratuais:** não apagar nada antes de classificação arquivística; preservar documentos de produto e trilha histórica. O repositório público não é reescrito.

## Estado de governança

As PRs preparatórias podem existir em draft, mas **nenhum merge, nem de documentação**, está autorizado enquanto persistirem os bloqueios de V9. Após cada merge autorizado, exigir CI da master e, para superfícies impactantes, o gate físico cumulativo seed42 no SHA exato. O encerramento desta frente depende de relatório final com **contagem antes/depois de todos os tipos textuais e binários**, exceções explicitamente autorizadas e rastreabilidade dos SHAs das PRs. Esta PR documental não certifica a troca no domínio SQL nem nos binários.

## Inventário binário extraído — evidência nova, sem modificação de bytes

**DOCX com menções (10 documentos, total 61 ocorrências):**

| Documento | Ocorrências |
| --- | ---: |
| `Documentos/Anexo_Arquitetura_Fase1_Jornada_v1.39.docx` | 3 |
| `Documentos/Anexo_Estrutura_Artefatos_Jornada_v1.43.docx` | 1 |
| `Documentos/Anexo_Modelo_Fisico_DER_Jornada_v1.39.docx` | 3 |
| `Documentos/Anexo_Pendencias_Desenvolvimento_Jornada_v1.47.docx` | 6 |
| `Documentos/Especificacao_Tecnica_Jornada_v3.62.docx` | 34 |
| `Documentos/Projeto_Integracao_Beneficios_Servicos_Sociais/Ficha_Projeto_Integracao_Beneficios_Servicos_Sociais.docx` | 7 |
| `Documentos/Requisitos/00_Indice_Mestre_Requisitos_Jornada_v1.0.docx` | 1 |
| `Documentos/Requisitos/00_Indice_Mestre_Requisitos_Jornada_v1.1.docx` | 1 |
| `Documentos/Requisitos/01_Requisitos_de_Negocio_Jornada_v1.1.docx` | 2 |
| `Documentos/Resumo_Executivo_Especificacao_Tecnica_Jornada_v1.1.docx` | 3 |

**PDFs com menções (10 documentos, total 59 ocorrências), exigindo regeneração a partir de fonte:**

| Documento | Ocorrências |
| --- | ---: |
| `Documentos/Anexo_Arquitetura_Fase1_Jornada_v1.39.pdf` | 3 |
| `Documentos/Anexo_Estrutura_Artefatos_Jornada_v1.43.pdf` | 1 |
| `Documentos/Anexo_Modelo_Fisico_DER_Jornada_v1.39.pdf` | 3 |
| `Documentos/Anexo_Pendencias_Desenvolvimento_Jornada_v1.47.pdf` | 6 |
| `Documentos/Especificacao_Tecnica_Jornada_v3.62.pdf` | 34 |
| `Documentos/Projeto_Integracao_Beneficios_Servicos_Sociais/Ficha_Projeto_Integracao_Beneficios_Servicos_Sociais.pdf` | 7 |
| `Documentos/Requisitos/00_Indice_Mestre_Requisitos_Jornada_v1.0.pdf` | 1 |
| `Documentos/Requisitos/00_Indice_Mestre_Requisitos_Jornada_v1.1.pdf` | 1 |
| `Documentos/Requisitos/01_Requisitos_de_Negocio_Jornada_v1.1.pdf` | 2 |
| `Documentos/Resumo_Executivo_v3.82.pdf` | 1 |

A correspondência entre cada PDF e sua fonte editável deve ser autenticada antes da regeneração. Arquivos sem fonte correlata exata não podem ser substituídos por conversão de documento arbitrário. O XML do Word requer QA visual pós-substituição. [Workflow e ferramenta de auditoria em PR #1332](https://github.com/lucianox777/Jornada/pull/1332). O novo registro preserva a evidência de que **a neutralização binária ainda NÃO foi concluída**.
