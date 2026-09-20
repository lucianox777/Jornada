# Anexo - Modelo Físico da Jornada do Cidadão v1.40

**Data:** 12/09/2026  
**Status:** CANDIDATO À CONSOLIDAÇÃO v5.00  
**Tecnologia relacional normativa:** Microsoft SQL Server  
**SolutionSchema alvo:** 3.70

## 1. Finalidade e notação

Este anexo descreve o inventário físico relacional da Jornada e a forma canônica de instalação do schema operacional. O modelo físico/DER é uma visão auxiliar de banco de dados e **não é classificado como UML**.

A documentação de entrega destinada à leitura deve ser publicada em **DOCX e PDF**, com os diagramas UML incorporados visualmente. O diagrama de classes da Identidade/Linkage e o diagrama de atividade da resolução de identidade fazem parte do conteúdo documental, sem exigir PlantUML, Mermaid ou software específico do leitor. Fontes técnicas auxiliares de geração de figura não são o artefato documental de entrega.

Esta revisão sincroniza a fonte textual corrente com o schema medido após a internalização da referência versionada de frequências de nomes. Os binários DOCX/PDF permanecem artefatos derivados e devem ser regenerados pelo processo documental antes de uma nova publicação de entrega; a atualização deste Markdown, isoladamente, não declara que esses binários já foram regenerados.

## 2. Definição canônica do schema

A instalação nova do SQL Server deve utilizar `Solution/database/Jornada_Fase1_v3.70.sql` como fonte canônica editável. Esse ponto de entrada aplica, em ordem determinística, o baseline histórico, a persistência progressiva, as estruturas de composição, a âncora CPF, as projeções/rulesets de blocking e as migrações versionadas da referência de frequências de nomes, e somente promove `Jornada.SolutionSchema=3.70` depois de verificar a existência de todos os objetos obrigatórios.

Para entrega a DBA ou ferramenta de deploy, `Solution/scripts/materialize-sql-installer.py` materializa deterministicamente a composição canônica em um único arquivo SQL Server autocontido, sem diretivas `:r`. O arquivo materializado não constitui uma segunda definição de schema: é um artefato gerado a partir da fonte canônica.

Os scripts em `Solution/database/migrations/` permanecem preservados como histórico e mecanismo de upgrade/reentrada. No upgrade que exige identidade progressiva, o backfill volumoso reutiliza o componente paginado/reentrante `ProgressiveIdentityOriginStore.BackfillPageAsync`, evitando uma segunda implementação T-SQL concorrente. O cutover permanece fail-closed e só prossegue após a verificação de completude do backfill.

`GET /health/ready` exige Base Normativa 3.62, SolutionSchema 3.70 e os objetos operacionais essenciais da identidade progressiva, composição e Linkage. Uma instalação parcial não pode ser declarada pronta.

A candidata v5.00 adota **Microsoft SQL Server como único runtime relacional suportado**. O baseline independente de ambiente permanece SQL Server 2022 exercitado em DEV/CI. Evidências anteriores em SQL Database in Microsoft Fabric são histórico de compatibilidade e não definem alvo operacional, DDL alternativo ou gate de release.

## 3. Inventário medido

O inventário é derivado automaticamente por `Solution/scripts/schema-inventory.py` e publicado como evidência pelo workflow `jornada-schema-inventory`.

A execução do PR #377, run `35533662679`, mediu o schema candidato com o ledger canônico incluído:

- tabelas no `Jornada_Fase1.sql` legado: **53**;
- tabelas próprias do núcleo `Jornada_Identidade_Progressiva.sql`: **2**;
- tabelas distintas introduzidas pelos scripts de migração: **23**;
- total distinto do schema operacional consolidado: **78 tabelas**;
- tabelas do schema atual que não pertencem ao baseline legado de 53: **25**.

