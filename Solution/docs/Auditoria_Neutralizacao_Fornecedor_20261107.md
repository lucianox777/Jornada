# Neutralização de fornecedor — inventário de decisão e exceções (07/11/2026)

**Decisão do PO:** o repositório representa a especificação do produto Jornada e não atribui automaticamente papéis de execução ou direitos a empresa específica. A substituição deve ser **contextual**, sem inventar responsabilidades: `empresa desenvolvedora`, `equipe mantenedora designada` e/ou `equipe de operação designada`. É proibido nesta PR mudar código, workflows, nomes de teste, SHA, evidência, recibos, arquivo LICENSE, arquivos versionados de release e histórico Git.

**Dependência de ordem:** esta PR é apenas documental, deve permanecer **DRAFT e não ser integrada antes da conclusão confirmada do item 2** do roteiro do PO. O texto do item 2 não foi disponibilizado nesta rodada; portanto, não foi presumida sua conclusão.

## Recorte e método

- Base de leitura: `master` **77fae0d9e2c1603c40e26a13987cd1a160ca4295**.
- Árvore obtida pela API Git: **2.538 blobs**, sendo **45 DOCX/PDF e outros binários de documentos** não inspecionados quanto ao conteúdo interno, pois o conector retorna texto e não extrai esses binários.
- Busca em texto indexado do GitHub, **sem diferenciar maiúsculas/minúsculas**, seguida de leitura integral dos **49 arquivos retornados** para contagem de ocorrências; contagem por menção literal, não por linha.
- **Antes: 82 menções em 49 arquivos textuais identificados**.
- **Após, nesta branch: 50 menções textuais remanescentes, todas listadas adiante como exceções deliberadas**. Foram neutralizadas **32 menções em 24 arquivos documentais**.
- A busca indexada e a exceção dos 45 binários **não permitem garantir zero absoluto em toda a árvore**. Fica vedado apresentar esta PR como eliminação física universal sem auditar DOCX/PDF e demais arquivos não indexados.
- Comando de conferência em checkout local, para conteúdo de texto: `git grep -I -oi 'P[R]ODAM' HEAD | wc -l`. O padrão de busca está escrito como expressão de caracteres para não acrescentar uma menção literal a este próprio relatório.

## Arquivos alterados — contagem antes → depois (32 → 0)

| Caminho | Antes | Depois |
| --- | ---: | ---: |
| `Documentos/Revisoes/README.md` | 1 | 0 |
| `LEIA-ME.txt` | 1 | 0 |
| `SECURITY.md` | 1 | 0 |
| `Solution/README.md` | 2 | 0 |
| `Produtos/ApoioSecretarias/README.md` | 1 | 0 |
| `Solution/docs/Territorializacao_Fase1.md` | 1 | 0 |
| `Solution/docs/API.md` | 1 | 0 |
| `Solution/docs/README.md` | 1 | 0 |
| `Solution/docs/Guia_Leitura_Glossario.md` | 1 | 0 |
| `Solution/docs/Monitor_Operacional.md` | 3 | 0 |
| `Solution/docs/Runbook_Operacao.md` | 3 | 0 |
| `Solution/docs/Invariantes_Arquitetura.md` | 1 | 0 |
| `Solution/docs/Ensaio_Unico_Paridade_HML.md` | 1 | 0 |
| `Solution/docs/Mapa_Modulos_Produto_Andaime.md` | 1 | 0 |
| `Solution/docs/Dividas_Tecnicas.md` | 1 | 0 |
| `Solution/install/windows-production/CLUSTER.md` | 1 | 0 |
| `Solution/docs/Runbook_Testes_Tecnicos.md` | 2 | 0 |
| `Solution/docs/Estado_Atual_Projeto.md` | 1 | 0 |
| `Solution/install/windows-production/README.md` | 1 | 0 |
| `Solution/docs/Plano_Desenvolvimento.md` | 1 | 0 |
| `Solution/docs/Gate_06_Segregacao_SEHAB_Evidencias.md` | 1 | 0 |
| `Solution/docs/Manual_Sistema_Consolidado_20261009.md` | 1 | 0 |
| `Solution/tests/README.md` | 2 | 0 |
| `Solution/docs/uml/Jornada_Implantacao.puml` | 2 | 0 |

