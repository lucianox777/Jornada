using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
public sealed class SeedDatabaseTests
{
    [Test]
    public async Task Canonical_seed_populates_reference_and_serving_data()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION para executar testes SQL Server.");

        var csb = new SqlConnectionStringBuilder(connectionString);
        var db = csb.InitialCatalog ?? string.Empty;
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase) && !db.Contains("dev", StringComparison.OrdinalIgnoreCase) && !db.Contains("local", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Por segurança, o banco de integração deve conter 'Test', 'Dev' ou 'Local' no nome.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));

        var gestores = await ScalarAsync(connection, "SELECT COUNT(*) FROM ref.gestor WHERE codigo IN ('SEHAB','SMADS','SMDET','SMS')");
        var tipos = await ScalarAsync(connection, "SELECT COUNT(*) FROM ref.tipo_registro WHERE codigo IN ('AA01','AR01','POT1','CRA1','CPO1','CAS1')");
        var sistemasOrigem = await ScalarAsync(connection, "SELECT COUNT(*) FROM ref.sistema_origem WHERE codigo IN ('HABITACAO','ASSISTENCIA','TRABALHO','SAUDE')");
        var pessoasOrigem = await ScalarAsync(connection, "SELECT COUNT(*) FROM silver.pessoa_origem");
        var registrosOrigem = await ScalarAsync(connection, "SELECT COUNT(*) FROM silver.registro_origem");
        var versaoExterna = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.columns WHERE object_id IN (OBJECT_ID('silver.pessoa_observacao'),OBJECT_ID('silver.registro_observacao')) AND name IN ('versao_registro_origem','versao_pessoa_origem')");
        var fatosNaoVigentesNasViews = await ScalarAsync(connection, "SELECT (SELECT COUNT(*) FROM serving.v_bi_beneficios_concedidos WHERE status_analitico<>'VIGENTE') + (SELECT COUNT(*) FROM serving.v_bi_servicos_prestados WHERE status_analitico<>'VIGENTE')");
        var codigosInvalidos = await ScalarAsync(connection, "SELECT COUNT(*) FROM ref.tipo_registro WHERE LEN(codigo)<>4 OR codigo LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_100_BIN2");
        var beneficios = await ScalarAsync(connection, "SELECT COUNT(*) FROM serving.registro_integrado WHERE natureza='BENEFICIO'");
        var servicos = await ScalarAsync(connection, "SELECT COUNT(*) FROM serving.registro_integrado WHERE natureza='SERVICO'");
        var tipoMedida = await ScalarAsync(connection, "SELECT COUNT(*) FROM ref.tipo_registro_versao WHERE tipo_medida='MONETARIO'");
        var atributosCatalogo = await ScalarAsync(connection, "SELECT COUNT(*) FROM ref.atributo_transversal");
        var atributosGold = await ScalarAsync(connection, "SELECT COUNT(*) FROM gold.pessoa_atributo");
        var jsonGold = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.columns WHERE object_id IN (OBJECT_ID('gold.beneficio_concedido'),OBJECT_ID('gold.servico_prestado')) AND name='atributos_json'");
        var jsonSilver = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('silver.registro_observacao') AND name='dados_json'");
        var servicosBi = await ScalarAsync(connection, "SELECT COUNT(*) FROM serving.v_bi_servicos_prestados");
        var registros = await ScalarAsync(connection, "SELECT COUNT(*) FROM serving.v_registros_pessoa");
        var possibilidades = await ScalarAsync(connection, "SELECT COUNT(*) FROM serving.v_possibilidades_compativeis");
        var linkageRuns = await ScalarAsync(connection, "SELECT COUNT(*) FROM identidade.linkage_run");
        var linkageResultados = await ScalarAsync(connection, "SELECT COUNT(*) FROM identidade.linkage_resultado");
        var entregasIncompletas = await ScalarAsync(connection, "SELECT COUNT(*) FROM ingestao.v_entrega_completude WHERE entrega_completa=0");
        var servingIncompleto = await ScalarAsync(connection, "SELECT COUNT(*) FROM serving.registro_integrado WHERE entrega_completa=0");
        var entregasProcessadasInconsistentes = await ScalarAsync(connection, "SELECT COUNT(*) FROM ingestao.entrega e LEFT JOIN ingestao.v_entrega_completude c ON c.entrega_id=e.entrega_id WHERE e.status='PROCESSADA' AND COALESCE(c.entrega_completa,0)=0");
        var tiposComSla = await ScalarAsync(connection, "SELECT COUNT(*) FROM ref.tipo_registro_versao WHERE monitorar_atraso=1 AND prazo_recebimento_dias IS NOT NULL AND marco_atraso_codigo IS NOT NULL");
        var atrasosBi = await ScalarAsync(connection, "SELECT COUNT(*) FROM serving.v_bi_atrasos WHERE status_atraso='EM_ATRASO'");
        var atrasoServico = await ScalarAsync(connection, "SELECT COUNT(*) FROM serving.v_bi_atrasos WHERE natureza='SERVICO' AND status_atraso='EM_ATRASO'");
        var atrasoBeneficio = await ScalarAsync(connection, "SELECT COUNT(*) FROM serving.v_bi_atrasos WHERE natureza='BENEFICIO' AND status_atraso='EM_ATRASO'");
        var slaInvalido = await ScalarAsync(connection, "SELECT COUNT(*) FROM ref.tipo_registro_versao WHERE (monitorar_atraso=0 AND (prazo_recebimento_dias IS NOT NULL OR marco_atraso_codigo IS NOT NULL)) OR (monitorar_atraso=1 AND (prazo_recebimento_dias IS NULL OR marco_atraso_codigo IS NULL))");
        var auditPurposeColumns = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('controle.api_evento') AND name IN('finalidade','finalidade_id','auditoria_reforcada')");
        var auditPessoas = await ScalarAsync(connection, "SELECT COUNT(*) FROM controle.api_evento_pessoa");
        var apiBiPurposeColumns = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('serving.v_bi_api') AND name IN('finalidade','auditoria_reforcada')");
        var purposeObjects = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.objects WHERE object_id IN(OBJECT_ID('ref.finalidade'),OBJECT_ID('ref.finalidade_versao'),OBJECT_ID('controle.credencial_finalidade'))");
        var credenciaisDev = await ScalarAsync(connection, "SELECT COUNT(*) FROM controle.credencial_api WHERE codigo_publico IN ('SEHAB','SMADS','SMDET','SMS','AA01','AR01','POT1','CRA1','CPO1')");
        var conferenciasDocumentais = await ScalarAsync(connection, "SELECT COUNT(*) FROM silver.pessoa_campo_verificacao_observacao");
        var referenciasDomiciliaresComGeografia = await ScalarAsync(connection, "SELECT COUNT(*) FROM silver.referencia_territorial_observacao rt JOIN silver.pessoa_atributo_observacao pa ON pa.pessoa_atributo_observacao_id=rt.pessoa_atributo_observacao_id WHERE rt.fonte_semantica='ENDERECO_RESIDENCIAL' AND rt.natureza_referencia='DOMICILIAR' AND rt.subprefeitura_id IS NOT NULL AND rt.distrito_id IS NOT NULL AND pa.atributo_codigo='ENDERECO_RESIDENCIAL'");
        var hierarquiaGeoVersionada = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.columns WHERE object_id IN(OBJECT_ID('ref.distrito'),OBJECT_ID('ref.subprefeitura')) AND name='observado_em'");
        var hierarquiaGeoConsistente = await ScalarAsync(connection, "SELECT COUNT(*) FROM silver.referencia_territorial_observacao rt JOIN ref.distrito d ON d.distrito_id=rt.distrito_id AND d.subprefeitura_id=rt.subprefeitura_id WHERE rt.distrito_id IS NOT NULL");
        var objetosGeoResidencialLegados = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.objects WHERE object_id IN (OBJECT_ID('silver.endereco_residencial_geografia_observacao'),OBJECT_ID('silver.v_pessoa_geografia_residencial'))");
        var referenciasTerritoriais = await ScalarAsync(connection, "SELECT COUNT(*) FROM silver.referencia_territorial_observacao");
        var referenciasDeclaradas = await ScalarAsync(connection, "SELECT COUNT(*) FROM silver.referencia_territorial_observacao WHERE natureza_referencia='REFERENCIA_TERRITORIAL_DECLARADA' AND fonte_semantica='REFERENCIA_TERRITORIAL'");
        var fatosComLinhagemTerritorial = await ScalarAsync(connection, "SELECT COUNT(*) FROM serving.registro_integrado WHERE referencia_territorial_observacao_id IS NOT NULL");
        var objetosTerritoriaisLegados = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.objects WHERE name IN ('logradouro_territorio','fonte_territorio_referencia','pessoa_territorio_referencia','v_pessoa_territorio_referencia_corrente','v_bi_territorio_referencia')");
        var pendenciasComEnvelhecimento = await ScalarAsync(connection, "SELECT COUNT(*) FROM serving.v_bi_pendencias_identidade WHERE dias_pendente>=0 AND faixa_envelhecimento IS NOT NULL");
        var vinculoMotivo = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('identidade.vinculo_fonte') AND name='motivo'");
        var vinculoStatusUuidConstraint = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('identidade.vinculo_fonte') AND name='ck_vinculo_status_uuid'");
        var qualidadeBeneficios = await ScalarAsync(connection, "SELECT COUNT(*) FROM serving.v_bi_qualidade_beneficios_concedidos");
        var qualidadeServicos = await ScalarAsync(connection, "SELECT COUNT(*) FROM serving.v_bi_qualidade_servicos_prestados");
        var indiceItensLote = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('ingestao.item_processado') AND name='IX_item_processado_lote_classe'");
        var defaultsRecebidoEm = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id WHERE (dc.parent_object_id=OBJECT_ID('ingestao.entrega') OR dc.parent_object_id=OBJECT_ID('bronze.entrega_arquivo')) AND c.name='recebido_em'");
        var bronzePayloadLegacy = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('bronze.entrega_arquivo') AND name='payload_zip'");
        var bronzeObjectColumns = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('bronze.entrega_arquivo') AND name IN('objeto_chave','payload_sha256','tamanho_bytes')");
        var bronzeObjectHashIndex = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('bronze.entrega_arquivo') AND name='IX_bronze_entrega_arquivo_payload_sha256'");
        var bronzeObjectHashIndexKey = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=OBJECT_ID('bronze.entrega_arquivo') AND ic.index_id=INDEXPROPERTY(OBJECT_ID('bronze.entrega_arquivo'),'IX_bronze_entrega_arquivo_payload_sha256','IndexId') AND ic.key_ordinal=1 AND c.name='payload_sha256'");
        var indiceLinkageNascimento = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('gold.pessoa') AND name='IX_gold_pessoa_linkage_nascimento'");
        var linkageHighWatermark = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('identidade.linkage_run') AND name='pessoa_observacao_id_high_watermark'");
        var linkageSnapshotColumnLegacy = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('identidade.linkage_run') AND name='pessoa_observacao_id_max_snapshot'");
        var qualidadeEnvios = await ScalarAsync(connection, "SELECT COUNT(*) FROM serving.v_bi_qualidade_envios");
        var qualidadeIdentidadeOrigem = await ScalarAsync(connection, "SELECT COUNT(*) FROM serving.v_bi_qualidade_identidade_origem");
        var cpfExpostoBi = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.columns WHERE object_id IN (OBJECT_ID('serving.v_bi_qualidade_envios'),OBJECT_ID('serving.v_bi_qualidade_identidade_origem'),OBJECT_ID('serving.v_bi_linkage'),OBJECT_ID('serving.v_bi_qualidade_pessoa')) AND name='cpf'");
        var pessoa = await ScalarAsync(connection, "SELECT COUNT(*) FROM serving.v_pessoa");
        var pessoaMunicipalLegacy = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.views WHERE object_id=OBJECT_ID('serving.v_pessoa_nucleo_municipal')");
        var pessoaV350Legacy = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.views WHERE object_id=OBJECT_ID('serving.v_pessoa_municipal')");
        var agenteHashAuditado = await ScalarAsync(connection, "SELECT COUNT(*) FROM controle.api_evento WHERE agente_cpf_hash IS NOT NULL AND DATALENGTH(agente_cpf_hash)=32 AND agente_hash_versao=1");
        var identificadoresExpostosBiApi = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('serving.v_bi_api') AND name IN('pessoa_uuid','agente_cpf_hash','credencial_id','api_evento_id')");
        var restricaoPurposeColumns = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('controle.restricao_projecao_jornada_versao') AND name IN('finalidade','finalidade_id')");
        var declaracaoLivre = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('controle.restricao_projecao_jornada_versao') AND name='declarado_por'");
        var identityMapEstado = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('identidade.identity_map') AND name IN('estado','estado_motivo','estado_em')");
        var identityMapEventos = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.objects WHERE object_id=OBJECT_ID('identidade.identity_map_estado_evento')");
        var correcaoIdentidade = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.objects WHERE object_id IN(OBJECT_ID('identidade.correcao_identidade'),OBJECT_ID('identidade.correcao_identidade_item'))");
        var casosGovernados = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.objects WHERE object_id IN(OBJECT_ID('identidade.caso_conflito_identidade'),OBJECT_ID('identidade.caso_conflito_identidade_item'),OBJECT_ID('identidade.sp_abrir_caso_conflito_identidade'),OBJECT_ID('identidade.sp_aplicar_caso_conflito_identidade'))");
        var divergenciasGestor = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.objects WHERE object_id IN(OBJECT_ID('qualidade.divergencia_gestor'),OBJECT_ID('qualidade.sp_registrar_desfecho_divergencia'),OBJECT_ID('qualidade.v_divergencia_gestor_aberta'))");
        var fatosComSujeitoDeclarado = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('gold.beneficio_concedido') AND name IN('pessoa_origem_id','sistema_origem_id','codigo_pessoa_origem','cpf_declarado','estado_atribuicao_identidade')");
        var fatosPendentesPermitidos = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('gold.beneficio_concedido') AND name='pessoa_uuid' AND is_nullable=1");
        var biFatosIdentidade = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.objects WHERE object_id=OBJECT_ID('serving.v_bi_fatos_identidade')");
        var biFatosComEstado = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.columns WHERE object_id IN(OBJECT_ID('serving.v_bi_beneficios_concedidos'),OBJECT_ID('serving.v_bi_servicos_prestados'),OBJECT_ID('serving.v_bi_registros')) AND name='estado_atribuicao_identidade'");
        var bronzeRetencaoColunas = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('bronze.entrega_arquivo') AND name IN('estado_armazenamento','expurgo_iniciado_em','expurgado_em','retencao_motivo')");
        var bronzeRetencaoObjetos = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.objects WHERE object_id IN(OBJECT_ID('controle.entrega_retencao_ciclo'),OBJECT_ID('serving.v_bi_retencao_bronze'))");
        var schemaHashes = await ScalarAsync(connection, "SELECT (SELECT COUNT(*) FROM ref.gestor_pessoa_versao WHERE status='ATIVA' AND DATALENGTH(pessoa_schema_sha256)=32) + (SELECT COUNT(*)*2 FROM ref.tipo_registro_versao WHERE status='ATIVA' AND DATALENGTH(schema_pessoa_sha256)=32 AND DATALENGTH(schema_registro_sha256)=32)");
        var casaAbrigoTipo = await ScalarAsync(connection, "SELECT COUNT(*) FROM ref.tipo_registro tr JOIN ref.tipo_registro_versao v ON v.tipo_registro_id=tr.tipo_registro_id WHERE tr.codigo='CAS1' AND tr.natureza='SERVICO' AND v.versao=1 AND v.origina_endereco_casa_abrigo_sigilosa=1");
        var casaAbrigoAtributo = await ScalarAsync(connection, "SELECT COUNT(*) FROM ref.atributo_transversal WHERE atributo_codigo='ENDERECO_CASA_ABRIGO_SIGILOSA' AND ativo=1");
        var casaAbrigoBloqueioCrossGestor = await ScalarAsync(connection, "SELECT CASE WHEN controle.fn_projecao_jornada_permitida((SELECT gestor_id FROM ref.gestor WHERE codigo='SMADS'),(SELECT gestor_id FROM ref.gestor WHERE codigo='SMS'),'ATRIBUTO_PESSOA','ENDERECO_CASA_ABRIGO_SIGILOSA')=0 THEN 1 ELSE 0 END");
        var casaAbrigoVisivelGestorResponsavel = await ScalarAsync(connection, "SELECT CASE WHEN controle.fn_projecao_jornada_permitida((SELECT gestor_id FROM ref.gestor WHERE codigo='SMADS'),(SELECT gestor_id FROM ref.gestor WHERE codigo='SMADS'),'ATRIBUTO_PESSOA','ENDERECO_CASA_ABRIGO_SIGILOSA')=1 THEN 1 ELSE 0 END");
        var cardinalidadeMultivalorada = await ScalarAsync(connection, "SELECT COUNT(*) FROM ref.atributo_transversal WHERE (atributo_codigo='TELEFONE_CONTATO' AND cardinalidade='MULTI' AND chave_instancia_codigo='TELEFONE_BR_CANONICO_V2') OR (atributo_codigo='EMAIL_CONTATO' AND cardinalidade='MULTI' AND chave_instancia_codigo='EMAIL_CANONICO_V2')");
        var colunasInstanciaAtributo = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.columns WHERE object_id IN(OBJECT_ID('silver.pessoa_atributo_observacao'),OBJECT_ID('gold.pessoa_atributo')) AND name='atributo_instancia_chave'");
        var telefonesCorrentesPessoa = await ScalarAsync(connection, "SELECT COUNT(*) FROM gold.pessoa_atributo WHERE pessoa_uuid='aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1' AND atributo_codigo='TELEFONE_CONTATO' AND vigencia_fim IS NULL");
        var indiceAtributoCorrente = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id=ic.object_id AND i.index_id=ic.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID('gold.pessoa_atributo') AND i.name='UX_gold_pessoa_atributo_corrente' AND c.name='atributo_instancia_chave'");
        var sucessaoUuid = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('identidade.pessoa') AND name='pessoa_uuid_sucessor'");
        var funcaoUuidCanonico = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.objects WHERE object_id=OBJECT_ID('identidade.fn_pessoa_uuid_canonico')");
        var biUuidExposto = await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.columns WHERE object_id IN(OBJECT_ID('serving.v_bi_beneficios_concedidos'),OBJECT_ID('serving.v_bi_servicos_prestados'),OBJECT_ID('serving.v_bi_registros'),OBJECT_ID('serving.v_bi_possibilidades'),OBJECT_ID('serving.v_bi_atrasos'),OBJECT_ID('serving.v_bi_qualidade_beneficios_concedidos'),OBJECT_ID('serving.v_bi_qualidade_servicos_prestados')) AND name='pessoa_uuid'");
        var credenciaisDevComIdDivergente = await ScalarAsync(connection, @"
SELECT COUNT(*)
FROM controle.credencial_api c
JOIN (VALUES
 ('SEHAB',CONVERT(uniqueidentifier,'11111111-1111-4111-8111-111111111111')),
 ('SMADS',CONVERT(uniqueidentifier,'11111111-1111-4111-8111-111111111112')),
 ('AA01', CONVERT(uniqueidentifier,'11111111-1111-4111-8111-111111111113')),
 ('CRA1', CONVERT(uniqueidentifier,'11111111-1111-4111-8111-111111111114')),
 ('SMS',  CONVERT(uniqueidentifier,'11111111-1111-4111-8111-111111111115')),
 ('SMDET',CONVERT(uniqueidentifier,'11111111-1111-4111-8111-111111111116')),
 ('AR01', CONVERT(uniqueidentifier,'11111111-1111-4111-8111-111111111117')),
 ('POT1', CONVERT(uniqueidentifier,'11111111-1111-4111-8111-111111111118')),
 ('CPO1', CONVERT(uniqueidentifier,'11111111-1111-4111-8111-111111111119'))
) x(codigo_publico,credencial_id) ON x.codigo_publico=c.codigo_publico
WHERE c.credencial_id<>x.credencial_id;");

        Assert.Multiple(() =>
        {
            Assert.That(gestores, Is.EqualTo(4));
            Assert.That(tipos, Is.EqualTo(6));
            Assert.That(sistemasOrigem, Is.EqualTo(4));
            Assert.That(pessoasOrigem, Is.GreaterThanOrEqualTo(10));
            Assert.That(registrosOrigem, Is.GreaterThanOrEqualTo(8));
            Assert.That(versaoExterna, Is.Zero);
            Assert.That(fatosNaoVigentesNasViews, Is.Zero);
            Assert.That(codigosInvalidos, Is.Zero);
            Assert.That(beneficios, Is.GreaterThanOrEqualTo(5));
            Assert.That(servicos, Is.GreaterThanOrEqualTo(2));
            Assert.That(tipoMedida, Is.GreaterThanOrEqualTo(3));
            Assert.That(atributosCatalogo, Is.GreaterThanOrEqualTo(4));
            Assert.That(atributosGold, Is.GreaterThanOrEqualTo(2));
            Assert.That(jsonGold, Is.Zero);
            Assert.That(jsonSilver, Is.Zero);
            Assert.That(servicosBi, Is.GreaterThanOrEqualTo(2));
            Assert.That(registros, Is.GreaterThanOrEqualTo(beneficios + servicos));
            Assert.That(possibilidades, Is.GreaterThanOrEqualTo(3));
            Assert.That(linkageRuns, Is.GreaterThanOrEqualTo(1));
            Assert.That(linkageResultados, Is.GreaterThanOrEqualTo(2));
            Assert.That(entregasIncompletas, Is.Zero);
            Assert.That(servingIncompleto, Is.Zero);
            Assert.That(entregasProcessadasInconsistentes, Is.Zero);
            Assert.That(tiposComSla, Is.EqualTo(5));
            Assert.That(atrasosBi, Is.GreaterThanOrEqualTo(2));
            Assert.That(atrasoServico, Is.GreaterThanOrEqualTo(1));
            Assert.That(atrasoBeneficio, Is.GreaterThanOrEqualTo(1));
            Assert.That(slaInvalido, Is.Zero);
            Assert.That(auditPurposeColumns, Is.Zero, "A auditoria da v3.47 não persiste finalidade declarada nem flag reforçada.");
            Assert.That(auditPessoas, Is.GreaterThanOrEqualTo(3));
            Assert.That(apiBiPurposeColumns, Is.Zero);
            Assert.That(purposeObjects, Is.Zero, "Catálogo e allowlist de finalidade foram removidos na v3.47.");
            Assert.That(credenciaisDev, Is.EqualTo(9));
            Assert.That(credenciaisDevComIdDivergente, Is.Zero);
            Assert.That(conferenciasDocumentais, Is.GreaterThanOrEqualTo(4));
            Assert.That(referenciasDomiciliaresComGeografia, Is.GreaterThanOrEqualTo(1));
            Assert.That(hierarquiaGeoVersionada, Is.EqualTo(2));
            Assert.That(hierarquiaGeoConsistente, Is.GreaterThanOrEqualTo(referenciasDomiciliaresComGeografia));
            Assert.That(referenciasTerritoriais, Is.GreaterThanOrEqualTo(referenciasDomiciliaresComGeografia));
            Assert.That(objetosGeoResidencialLegados, Is.Zero);
            Assert.That(referenciasDeclaradas, Is.GreaterThanOrEqualTo(1));
            Assert.That(fatosComLinhagemTerritorial, Is.GreaterThanOrEqualTo(1));
            Assert.That(objetosTerritoriaisLegados, Is.Zero);
            Assert.That(pendenciasComEnvelhecimento, Is.GreaterThanOrEqualTo(1));
            Assert.That(vinculoMotivo, Is.EqualTo(1), "v3.42 persiste o motivo do conflito determinístico de identidade.");
            Assert.That(vinculoStatusUuidConstraint, Is.EqualTo(1), "CONFLITO/NAO_RESOLVIDO não podem carregar pessoa_uuid.");
            Assert.That(qualidadeBeneficios, Is.GreaterThanOrEqualTo(1));
            Assert.That(qualidadeServicos, Is.GreaterThanOrEqualTo(1));
            Assert.That(indiceItensLote, Is.EqualTo(1));
            Assert.That(indiceLinkageNascimento, Is.EqualTo(1));
            Assert.That(linkageHighWatermark, Is.EqualTo(1));
            Assert.That(linkageSnapshotColumnLegacy, Is.Zero);
            Assert.That(qualidadeEnvios, Is.GreaterThanOrEqualTo(1));
            Assert.That(qualidadeIdentidadeOrigem, Is.GreaterThanOrEqualTo(1));
            Assert.That(cpfExpostoBi, Is.Zero);
            Assert.That(defaultsRecebidoEm, Is.Zero);
        Assert.That(bronzePayloadLegacy, Is.Zero, "A Bronze v3.37 não armazena o ZIP em VARBINARY(MAX).");
        Assert.That(bronzeObjectColumns, Is.EqualTo(3), "A Bronze externa exige chave, hash e tamanho no SQL.");
        Assert.That(bronzeObjectHashIndex, Is.EqualTo(1), "payload_sha256 deve estar indexado para referência/expurgo da Bronze sem exceder o limite de chave do SQL Server.");
        Assert.That(bronzeObjectHashIndexKey, Is.EqualTo(1), "payload_sha256 deve ser a chave física do índice Bronze; objeto_chave permanece apenas como confirmação/included column.");
            Assert.That(pessoa, Is.GreaterThanOrEqualTo(4));
            Assert.That(pessoaMunicipalLegacy, Is.Zero, "A view de nomenclatura anterior deve ser removida no upgrade v3.50.");
            Assert.That(pessoaV350Legacy, Is.Zero, "A view serving.v_pessoa_municipal deve ser removida no upgrade v3.51.");
            Assert.That(agenteHashAuditado, Is.GreaterThanOrEqualTo(1));
            Assert.That(identificadoresExpostosBiApi, Is.Zero);
            Assert.That(restricaoPurposeColumns, Is.Zero);
            Assert.That(declaracaoLivre, Is.Zero);
            Assert.That(identityMapEstado, Is.EqualTo(3));
            Assert.That(identityMapEventos, Is.EqualTo(1));
            Assert.That(correcaoIdentidade, Is.EqualTo(2));
            Assert.That(casosGovernados, Is.EqualTo(4), "v3.45 exige abertura e aplicação governada independentes de CPF.");
            Assert.That(divergenciasGestor, Is.EqualTo(3), "Retorno ativo de divergências deve estar materializado.");
            Assert.That(fatosComSujeitoDeclarado, Is.EqualTo(5), "Gold factual deve preservar sujeito declarado e estado de atribuição.");
            Assert.That(fatosPendentesPermitidos, Is.EqualTo(1), "pessoa_uuid é atribuição canônica anulável, não gate do fato.");
            Assert.That(biFatosIdentidade, Is.Zero, "v3.46 remove a segunda fonte de verdade agregada FatosIdentidade.");
            Assert.That(biFatosComEstado, Is.EqualTo(3), "As três views factuais devem expor o estado de atribuição diretamente.");
            Assert.That(bronzeRetencaoColunas, Is.EqualTo(4), "A retenção preserva metadados e explicita a disponibilidade do payload Bronze.");
            Assert.That(bronzeRetencaoObjetos, Is.EqualTo(2), "Retenção Bronze deve possuir ciclo auditável e view operacional.");
            Assert.That(schemaHashes, Is.GreaterThanOrEqualTo(16), "Schemas ativos do seed devem ter SHA-256 aprovado.");
            Assert.That(casaAbrigoTipo, Is.EqualTo(1), "CAS1 representa no DEV um Tipo de Serviço explicitamente habilitado a originar endereço de casa-abrigo-sigilosa.");
            Assert.That(casaAbrigoAtributo, Is.EqualTo(1));
            Assert.That(casaAbrigoBloqueioCrossGestor, Is.EqualTo(1), "O endereço de casa-abrigo-sigilosa nunca é projetado para outro Gestor.");
            Assert.That(casaAbrigoVisivelGestorResponsavel, Is.EqualTo(1), "O Gestor responsável preserva acesso ao dado que originou.");
            Assert.That(cardinalidadeMultivalorada, Is.EqualTo(2), "Telefone e e-mail admitem múltiplas instâncias correntes, com chave normalizada explícita.");
            Assert.That(colunasInstanciaAtributo, Is.EqualTo(2));
            Assert.That(telefonesCorrentesPessoa, Is.EqualTo(2), "Dois telefones comprovados distintos devem coexistir como correntes.");
            Assert.That(indiceAtributoCorrente, Is.EqualTo(1), "A unicidade corrente deve incluir a chave da instância.");
            Assert.That(sucessaoUuid, Is.EqualTo(1), "UUID fundido deve poder apontar para seu sucessor canônico.");
            Assert.That(funcaoUuidCanonico, Is.EqualTo(1));
            Assert.That(biUuidExposto, Is.Zero, "Modelo BI padrão não deve depender de pessoa_uuid em views de consumo.");
        });
    }

    [Test]
    public async Task Territorial_reference_precedence_is_explicit_then_evidence_then_recency()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION para executar testes SQL Server.");

        var csb = new SqlConnectionStringBuilder(connectionString);
        var db = csb.InitialCatalog ?? string.Empty;
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase) && !db.Contains("dev", StringComparison.OrdinalIgnoreCase) && !db.Contains("local", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Por segurança, o banco de integração deve conter 'Test', 'Dev' ou 'Local' no nome.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();

        long pessoaObservacaoId;
        long gestorId;
        using (var ids = connection.CreateCommand())
        {
            ids.Transaction = tx;
            ids.CommandText = """
                SELECT TOP(1) po.pessoa_observacao_id, po.gestor_id
                FROM silver.pessoa_observacao po
                WHERE po.codigo_pessoa_origem='CRAS001'
                ORDER BY po.pessoa_observacao_id DESC;
                """;
            using var reader = await ids.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True, "Seed deve conter CRAS001.");
            pessoaObservacaoId = reader.GetInt64(0);
            gestorId = reader.GetInt64(1);
        }

        // Mesmo com um ENDERECO_RESIDENCIAL comprovado artificialmente mais recente,
        // a referência explicitamente declarada pela fonte continua prevalecendo.
        using (var updateResidential = connection.CreateCommand())
        {
            updateResidential.Transaction = tx;
            updateResidential.CommandText = """
                UPDATE silver.pessoa_atributo_observacao
                SET atualizado_em_origem='2026-08-30T00:00:00-03:00'
                WHERE pessoa_observacao_id=@po AND atributo_codigo='ENDERECO_RESIDENCIAL';
                """;
            updateResidential.Parameters.AddWithValue("@po", pessoaObservacaoId);
            await updateResidential.ExecuteNonQueryAsync();
        }

        async Task<string> SelectedSourceRecordAsync()
        {
            using var q = connection.CreateCommand();
            q.Transaction = tx;
            q.CommandText = """
                SELECT pa.source_record_id
                FROM silver.v_pessoa_referencia_territorial v
                JOIN silver.pessoa_atributo_observacao pa ON pa.pessoa_atributo_observacao_id=v.pessoa_atributo_observacao_id
                WHERE v.pessoa_observacao_id=@po;
                """;
            q.Parameters.AddWithValue("@po", pessoaObservacaoId);
            return Convert.ToString(await q.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        }

        Assert.That(await SelectedSourceRecordAsync(), Is.EqualTo("CRAS001-REF-TERR-1"),
            "REFERENCIA_TERRITORIAL explícita deve vencer fallback DOMICILIAR, independentemente de recência do endereço cadastral.");

        async Task<long> InsertExplicitAsync(string sourceRecordId, string status, DateTimeOffset semanticDate)
        {
            long attributeId;
            using (var attr = connection.CreateCommand())
            {
                attr.Transaction = tx;
                attr.CommandText = """
                    INSERT silver.pessoa_atributo_observacao(
                        source_record_id,pessoa_observacao_id,fonte_gestor_id,atributo_codigo,valor,status_evidencia,evidencia_tipo,
                        referencia_evidencia,verificado_em,atualizado_em_origem,ingested_at)
                    OUTPUT INSERTED.pessoa_atributo_observacao_id
                    VALUES(@source,@po,@gestor,'REFERENCIA_TERRITORIAL',N'CEP=01001000|NUMERO=500',@status,'FONTE_INSTITUCIONAL',
                           @semantic,@verified,@semantic,SYSDATETIMEOFFSET());
                    """;
                attr.Parameters.AddWithValue("@source", sourceRecordId);
                attr.Parameters.AddWithValue("@po", pessoaObservacaoId);
                attr.Parameters.AddWithValue("@gestor", gestorId);
                attr.Parameters.AddWithValue("@status", status);
                attr.Parameters.AddWithValue("@semantic", semanticDate);
                attr.Parameters.AddWithValue("@verified", status == "COMPROVADO" ? (object)semanticDate : DBNull.Value);
                attributeId = Convert.ToInt64(await attr.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
            }
            using (var rt = connection.CreateCommand())
            {
                rt.Transaction = tx;
                rt.CommandText = """
                    INSERT silver.referencia_territorial_observacao(
                        pessoa_atributo_observacao_id,natureza_referencia,fonte_semantica,subprefeitura_id,distrito_id,origem_geografia,referencia_malha,resolvido_em)
                    VALUES(@attr,'ACOLHIMENTO_INSTITUCIONAL','REFERENCIA_TERRITORIAL',NULL,NULL,NULL,NULL,NULL);
                    """;
                rt.Parameters.AddWithValue("@attr", attributeId);
                await rt.ExecuteNonQueryAsync();
            }
            return attributeId;
        }

        await InsertExplicitAsync("PREC-COMPROVADO-ANTIGO", "COMPROVADO", DateTimeOffset.Parse("2026-08-25T09:00:00-03:00", System.Globalization.CultureInfo.InvariantCulture));
        Assert.That(await SelectedSourceRecordAsync(), Is.EqualTo("PREC-COMPROVADO-ANTIGO"),
            "Entre referências explícitas, COMPROVADO deve vencer DECLARADO mesmo sendo mais antigo.");

        await InsertExplicitAsync("PREC-COMPROVADO-NOVO", "COMPROVADO", DateTimeOffset.Parse("2026-08-28T09:00:00-03:00", System.Globalization.CultureInfo.InvariantCulture));
        Assert.That(await SelectedSourceRecordAsync(), Is.EqualTo("PREC-COMPROVADO-NOVO"),
            "Com a mesma qualidade de evidência, a referência explícita mais recente deve prevalecer.");

        await tx.RollbackAsync();
    }

    [Test]
    public async Task Sla_uses_Sao_Paulo_calendar_date_for_official_utc_receipt_timestamp()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION para executar testes SQL Server.");

        var csb = new SqlConnectionStringBuilder(connectionString);
        var db = csb.InitialCatalog ?? string.Empty;
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase) && !db.Contains("dev", StringComparison.OrdinalIgnoreCase) && !db.Contains("local", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Por segurança, o banco de integração deve conter 'Test', 'Dev' ou 'Local' no nome.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));

        Guid entregaId;
        using (var pick = connection.CreateCommand())
        {
            pick.CommandText = "SELECT TOP(1) ri.entrega_id FROM serving.v_bi_atrasos a JOIN serving.registro_integrado ri ON ri.registro_observacao_id=a.registro_observacao_id ORDER BY a.registro_observacao_id;";
            entregaId = (Guid)(await pick.ExecuteScalarAsync() ?? throw new AssertionException("Seed sem registro monitorado por SLA."));
        }

        using (var update = connection.CreateCommand())
        {
            update.CommandText = "UPDATE ingestao.entrega SET recebido_em=CONVERT(datetimeoffset(7),'2026-08-28T00:30:00+00:00') WHERE entrega_id=@id;";
            update.Parameters.AddWithValue("@id", entregaId);
            await update.ExecuteNonQueryAsync();
        }

        using var query = connection.CreateCommand();
        query.CommandText = "SELECT TOP(1) a.data_recebimento_sla FROM serving.v_bi_atrasos a JOIN serving.registro_integrado ri ON ri.registro_observacao_id=a.registro_observacao_id WHERE ri.entrega_id=@id;";
        query.Parameters.AddWithValue("@id", entregaId);
        var businessDate = Convert.ToDateTime(await query.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.That(businessDate.Date, Is.EqualTo(new DateTime(2026, 8, 27)));
    }

    private static async Task<int> ScalarAsync(SqlConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