Essa medição substitui as contagens históricas 53, 64, 66, 69 e a estimativa intermediária 70. O inventário corrente é uma propriedade derivada do manifesto/código e deve ser regenerado quando houver mudança estrutural.

## 4. Tabelas fora do baseline legado

As 25 tabelas fora do baseline legado são:

1. `auditoria.decisao_identidade_evento`
2. `controle.runtime_componente`
3. `identidade.blocking_chave`
4. `identidade.composicao_aplicacao`
5. `identidade.composicao_historico_aplicado`
6. `identidade.composicao_plano`
7. `identidade.composicao_publicacao`
8. `identidade.composicao_recomposicao_plano`
9. `identidade.composicao_uuid_reserva`
10. `identidade.cpf_ancora`
11. `identidade.linkage_quality_estimate`
12. `identidade.linkage_ruleset`
13. `identidade.linkage_ruleset_passe`
14. `identidade.linkage_ruleset_passe_campo`
15. `identidade.pessoa_origem_progressiva`
16. `identidade.pessoa_origem_progressiva_evento`
17. `jornada.schema_migration`
18. `ref.base_pessoa_origem`
19. `ref.frequencia_nome`
20. `ref.frequencia_nome_cobertura`
21. `ref.frequencia_nome_versao`
22. `ref.sistema_origem_base_pessoa`
23. `ref.tipo_identificador_pessoa`
24. `silver.pessoa_identificador_observacao`
25. `silver.pessoa_origem_sistema`

## 5. Inventário completo - 78 tabelas

### auditoria
- `auditoria.decisao_identidade_evento`

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
- `controle.runtime_componente`

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
- `identidade.linkage_quality_estimate`
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

### jornada
- `jornada.schema_migration`

### qualidade
- `qualidade.avaliacao_possibilidade`
- `qualidade.divergencia_gestor`
- `qualidade.possibilidade_implementacao`
- `qualidade.qc_registro_implementacao`
- `qualidade.qc_registro_resultado`

### ref
- `ref.atributo_transversal`
- `ref.base_pessoa_origem`
- `ref.distrito`
- `ref.frequencia_nome`
- `ref.frequencia_nome_cobertura`
- `ref.frequencia_nome_versao`
- `ref.gestor`
- `ref.gestor_pessoa_versao`
- `ref.sistema_origem`
- `ref.sistema_origem_base_pessoa`
- `ref.subprefeitura`
- `ref.tipo_identificador_pessoa`
- `ref.tipo_registro`
- `ref.tipo_registro_versao`

### serving
- `serving.registro_integrado`

### silver
- `silver.pessoa_atributo_observacao`
- `silver.pessoa_campo_verificacao_observacao`
- `silver.pessoa_identificador_observacao`
- `silver.pessoa_observacao`
- `silver.pessoa_origem`
- `silver.pessoa_origem_sistema`
- `silver.referencia_territorial_observacao`
- `silver.registro_observacao`
- `silver.registro_origem`

## 6. Referência versionada de frequências de nomes e projeção Gold

A referência populacional de nomes é interna ao banco operacional para permitir calibração/replay reproduzíveis sem depender do estado corrente de uma fonte externa.

- `ref.frequencia_nome_versao` identifica uma edição imutável da referência, com ciclo de publicação e hash de conteúdo.
- `ref.frequencia_nome` armazena as frequências observadas/publicadas por versão e dimensões disponíveis. Ausência ou supressão na fonte não deve ser convertida automaticamente em frequência zero.
- `ref.frequencia_nome_cobertura` registra a cobertura/proveniência do carregamento, separando completude observada de inferências estatísticas.
- `identidade.modelo_linkage` e `identidade.linkage_run` preservam a versão de referência usada pelo modelo/execução, permitindo replay mesmo após a ativação de uma versão posterior.

`gold.pessoa` não materializa uma frequência populacional única, pois esse valor depende da versão de referência. Em vez disso, mantém a chave semântica estável para consulta da referência versionada:

