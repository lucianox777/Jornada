import copy
import importlib.util
from pathlib import Path
import unittest

SOURCE = Path(__file__).resolve().parents[2] / "scripts" / "v9-e2e-1k-person-census.py"
spec = importlib.util.spec_from_file_location("person_census", SOURCE)
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


class PersonCensusTests(unittest.TestCase):
    def setUp(self):
        self.uid = "00000000-0000-4000-8000-000000000001"
        self.response = {"pessoaUuid":self.uid,"schemaRef":"schema",
                         "dados":dict(codigoPessoaOrigem="synthetic",cpf=None,cpfAusenteMotivo="SEM_CPF",
                                      nomeCompleto="PESSOA",dataNascimento=None,nomeMae=None,
                                      atributosTransversais=[],conferenciasDocumentais=[]),
                         "metadados":dict(pessoaUuidSolicitado=self.uid,redirecionadoPorFusao=False,
                                          estadoIdentidade="REFERENCIA",fontesDistintas=1,
                                          estadoConcordancia="BASELINE_FONTE_UNICA",completudeNucleo="PARCIAL",
                                          atualizadoEm="2026-11-25T00:00:00+00:00")}

    def test_delimiters_and_null_are_unambiguous(self):
        self.assertEqual(m.field("A"),"4100")
        self.assertEqual(m.field(None),"-")
        self.assertNotEqual(m.field("-"),m.field(None))
        self.assertNotEqual(m.field("|"),"|")

    def test_payload_drift_and_duplicate_detected(self):
        a=m.digest_rows([self.response],[self.uid],"schema")
        b=copy.deepcopy(self.response)
        b["dados"]["nomeCompleto"]="OUTRA"
        self.assertNotEqual(a,m.digest_rows([b],[self.uid],"schema"))
        with self.assertRaises(ValueError):
            m.digest_rows([self.response,self.response],[self.uid],"schema")
        with self.assertRaises(ValueError):
            m.digest_rows([self.response],[],"schema")

    def test_unexpected_field_rejected(self):
        self.response["dados"]["notAllowed"]="x"
        with self.assertRaises(ValueError):
            m.digest_rows([self.response],[self.uid],"schema")


if __name__ == "__main__":
    unittest.main()
