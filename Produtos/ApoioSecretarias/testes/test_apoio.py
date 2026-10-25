import json
import shutil
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest.mock import patch
from apoio import core

ROOT = Path(__file__).resolve().parents[1]


class ApoioTestes(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.base = Path(self.tmp.name)
        self.origin = self.base / 'origem'
        self.out = self.base / 'saida'
        self.cfgdir = self.base / 'config'
        self.origin.mkdir()
        self.cfgdir.mkdir()
        shutil.copy(ROOT / 'exemplos/entrada/pessoas.csv', self.origin / 'pessoas.csv')
        shutil.copy(ROOT / 'exemplos/esquemas/pessoa.sintetica.schema.json', self.cfgdir / 'pessoas.schema.json')
        self.profile = self.cfgdir / 'perfil.json'
        prof = json.loads((ROOT / 'exemplos/perfil.sintetico.json').read_text())
        prof['contratos']['pessoas']['arquivo'] = 'pessoas.schema.json'
        self.profile.write_text(json.dumps(prof), encoding='utf-8')

    def run_it(self, *, input=None, send=None):
        return core.executar(input or self.origin, self.profile, self.out, enviar=send)

    def change(self, action):
        p = json.loads(self.profile.read_text())
        action(p)
        self.profile.write_text(json.dumps(p), encoding='utf-8')

    def test_deterministico_zip_tres_arquivos(self):
        x = self.run_it()
        self.assertEqual(x['estado'], 'GERADO_NAO_ENVIADO')
        y = self.run_it()
        self.assertEqual(y['estado'], 'GERADO_NAO_ENVIADO')
        self.assertEqual(x['pacotes'], y['pacotes'])
        z = list(self.out.glob('*.zip'))
        self.assertEqual(len(z), 1)
        self.assertEqual(z[0].name.split('_')[-1], core.sha256(z[0]) + '.zip')
        with zipfile.ZipFile(z[0]) as f:
            self.assertEqual(f.namelist(), list(core.ENVELOPE))
            self.assertEqual(len(f.read('pessoas.jsonl').splitlines()), 2)
            self.assertEqual(f.read('registros.jsonl'), b'')
        self.assertTrue(Path(x['relatorioHtml']).exists())
        self.assertTrue(Path(x['relatorioJson']).exists())

    def test_arquivo_unico(self):
        self.assertEqual(self.run_it(input=self.origin / 'pessoas.csv')['estado'], 'GERADO_NAO_ENVIADO')

    def test_arquivo_desconhecido_trava_tudo(self):
        (self.origin / 'extra.csv').write_text('x\n1\n')
        r = self.run_it()
        self.assertEqual(r['estado'], 'BLOQUEADO')
        self.assertEqual(r['erros'][0]['codigo'], 'ARQUIVO_FORA_PERFIL')
        self.assertEqual(list(self.out.glob('*.zip')), [])
        self.assertTrue(Path(r['relatorioJson']).exists())

    def test_cabecalho_inesperado_rejeitado(self):
        csv = self.origin / 'pessoas.csv'
        csv.write_text(csv.read_text().replace('mae\n', 'mae;outra\n'))
        r = self.run_it()
        self.assertEqual(r['erros'][0]['codigo'], 'COLUNAS_INVALIDAS')
        self.assertFalse(list(self.out.glob('*.zip')))

    def test_cpf_sem_motivo_rejeitado(self):
        csv = self.origin / 'pessoas.csv'
        csv.write_text(csv.read_text().replace('NAO_INFORMADO_ORIGEM', ''))
        r = self.run_it()
        self.assertEqual(r['erros'][0]['codigo'], 'CPF_SEM_MOTIVO')
        self.assertEqual(r['erros'][0]['campo'], 'cpfAusenteMotivo')

    def test_contract_hash_rejeitado(self):
        schema = self.cfgdir / 'pessoas.schema.json'
        schema.write_bytes(schema.read_bytes() + b' ')
        r = self.run_it()
        self.assertEqual(r['erros'][0]['codigo'], 'CONTRATO_DIVERGENTE')
        self.assertFalse(list(self.out.glob('*.zip')))

    def test_data_invalida_com_linha(self):
        csv = self.origin / 'pessoas.csv'
        csv.write_text(csv.read_text().replace('1990-01-02', '1990-99-99'))
        r = self.run_it()
        self.assertEqual(r['erros'][0]['codigo'], 'FORA_CONTRATO')
        self.assertEqual(r['erros'][0]['campo'], 'dataNascimento')
        self.assertEqual(r['erros'][0]['linha'], 2)

    def test_envio_desligado_nao_chama_rede(self):
        with patch.object(core, '_enviar') as transmit:
            r = self.run_it(send=False)
            self.assertEqual(r['estado'], 'GERADO_NAO_ENVIADO')
            transmit.assert_not_called()

    def test_sem_credencial_rejeita_antes_de_gerar(self):
        with patch.dict('os.environ', {'JORNADA_ACCESS_KEY': ''}):
            r = self.run_it(send=True)
        self.assertEqual(r['estado'], 'BLOQUEADO')
        self.assertEqual(r['erros'][0]['codigo'], 'CREDENCIAL_AUSENTE')
        self.assertFalse(list(self.out.glob('*.zip')))

    def test_envio_recebido_nao_significa_processado(self):
        with patch.object(core, '_enviar', return_value={'http': 202, 'recibo': {'entregaId': 'test'}}) as mocked:
            r = core.executar(self.origin, self.profile, self.out, enviar=True, access_key='TESTE')
        self.assertEqual(r['estado'], 'RECEBIDO')
        self.assertEqual(r['transmissao']['http'], 202)
        mocked.assert_called_once()

    def test_falha_http_preserva_zip(self):
        with patch.object(core, '_enviar', side_effect=core.Falha('ENVIO_HTTP', 'API recusou envio, HTTP 403')):
            r = core.executar(self.origin, self.profile, self.out, enviar=True, access_key='TESTE')
        self.assertEqual(r['estado'], 'ENVIO_FALHOU')
        self.assertEqual(len(list(self.out.glob('*.zip'))), 1)
        self.assertEqual(r['erros'][0]['codigo'], 'ENVIO_HTTP')

    def test_csv_com_valor_extra_nao_pode_passar(self):
        p = self.origin / 'pessoas.csv'
        p.write_text(p.read_text() + 'P3;S3;Teste;2000-01-01;11144477735;;Mae;EXTRA\n')
        r = self.run_it()
        self.assertEqual(r['estado'], 'BLOQUEADO')
        self.assertEqual(list(self.out.glob('*.zip')), [])

    def config_records(self):
        schema = ROOT / 'exemplos/esquemas/registro.sintetico.schema.json'
        shutil.copy(schema, self.cfgdir / 'registro.schema.json')
        def set_records(cfg):
            cfg['arquivos'].append({'padrao': 'registros.jsonl', 'papel': 'registros', 'formato': 'jsonl', 'obrigatorio': False})
            cfg.update({'natureza': 'BENEFICIO', 'codigoTipo': 'AA01', 'tipoVersao': 1})
            cfg['contratos']['registros'] = {'arquivo': 'registro.schema.json', 'sha256': core.sha256(schema), 'versao': 1}
        self.change(set_records)

    def test_registros_referenciando_pessoas(self):
        self.config_records()
        (self.origin / 'registros.jsonl').write_text(json.dumps({'idPessoaEntrega':'P1','codigoRegistroOrigem':'AA01-1','operacao':'INCLUSAO'}) + '\n')
        result = self.run_it()
        self.assertEqual(result['estado'], 'GERADO_NAO_ENVIADO')
        with zipfile.ZipFile(next(self.out.glob('*.zip'))) as z:
            self.assertEqual(len(z.read('registros.jsonl').splitlines()), 1)
            self.assertIn(b'"codigoTipo":"AA01"', z.read('manifest.json'))

    def test_registro_sem_pessoa_rejeitado(self):
        self.config_records()
        (self.origin / 'registros.jsonl').write_text(json.dumps({'idPessoaEntrega':'INEXISTENTE','codigoRegistroOrigem':'AA01-1','operacao':'INCLUSAO'}) + '\n')
        result = self.run_it()
        self.assertEqual(result['estado'], 'BLOQUEADO')
        self.assertEqual(result['erros'][0]['codigo'], 'REFERENCIA_INVALIDA')
        self.assertFalse(list(self.out.glob('*.zip')))

    def test_contrato_versao_incompativel(self):
        self.change(lambda cfg: cfg['contratos']['pessoas'].update({'versao': 4}))
        result = self.run_it()
        self.assertEqual(result['erros'][0]['codigo'], 'CONTRATO_VERSAO')

    def test_jsonl_registro_campo_desconhecido(self):
        self.config_records()
        (self.origin / 'registros.jsonl').write_text(json.dumps({'idPessoaEntrega':'P1','codigoRegistroOrigem':'AA01-1','operacao':'INCLUSAO','campoExtra':'X'}) + '\n')
        result = self.run_it()
        self.assertEqual(result['erros'][0]['codigo'], 'FORA_CONTRATO')
        self.assertFalse(list(self.out.glob('*.zip')))

    def test_duplicate_cpf_or_person_bloqueio(self):
        p = self.origin / 'pessoas.csv'
        p.write_text(p.read_text() + 'P1;S3;Teste;2000-01-01;11144477735;;Mae\n')
        result = self.run_it()
        self.assertEqual(result['erros'][0]['codigo'], 'ID_DUPLICADO')
        self.assertFalse(list(self.out.glob('*.zip')))

    def test_jsonl_registro_vazio_permitido(self):
        self.config_records()
        (self.origin / 'registros.jsonl').write_text('')
        result = self.run_it()
        self.assertEqual(result['estado'], 'GERADO_NAO_ENVIADO')

    def test_diretorio_de_saida_dentro_da_origem_trava(self):
        r = core.executar(self.origin, self.profile, self.origin / 'saida')
        self.assertEqual(r['estado'], 'BLOQUEADO')
        self.assertEqual(r['erros'][0]['codigo'], 'SAIDA_INVALIDA')

    def test_relatorio_erro_nao_modifica_pasta_de_origem(self):
        destino_invalido = self.origin / 'saida'
        before = sorted(p.relative_to(self.origin).as_posix() for p in self.origin.rglob('*'))
        report = core.executar(self.origin, self.profile, destino_invalido)
        self.assertEqual(report['estado'], 'BLOQUEADO')
        self.assertFalse(destino_invalido.exists())
        after = sorted(p.relative_to(self.origin).as_posix() for p in self.origin.rglob('*'))
        self.assertEqual(before, after)
        self.assertTrue(Path(report['relatorioJson']).exists())

    def test_arquivo_alterado_durante_leitura_bloqueia(self):
        original = core._ler_arquivo
        def alterar(path, rule, schema):
            people = original(path, rule, schema)
            with path.open('a', encoding='utf-8') as stream:
                stream.write('\n')
            return people
        with patch.object(core, '_ler_arquivo', side_effect=alterar):
            report = self.run_it()
        self.assertEqual(report['estado'], 'BLOQUEADO')
        self.assertEqual(report['erros'][0]['codigo'], 'ORIGEM_ALTERADA')
        self.assertFalse(list(self.out.glob('*.zip')))

    def test_envio_usar_mesmos_bytes_para_hash_e_payload(self):
        self.run_it()
        pacote = next(self.out.glob('*.zip'))
        sent = {}
        class Response:
            status = 202
            def __enter__(self): return self
            def __exit__(self, *args): return False
            def read(self, n): return b'{"entregaId":"teste"}'
        class Opener:
            def open(self, req, timeout):
                sent['data'] = req.data
                sent['idempotency'] = req.headers['Idempotency-key']
                return Response()
        self.change(lambda prof: prof.update({'endpoint':'https://api-exemplo.invalid/api/v1/ingestao/entregas'}))
        with patch.object(core, '_no_redirect', return_value=Opener()):
            result = core.reenviar_zip_existente(pacote, self.profile, self.out, access_key='CHAVE-SINTETICA')
        self.assertEqual(result['estado'], 'RECEBIDO')
        self.assertEqual(core.hashlib.sha256(sent['data']).hexdigest(), core.sha256(pacote))
        self.assertEqual(sent['idempotency'], 'sha256:' + core.sha256(pacote))

    def test_linha_em_branco_jsonl_bloqueada(self):
        self.config_records()
        (self.origin / 'registros.jsonl').write_text(
            '{"idPessoaEntrega":"P1","codigoRegistroOrigem":"AA01","operacao":"INCLUSAO"}\n\n'
        )
        report = self.run_it()
        self.assertEqual(report['estado'], 'BLOQUEADO')
        self.assertEqual(report['erros'][0]['codigo'], 'JSONL_LINHA_VAZIA')
        self.assertFalse(list(self.out.glob('*.zip')))

    def test_pasta_recebe_arquivo_tardio_bloqueia(self):
        original = core._ler_arquivo
        def alterar(path, rule, schema):
            rows = original(path, rule, schema)
            (self.origin / 'arquivo_tardio.csv').write_text('nao mapeado')
            return rows
        with patch.object(core, '_ler_arquivo', side_effect=alterar):
            report = self.run_it()
        self.assertEqual(report['estado'], 'BLOQUEADO')
        self.assertEqual(report['erros'][0]['codigo'], 'ORIGEM_ALTERADA')
        self.assertFalse(list(self.out.glob('*.zip')))

    def test_perfil_muda_durante_entrada_bloqueia(self):
        original = core._inventario
        def alterar(*args):
            ret = original(*args)
            self.profile.write_text(self.profile.read_text() + '\n')
            return ret
        with patch.object(core, '_inventario', side_effect=alterar):
            report = self.run_it()
        self.assertEqual(report['estado'], 'BLOQUEADO')
        self.assertEqual(report['erros'][0]['codigo'], 'PERFIL_ALTERADO')
        self.assertFalse(list(self.out.glob('*.zip')))

    def test_schema_muda_durante_entrada_bloqueia(self):
        original = core._inventario
        def alterar(*args):
            ret = original(*args)
            schema = self.cfgdir / 'pessoas.schema.json'
            schema.write_bytes(schema.read_bytes() + b' ')
            return ret
        with patch.object(core, '_inventario', side_effect=alterar):
            report = self.run_it()
        self.assertEqual(report['estado'], 'BLOQUEADO')
        self.assertEqual(report['erros'][0]['codigo'], 'CONTRATO_ALTERADO')
        self.assertFalse(list(self.out.glob('*.zip')))

    def test_config_unknown_trava(self):
        self.change(lambda x: x.update({'ignorarErros': True}))
        r = self.run_it()
        self.assertEqual(r['erros'][0]['codigo'], 'PERFIL_INVALIDO')

    def test_reenviar_existente_nao_reempacota(self):
        first = self.run_it()
        zip_file = next(self.out.glob('*.zip'))
        digest = core.sha256(zip_file)
        with patch.object(core, '_enviar', return_value={'http':202,'recibo':{'entregaId':'sintetico'}}) as fn:
            retry = core.reenviar_zip_existente(zip_file, self.profile, self.out, access_key='FAKE')
        self.assertEqual(retry['estado'], 'RECEBIDO')
        self.assertEqual(core.sha256(zip_file), digest)
        self.assertEqual(len(list(self.out.glob('*.zip'))), 1)
        fn.assert_called_once()

    def test_reenviar_zip_alterado_bloqueado(self):
        self.run_it()
        zip_file = next(self.out.glob('*.zip'))
        zip_file.write_bytes(zip_file.read_bytes() + b'ADULTERADO')
        with patch.object(core, '_enviar') as fn:
            result = core.reenviar_zip_existente(zip_file, self.profile, self.out, access_key='FAKE')
        self.assertEqual(result['estado'], 'BLOQUEADO')
        self.assertEqual(result['erros'][0]['codigo'], 'ZIP_DIVERGENTE')
        fn.assert_not_called()

    def test_sem_pessoa_nao_entrega(self):
        (self.origin / 'pessoas.csv').write_text('id;codigo;nome;nascimento;cpf;cpf_ausente_motivo;mae\n')
        r = self.run_it()
        self.assertEqual(r['estado'], 'BLOQUEADO')


if __name__ == '__main__':
    unittest.main()
