> **Decisão V9, 13/10/2026:** não exigir comparação V8×V9
> como requisito para desenvolver, calibrar ou julgar se a V9
> funciona. A V8 só permanece para replay histórico.
> A V9 será avaliada diretamente por gabarito, TRAIN/VALIDATION/TEST,
> falsos vínculos, recall, suporte e modelo estatístico. A DT-15
> conserva exclusivamente o dossiê de substituição ATIVO × PROPOSTO
> quando houver uma decisão de ativação; esse dossiê não obriga
> superioridade da V9 em relação à versão histórica ativa.

# V9 — TF IBGE congelado: prenomes e proxy explícito de sobrenomes (13–30/10/2026)

> **Estado de integração em 30/10:** `ExactFirst` está no contrato portátil de calibração; `ExactLast` possui marginal e preview identificados como proxy `LastContentSurname`, mas **não** está integrado ao runtime/estimador TRAIN U. A HEAD desta PR incorpora exportação explícita de ambas as unidades, ainda sem CI na nova HEAD da #1215. Nenhuma aprovação DT-15 ou ativação decorre deste documento.

## Entrega

O `FrozenIbgeNominalTfV9.LoadAsync` resolve **por ID explícito** a versão `ref.frequencia_nome_versao`, lê `codigo`, `status`, `conteudo_sha256` e rejeita `CARREGANDO`, `VALIDADA`, referência inexistente e hash ausente. Aceita `ATIVA` e `OBSOLETA` (esta somente para replay analítico reprodutível): não consulta a versão ativa do dia, não troca ID e não carrega novas frequências no hot path. Em seguida emprega `NominalTermFrequencyReferenceStore.PrepareAsync`, já restrito às marginais publicadas **pessoa = prenome TOTAL do município de São Paulo 3550308** e **mãe = prenome FEMININO Brasil**, com cobertura declarada. Reconstrói a tabela de entradas para detectar valores divergentes e fixa uma identidade SHA-256 própria do subconjunto materializado, distinta da hash de conteúdo da referência IBGE completa. A hash IBGE do catálogo segue como identidade da versão, não é recalculada a partir do subconjunto TF.

`CreatePreview` fornece ao scorer FS para `ExactFirst` a frequência do prenome e, para `ExactLast`, a frequência de sobrenome em qualquer posição **somente como proxy explícito** de `LastContentSurname`, ambas encontradas no snapshot fixado; o chamador **não informa nem substitui `TermFrequency`**. O contexto `PopulationFrameFingerprint` inclui código e hash publicados e o universo geográfico de cada lado; `SINTETICO_IBGE`, a identidade do corpus sintético 30 mil, seed, versão do gerador e hipóteses permanecem exigidos pelo guard canônico. Peso `w_TF`, `UnitReferenceU`, floor e suporte mínimo continuam argumentos **explícitos de calibração**; a fábrica **não escolhe** peso/limiar nem infere `u` do IBGE. A avaliação usa o scorer FS C# existente com **uma única contribuição nominal conjunta**.

O retorno `null` com `weight=0` tem sentido **único e intencional de BF=1**, sem exigir uma frequência desabilitada; já com peso positivo, prenome desconhecido/suprimido, comparações inconsistentes, parâmetros inválidos e nível não suportado **falham fechado**. Não inferir desconhecido como `0` ou `1`. Não aplicar marginal de prenome ao `ExactFull` ou `ExactFirstLast`, nem tratar a marginal IBGE de sobrenome em qualquer posição como observação da última posição. A marginal de sobrenome existe como proxy `LastContentSurname`, mas seu uso operacional exige `u` TRAIN condicionado ao blocking e contrato portátil próprio.

## Limites importantes

O caminho SQL `LoadAsync` é o único que **confere** status/digest contra a versão publicada em `ref`. O caminho puro `BindPublished` só combina dados **já autenticados**; ele pode receber entradas artificiais em testes e **não certifica** a procedência por si. A hash materializada não equivale a validação populacional, e metadados `SINTETICO_IBGE` não provam amostragem real.

