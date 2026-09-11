-- Jornada do Cidadão - Fase 1 - baseline operacional consolidado v3.70
-- Executar com sqlcmd a partir da pasta Solution.
-- Microsoft SQL Server é a tecnologia relacional normativa.
-- Este arquivo é o ponto único de instalação nova; scripts em database/migrations permanecem como histórico/reentrada de upgrade.

:on error exit
:r database/Jornada_Fase1.sql
:r database/Jornada_Identidade_Progressiva.sql
:r database/migrations/20260908_Identidade_Composicao_Ledger.sql
:r database/migrations/20260909_Identidade_Composicao_Referencia_Lock.sql
:r database/migrations/20260909_Identidade_Composicao_Aplicacao.sql
:r database/migrations/20260909_Identidade_Composicao_Recomposicao_Plano.sql
:r database/migrations/20260909_Identidade_Composicao_Publicacao.sql
:r database/migrations/20260909_Identidade_Composicao_Historico_Serving.sql
:r database/migrations/20260908_Identidade_Progressiva_Processor.sql
:r database/migrations/20260908_Identidade_Progressiva_Serving.sql
:r database/migrations/20260910_Linkage_Blocking_Chave.sql
:r database/migrations/20260910_Linkage_RuleSet_Passes.sql
:r database/migrations/20260911_Linkage_Blocking_Projection_Contract.sql

-- CPF âncora é aplicado por último na instalação sem seed; em DEV o bootstrap reaplica após o seed
-- para reservar CPFs históricos. A migração é idempotente.
:r database/migrations/20260907_Cpf_Ancora.sql

:r database/migrations/20260910_Schema_Consolidation_370.sql
