# DC-SYN-01-E1 — referência diária de nascimento do corpus sintético

**Data:** 29/09/2026  
**Estado:** decisão técnica aceita para preparação do corpus; **não** é validação estatística real, promoção de modelo nem autorização de produção.  
**Escopo:** somente a fonte de `data_nascimento` do perfil sintético `demographic-primary`.

## Decisão

A geração sintética deixa de admitir uma distribuição uniforme implícita de datas de nascimento. A referência diária será um **snapshot local, imutável e versionado**, compatível com `JORNADA_SYNTH_BIRTH_DAILY_V1`, construído a partir de duas classes de evidência:

1. **Censo 2022 / SIDRA 9514**, município de São Paulo (3550308), sexo Total e declaração de idade Total, para determinar o peso das coortes na data de referência do Censo (01/08/2022).
2. **SINASC-SP por residência**, para datas observadas posteriores ao corte censitário quando houver snapshot oficial congelado e identificável.

A ferramenta de construção é offline: recebe arquivos locais, produz JSON canônico e manifesto com SHA-256 e não consulta fonte remota em runtime. O gerador C# continua apenas consumindo o snapshot materializado e falha fechado quando ele não existe ou é inválido.

## Conversão de idade censitária em janela de nascimento

Para idade simples `k` em 01/08/2022, a janela de datas é `01/08/(2021-k) .. 31/07/(2022-k)`.

O peso censitário da idade é distribuído deterministicamente pelos dias da janela. A divisão inteira usa quociente e maior resto em ordem cronológica; portanto, conserva exatamente o total e é reproduzível. Faixas etárias são fallback; idades simples publicadas têm precedência.

## Categoria aberta 100+

A tabela 9514 publica `100 anos ou mais` como categoria aberta e não identifica sua distribuição interna. O projeto não registra decomposição 100, 101, 102... como observação do IBGE.

Enquanto não houver fonte oficial mais granular, qualquer limite superior é **hipótese de modelagem declarada**. A ferramenta atual usa 122 somente como teto técnico explícito e registra isso no manifesto. A aproximação pode ser substituída sem alterar o contrato diário.

## Parte pós-Censo

Datas SINASC válidas em ou após 01/08/2022 substituem o valor censitário derivado para o mesmo dia; não são somadas. Snapshots preliminares e finais não podem ser misturados silenciosamente. Cada entrada deve constar do manifesto com hash.

## Proveniência e reprodutibilidade

O artefato materializado registra schema, fonte/período, geografia, linhas positivas e únicas, SHA-256 próprio e das entradas, método de conversão e tratamento de 100+, sem dependência de rede em runtime. O fingerprint do corpus já incorpora a proveniência da referência diária; mudar o snapshot muda a identidade reprodutível do corpus.

## Tamanho do corpus demográfico primário

O tamanho padrão é **9.596 pessoas**. A escolha usa o dimensionamento conservador de amostra para proporção, com **95% de confiança**, `p=0,5` e **margem de erro máxima de aproximadamente ±1 ponto percentual**; para a população municipal de São Paulo, a correção por população finita é pequena nessa ordem de grandeza.

Esse cálculo justifica o tamanho do **corpus demográfico primário**, não a precisão de métricas em eventos raros nem a validação do linkage. Homônimos extremos, centenários, colisões maternas e demais eventos raros permanecem em challenge sets/estratos separados e não são artificialmente super-representados nos 9.596.

A seed padrão permanece 42 e participa do fingerprint, permitindo regeneração e comparação pareada. `--people` continua disponível para ensaios explícitos de sensibilidade/escala; alterar o tamanho não redefine silenciosamente o padrão.

## Gates

Antes de tornar o snapshot padrão do perfil `demographic-primary`: materializar de exports oficiais congelados; validar hashes/totais; executar testes da ferramenta; carregar por `SyntheticDailyBirthDistribution.LoadAsync`; provar unicidade, positividade e determinismo; executar a suíte unitária; manter a issue #31 como gate separado para validação estatística real.

## Fora de escopo

Não altera scorer FS, `m/u`, thresholds, blocking, DDL, migrations, contratos de identidade, Evaluation, workflows ou pacotes. Corpus sintético não é evidência de representatividade real.

## Artefatos

- `Solution/tools/birth-reference/generate_birth_reference.py`
- `Solution/tools/birth-reference/test_generate_birth_reference.py`
- `Solution/data/reference/synthetic-birth-sp/manifest.json`
- futuro snapshot materializado em `Solution/data/reference/synthetic-birth-sp/`