**Escopo estritamente PREVIEW/read-only:** o caminho de `GENERATE_DRAFT`, pesos V8, Rank/Resolve, VALIDATE/ACTIVATE, tabelas de modelos, referências IBGE publicadas e `JornadaLocal` não foram alterados. A PR não promete vinculação operacional V9 completa. Para avançar são necessários integração explícita com calibração/score V9 congelado, `u` dos passes de blocking SQL reais, avaliação V9 em VALIDATION/TEST selados e DT-15. A comparação V8×V9 não é gate obrigatório de funcionamento da V9; o dossiê ATIVO × PROPOSTO só é exigido no rito de substituição da DT-15. A matriz 30 mil da PR #993 mede somente suporte M/U em seleção proxy; não autoriza afirmar suficiência.


## Decisão vigente: proxy de sobrenome congelado (30/10/2026)

O IBGE publica frequência de sobrenomes independentemente da posição. Portanto, a marginal SOBRENOME/TODOS é somente um proxy inicial para o último sobrenome significativo, nunca frequência observada dessa posição. A coleta pode registrar somente o último sobrenome quando não for possível registrar todos. A extração V9 utiliza BrazilianNameComponents.LastContentSurname e identifica a origem como IbgeSurnameAnyPositionProxy.

Recortes imutáveis por versão publicada: pessoa, São Paulo município 3550308, NOME/TODOS e SOBRENOME/TODOS; mãe, Brasil, NOME/FEMININO e SOBRENOME/TODOS. O LoadAsync vincula a referência pelo ID e SHA-256 publicados; não consulta a versão ativa do dia. A materialização de sobrenomes foi acrescentada no ramo de integração da PR #1215. Os parágrafos originais acima descrevem o escopo anterior de 13/10, não o código atualizado.

ExactFirst utiliza a marginal de prenome; ExactLast utiliza o proxy de sobrenome. ExactFull e ExactFirstLast não podem receber uma marginal simples nem o produto de marginais como frequência conjunta. Frequência ausente ou suprimida não equivale a zero. TF=0 é neutralização explícita; TF positivo exige frequência publicada, unidade compatível e referência congelada. O peso TF é escolhido pelo calibrador V9, sem preferência fixa, com avaliação final Rank e Resolve, recall e orçamento de falsos vínculos em VALIDATION e TEST selado antes de ativação.

Os testes devem verificar a decisão acima, inclusive os dois recortes e a origem proxy, sem exigir que TF=0 ou TF=1 vença antecipadamente. A implementação na PR #1215 ainda exige confirmação de compilação, uso pelo coordenador e gates do mesmo SHA; não se deve declarar a CI aprovada nem efetuar merge com base em revisão anterior.


## Correção de identidade do frame materno — PR #1215 (30/10/2026)

O commit `ea98edec7f35b92dfd2cdeba962f97b59d59562f` corrigiu a montagem de `NominalFrequencyReferenceContractV9.PopulationFrameFingerprint` em `FrozenIbgeNominalTfV9.MakeReference`: o nível `ExactLast` da mãe agora é identificado como `BR_TODOS`, em consonância com a consulta SQL de sobrenomes `tipo=SOBRENOME, sexo=TODOS, escopo=BRASIL`. Antes da correção, esse caminho usava indevidamente o rótulo `BR_FEMININO` de prenomes maternos, mesmo lendo sobrenomes `TODOS`.

A matriz canônica de recortes, fixada pelo ID/SHA-256 da versão publicada, é:

| Lado | Nível nominal | Marginal IBGE | Frame declarado |
| --- | --- | --- | --- |
| Pessoa | `ExactFirst` | Prenome, município São Paulo 3550308, TODOS | `SP_3550308_TODOS` |
| Pessoa | `ExactLast` | Sobrenome em qualquer posição, município São Paulo 3550308, TODOS | `SP_3550308_TODOS` |
| Mãe | `ExactFirst` | Prenome, Brasil, FEMININO | `BR_FEMININO` |
| Mãe | `ExactLast` | Sobrenome em qualquer posição, Brasil, TODOS | `BR_TODOS` |

