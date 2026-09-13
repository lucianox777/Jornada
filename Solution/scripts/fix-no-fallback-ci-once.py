from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]

def rw(path, fn):
    p=ROOT/path; s=p.read_text(encoding='utf-8'); n=fn(s); p.write_text(n,encoding='utf-8',newline='\n')
def once(s,a,b,label):
    if s.count(a)!=1: raise SystemExit(f'{label}: esperado 1, achado {s.count(a)}')
    return s.replace(a,b,1)

# PostgreSQL: separate legacy territorial and current residential selections.
rw('src/Jornada.Processor.Worker/PostgreSqlProcessorRepository.cs', lambda s: once(s,
'''    private sealed record PgTerritorialSelection(
        long? ReferenciaTerritorialObservacaoId, string? NaturezaReferenciaTerritorial, long? SubprefeituraId, long? DistritoId, long? EnderecoResidencialGeografiaObservacaoId, long? SubprefeituraResidenciaId, long? DistritoResidenciaId);''',
'''    private sealed record PgTerritorialSelection(
        long? ReferenciaTerritorialObservacaoId, string? NaturezaReferenciaTerritorial, long? SubprefeituraId, long? DistritoId);
    private sealed record PgResidentialSelection(
        long? EnderecoResidencialGeografiaObservacaoId, long? SubprefeituraId, long? DistritoId);''','pg selection records'))

# Canonical installer keeps the architectural marker expected by technical closure.
rw('database/Jornada_Fase1_v3.71.sql', lambda s: s.replace('-- Jornada do Cidadão - Fase 1 - baseline operacional consolidado v3.71', '-- Jornada do Cidadão - Fase 1 - baseline operacional consolidado v3.71\n-- Microsoft SQL Server é a tecnologia relacional normativa.'))

# Rebuild the 3.71 migration tail: current BI reads residence directly and preserves full linkage contract.
p=ROOT/'database/migrations/20260913_Endereco_Residencial_Sem_Fallback.sql'; s=p.read_text(encoding='utf-8')
start=s.index("GO\nIF EXISTS(SELECT 1 FROM sys.extended_properties")
s=s[:start]+r'''GO
CREATE OR ALTER VIEW serving.v_bi_linkage AS
SELECT po.pessoa_observacao_id,g.codigo gestor,so.codigo sistema_origem,po.source_as_of,vc.status,vc.metodo_resolucao,
       CASE WHEN vc.metodo_resolucao='LINKAGE_PROBABILISTICO' THEN vc.score END score,
       CASE WHEN vc.pessoa_uuid IS NULL THEN 0 ELSE 1 END possui_uuid,
       CASE WHEN vc.metodo_resolucao='CPF_DETERMINISTICO' AND vc.status='RESOLVIDO' THEN 1 ELSE 0 END resolvido_cpf,
       CASE WHEN vc.metodo_resolucao='LINKAGE_PROBABILISTICO' AND vc.status='RESOLVIDO' THEN 1 ELSE 0 END resolvido_probabilistico,
       CASE WHEN vc.status='NAO_RESOLVIDO' THEN 1 ELSE 0 END nao_resolvido,
       CASE WHEN vc.status='CONFLITO' THEN 1 ELSE 0 END conflito,
       CASE WHEN lrres.motivo='SEM_CANDIDATO_NO_BLOCO_DATA_NASCIMENTO' THEN 1 ELSE 0 END sem_candidato_no_bloco,
       lrres.motivo motivo_linkage,
       CASE WHEN po.cpf IS NULL THEN 0 ELSE 1 END cpf_preenchido,po.cpf_ausente_motivo,
       CASE WHEN DATEPART(DAY,po.data_nascimento)=1 AND DATEPART(MONTH,po.data_nascimento)=1 THEN 1 ELSE 0 END nascimento_0101,
       CASE WHEN rg.subprefeitura_id IS NULL OR rg.distrito_id IS NULL THEN 0 ELSE 1 END geografia_residencia_preenchida,
       ml.versao modelo_linkage_versao,vc.linkage_run_id,lr.tipo_run,lr.iniciado_em linkage_run_iniciado_em,
       lr.finalizado_em linkage_run_finalizado_em,ptl.valor t_linkage,lr.status linkage_run_status,
       COALESCE(sp.nome,'SEM_ENDERECO_RESIDENCIAL_RESOLVIDO') subprefeitura_residencia,
       COALESCE(d.nome,'SEM_ENDERECO_RESIDENCIAL_RESOLVIDO') distrito_residencia
FROM silver.pessoa_observacao po
JOIN ref.gestor g ON g.gestor_id=po.gestor_id
JOIN silver.pessoa_origem pori ON pori.pessoa_origem_id=po.pessoa_origem_id
JOIN ref.sistema_origem so ON so.sistema_origem_id=pori.sistema_origem_id
LEFT JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
LEFT JOIN identidade.modelo_linkage ml ON ml.modelo_id=vc.modelo_id
LEFT JOIN identidade.linkage_run lr ON lr.linkage_run_id=vc.linkage_run_id
LEFT JOIN identidade.parametro_linkage ptl ON ptl.modelo_id=vc.modelo_id AND ptl.nome='T_LINKAGE'
LEFT JOIN identidade.linkage_resultado lrres ON lrres.linkage_run_id=vc.linkage_run_id AND lrres.pessoa_observacao_id=po.pessoa_observacao_id
LEFT JOIN silver.v_pessoa_geografia_residencial rg ON rg.pessoa_observacao_id=po.pessoa_observacao_id
LEFT JOIN ref.subprefeitura sp ON sp.subprefeitura_id=rg.subprefeitura_id
LEFT JOIN ref.distrito d ON d.distrito_id=rg.distrito_id;
GO
CREATE OR ALTER VIEW serving.v_bi_qualidade_pessoa AS
SELECT po.pessoa_observacao_id,g.codigo gestor,po.source_as_of,
       COALESCE(sp.nome,'SEM_ENDERECO_RESIDENCIAL_RESOLVIDO') subprefeitura_residencia,
       COALESCE(d.nome,'SEM_ENDERECO_RESIDENCIAL_RESOLVIDO') distrito_residencia,
       rg.situacao_geografia,rg.referencia_malha,
       CASE WHEN po.cpf IS NULL THEN 0 ELSE 1 END cpf_preenchido,
       CASE WHEN LEN(LTRIM(RTRIM(po.nome_completo)))>0 THEN 1 ELSE 0 END nome_preenchido,
       CASE WHEN po.data_nascimento IS NULL THEN 0 ELSE 1 END nascimento_preenchido,
       CASE WHEN LEN(LTRIM(RTRIM(po.nome_mae)))>0 THEN 1 ELSE 0 END nome_mae_preenchido,
       CASE WHEN rg.subprefeitura_id IS NULL OR rg.distrito_id IS NULL THEN 0 ELSE 1 END geografia_residencia_preenchida,
       po.cpf_ausente_motivo,gp.status_cpf
FROM silver.pessoa_observacao po
JOIN ref.gestor g ON g.gestor_id=po.gestor_id
LEFT JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
LEFT JOIN gold.pessoa gp ON gp.pessoa_uuid=vc.pessoa_uuid
LEFT JOIN silver.v_pessoa_geografia_residencial rg ON rg.pessoa_observacao_id=po.pessoa_observacao_id
LEFT JOIN ref.subprefeitura sp ON sp.subprefeitura_id=rg.subprefeitura_id
LEFT JOIN ref.distrito d ON d.distrito_id=rg.distrito_id;
GO
CREATE OR ALTER VIEW serving.v_bi_qualidade_identidade_origem AS
SELECT l.* FROM serving.v_bi_linkage l;
GO
IF EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.SolutionSchema')
 EXEC sys.sp_updateextendedproperty @name=N'Jornada.SolutionSchema',@value=N'3.71';
ELSE EXEC sys.sp_addextendedproperty @name=N'Jornada.SolutionSchema',@value=N'3.71';
COMMIT;
'''
p.write_text(s,encoding='utf-8',newline='\n')