**Cautelas semânticas:** §378 continua exigindo integração de autenticação e separação de ambientes; não virou responsabilidade da equipe de desenvolvimento por omissão. Territorialização segue sob responsabilidade do Gestor/origem, sem consultas Pessoa-a-Pessoa a serviço externo no fluxo normal. Agendamento deve ocorrer em mecanismo corporativo homologado, com responsável de operação **a ser designado** pelos instrumentos institucionais pertinentes; esta PR não o designa. Aprovações de HML/Produção permanecem gates distintos de CI DEV. Nomes de pessoas/órgãos públicos e demais responsabilidades institucionais foram preservados.

## Exceções: referências históricas/artefatos imutáveis (16 menções, 13 arquivos)

| Caminho | Menções |
| --- | ---: |
| `Documentos/Estado_Engenharia_v4.02.md` | 1 |
| `Solution/docs/archive/releases/RELEASE_v4.02.txt` | 1 |
| `Solution/docs/archive/releases/NOTA_ENGENHARIA_V4.02.txt` | 1 |
| `Documentos/Revisoes/2026-09-27_checkpoint.md` | 1 |
| `Documentos/Resumo_Executivo_v3.82.md` | 1 |
| `Documentos/Revisoes/2026-09-21_checkpoint.md` | 1 |
| `Documentos/Requisitos/00_Indice_Mestre_Requisitos_Jornada_v1.0.md` | 1 |
| `Documentos/Requisitos/00_Indice_Mestre_Requisitos_Jornada_v1.1.md` | 1 |
| `Documentos/Requisitos/01_Requisitos_de_Negocio_Jornada_v1.1.md` | 2 |
| `Solution/README_Historico_Engenharia_v4.03.md` | 3 |
| `Solution/docs/V9_Suporte_Massa_Backoff_20261019.md` | 1 |
| `Solution/docs/diagrams/Arquitetura_Fase1_v3.54.dot` | 1 |
| `Solution/docs/diagrams/Arquitetura_Fase1_v3.54.svg` | 1 |

**Justificativa:** versões de engenharia/releases, checkpoints datados, baselines de requisitos com versões DOCX/PDF correspondentes e documentos de evidência técnica preservam proveniência e conteúdo de seus cortes históricos. Os diagramas v3.54 `.dot`/`.svg` são fontes/saídas de arquitetura versionada e não devem ser retocados isoladamente. Se o PO determinar uma revisão das versões atuais de requisitos, atualizar **de modo coerente** Markdown+DOCX+PDF e emitir nova edição; não alterar cópias e hashes legados silenciosamente.

## Exceções protegidas: código, SQL, migrations, scripts e workflow (34 menções, 12 arquivos)

| Caminho | Menções |
| --- | ---: |
| `Solution/src/Jornada.Processor.Worker/GeographicEnrichment.cs` | 1 |
| `Solution/scripts/consolidate-requirements.py` | 2 |
| `Solution/database/Jornada_Fase1.sql` | 4 |
| `Solution/database/baselines/Jornada_Fase1_v3.55.sql` | 4 |
| `Solution/database/baselines/Jornada_Fase1_v3.57.sql` | 4 |
| `Solution/database/baselines/Jornada_Fase1_v3.58.sql` | 4 |
| `Solution/database/baselines/Jornada_Fase1_v3.59.sql` | 4 |
| `Solution/database/baselines/Jornada_Fase1_v3.60.sql` | 4 |
| `Solution/database/baselines/Jornada_Fase1_v3.65.sql` | 4 |
| `Solution/database/migrations/20260920_Linkage_Model_Promotion_Ledger.sql` | 1 |
| `Solution/database/migrations/20260921_Pessoa_V5_Contrato_371.sql` | 1 |
| `.github/workflows/ci.yml` | 1 |

