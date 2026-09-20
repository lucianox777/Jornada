-- Jornada do Cidadão - Fase 1 - baseline operacional consolidado v3.70
-- GERADO de database/migrations/manifest.txt por scripts/schema-manifest.py.
-- Não editar a lista de includes manualmente; altere o manifesto e regenere.
-- Microsoft SQL Server é a tecnologia relacional normativa.

:on error exit
:r database/Jornada_Fase1.sql
:r database/migrations/20260916_Schema_Migration_Ledger.sql
:r database/Jornada_Identidade_Progressiva.sql
:r database/migrations/20260907_Cpf_Ancora.sql
:r database/migrations/20260908_Identidade_Composicao_Ledger.sql
:r database/migrations/20260908_Identidade_Progressiva_Processor.sql
:r database/migrations/20260908_Identidade_Progressiva_Serving.sql
:r database/migrations/20260909_Identidade_Composicao_Aplicacao.sql
:r database/migrations/20260909_Identidade_Composicao_Recomposicao_Plano.sql
:r database/migrations/20260909_Identidade_Composicao_Publicacao.sql
:r database/migrations/20260909_Identidade_Composicao_Historico_Serving.sql
:r database/migrations/20260909_Identidade_Composicao_Referencia_Lock.sql
:r database/migrations/20260910_Linkage_Blocking_Chave.sql
:r database/migrations/20260910_Linkage_RuleSet_Passes.sql
:r database/migrations/20260911_Linkage_Blocking_Projection_Contract.sql
:r database/migrations/20260912_Nome_Mae_Anulavel.sql
:r database/migrations/20260912_Frequencia_Nomes_Referencia.sql
:r database/migrations/20260912_Frequencia_Nomes_Cobertura.sql
:r database/migrations/20260912_Gold_Nome_Publicacao.sql
:r database/migrations/20260912_Linkage_Run_Frequencia_Nome_Proveniencia.sql
:r database/migrations/20260913_Base_Pessoa_Origem.sql
:r database/migrations/20260913_Pessoa_Identificadores_Multiplos.sql
:r database/migrations/20260913_Pessoa_Observacao_Sem_Identificador.sql
:r database/migrations/20260919_Fato_Referencia_Pessoa_Entrega.sql
:r database/migrations/20260919_Pessoa_Origem_Runtime_V4_Cutover.sql
:r database/migrations/20260913_BI_Qualidade_Resolucao.sql
:r database/migrations/20260913_BI_Qualidade_Resolucao_Gestor_Real.sql
:r database/migrations/20260913_BI_Qualidade_Resolucao_Estrato_Cpf.sql
:r database/migrations/20260914_Operational_Monitor.sql
:r database/migrations/20260915_Linkage_LogOdds_Margin.sql
:r database/migrations/20260915_Linkage_Model_Promotion_Contract.sql
:r database/migrations/20260916_Linkage_U_Support_Reachability.sql
:r database/migrations/20260917_Linkage_Llr_Monotonicity.sql
:r database/migrations/20260919_Linkage_Publicacao_Progressiva.sql
:r database/migrations/20260919_Gold_Pessoa_Progressiva.sql
:r database/migrations/20260920_Linkage_Llr_Monotonicity_Tolerance.sql
:r database/migrations/20260920_Linkage_Conflito_Revisao_Governada.sql
:r database/migrations/20260910_Schema_Consolidation_370.sql
