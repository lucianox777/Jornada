-- DT-06: registro imutável da baseline nova, commit-fonte 42470cc1aa2166435e6776948a84b4baf824c0d7.
-- NÃO executar isoladamente nem editar checksum de migração já aplicada.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF OBJECT_ID(N'jornada.schema_migration',N'U') IS NULL
    THROW 51360, 'DT06: ledger ausente; instalar o wrapper antes.', 1;
IF COALESCE(CONVERT(nvarchar(32),(SELECT value FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.SolutionSchema')),N'') <> N'3.70'
    THROW 51361, 'DT06: wrapper não concluiu o schema 3.70.', 1;
DECLARE @expected TABLE(migration_name NVARCHAR(260) NOT NULL PRIMARY KEY, sha256 CHAR(64) NOT NULL);
INSERT @expected(migration_name,sha256) VALUES
    (N'migrations/20260916_Schema_Migration_Ledger.sql', '0805a4b3c8e5be7762b0a3d2a8d3b96be294daab2009c8381e3a735f4a53168b'),
    (N'Jornada_Identidade_Progressiva.sql', 'f388f29274c4511cbf521ab2492ee5db827b8c330871c13a627c7bce90e539de'),
    (N'migrations/20260907_Cpf_Ancora.sql', 'f103f414a3218f8b9478ea6a160411c85a0141c1260ed449e39753b6fa9c9a43'),
    (N'migrations/20260908_Identidade_Composicao_Ledger.sql', 'ee10532b12f0da7220c70bebfc2c15a262298800285220bcb2f9b78652335d09'),
    (N'migrations/20260908_Identidade_Progressiva_Processor.sql', 'd26d18cde63c9a36e51d07bf789f6643746ecac2b7b451cde5be60c387a7f856'),
    (N'migrations/20260908_Identidade_Progressiva_Serving.sql', 'baaa138c9bbf6c2c27f35f1d85cddf6c1f40c7e503d1f063faa877050410e030'),
    (N'migrations/20260909_Identidade_Composicao_Aplicacao.sql', '0e37ba16ffd1cf20135827a938beb8e874d4ae1bb1497137961fc5e0eb757477'),
    (N'migrations/20260909_Identidade_Composicao_Recomposicao_Plano.sql', 'c74dd6e06b1f3c6faaa4ac4ff21563681cb781f326f3ecb377da1a5fd633a39e'),
    (N'migrations/20260909_Identidade_Composicao_Publicacao.sql', '7fceee46528de9d13b7a366ce49d117f02c1a62f5d8176e6e76bac8ab3c5470e'),
    (N'migrations/20260909_Identidade_Composicao_Historico_Serving.sql', '854a2d5dc9396a8ba25a8f47af2381f4592019fbe6ac7fa49a7d97ce0803c14f'),
    (N'migrations/20260909_Identidade_Composicao_Referencia_Lock.sql', '9c8e05cc2f6e806ef0e0c8daed248ae0b31bc8524a85a98056aa4d45a05a32d9'),
    (N'migrations/20260910_Linkage_Blocking_Chave.sql', '2da3a69897eef3ad77c46a349da6bf315930cd9cb6959d679edb5f6e4748c9e9'),
    (N'migrations/20260910_Linkage_RuleSet_Passes.sql', 'eef51b6b990965ce0a7fe34c0939a825e641bf58f3967b49980a18fcb2928be4'),
    (N'migrations/20260911_Linkage_Blocking_Projection_Contract.sql', 'cab5344380af425c9a3c2380e514d9307af1aa13cce0f2310eab4d72a2f1d1f2'),
    (N'migrations/20260912_Nome_Mae_Anulavel.sql', 'd55ae7dc28c0f752d88d8f6a78563d041801b1e6488e3248adc6bf56a96ea60e'),
    (N'migrations/20260912_Frequencia_Nomes_Referencia.sql', 'a6a8c1cf81e20fd8b4677564e531ae9ab658aae17d7badcf6b75906a9ab3f7d4'),
    (N'migrations/20260912_Frequencia_Nomes_Cobertura.sql', 'b7bab91162f960580a29400d3edebd7b0f982689ebfa158c3c5393d3946a2a6d'),
    (N'migrations/20260912_Gold_Nome_Publicacao.sql', 'a2ff06bffea881d999b06dd2a53bb9cf121eb5d5ed0753a9ebd30f7fa1981f64'),
    (N'migrations/20260912_Linkage_Run_Frequencia_Nome_Proveniencia.sql', 'c9acf2fe2941fe804da69b5b5341acc29616e27f09328182d97871fdfd0022e0'),
    (N'migrations/20260913_Base_Pessoa_Origem.sql', '88485a69bc244ef868821a2c68f351c1d4e38ccfb8e5608b090eaad2f735ab9b'),
    (N'migrations/20260913_Pessoa_Identificadores_Multiplos.sql', '62b380ffaa98d315928d8dbf213cb570104807eb3a9f107b2dc2a0cacf765293'),
    (N'migrations/20260913_Pessoa_Observacao_Sem_Identificador.sql', 'c2416d08286c9de6ec0b6c90063fd24c03db4cdb47c59f069ebb5954af517804'),
    (N'migrations/20260919_Fato_Referencia_Pessoa_Entrega.sql', 'b354f875569a68c4b0fa5b84fb8d624c2008df6df703e3fb39e36c3ec9816eb8'),
    (N'migrations/20260919_Pessoa_Origem_Runtime_V4_Cutover.sql', 'e913ca32ab9371a06d7831b7525988dee29f68c4552b69b1e4c380b4bc4c8593'),
    (N'migrations/20260913_BI_Qualidade_Resolucao.sql', '0a5d40c63df04c52f4ad8b426d0fc436d4eb2e6435f6fdb14ff6c7801e08e440'),
    (N'migrations/20260913_BI_Qualidade_Resolucao_Gestor_Real.sql', '2502bbd85900e9f374151771eed8a87ee945182ae26333480d3d0b512b2374e1'),
    (N'migrations/20260913_BI_Qualidade_Resolucao_Estrato_Cpf.sql', '859dae7704498d60339e8b04759865f4bbf17a9a2c7d4f21aa9dfa90b6c68ce0'),
    (N'migrations/20260914_Operational_Monitor.sql', '3a96dbc858ca38e570ff42d7861f03c72b1f2221571efeeaf28e7d2a1b2110bb'),
    (N'migrations/20260915_Linkage_LogOdds_Margin.sql', '6c59e8bcb229fbdca39bf67ad5857e2a737ffe7c2560d8cfd3fb4ec4566cb884'),
    (N'migrations/20260915_Linkage_Model_Promotion_Contract.sql', 'e43fd60ae4dd774601338e83f08c09cc896703a77bec5ba44dcbf401b768512f'),
    (N'migrations/20260916_Linkage_U_Support_Reachability.sql', '2b60d2aa3c2a06ba09da789be9616dda46ff7619053fbf026f49908f8d93f36b'),
    (N'migrations/20260917_Linkage_Llr_Monotonicity.sql', 'b1c151bbdc450afeac8caf825ee7bf00b530d66da5d57199e90ef84d98ba3076'),
    (N'migrations/20260919_Linkage_Publicacao_Progressiva.sql', '27738da1ed24372221333fa37e2911df178c8c31e09dfbbecb7d58dc31d6efd3'),
    (N'migrations/20260919_Gold_Pessoa_Progressiva.sql', '54e9e5d73fb3cf5752402dc8a6ec3fb0513b477f20411e7d9217226a5599fdb7'),
    (N'migrations/20260920_Linkage_Llr_Monotonicity_Tolerance.sql', 'f92622022b3d08d81b3c9d01363c2e329c7a4e91112d395f3fabc9bddd1e47bc'),
    (N'migrations/20260920_Linkage_Conflito_Revisao_Governada.sql', 'a254bd09db1d8c18f731541e37677bf6ba753baf0ea2c3d34efe7b2a4d53e2b9'),
    (N'migrations/20260920_Pessoa_Nome_Referencia_Serving.sql', '56e5d33b6dc47fd37909c43f9093e55e70091eff37ffd1756923635a0e201420'),
    (N'migrations/20260920_Identidade_Decisao_Ledger.sql', 'b2a0961d9c5d39351e32eccc3eba555c07c107aba8b044789e0db43aeca4636a'),
    (N'migrations/20260921_Identidade_Decisao_Evidencia_Estruturada.sql', 'eb833008fc44c7512a98d4c04debf4b19a92f973747595cd0afb934ae26227cd'),
    (N'migrations/20260921_Identidade_Confirmacao_Simplificada.sql', '339cb29e1e2ce7cdc34ea04c71013f87448e4fcb74021da5e79bec9ab6c77043'),
    (N'migrations/20260921_Nis_Rg_Identificadores_Secundarios.sql', '3ab81304fea9d991a8b9489d45c8d184378d8323902888922973d203ee787374'),
    (N'migrations/20260921_Pessoa_V5_Contrato_371.sql', '53f631ce16e19d80822c74f137a14b2bd56cdb73fcfd3478381793b3816f3136'),
    (N'migrations/20260920_Linkage_Model_Promotion_Ledger.sql', 'eda62b1fc33ea94d95d7c2ba3d26e08a47b74d19656024b0bee8b358cd01cf85'),
    (N'migrations/20260920_Linkage_Implementation_Conference_Evidence.sql', '3272eae6303dc224275ca2d83e6e7180ca7a341b0c328d63b1f65aee55987fe0'),
    (N'migrations/20260920_Linkage_Conference_Command_Governance.sql', 'd6e75f14fce8416a55afabe4b59ad7fdb9d324f321d216b4dafc2e27a0309391'),
    (N'migrations/20260922_Linkage_Synthetic_Evaluation_Evidence.sql', 'cecc7c2066beaa1180c2c0d21b4896b54b472e9ffa46f077cf99e58b80803c7d'),
    (N'migrations/20260922_Linkage_Synthetic_Evaluation_Retention.sql', '6c3255b7e3180b7fb418e05a06f19c2140d31d7560aadba29569c19b3efd7fec'),
    (N'migrations/20260922_Processor_Lease_Heartbeat_Isolation.sql', 'ccf8d6cc67472467227c5cd30c2a1ef650fc8217442b8537928ae9ca6905af8d'),
    (N'migrations/20260926_Linkage_Run_Incremental_Metrics_371.sql', 'bec9f8c7d5502cf443f63ce959a7aca804e2cba5c54e795e70a1e3774b9b448a'),
    (N'migrations/20260926_Ibge_Nominal_U_Derived_371.sql', '454b6a3494be950012c0ee7b20842fa8bbb2e46239b1af89bd9ef094d7a10ae9'),
    (N'migrations/20260910_Schema_Consolidation_370.sql', 'a78f1b8aa392e77ee172dcafdb5e0faba93ed08694c434db6698bb8db55ad13b');
IF (SELECT COUNT(*) FROM @expected) <> 51
    THROW 51362, 'DT06: quantidade congelada divergente.', 1;
IF EXISTS(SELECT 1 FROM jornada.schema_migration)
    THROW 51363, 'DT06: ledger não vazio; use o fluxo de upgrade, nunca baseline.', 1;
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT jornada.schema_migration(migration_name,sha256)
      SELECT migration_name,sha256 FROM @expected;
    IF @@ROWCOUNT <> 51
        THROW 51364, 'DT06: ledger não gravado integralmente.', 1;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