**Justificativa técnica:** identificadores SQL históricos, inclusive valor persistido de origem geográfica `ENRIQUECIMENTO_<nome_legado>`, aparecem no schema/baselines/migrations; sua troca não é editorial, exigiria avaliação de dados, migração e contrato. A ocorrência em `.cs` é comentário de código; em `.py` há comentário e literal de saída documental; no workflow, comentário de gate. Tudo preservado. **Nenhum arquivo de código, teste automatizado, SQL, workflow ou LICENSE foi alterado**. Qualquer neutralização futura nessas superfícies requer consulta prévia ao PO e PR técnica independente.

## Documentos eventualmente exclusivos do contrato — triagem, sem exclusão

- **Minutas, e-mails de contratação, plano de capacitação do fornecedor e quadro de responsabilidades contratuais**: não foram encontrados arquivos com essas categorias nos **nomes dos 2.538 blobs** da árvore, tampouco resultados textuais para as buscas específicas de minuta contratual e plano de capacitação. **Não é prova de inexistência em arquivos binários**.
- **Candidatos a revisão pelo PO, sem remoção proposta como fato consumado:** `Documentos/Estado_Engenharia_v4.02.md`, `Documentos/Resumo_Executivo_v3.82.md`, `Solution/docs/archive/releases/NOTA_ENGENHARIA_V4.02.txt`, `Solution/docs/archive/releases/RELEASE_v4.02.txt`. Estes arquivos citam arranjos anteriores de fornecedor, **mas também registram decisões técnicas de produto e versões**. Não está comprovado que existam *somente* por causa de contrato. A retirada do repositório seria possível **apenas após classificação expressa do PO e preservação de proveniência/rastreabilidade**; enquanto isso, não eliminar nem fazer `git filter-branch`/`filter-repo`.
- **Requisitos v1.1**: manter espelhos textuais/binários e aprovação institucional. Para neutralização de nova edição é necessária decisão sobre atualização sincronizada, sem adulterar baselines antigos.
- **Itens não auditados**: conteúdo interno dos 45 binários, outros não indexados e eventuais anexos fora da árvore Git. Não inferir contagem zero nesse conjunto.

## Critérios de integração

1. Confirmar execução prévia do **item 2** da lista do PO; nenhuma integração antecipada.
2. Validar via diff que só os **24 arquivos documentais** acima e **este relatório** mudaram.
3. Verificar que a soma residual textual é **50**, toda justificada nos **25 arquivos de exceção**, e que cada arquivo alterado contém **zero** menções.
4. Identificar a exceção dos binários e de qualquer ocorrência que apareça em nova varredura; não relaxar a regra de rastreabilidade.
5. Não remover documento contratual, não tocar código/testes/workflows e não fazer merge sem confirmação do PO sobre a dependência.

## Apêndice — lista exata dos 45 documentos binários não inspecionados

Os arquivos abaixo **não foram alterados nem tiveram o conteúdo interno examinado** nesta PR. Não são contabilizados como menções comprovadas; são **limitação explícita** do levantamento textual, inclusive espelhos normativos DOCX/PDF.