`ExactLast` continua sendo uma **projeção do último sobrenome significativo**, cuja frequência é aproximada pela marginal IBGE **em qualquer posição**; o rótulo `BR_TODOS` não converte esse proxy em observação da última posição. O código não deve somar marginais como se fossem distribuição conjunta nem tratar frequências suprimidas como zero. `TF=0` continua neutro por escolha explícita do modelo; `TF>0` exige referência e unidade válidas.

O teste `Frozen_surname_proxy_uses_last_content_surname_and_distinct_ibge_frames` verifica explicitamente `BR_TODOS` para a mãe e `SP_3550308_TODOS` para a pessoa. A presença desse teste **não comprova sua execução**: compilação, CI e gates da HEAD exata da PR #1215 devem ser consultados antes de merge. A correção altera a identidade declarada do frame, não os dados SQL, o bootstrap sintético, a calibração ou a ativação do modelo.

**Guarda adicional de cobertura (commit `69ea6112`):** antes de materializar sobrenomes, `LoadAsync` agora exige metadados `ref.frequencia_nome_cobertura` da **mesma versão fixada** para `SOBRENOME/MUNICIPIO` (PARCIAL ou COMPLETA, ausência não publicada/suprimida) e `SOBRENOME/BRASIL` (COMPLETA, ausência não publicada/suprimida). A cobertura de `NOME` sozinha não autoriza frequência de `SOBRENOME`. Se a cobertura faltar, o carregamento falha fechado; nenhuma frequência é substituída por zero, nem há fallback para outra versão ou geografia. A cobertura municipal é declarada por escopo na tabela de metadados; as linhas efetivamente usadas continuam restritas no SQL a SP/3550308. A mudança precisa de teste físico SQL e CI na HEAD atual para ser considerada validada.



## Auditoria de integração — 30/10/2026, HEAD `bc24e341` (não certificada)

O schema canônico `20260912_Frequencia_Nomes_Cobertura.sql` define a cobertura por `(versao,tipo,escopo_geografico,inclui_sexo,inclui_periodo_nascimento)`, **sem coluna de município**. Assim, a verificação de cobertura `MUNICIPIO` em `LoadSurnameEntriesAsync` é **por escopo**, não uma declaração individual de cobertura para 3550308. A consulta de **valores** de sobrenomes da pessoa, separadamente, exige `uf_codigo='35'` e `municipio_codigo='3550308'`; a mãe usa `BRASIL/00/0000000`. Não alegar que o guard de metadados, isoladamente, certifica cobertura municipal específica. A suficiência/ausência de dados da cidade deve ser comprovada pelos dados congelados e por teste SQL físico, não por campo inexistente.

A exportação operacional `FrozenIbgeNominalTfV9.ExportForSnapshot(side)` na HEAD auditada usa apenas `personFirst/motherFirst` e o nível implícito `ExactFirst`. O preview `CreatePreview(..., ExactLast, ...)` usa corretamente `LastContentSurname` com fonte `IbgeSurnameAnyPositionProxy`, mas **não** demonstra que o proxy de sobrenome foi exportado, persistido ou consumido pelo `V9FittedSnapshotExecutionRuntime`. A integração do `ExactLast` no artefato operacional, com `u` condicionado aos pares do blocking TRAIN da mesma unidade, permanece **PENDENTE**. Não afirmar TF multicomponente em produção a partir deste preview.

Estado de evidência: **DECIDIDO** = marginais IBGE separadas e proxy explícito; **IMPLEMENTADO** = carregamento SQL, snapshot e preview `ExactLast`; **TESTADO** = somente o que constar dos checks/artefatos no SHA exato; **APROVADO** = depende dos gates estatísticos e DT-15; **ATIVADO** = não. A CI geral verde não substitui a prova física 30k, o TEST selado nem a autorização institucional.


### Exportação explícita de marginais (integrada à PR #1215, não ao runtime)

A implementação na branch `integration/v9-soft-merge-20261030` passa a exigir o nível nominal como argumento de `FrozenIbgeNominalTfV9.ExportForSnapshot(side, level)`. `ExactFirst` exporta somente prenomes com unidade `FirstName`; `ExactLast` exporta somente a marginal de sobrenomes em qualquer posição, identificada por `LastContentSurname` e `IbgeSurnameAnyPositionProxy`. `ExactFull` e `ExactFirstLast` são rejeitados. Os testes distinguem os quatro recortes, impedem a troca silenciosa de unidade e verificam a identidade de origem e o frame geográfico.

