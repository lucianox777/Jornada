# DC-SYN-01-E1 — referência diária de nascimento do corpus sintético

**Data:** 29/09/2026  
**Estado:** decisão técnica aceita para preparação do corpus; **não** é validação estatística real, promoção de modelo nem autorização de produção.  
**Escopo:** somente a fonte de `data_nascimento` do perfil sintético `demographic-primary`.

## Decisão

A referência de `data_nascimento` do corpus `demographic-primary` usa **uma única fonte demográfica observada**: Censo Demográfico 2022 / SIDRA tabela 9514, município de São Paulo (3550308), sexo Total e forma de declaração da idade Total.

Não se usa SINASC nesta decisão. Para datas posteriores ao corte censitário, a Jornada prolonga até uma data de corte explícita a taxa diária média implícita da coorte de idade zero. Essa extensão é **modelo declarado**, não série observada de nascimentos.

A ferramenta é offline: recebe um export local congelado do SIDRA, produz JSON canônico e manifesto com SHA-256 e não consulta rede em runtime.

## Conversão da idade em data

Para idade simples `k` na referência censitária, a janela é `01/08/(2021-k) .. 31/07/(2022-k)`. O peso observado da idade é distribuído uniformemente pelos dias, com rateio inteiro determinístico por quociente/maior resto. O total é conservado.

Idades simples têm precedência; faixa publicada só pode atuar como fallback. A entrada falha fechado se faltar qualquer idade 0..99, houver sobreposição, faltar `100 anos ou mais`, faltar a linha `Total` ou a soma etária divergir dela.

## Categoria aberta 100+

O SIDRA publica `100 anos ou mais` sem decomposição interna. DC-SYN-01-E1 adota a convenção técnica simples **100 a 105 anos, uniforme entre as seis idades**. O teto 105 não é observação do IBGE e não deve ser interpretado como estimativa da distribuição real dos centenários; serve somente para transformar a categoria aberta em datas plausíveis no corpus sintético.

## Parte pós-Censo

A partir de 01/08/2022 até `--post-census-cutoff`, inclusive, usa-se a taxa diária média da coorte censitária de idade zero. O total implícito do intervalo é arredondado e distribuído deterministicamente pelos dias. Não há consulta, dependência ou snapshot SINASC.

A data de corte é parte da proveniência do artefato. Esta aproximação pressupõe estabilidade da taxa recente de nascimentos; não pretende reconstruir a natalidade diária real.

## Proveniência e limitações

### IBGE — Censo Demográfico 2022 / SIDRA 9514

Fonte: Instituto Brasileiro de Geografia e Estatística (IBGE), Censo Demográfico 2022, tabela SIDRA **9514 — População residente, por sexo, idade e forma de declaração da idade**.

- tabela: https://sidra.ibge.gov.br/tabela/9514
- território: Município de São Paulo, código IBGE 3550308;
- variável: população residente;
- sexo: Total;
- forma de declaração da idade: Total;
- referência usada: 01/08/2022;
- snapshot local deve registrar SHA-256 do export efetivamente usado.

O SIDRA fornece estoque populacional por idade, não distribuição diária de nascimentos. São modelagens da Jornada: conversão idade→janela, uniformidade dentro da janela, convenção 100–105 e extrapolação pós-Censo.

Limitações aceitas e declaradas: não se modelam dia da semana, feriados, concentração em datas convencionais como 01/01, nem associação entre geração/idade e distribuição de nomes. Essas limitações não são tratadas como estatística publicada pelo IBGE e podem ser exercitadas separadamente em famílias de desafio.

## Tamanho do corpus demográfico primário

O tamanho padrão é **30.000 pessoas**. Este valor substitui a escolha anterior de 9.596. A motivação agora é operacional/estatística para o ensaio sintético: ampliar suporte nas caudas e nos estratos sem transformar o corpus primário em challenge set. **Não** se declara que 30.000 é uma amostra probabilística real da população paulistana nem que uma margem de erro amostral clássica valida o linkage.

A seed padrão permanece 42 e participa do fingerprint. `--people` continua disponível para ensaios explícitos de sensibilidade/escala; o padrão reprodutível desta decisão é 30.000.


## Gates

Antes de tornar o snapshot padrão: congelar o export oficial SIDRA 9514; registrar e validar SHA-256; reconciliar idades com a linha `Total`; confirmar cobertura 0..99 + 100+; confirmar convenção 100–105; declarar o corte pós-Censo; executar os testes da ferramenta; carregar o JSON por `SyntheticDailyBirthDistribution.LoadAsync`; provar unicidade, positividade e determinismo; executar a suíte unitária. A issue #31 continua sendo gate separado para validação estatística real.

## Fora de escopo

Não altera scorer FS, `m/u`, thresholds, blocking, DDL, migrations, contratos de identidade, Evaluation, workflows ou pacotes. Corpus sintético não é evidência de representatividade real.

## Artefatos

- `Solution/tools/birth-reference/generate_birth_reference.py`
- `Solution/tools/birth-reference/test_generate_birth_reference.py`
- `Solution/data/reference/synthetic-birth-sp/manifest.json`
- futuro snapshot materializado em `Solution/data/reference/synthetic-birth-sp/`
