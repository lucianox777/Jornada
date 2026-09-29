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

O tamanho padrão é **30.000 pessoas**. Este valor substitui a escolha anterior de 9.596. A motivação agora é operacional/estatística para o ensaio sintético: ampliar suporte nas caudas e nos estratos sem transformar o corpus primário em challenge set. **Não** se declara que 30.000 é uma amostra probabilística real da população paulistana nem que uma margem de erro amostral clássica valida o linkage.

A seed padrão permanece 42 e participa do fingerprint. `--people` continua disponível para ensaios explícitos de sensibilidade/escala; o padrão reprodutível desta decisão é 30.000.

## Fontes públicas e proveniência

### IBGE — Censo Demográfico 2022 / SIDRA 9514

Fonte primária: Instituto Brasileiro de Geografia e Estatística (IBGE), Censo Demográfico 2022, tabela SIDRA **9514 — População residente, por sexo, idade e forma de declaração da idade**.

- tabela: https://sidra.ibge.gov.br/tabela/9514
- divulgação: Censo Demográfico 2022 — População por idade e sexo, resultados do universo;
- período: 2022;
- território usado: **Município de São Paulo, código IBGE 3550308**;
- variável: população residente;
- sexo: Total;
- forma de declaração da idade: Total;
- referência censitária usada pelo modelo: 01/08/2022;
- população municipal publicada no Censo 2022: **11.451.999 pessoas**;
- a tabela foi republicada/corrigida pelo IBGE em 22/12/2023 no conjunto de tabelas 1209, 9514 e 9515; o snapshot local deve registrar o hash do export efetivamente usado.

O SIDRA fornece o **estoque populacional por idade**. Ele não fornece uma série diária de nascimentos. A transformação idade → data diária é, portanto, modelo declarado da Jornada: idade simples `k` recebe a janela de 12 meses correspondente e o peso é repartido deterministicamente pelos dias.

A categoria `100 anos ou mais` é aberta. Qualquer decomposição interna é aproximação de modelagem e nunca deve ser descrita como idade simples observada pelo IBGE.

### Secretaria Municipal da Saúde de São Paulo — SINASC

Fonte primária: Secretaria Municipal da Saúde de São Paulo / Coordenação de Epidemiologia e Informação, **Sistema de Informações sobre Nascidos Vivos (SINASC)**.

- página de dados abertos: https://prefeitura.sp.gov.br/web/saude/w/epidemiologia_e_informacao/nascidos_vivos/306422
- página do sistema: https://prefeitura.sp.gov.br/saude/w/epidemiologia_e_informacao/nascidos_vivos/29569
- notas técnicas: https://prefeitura.sp.gov.br/web/saude/w/tabnet/8241
- dados preliminares: https://prefeitura.sp.gov.br/web/saude/w/epidemiologia_e_informacao/nascidos_vivos/312653
- arquivos publicados: DBF, CSV e XLSX; a página de dados abertos lista séries anuais de 2006 a 2025 na consulta realizada em 29/09/2026;
- fonte administrativa original: **Declaração de Nascido Vivo (DN)**, padronizada pelo Ministério da Saúde;
- unidade selecionada para esta referência: nascidos vivos de **mães/parturientes residentes no Município de São Paulo**, independentemente do município de ocorrência do parto;
- não usar a seleção “ocorridos no Município de São Paulo”, pois ela inclui partos de residentes de outros municípios e não representa a população residente pretendida.

A própria SMS documenta que, desde 2007, a seleção de residentes recupera nascimentos de mães residentes em São Paulo independentemente do município de ocorrência, inclusive por retroalimentação. A SMS também declara que dados preliminares são um retrato na data de publicação e podem mudar por novos registros e procedimentos de qualidade até a edição final. Por isso, **cada snapshot SINASC usado deve ser congelado, datado e hasheado**; preliminar e final são proveniências distintas.

O SINASC é usado somente para o trecho pós-corte censitário disponível no snapshot. Para uma data em ou após 01/08/2022 presente no SINASC selecionado, a contagem observada substitui o valor derivado da coorte censitária para aquele dia. Não há soma das duas fontes.

### O que é dado e o que é modelo

**Observado/publicado:** população residente por idade no Censo/SIDRA; registros/contagens de nascidos vivos do SINASC; código territorial; datas/períodos e situação do snapshot.

**Modelado pela Jornada:** conversão da idade censitária em janela anual de nascimento; uniformidade intrajanela; rateio inteiro por maior resto; tratamento da cauda 100+ quando não houver idade simples oficial; composição temporal Censo + SINASC.

Nenhuma dessas aproximações deve ser apresentada como estatística publicada pelo IBGE ou pela SMS.

## Gates

Antes de tornar o snapshot padrão do perfil `demographic-primary`: materializar de exports oficiais congelados; validar hashes/totais; executar testes da ferramenta; carregar por `SyntheticDailyBirthDistribution.LoadAsync`; provar unicidade, positividade e determinismo; executar a suíte unitária; manter a issue #31 como gate separado para validação estatística real.

## Fora de escopo

Não altera scorer FS, `m/u`, thresholds, blocking, DDL, migrations, contratos de identidade, Evaluation, workflows ou pacotes. Corpus sintético não é evidência de representatividade real.

## Artefatos

- `Solution/tools/birth-reference/generate_birth_reference.py`
- `Solution/tools/birth-reference/test_generate_birth_reference.py`
- `Solution/data/reference/synthetic-birth-sp/manifest.json`
- futuro snapshot materializado em `Solution/data/reference/synthetic-birth-sp/`
