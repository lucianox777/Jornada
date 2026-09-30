# DC-SYN-01-E2 — emenda: referência diária de nascimento pela Projeção da População IBGE

**Data:** 30/09/2026  
**Emenda:** substitui, para E1, a origem Censo/SIDRA 9514 pela Projeção da População do IBGE — Revisão 2024. O contrato de saída permanece `JORNADA_SYNTH_BIRTH_DAILY_V1`.

## Decisão

A fonte oficial é o arquivo `projecoes_2024_tab1_idade_simples.xlsx` (SHA-256 `6E5C3D21A2E8FF50BADD7BE2785E1664B41A43277543BE541641B0CD802C3205`), Projeções da População do Brasil e Unidades da Federação 2000–2070, Revisão 2024. A seleção é **UF São Paulo, sexo Total (rótulo `Ambos` no XLSX), referência 01/07/2026**.

O XLSX oficial pertence à camada REF imutável. A geração é offline e registra SHA-256 do arquivo efetivamente usado. O gerador sintético não consulta IBGE ou SIDRA em runtime.

## Transformação

Para idade simples `k`, a janela de nascimento é `02/07/(2026-k-1) .. 01/07/(2026-k)`. O peso da idade é distribuído uniformemente pelos dias da janela, com rateio inteiro determinístico e conservação do total.

A entrada deve conter idades 0..89 e a categoria 90+. Como a Projeção 2024 publica 90+ como célula aberta, sua decomposição usa um **benchmark auxiliar oficial do Censo 2022 para a própria UF São Paulo**: Tabela SIDRA 9514, sexo Total, forma de declaração Total, 5.095 pessoas de 100 anos ou mais em 44.411.238 residentes. Esse percentual observado (≈0,01147%) é aplicado ao estoque projetado de 46.179.008 em 01/07/2026, produzindo alvo inteiro de **5.298 pessoas 100+** dentro da cauda. A razão geométrica da cauda 90–115 é então calibrada deterministicamente para atingir esse alvo, em vez de prolongar indefinidamente a razão 89/88. O Censo 2022 calibra somente a **forma interna** da célula aberta; o total populacional e o total 90+ continuam vindo da Projeção 2024. A decomposição permanece limitada a **115 anos**, decisão versionada desta E2; esse limite **não é observação do IBGE, proibição cadastral ou limite estrutural**, e não se aplica a registros históricos de pessoas falecidas. O rateio por maior resto conserva exatamente o total publicado de 90+.

A ferramenta falha fechado se faltar SP, sexo Total (rótulo `Ambos` no XLSX), 2026, qualquer idade exigida ou 90+, se a razão necessária à cauda for inválida, ou se a soma etária divergir do Total publicado quando esse Total estiver presente.

## Mudança em relação à E1

São removidos como **fonte primária da distribuição**: SIDRA 9514 municipal, município 3550308, convenção uniforme 100–105 e extrapolação pós-Censo pela coorte de idade zero. A Tabela 9514 retorna apenas como benchmark auxiliar congelado de centenários da **UF São Paulo**, Censo 2022, para calibrar a forma da célula aberta 90+; não substitui a Projeção 2024 como estoque de referência. Não há corte pós-Censo.

A referência representa **estoque populacional por idade em 01/07/2026**, não uma série observada de nascimentos. A uniformização dentro da janela anual é modelagem sintética declarada.

## Arquitetura

`tools/birth-reference` conhece a fonte IBGE e produz a referência canônica. `SyntheticDailyBirthDistribution` conhece somente `JORNADA_SYNTH_BIRTH_DAILY_V1`. O corpus sintético amostra essa referência congelada, preservando separação entre aquisição/transformação demográfica e geração de pessoas.

## Escopo e gates

Superfície: `Solution/tools/birth-reference`, `Solution/data/reference/synthetic-birth-sp`, esta documentação e testes. Não altera scorer, DDL, migrations, workflows, Plano ou Dívidas Técnicas.

Antes da publicação da REF: congelar o XLSX oficial; registrar SHA-256; reconciliar o total; executar testes da ferramenta; carregar o JSON pelo consumidor C#; provar positividade, unicidade e determinismo. A validação estatística real permanece separada.


## Tamanho do corpus

Esta emenda não altera o tamanho do corpus demográfico primário definido em E1: o padrão permanece **30.000 pessoas**, com seed padrão 42. O parâmetro de tamanho continua sendo uma escolha operacional do ensaio sintético e não uma alegação de amostragem probabilística da população de São Paulo.
