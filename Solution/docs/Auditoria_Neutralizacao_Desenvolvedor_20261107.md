> **ATUALIZAÇÃO DE ESCOPO — mandato final 08/11/2026:** as contagens e os caminhos abaixo são o levantamento da base histórica desta PR e devem ser recontados por `git grep -I -i` + extrator de binários após todos os merges. Esta PR textual **não comprova neutralização integral**; o pacote de 14 binários da #1332 segue **AGUARDA EXECUÇÃO MANUAL DO PO**. Sem alterar exceções SQL hash-fixadas nem PDFs sem fonte. Ver [mandato oficial](Decisao_V9_Mandato_Integracao_Final_Teste_Geral_20261108.md) (pendente de integração pela #1318).

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

## Inventário SQL da branch reconciliada — 08/11/2026 (continuidade do mandato final)

**Base inspecionada:** HEAD da PR #1333 `001a426cce5a1c1a1553e66a258ff647f3d935aa`, baseado em master `db75f3a3f1a3ad38ec6d4f8f1ae3b2e254694b54`. Leitura pontual de todos os **nove caminhos SQL retornados pela busca textual indexada** por referência nominal; contagem literal por correspondência sem distinguir maiúsculas/minúsculas, usando o conteúdo integral de cada arquivo. **Não equivale ao `git grep -I -i` integral**, que só poderá certificar a árvore após os merges; tampouco inspeciona binários.

| Caminho SQL | Ocorrências | Git blob SHA-1 (não é SHA-256 de manifesto) | Situação e regra de precedência |
| --- | ---: | --- | --- |
| `Solution/database/Jornada_Fase1.sql` | **4** | `46d59c5d4a26d963952c1c06188a6abb21cfdc69` | **BLOQUEIA NEUTRALIZAÇÃO GLOBAL:** schema corrente, não é exceção histórica autorizada; precisa de nova migração idempotente, preservação de legado e testes de upgrade |
| `Solution/database/baselines/Jornada_Fase1_v3.55.sql` | 4 | `444637e0628cb7f8bc029e5cc9c7890ce9fc356a` | Baseline histórica; comprovar SHA de manifesto selado antes de declarar exceção |
| `Solution/database/baselines/Jornada_Fase1_v3.57.sql` | 4 | `8dd193bc8a2fcfc023dab4738e88390076d0a53c` | Idem |
| `Solution/database/baselines/Jornada_Fase1_v3.58.sql` | 4 | `49095a36dbffd969bb91f0ff02e880e2666efbb2` | Idem |
| `Solution/database/baselines/Jornada_Fase1_v3.59.sql` | 4 | `9ae941bd2c0ec6ac41822e231ec69d0efc097400` | Idem |
| `Solution/database/baselines/Jornada_Fase1_v3.60.sql` | 4 | `30c9ac61c599167b8d5a0456471d0207807bb76b` | Idem |
| `Solution/database/baselines/Jornada_Fase1_v3.65.sql` | 4 | `5b4d0e4e4a979906f7fc2d7caf080ddf687f8389` | Idem |
| `Solution/database/migrations/20260920_Linkage_Model_Promotion_Ledger.sql` | 1 | `1903740c3612b334d5c1c75b62c403bfbdaa7ba2` | Comentário histórico; exigir verificação SHA-256 selado antes de conservar exceção |
| `Solution/database/migrations/20260921_Pessoa_V5_Contrato_371.sql` | 1 | `6ccda8bf6ac0f52846f85b4bfec9379926609a29` | Dominio histórico em CHECK; exigir migração de dados antes de alterar literal |
| **Subtotal SQL identificado** | **30** | — | **4 no schema corrente + 26 em versões anteriores** |

**Distinções obrigatórias:** o antigo subtotal de 34 menções remanescentes no levantamento original incluía 4 ocorrências adicionais em C#/script/workflow de uma master anterior; não foi reproduzido nesta leitura restrita a SQL e **não** se afirma que restem 34 na árvore atual. A neutralização textual proposta nos 37 arquivos da PR permanece separada da neutralização SQL. O nome institucional anterior aparece nos históricos apenas para inventariar vestígios — não autoriza novas referências. Os binários permanecem inalterados na master: 14 edições propostas no artifact 13627958839 não aplicadas, além de PDFs com exceções de renderização/fonte. **Nenhuma exceção por SHA-256 foi ratificada somente pelo Git blob**.

**Bloqueio funcional:** uma substituição textual direta do valor `ENRIQUECIMENTO_...` no CHECK do schema corrente rejeitaria linhas históricas e pode mudar semântica. Manter `Jornada_Fase1.sql` inalterado por esta PR de documentação até haver upgrade SQL auditado: (a) domínio novo `ENRIQUECIMENTO_DESENVOLVEDOR`, (b) remapeamento de dados legado em migração idempotente, (c) constraints e compatibilidade dos replays, (d) testes físicos SQL e autorização de execução. Não adulterar migrations/baselines hash-fixadas e não alegar neutralização total. Esta **constatação de bloqueio não aprova nem executa a migração**. GOV-DOC-01: prevalecem a regra do PO e o [mandato final](Decisao_V9_Mandato_Integracao_Final_Teste_Geral_20261108.md), que está em branch documental #1318 até validação.

