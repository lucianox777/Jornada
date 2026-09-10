# Anexo - Modelo Físico da Jornada do Cidadão v1.40

**Data:** 10/09/2026  
**Status:** CANDIDATO À CONSOLIDAÇÃO v5.00  
**Tecnologia relacional normativa:** Microsoft SQL Server  
**SolutionSchema alvo:** 3.70

## 1. Finalidade e notação

Este anexo descreve o inventário físico relacional da Jornada e a forma canônica de instalação do schema operacional. O modelo físico/DER é uma visão auxiliar de banco de dados e **não é classificado como UML**.

A documentação de entrega destinada à leitura deve ser publicada em **DOCX e PDF**, com os diagramas UML incorporados visualmente. O diagrama de classes da Identidade/Linkage e o diagrama de atividade da resolução de identidade fazem parte do conteúdo documental, sem exigir PlantUML, Mermaid ou software específico do leitor. Fontes técnicas auxiliares de geração de figura não são o artefato documental de entrega.

## 2. Definição canônica do schema

A instalação nova do SQL Server deve utilizar `Solution/database/Jornada_Fase1_v3.70.sql` como fonte canônica editável. Esse ponto de entrada aplica, em ordem determinística, o baseline histórico, a persistência progressiva, as estruturas de composição, a âncora CPF e as projeções/rulesets de blocking, e somente promove `Jornada.SolutionSchema=3.70` depois de verificar a existência de todos os objetos obrigatórios.

Para entrega a DBA ou ferramenta de deploy, `Solution/scripts/materialize-sql-installer.py` materializa deterministicamente a composição canônica em um único arquivo SQL Server autocontido, sem diretivas `:r`. O arquivo materializado não constitui uma segunda definição de schema: é um artefato gerado a partir da fonte canônica.

Os scripts em `Solution/database/migrations/` permanecem preservados como histórico e mecanismo de upgrade/reentrada. No upgrade que exige identidade progressiva, o backfill volumoso reutiliza o componente paginado/reentrante `ProgressiveIdentityOriginStore.BackfillPageAsync`, evitando uma segunda implementação T-SQL concorrente. O cutover permanece fail-closed e só prossegue após a verificação de completude do backfill.

`GET /health/ready` exige Base Normativa 3.62, SolutionSchema 3.70 e os objetos operacionais essenciais da identidade progressiva, composição e Linkage. Uma instalação parcial não pode ser declarada pronta.

## 3. Inventário medido

O inventário é derivado automaticamente por `Solution/scripts/schema-inventory.py` e publicado como evidência pelo workflow `jornada-schema-inventory`.

Resultado medido na consolidação de 10/09/2026:

- tabelas no `Jornada_Fase1.sql` legado: **53**;
- tabelas próprias do núcleo `Jornada_Identidade_Progressiva.sql`: **2**;
- tabelas distintas introduzidas pelos scripts de migração de schema: **11**;
- total distinto do schema operacional consolidado: **66 tabelas**;
- tabelas do schema atual que não pertencem ao baseline legado de 53: **13**.

Portanto, a contagem histórica 53/53 não representa o schema corrente. A contagem anterior de 64 também estava incompleta: o inventário automatizado encontrou 66 tabelas distintas.

## 4. Tabelas fora do baseline legado

As 13 tabelas adicionais são:

1. `identidade.blocking_chave`
2. `identidade.composicao_aplicacao`
3. `identidade.composicao_historico_aplicado`
4. `identidade.composicao_plano`
5. `identidade.composicao_publicacao`
6. `identidade.composicao_recomposicao_plano`
7. `identidade.composicao_uuid_reserva`
8. `identidade.cpf_ancora`
9. `identidade.linkage_ruleset`
10. `identidade.linkage_ruleset_passe`
11. `identidade.linkage_ruleset_passe_campo`
12. `identidade.pessoa_origem_progressiva`
13. `identidade.pessoa_origem_progressiva_evento`

## 5. Inventário completo - 66 tabelas

### bronze
- `bronze.entrega_arquivo`

### controle
- `controle.api_evento`
- `controle.api_evento_pessoa`
- `controle.bronze_manutencao_ciclo`
- `controle.bronze_manutencao_estado`
- `controle.credencial_api`
- `controle.entrega_retencao_ciclo`
- `controle.modo_carga_inicial`
- `controle.restricao_projecao_jornada_versao`

### gold
- `gold.beneficio_concedido`
- `gold.pessoa`
- `gold.pessoa_atributo`
- `gold.servico_prestado`

### identidade
- `identidade.blocking_chave`
- `identidade.caso_conflito_identidade`
- `identidade.caso_conflito_identidade_item`
- `identidade.composicao_aplicacao`
- `identidade.composicao_historico_aplicado`
- `identidade.composicao_plano`
- `identidade.composicao_publicacao`
- `identidade.composicao_recomposicao_plano`
- `identidade.composicao_uuid_reserva`
- `identidade.correcao_identidade`
- `identidade.correcao_identidade_item`
- `identidade.cpf_ancora`
- `identidade.estatistica_linkage`
- `identidade.frequencia_linkage`
- `identidade.identity_map`
- `identidade.identity_map_estado_evento`
- `identidade.linkage_resultado`
- `identidade.linkage_ruleset`
- `identidade.linkage_ruleset_passe`
- `identidade.linkage_ruleset_passe_campo`
- `identidade.linkage_run`
- `identidade.linkage_run_item`
- `identidade.modelo_linkage`
- `identidade.parametro_linkage`
- `identidade.pessoa`
- `identidade.pessoa_origem_progressiva`
- `identidade.pessoa_origem_progressiva_evento`
- `identidade.vinculo_fonte`

### ingestao
- `ingestao.entrega`
- `ingestao.item_processado`
- `ingestao.item_processado_resumo`
- `ingestao.lote`

### qualidade
- `qualidade.avaliacao_possibilidade`
- `qualidade.divergencia_gestor`
- `qualidade.possibilidade_implementacao`
- `qualidade.qc_registro_implementacao`
- `qualidade.qc_registro_resultado`

### ref
- `ref.atributo_transversal`
- `ref.distrito`
- `ref.gestor`
- `ref.gestor_pessoa_versao`
- `ref.sistema_origem`
- `ref.subprefeitura`
- `ref.tipo_registro`
- `ref.tipo_registro_versao`

### serving
- `serving.registro_integrado`

### silver
- `silver.pessoa_atributo_observacao`
- `silver.pessoa_campo_verificacao_observacao`
- `silver.pessoa_observacao`
- `silver.pessoa_origem`
- `silver.referencia_territorial_observacao`
- `silver.registro_observacao`
- `silver.registro_origem`

## 6. Regras de evolução

1. Microsoft SQL Server permanece a tecnologia relacional normativa.
2. Toda instalação nova deve partir do ponto canônico v3.70 ou sucessor.
3. O marcador `Jornada.SolutionSchema` só pode ser promovido após verificação de completude.
4. O inventário de tabelas deve ser gerado por script, não mantido apenas por contagem manual em anexos.
5. PostgreSQL pode possuir implementações paralelas em escopos explicitamente suportados, mas não redefine o baseline relacional normativo.
6. Fabric permanece no escopo analítico/compatibilidade definido pela arquitetura.
7. DER/modelo físico é auxiliar; diagramas normativos de estrutura/fluxo devem usar UML.
8. Os artefatos de leitura/entrega dos diagramas devem ser DOCX/PDF com as figuras incorporadas, sem exigir formatos especializados do destinatário.