**Limite essencial:** `V9FellegiSunterAdaptiveCalibrationAdapter.ExportFrozenSnapshot` continua chamando explicitamente `ExactFirst`, porque o contrato portátil `V9SnapshotFirstNameTf` e `V9TrainSqlFirstNameUCalibration` estimam/consomem apenas a unidade de prenome. O novo export de sobrenomes **não é ativação nem integração operacional**: ainda faltam `u` da unidade `LastContentSurname` condicionado ao blocking TRAIN, esquema portátil compatível, execução Rank→Resolve, avaliação VALIDATION/TEST selada e aprovação DT-15. Nenhuma frequência de sobrenome foi interpretada como observação da última posição; nenhuma métrica de desempenho foi inferida.

Estado desta integração: **IMPLEMENTADO NA BRANCH DA PR #1215**, **NÃO TESTADO NA NOVA HEAD**, **NÃO APROVADO**, **NÃO ATIVADO**. A probe física anterior passou na HEAD `bc24e341`, mas não certifica o código novo; validar os checks e gates na HEAD resultante antes de qualquer merge.


## Contrato de operação, calibração e convergência — decisão de 30/10/2026

Esta seção explicita decisões de governança e planejamento; não certifica execução física, nem altera a política operacional.

### Três operações distintas

1. **Solicitar nova calibração:** ato explícito do operador. A decisão de recalibrar não é tomada automaticamente pelo sistema. A solicitação abre o fluxo governado de rascunho e validação; não ativa modelo.
2. **Descobrir e selecionar parâmetros:** após solicitação autorizada de calibração, o calibrador pesquisa e seleciona automaticamente a configuração segundo métricas, orçamento de falsos vínculos e critérios estatísticos fixados antes da avaliação. Essa seleção técnica não substitui a decisão do operador, a validação selada nem a autorização operacional. Não impor previamente que TF=0, TF=1 ou uma estratégia específica vença.
3. **Autorizar uso operacional:** ato explícito de governança, com participação do operador, após evidências exigidas, validação e rito DT-15. Aprovar um rascunho ou escolher parâmetros não equivale a ativar. A política inferior DT-10 permanece **default OFF** até autorização formal.

**Exceção delimitada:** bootstrap estatístico não é uma solicitação de recalibração operacional e não segue o rito de autorização de modelo; seus resultados não podem, isoladamente, promover um modelo a produção.

### Rascunho, validação e ativação

Registrar separadamente: quem solicitou a recalibração, identidade dos dados e do snapshot, espaço de busca e critérios pré-declarados, parâmetros selecionados, resultados TRAIN, VALIDATION e TEST selado, decisão do operador e autorização DT-15. Falhas de contrato, proveniência ou gates impedem a promoção (fail-closed). A seleção automática ocorre **dentro** da calibração solicitada, não na decisão de iniciar ou ativar.

### Convergência do desenvolvimento

O gargalo identificado é **dispersão**: calibração exploratória prolongada, PRs paralelos e documentação fragmentada; não se reduz a um problema exclusivamente documental. Congelar a expansão exploratória da V9 e concentrar o trabalho em um candidato identificável, integração, evidências na HEAD exata, TEST inédito selado e gates DT-15. Não abrir nova busca upper × lower antes de decisão explícita de retomada. Uma CI verde, um preview ou uma probe TRAIN não demonstram certificação.

### Entrega consolidada à PRODAM

Preparar especificação única com arquitetura, contratos de dados, APIs, instalação, segurança, operação, recuperação, rastreabilidade, calibração e critérios de aceite, distinguindo **decidido**, **implementado**, **testado**, **aprovado** e **ativado**. SQL local é ambiente de desenvolvimento; SQL Fabric Database é alternativa de banco SQL em homologação e produção, **sem dependência do Fabric Analytics**. A especificação não deve descrever a escolha automática de parâmetros como recalibração automática ou ativação automática.