# Semantic model: names say what the data means, not how it was reached.
p=ROOT/'bi/Jornada.SemanticModel/definition/tables/Linkage.tmdl'; s=p.read_text(encoding='utf-8')
s=s.replace("measure 'Fallback probabilístico' = DIVIDE(SUM(Linkage[resolvido_fallback]),COUNTROWS(Linkage))", "measure 'Resolução probabilística' = DIVIDE(SUM(Linkage[resolvido_probabilistico]),COUNTROWS(Linkage))")
s=s.replace("measure 'Score médio fallback'", "measure 'Score médio probabilístico'")
s=s.replace('resolvido_fallback','resolvido_probabilistico')
s=s.replace('column geografia_preenchida','column geografia_residencia_preenchida').replace('sourceColumn: geografia_preenchida','sourceColumn: geografia_residencia_preenchida')
# remove obsolete territorial-nature column block
s=s.replace("\n\tcolumn natureza_referencia_territorial\n\t\tdataType: string\n\t\tsummarizeBy: none\n\t\tsourceColumn: natureza_referencia_territorial\n",'')
p.write_text(s,encoding='utf-8',newline='\n')

p=ROOT/'bi/Jornada.SemanticModel/definition/tables/QualidadePessoa.tmdl'; s=p.read_text(encoding='utf-8')
s=s.replace("measure 'Geografia %' = DIVIDE(SUM(QualidadePessoa[geografia_preenchida]),COUNTROWS(QualidadePessoa))", "measure 'Geografia de residência %' = DIVIDE(SUM(QualidadePessoa[geografia_residencia_preenchida]),COUNTROWS(QualidadePessoa))")
s=s.replace('column subprefeitura\n','column subprefeitura_residencia\n').replace('sourceColumn: subprefeitura\n','sourceColumn: subprefeitura_residencia\n')
s=s.replace('column distrito\n','column distrito_residencia\n').replace('sourceColumn: distrito\n','sourceColumn: distrito_residencia\n')
s=s.replace('column geografia_preenchida','column geografia_residencia_preenchida').replace('sourceColumn: geografia_preenchida','sourceColumn: geografia_residencia_preenchida')
s=s.replace("\n\tcolumn natureza_referencia_territorial\n\t\tdataType: string\n\t\tsummarizeBy: none\n\t\tsourceColumn: natureza_referencia_territorial\n",'')
p.write_text(s,encoding='utf-8',newline='\n')
print('fixed pg records, canonical marker and BI contracts')