- `Documentos/Anexo_Arquitetura_Fase1_Jornada_v1.39.docx`
- `Documentos/Anexo_Arquitetura_Fase1_Jornada_v1.39.pdf`
- `Documentos/Anexo_Estrutura_Artefatos_Jornada_v1.43.docx`
- `Documentos/Anexo_Estrutura_Artefatos_Jornada_v1.43.pdf`
- `Documentos/Anexo_Fluxos_Sequencia_Fato_Identidade_Jornada_v1.12.docx`
- `Documentos/Anexo_Fluxos_Sequencia_Fato_Identidade_Jornada_v1.12.pdf`
- `Documentos/Anexo_Modelo_Fisico_DER_Jornada_v1.39.docx`
- `Documentos/Anexo_Modelo_Fisico_DER_Jornada_v1.39.pdf`
- `Documentos/Anexo_Modelo_Fisico_Jornada_v1.40.docx`
- `Documentos/Anexo_Modelo_Fisico_Jornada_v1.40.pdf`
- `Documentos/Anexo_Pendencias_Desenvolvimento_Jornada_v1.47.docx`
- `Documentos/Anexo_Pendencias_Desenvolvimento_Jornada_v1.47.pdf`
- `Documentos/Anexo_SLA_Atraso_Recebimento_Jornada_v1.22.docx`
- `Documentos/Anexo_SLA_Atraso_Recebimento_Jornada_v1.22.pdf`
- `Documentos/Anexo_Seguranca_Auditoria_Acesso_Jornada_v1.34.docx`
- `Documentos/Anexo_Seguranca_Auditoria_Acesso_Jornada_v1.34.pdf`
- `Documentos/Anexo_Telas_BI_Jornada_v1.48.docx`
- `Documentos/Anexo_Telas_BI_Jornada_v1.48.pdf`
- `Documentos/Especificacao_Tecnica_Jornada_v3.62.docx`
- `Documentos/Especificacao_Tecnica_Jornada_v3.62.pdf`
- `Documentos/Projeto_Integracao_Beneficios_Servicos_Sociais/Apresentacao_Integracao_Beneficios-core.pdf`
- `Documentos/Projeto_Integracao_Beneficios_Servicos_Sociais/Ficha_Projeto_Integracao_Beneficios_Servicos_Sociais.docx`
- `Documentos/Projeto_Integracao_Beneficios_Servicos_Sociais/Ficha_Projeto_Integracao_Beneficios_Servicos_Sociais.pdf`
- `Documentos/Requisitos/00_Indice_Mestre_Requisitos_Jornada_v1.0.docx`
- `Documentos/Requisitos/00_Indice_Mestre_Requisitos_Jornada_v1.0.pdf`
- `Documentos/Requisitos/00_Indice_Mestre_Requisitos_Jornada_v1.1.docx`
- `Documentos/Requisitos/00_Indice_Mestre_Requisitos_Jornada_v1.1.pdf`
- `Documentos/Requisitos/01_Requisitos_de_Negocio_Jornada_v1.1.docx`
- `Documentos/Requisitos/01_Requisitos_de_Negocio_Jornada_v1.1.pdf`
- `Documentos/Requisitos/02_Requisitos_Funcionais_Jornada_v1.0.docx`
- `Documentos/Requisitos/02_Requisitos_Funcionais_Jornada_v1.0.pdf`
- `Documentos/Requisitos/02_Requisitos_Funcionais_Jornada_v1.1.docx`
- `Documentos/Requisitos/02_Requisitos_Funcionais_Jornada_v1.1.pdf`
- `Documentos/Requisitos/03_Requisitos_Nao_Funcionais_Jornada_v1.0.docx`
- `Documentos/Requisitos/03_Requisitos_Nao_Funcionais_Jornada_v1.0.pdf`
- `Documentos/Requisitos/03_Requisitos_Nao_Funcionais_Jornada_v1.1.docx`
- `Documentos/Requisitos/03_Requisitos_Nao_Funcionais_Jornada_v1.1.pdf`
- `Documentos/Requisitos/04_Requisitos_Tecnicos_Jornada_v1.1.docx`
- `Documentos/Requisitos/04_Requisitos_Tecnicos_Jornada_v1.1.pdf`
- `Documentos/Requisitos/05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.0.docx`
- `Documentos/Requisitos/05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.0.pdf`
- `Documentos/Requisitos/05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.1.docx`
- `Documentos/Requisitos/05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.1.pdf`
- `Documentos/Resumo_Executivo_Especificacao_Tecnica_Jornada_v1.1.docx`
- `Documentos/Resumo_Executivo_v3.82.pdf`
