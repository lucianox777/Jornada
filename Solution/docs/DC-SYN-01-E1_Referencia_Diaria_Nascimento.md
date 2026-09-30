# DC-SYN-01-E2 — emenda: referência diária de nascimento pela Projeção da População IBGE

**Data:** 30/09/2026  
**Emenda:** substitui, para E1, a origem Censo/SIDRA 9514 pela Projeção da População do IBGE — Revisão 2024. O contrato de saída permanece `JORNADA_SYNTH_BIRTH_DAILY_V1`.

## Decisão

A fonte oficial é o arquivo `projecoes_2024_tab1_idade_simples.xlsx` (SHA-256 `6E5C3D21A2E8FF50BADD7BE2785E1664B41A43277543BE541641B0CD802C3205`), Projeções da População do Brasil e Unidades da Federação 2000–2070, Revisão 2024. A seleção é **UF São Paulo, sexo Total (rótulo `Ambos` no XLSX), referência 01/07/2026**.

O XLSX oficial pertence à camada REF imutável. A geração é offline e registra SHA-256 do arquivo efetivamente usado. O gerador sintético não consulta IBGE ou SIDRA em runtime.

## Transformação

Para idade simples `k`, a janela de nascimento é `02/07/(2026-k-1) .. 01/07/(2026-k)`. O peso da idade é distribuído uniformemente pelos dias da janela, com rateio inteiro determinístico e conservação do total.

A entrada deve conter idades 0..89 e a categoria 90+. A categoria aberta 90+ é decomposta por decaimento geométrico cuja razão é derivada **do próprio snapshot**, pela razão população(89)/população(88); nenhum parâmetro demográfico externo é introduzido. O rateio conserva exatamente o total 90+.

A ferramenta falha fechado se faltar SP, sexo Total (rótulo `Ambos` no XLSX), 2026, qualquer idade exigida ou 90+, se a razão necessária à cauda for inválida, ou se a soma etária divergir do Total publicado quando esse Total estiver presente.

## Mudança em relação à E1

São removidos: SIDRA 9514, município 3550308, data censitária 01/08/2022, convenção uniforme 100–105 e extrapolação pós-Censo pela coorte de idade zero. Não há corte pós-Censo.

A referência representa **estoque populacional por idade em 01/07/2026**, não uma série observada de nascimentos. A uniformização dentro da janela anual é modelagem sintética declarada.

## Arquitetura

`tools/birth-reference` conhece a fonte IBGE e produz a referência canônica. `SyntheticDailyBirthDistribution` conhece somente `JORNADA_SYNTH_BIRTH_DAILY_V1`. O corpus sintético amostra essa referência congelada, preservando separação entre aquisição/transformação demográfica e geração de pessoas.

## Escopo e gates

Superfície: `Solution/tools/birth-reference`, `Solution/data/reference/synthetic-birth-sp`, esta documentação e testes. Não altera scorer, DDL, migrations, workflows, Plano ou Dívidas Técnicas.

Antes da publicação da REF: congelar o XLSX oficial; registrar SHA-256; reconciliar o total; executar testes da ferramenta; carregar o JSON pelo consumidor C#; provar positividade, unicidade e determinismo. A validação estatística real permanece separada.
