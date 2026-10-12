# Justificativa técnica e normativa — proxy IBGE para último sobrenome na V9

**Data:** 12/10/2026  
**Natureza:** registro de fundamentação e rastreabilidade de correção de conformidade; **não é nova decisão arquitetural**.  
**Decisão vinculante:** [DC-LK-TF, linha 95](Decisoes_Canonicas_Identidade_Linkage_20260929.md#dc-lk-tf--norma-vigente-de-frequência-nominal-fs-e-calibrador-09102026); [Calibrador FS, §10](Calibrador_FS_Specification.md#10-nome-completo-e-componentes-nominais-decisão-vigente).  
**Alteração justificada:** [PR #955](https://github.com/lucianox777/Jornada/pull/955), integrada no `master` pelo merge `77178a1e229e8ef7af61250633eec9e65bb548c7`.  
**Regressão corrigida:** [PR #951](https://github.com/lucianox777/Jornada/pull/951); evolução governada da [V9, issue #946](https://github.com/lucianox777/Jornada/issues/946).

## 1. Fato gerador e precedência

A [decisão canônica DC-LK-TF](Decisoes_Canonicas_Identidade_Linkage_20260929.md) estabelece expressamente, na linha 95, que o sobrenome usado na comparação é o **último sobrenome significativo**, extraído por `BrazilianNameComponents.LastContentSurname`, para a pessoa e para a mãe. Como o IBGE publica frequência de sobrenomes **independentemente de sua posição**, essa distribuição é autorizada como **proxy inicial**, não como medida observada da posição final.

A [especificação do Calibrador, §10](Calibrador_FS_Specification.md) confirma a mesma distinção: sobrenome em qualquer posição é uma aproximação inicial e não frequência observada do último sobrenome. A DC-LK-TF também exige evitar dupla contagem, calibrar peso TF e selecionar representação por evidência.

A PR #951 introduziu uma regra mais restritiva em `CompositeNominalTermFrequencyContractV9.RequireMatchingReference`: `IbgePublishedMarginal` era aceita apenas para `FirstName` e a combinação de fonte IBGE com `LastContentSurname` era sempre rejeitada. O respectivo teste exigia a rejeição e a documentação técnica a apresentava como política. **Isso convertia uma limitação estatística conhecida em uma proibição não autorizada**, contrariando a decisão canônica. Um documento técnico ou teste novo não altera a norma de maior precedência, nem constitui emenda explícita.

A PR #955 **corrigiu o defeito de aderência**, sem modificar a decisão. O objetivo não foi declarar equivalência estatística entre as duas unidades, mas permitir o *bootstrap* expressamente aprovado com identificação inequívoca de sua aproximação.

## 2. Justificativa estatística: permitir proxy não é declarar exatidão

A frequência publicada de um sobrenome em **qualquer posição** e a frequência do **último sobrenome significativo** não são, em geral, a mesma variável aleatória. Uma pessoa pode ter mais de um sobrenome; um sobrenome observado em posição intermediária contribui para a estatística publicada, mas pode não ser seu último sobrenome significativo. A magnitude e o sentido da diferença dependem da população, da estrutura dos nomes, das regras de extração e dos recortes de referência; **esta alteração não os estimou**.

Entretanto, a norma vigente deliberadamente permite usar a estatística publicada como **aproximação inicial versionada**, enquanto o Calibrador constrói e valida uma referência melhor. Exigir desde o primeiro *bootstrap* a frequência verdadeira por posição final eliminaria essa opção sem uma nova decisão do proprietário. A permissão preserva o caráter inicial e substituível da evidência do IBGE.

Portanto devem permanecer **separadas** as afirmações:

- **Fato sobre a fonte:** a marginal publicada pelo IBGE representa a presença de sobrenome em qualquer posição, conforme a semântica da referência adotada.
- **Hipótese operacional autorizada:** utilizar essa marginal como **proxy inicial** para `LastContentSurname`, com fonte, recorte geográfico, versão, frame e limitações rastreáveis.
- **Afirmação não autorizada:** apresentar a marginal como frequência **empiricamente medida** do último sobrenome, ou como distribuição conjunta observada de primeiro nome e último sobrenome.

A procedência e os recortes da referência inicial já definidos em [DC-LK-02A](Decisoes_Canonicas_Identidade_Linkage_20260929.md) continuam vinculantes: pessoa — sobrenomes `SOBRENOME/TODOS`, **São Paulo município 3550308**; mãe — sobrenomes `SOBRENOME/TODOS`, **Brasil**. A correção do guard **não implementa, modifica ou dispensa** essas restrições na ingestão/Calibrador.

## 3. Por que a implementação usa proveniência explícita

A PR #955 acrescentou o valor `NominalFrequencySourceV9.IbgeSurnameAnyPositionProxy` (preservando os valores anteriores do enum) e versionou o guard como `V9_NOMINAL_TF_UNIT_GUARD_V2`.

No contrato corrigido:

| Nível nominal V9 | Unidade de frequência | Fonte IBGE elegível no guard | Semântica |
| --- | --- | --- | --- |
| `ExactFirst` | `FirstName` | `IbgePublishedMarginal` | Marginal de primeiro nome no contrato da referência |
| `ExactLast` | `LastContentSurname` | **`IbgeSurnameAnyPositionProxy`** | **Proxy inicial explicitamente identificado**, sem fingir medição da posição final |
| `ExactFull` | `FullName` | **Nenhuma marginal IBGE simples** | Exige frequência da unidade integral compatível |
| `ExactFirstLast` | `FirstLastJoint` | **Nenhuma marginal IBGE simples** | Exige frequência conjunta observada/modelada e calibrada sob contrato próprio |
| `Missing`, `High*`, `Other`, `ReversedFirstLast` | Sem TF na guarda atual | Nenhuma | Não assumir ajuste TF sem estado calibrado |

A origem genérica `IbgePublishedMarginal` **não passou a autorizar implicitamente** o último sobrenome. O produtor deve classificar explicitamente a fonte como `IbgeSurnameAnyPositionProxy`. Assim, o guard evita que uma marginal qualquer seja tomada por uma referência empiricamente exata da posição final. O guard também impede o uso do proxy para `FullName`, `FirstLastJoint` ou `FirstName`.

**Importante:** `RequireMatchingReference` só valida a **elegibilidade declarada do contrato** e ainda exige fingerprints, suporte mínimo e sinalizador de validação populacional. Esses metadados não comprovam por si só procedência, representatividade, independência, significância estatística ou qualidade dos valores. No *bootstrap*, não transformar o nome desses campos em alegação de que a frequência por último sobrenome foi medida: a origem permanece **proxy**.

## 3-A. Complemento posterior: distinguir a aprovação sintética do suporte real (V3)

A especificação acima descreve historicamente o guard **V2 da PR #955**. A decisão canônica já documentava o bootstrap inicial `SINTETICO_IBGE` de **30.000 pessoas**, com origem, versão/seed de gerador, hipóteses e fingerprints; esse corpus **não é amostra observada municipal**. Portanto não se deve exigir contagem de suporte **real observado** nem atestado de validação **populacional real** para admitir sua referência IBGE inicial. A fonte real, que progressivamente substitui o bootstrap, deve continuar sujeita a esses dois gates.

A [evolução V3 do contrato](Linkage_V9_TF_Unit_Safety_Audit_20261012.md) exige proveniência sintética explícita, 30.000 pessoas e identidade/versionamento do corpus gerado para fontes IBGE; **rejeita misturar o marcador `SINTETICO_IBGE` com alegações de suporte/validação reais**. Fontes observadas reais continuam exigindo suporte e validação independentes, sem marcador sintético. Não se altera a distinção normativa entre marginal IBGE de sobrenome em qualquer posição e frequência da posição final; nenhuma dessas verificações declarativas comprova fidelidade real do corpus, nem autoriza aplicar TF ou ativar V9.

## 4. Alternativas consideradas e rejeitadas

1. **Manter a rejeição absoluta da PR #951:** descartaria um bootstrap permitido pela DC-LK-TF, sem emenda formal, mesmo quando a fonte fosse explicitamente rotulada como proxy.
2. **Liberar a fonte IBGE genérica para todos os níveis:** ocultaria a diferença entre prenome, sobrenome em qualquer posição, nome integral e distribuição conjunta; também abriria caminho para a regressão conceitual de TF da V8.
3. **Aceitar o proxy como frequência exata do último sobrenome:** afirmaria equivalência empírica não sustentada pela fonte e violaria diretamente a linha 95.
4. **Multiplicar marginais de prenome e sobrenome como frequência conjunta:** imporia independência não demonstrada e poderia contar duas vezes evidências derivadas do mesmo nome.
5. **Fixar o peso TF do proxy ou forçar sua aplicação:** anteciparia decisão estatística que pertence ao Calibrador e aos gates TRAIN/VALIDATION/TEST; o peso pode inclusive ser zero quando assim selecionado e aprovado.

A alternativa adotada foi **permissão condicional com proveniência explícita**, mantendo a separação entre dado publicado, aproximação bootstrap e parâmetro operacional aprovado.

## 5. Mudanças e comprovação efetuadas na PR #955

Foram alterados exclusivamente três arquivos:

- `Solution/src/Jornada.Contracts/CompositeNominalTermFrequencyContractV9.cs`: fonte `IbgeSurnameAnyPositionProxy`, restrição de uso a `LastContentSurname`, rejeição de fonte genérica para essa unidade e contrato `V2`.
- `Solution/tests/Jornada.Tests/Unit/CompositeNominalFrequencyV9Tests.cs`: regressão positiva para o proxy em `ExactLast`; casos negativos para origem não marcada, níveis/unidades incompatíveis, suporte insuficiente, fingerprint ausente ou validação não declarada; mantida rejeição de marginais para `ExactFull` e `ExactFirstLast`.
- `Solution/docs/Linkage_V9_TF_Unit_Safety_Audit_20261012.md`: retificação da proibição para a autorização condicionada, remetendo às decisões canônicas.

O HEAD `1ee58068989b308ea1d0731a5b43e1d744d7b83b` da PR #955 passou pela execução [`jornada-ci` #38287642476](https://github.com/lucianox777/Jornada/actions/runs/38287642476), concluída com **sucesso**: unitários, integração SQL, E2E, harness, CodeQL, guards, build determinístico e agregadores. Gates de escala, DT10, recuperação bronze e promoção/RC foram **ignorados por não se aplicarem ao escopo**. Os workflows separados de validação independente e instalador Windows **não foram disparados** pela PR documental/contrato sob seus filtros de caminhos; não foram contabilizados como aprovações novas.

## 6. Limites de efeito e gates ainda obrigatórios

A PR #955 alterou **somente um guard de contrato, testes e documentação**. Não implementou um estimador de frequência posicional, não estimou peso TF, não conectou o novo proxy ao score C# ativo, não publicou parâmetros FS nem mudou `Rank`, `Resolve`, `GENERATE_DRAFT`, `VALIDATE`, `ACTIVATE`, bancos ou snapshots históricos. A V8 permanece exclusivamente para replay; a V9 segue a única arquitetura-alvo, mas **sem ativação operacional por essa PR**.

Para eventual aplicação do proxy no scorer ainda são necessários, entre outros: origem/recorte/versionamento e suficiência auditáveis, modelagem de seu erro de aproximação, `m/u` e TF no mesmo universo efetivo de *blocking*, hipótese de dependência controlada, seleção do peso `w_TF` pelo Calibrador, TRAIN/VALIDATION/TEST independentes, comparação de cálculo C# × Splink e orçamento governado de FP/FDR/recall com promoção DT-15.

**Síntese:** a correção restabelece uma **permissão normativa de bootstrap** sem converter um **proxy estatístico** em **verdade observada**, e sem autorização de uso decisório. O documento registra as razões; **não emenda a DC-LK-TF**.
