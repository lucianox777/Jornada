from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]

def patch(path, old, new):
    p=ROOT/path
    s=p.read_text(encoding='utf-8')
    if old not in s:
        raise SystemExit(f'trecho ausente em {path}: {old[:120]}')
    p.write_text(s.replace(old,new,1),encoding='utf-8',newline='\n')

p='scripts/technical-closure-gate-v405.py'
patch(p, '''    require(processor_models, [
        'var sourceCode = OptionalString(json, "codigoPessoaOrigem")',
        'var cpf = OptionalString(json, "cpf")',
        'codigoPessoaOrigem ausente exige CPF preenchido para derivação do código de origem.',
    ], "fallback CPF -> codigoPessoaOrigem")
''', '''    require(processor_models, [
        'var sourceCode = OptionalString(json, "codigoPessoaOrigem")',
        'var cpf = OptionalString(json, "cpf")',
        'batch.PessoaSchemaVersao >= 4',
        'a Jornada não deriva chave de origem do CPF',
        'compatibilidade de replay v1-v3',
    ], "codigoPessoaOrigem obrigatório na linha corrente e compatibilidade histórica")
''')
patch(p, '''        "@solution=N'3.69'",
        "SQL_SCHEMA_INCOMPATIVEL",''', '''        "@solution=N'3.71'",
        "silver.endereco_residencial_geografia_observacao",
        "silver.v_pessoa_geografia_residencial",
        "SQL_SCHEMA_INCOMPATIVEL",''')

# Current API docs: source keys are no longer synthesized from CPF.
p='docs/API.md'; q=ROOT/p; s=q.read_text(encoding='utf-8')
s=s.replace('- toda Pessoa exige `codigoPessoaOrigem` **ou** CPF preenchido; se o código estiver ausente, a Jornada usa o CPF como `codigoPessoaOrigem` interno;', '- toda Pessoa no contrato corrente exige `codigoPessoaOrigem`; CPF identifica civilmente a Pessoa e não substitui a chave local do sistema de origem;')
s=s.replace('fallback probabilístico','resolução probabilística').replace('fallback probabilistica','resolução probabilística')
q.write_text(s,encoding='utf-8',newline='\n')

# Current requirements wording: probabilistic resolution is a separate route, not a fallback.
for rel in ['../Documentos/Requisitos/02_Requisitos_Funcionais_Jornada_v1.1.md','../Documentos/Requisitos/04_Requisitos_Tecnicos_Jornada_v1.1.md']:
    q=(ROOT/rel).resolve(); s=q.read_text(encoding='utf-8')
    s=s.replace('fallback probabilístico','processo de resolução probabilística').replace('fallback probabilistica','processo de resolução probabilística')
    q.write_text(s,encoding='utf-8',newline='\n')
print('gates/docs aligned')