## Pacotes do PO encontrados em 08/11/2026

Depois do merge #1332, a branch `tools/neutralizacao-binarios-auditoria-ci-20261107` recebeu `f5288c965406efdd32fd9fb9cf0ef5784a9e1ae6` e `d89db326a5d21115ee8761f8a0eab60ebdb46d94`. Não fazem parte da master. O primeiro commit contém 10 DOCX e 1 PDF corrigidos. O segundo retira a ficha administrativa `.docx` e `.pdf` por decisão documentada do PO, preservando os commits antigos. Saldo líquido: 9 DOCX corrigidos, 1 PDF corrigido e 2 documentos retirados. O saldo dos PDFs não alterados é estimado em 51 ocorrências em 8 arquivos, sujeito a varredura física final. O presente registro não certifica master, visualização, nem resultado global. Integrar na PR #1333 antes da validação, após as pré-condições do mandato.

## Retificação da classificação do SQL territorial — 08/11/2026 (prova da frente G já integrada)

**Achado posterior ao inventário de nove caminhos:** a nota anterior que descrevia `Solution/database/Jornada_Fase1.sql` (quatro menções) como **sem migração e bloqueador da criação do domínio novo** estava desatualizada. A frente **G/#1334 foi MESCLADA** em `749c0bc4021626738786653f87eac0d513e2d0a1` antes da master `db75f3a3...`, que já contém `Solution/database/postrelease/v3.71/20261107_Territorial_Origem_Desenvolvedor.sql` e os instaladores `Jornada_Fase1_v3.71_fresh.sql` / `Jornada_Fase1_v3.71_upgrade.sql`. Em SQL Server descartável, o job `territorial-v371-real-sql-smoke` do [run 42427310276](https://github.com/lucianox777/Jornada/actions/runs/42427310276) passou: cria versão **3.70**, insere uma linha geográfica sintética com valor antigo, executa extensão **3.71 duas vezes**, prova conversão ao domínio `ENRIQUECIMENTO_DESENVOLVEDOR`, ausência do valor anterior e dois CHECKs confiáveis. Trata-se de **teste físico da migração existente**, NÃO de ativação institucional da 3.71 em HML/PROD nem de promoção do schema candidato.

**Precedência de proveniência:** `CANDIDATE_INFO.json` declara schema **3.70** como `canonical_ddl=Solution/database/Jornada_Fase1_v3.70.sql`, manifesto `Solution/database/migrations/manifest.txt` e SHA-256 declarado `c1d7d37f505563baea5d71d310e996945f5ffbf59d34fa6e24defbed3252e4b8`. A fachada `Jornada_Fase1_v3.70.sql` inclui `Jornada_Fase1.sql` histórico (blob Git `46d59c5d4a26d963952c1c06188a6abb21cfdc69`) e migrações antigas. **Não neutralizar por substituição textual no bootstrap 3.70, nos CHECKs históricos nem nas migrações antigas**, sob pena de alterar as provas da versão anterior. A extensão transacional 3.71 é a mudança aditiva e idempotente correta. **Git blob SHA-1 e SHA-256 de manifesto são objetos distintos:** o SHA do manifesto é declaração observada em `CANDIDATE_INFO`, não atestado isolado de cada baseline v3.55–v3.65.

**Correção de status do inventário acima:** continuam fisicamente visíveis as quatro ocorrências em `Jornada_Fase1.sql` e as 26 nos outros oito SQLs; **não são prova de implantação v3.71 nem zero nominal**. Porém a necessidade funcional de **CRIAR uma nova migração** para esses quatro tokens está **SUPERADA** pela G/#1334 e por sua prova SQL; preservá-los requer classificação explícita como dependência histórica do 3.70, e **a comprovação dos hashes de cada arquivo histórico ainda está pendente**. Não prometer substituição no bootstrap nem no SQL versionado. O encerramento do requisito documental L requer que o índice de exceções, a CI no SHA final e os PDFs restantes sejam reconciliados com o PO. Uma eventual **adoção operacional da extensão 3.71** continua sujeita a decisão de promoção própria, fora deste mandato. A neutralização de documentos não pode fingir que ela ocorreu.

**Referências exatas:** merge #1334 `749c0bc4`; run físico 42427310276; `CANDIDATE_INFO.json`; `Solution/database/postrelease/v3.71/README.md`; `Solution/database/postrelease/v3.71/20261107_Territorial_Origem_Desenvolvedor.sql`; auditoria de nove caminhos SQL nesta página. Registro técnico desta retificação segue GOV-DOC-01, sem nova decisão científica ou alteração executável do domínio.