- `nome_publicacao_normalizado`;
- `nome_publicacao_metodo_versao`;
- `nome_publicacao_normalizacao_versao`.

A chave é produzida a partir da normalização canônica já persistida na Silver e representa somente a semântica de **nome** publicada e implementada. Não existe, nesta camada, decomposição posicional nem inferência de `SOBRENOME` a partir de `nome_completo`.

Também não há índice novo sobre `nome_publicacao_normalizado` apenas por sua existência: criação de índice deve ser sustentada por medição de cardinalidade/seletividade, custo de escrita e ganho nos consumidores reais de blocking/lookup.

## 6.1. Proveniência da revisão governada de conflitos de Linkage

A fila institucional continua materializada em `qualidade.divergencia_gestor`; não é criada uma segunda tabela de revisão. A coluna anulável `linkage_resultado_id` referencia `identidade.linkage_resultado` e é preenchida somente para divergências originadas de conflito probabilístico publicado. Assim, modelo, run, candidatos, scores e margem permanecem na evidência imutável de Linkage em vez de serem copiados para a fila.

O índice filtrado `UX_divergencia_gestor_linkage_aberta` impede mais de uma divergência probabilística aberta para a mesma observação. A procedure `qualidade.sp_registrar_conflitos_linkage_publicados` opera dentro da transação de publicação do run e atualiza a proveniência em replay; conflitos cobertos por precedência determinística/governada não são duplicados. A view interna `qualidade.v_divergencia_linkage_contexto` reúne a fila e a evidência probabilística para auditoria restrita, sem alterar o contrato HTTP público.

A evolução de revisão governada de Linkage adiciona coluna, FK, índice, procedure e view sem acrescentar tabela. Separadamente, o ledger canônico de autoria dos atos governados acrescenta `auditoria.decisao_identidade_evento`, elevando o inventário corrente para 70 tabelas.

## 6.2. Ledger canônico de decisão de identidade

`auditoria.decisao_identidade_evento` é append-only e referencia a correção, o caso governado ou a divergência que materializou o ato. `operacao_id` é gerado pelo SQL Server; a autoria é a credencial `GESTOR` autenticada e validada contra o Gestor do objeto. `correlation_id` permanece contexto técnico, não identidade do decisor.

A API grava o evento antes do commit da mesma transação. Falha no ledger reverte a mutação de identidade. `controle.api_evento` continua sendo telemetria/auditoria HTTP e não concorre como fonte de verdade da decisão governada.

## 7. Regras de evolução

1. Microsoft SQL Server permanece a tecnologia relacional normativa e o baseline independente de ambiente do contrato relacional.
2. Toda instalação nova deve partir do ponto canônico v3.70 ou sucessor.
3. O marcador `Jornada.SolutionSchema` só pode ser promovido após verificação de completude.
4. O inventário de tabelas deve ser gerado por script, não mantido apenas por contagem manual em anexos.
5. PostgreSQL não integra o runtime relacional da candidata v5.00; a implementação anterior permanece apenas no histórico Git para eventual projeto independente.
6. **SQL Database in Microsoft Fabric não é alvo operacional nem gate desta candidata; Lakehouse e SQL Analytics Endpoint permanecem analíticos/compatibilidade e não são fonte de verdade operacional implícita.**
7. DER/modelo físico é auxiliar; diagramas normativos de estrutura/fluxo devem usar UML.
8. Os artefatos de leitura/entrega dos diagramas devem ser DOCX/PDF com as figuras incorporadas, sem exigir formatos especializados do destinatário.
9. Uma referência estatística externa incorporada à Jornada deve ser versionada e preservada; replay de modelo/run não pode depender da referência que estiver ativa no momento da reexecução.
10. Campos de residência devem usar semântica explícita de residência quando esse for realmente o conceito. `referencia_territorial` permanece um conceito mais amplo e não deve ser renomeado automaticamente para residência.
