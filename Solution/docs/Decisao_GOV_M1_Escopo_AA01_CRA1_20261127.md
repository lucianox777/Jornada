# GOV-M1-ESCOPO-AA01-CRA1-20261127 — escopo factual inicial do M1

**Data:** 27/11/2026. **Autoridade:** Product Owner. **Estado:** DECIDIDO; registro documental e apontadores nos dois índices na PR #1946; integração protegida pendente. **Política:** GOV-DOC-01.

## Fonte, contexto e motivo

O PO respondeu à proposta do ChatGPT de ampliar a preparação do M1 aos seis contratos factuais: “Não. Só vamos trabalhar com os dois validados por enquanto. Continue com trabalho útil na frente M1.” A referência inequívoca são os dois contratos já exercitados no kit auxiliar: AA01 v1 (BENEFICIO, fixture SEHAB) e CRA1 v1 (SERVICO, fixture SMADS).

A decisão delimita a entrega inicial e rejeita, neste momento, a ampliação aos demais contratos. A expressão “validados” refere-se às verificações offline já relatadas dos exemplos e JSON Schemas; não afirma homologação institucional, aprovação do catálogo SQL, execução HTTP/SQL completa ou certificação M1.

## Decisão

1. Trabalhar, por enquanto, exclusivamente com os contratos factuais **AA01 v1 e CRA1 v1** na preparação M1 autorizada.
2. Não acrescentar AR01, CAS1, CPO1, POT1 ou novos Tipos como entregas ou pré-condições deste incremento. Retomada de outros contratos depende de nova orientação do PO.
3. Preservar os schemas, versões, bytes dos exemplos existentes e os contratos do produto. Os contratos cadastrais de Pessoa necessários aos dois fluxos continuam aplicáveis, sem representar novos Tipos factuais.
4. Preservar GESTOR e suas permissões existentes. A limitação de contratos não remove GESTOR e não autoriza seu uso como fallback no Apoio centralizado; permanece a regra da #1194.
5. Continuar preparação útil do M1 sem alterar a prioridade vigente da Fase A, a branch #1940, workflows, parâmetros científicos ou ambiente protegido.

## Cobertura, segurança e precedência

A recomendação anterior de condicionar o M1 à cobertura técnica de todos os seis contratos foi **proposta não aprovada** e está rejeitada para esta entrega. Esta decisão não revoga controles de segurança, o requisito de autorização por recurso da #1194, a proteção de `jornada-local` ou os gates do mandato E2E vigente.

A configuração anterior do probe auxiliar exigia quatro recursos distintos. Não inventar recursos ou contratos para satisfazê-la. Preparar um modo dirigido aos dois fluxos autorizados e explicitar quais distinções de autorização não são exercitadas. AA01/SEHAB e CRA1/SMADS diferem simultaneamente em natureza, recurso e Gestor; sua recusa cruzada, isoladamente, não prova isolamento entre dois benefícios do mesmo Gestor. Lacunas devem permanecer NÃO EXERCITADAS, nunca PASS ou dispensa implícita. Se uma prova de segurança indispensável não puder ser produzida no escopo, apresentar a limitação ao PO, sem alargar o escopo ou liberar acesso inseguro.

O exemplo cadastral SMS preservado em revisões anteriores é histórico e não acrescenta um terceiro fluxo ativo à preparação decorrente desta decisão. Não alterar nem apagar requisitos gerais do produto ou testes históricos por causa desta delimitação.

## Estados e critério de conclusão

- **DECIDIDO:** escopo factual inicial AA01/CRA1.
- **Preparação auxiliar:** artefatos candidatos no chat; testes offline não equivalem a testes do produto.
- **Produto/HTTP/SQL/CI:** nenhum PASS novo decorre desta decisão.
- **Documentação:** este arquivo e apontadores em `Indice_Decisoes_Vigentes.md` e `Indice_Acervo_Documental.md` constam do diff documental #1946. A revisão, checks e integração protegida são pendentes; não declarar registro integral concluído antes da integração.
- **Ativação:** nenhuma; HML/PROD e dados reais permanecem fora do escopo.

## Rastreabilidade

- [GOV-DOC-01](Politica_Registro_Decisoes_Discussoes.md).
- [Mandato E2E contínuo](Decisao_GOV_V9_Mandato_E2E_Continuo_20261126.md), preservado fora do recorte aqui decidido.
- [#1194 — autorização por recurso](https://github.com/lucianox777/Jornada/issues/1194).
- [#1853 — Resultado.Api](https://github.com/lucianox777/Jornada/issues/1853).
- [#1855 — Apoio e ZIP](https://github.com/lucianox777/Jornada/issues/1855).
- [#1851 — checkpoint do executor](https://github.com/lucianox777/Jornada/issues/1851), sem transformar registro auxiliar em checkpoint.
- Base documental examinada: `250aedc5f71aec2fd54f7af80b404450d800ce08`.
- Kit anterior: `Jornada_M1a_Kit_Preliminar_rev06_20261127.zip`, SHA-256 `4da6b1e40c4024881615b50f8e18515f7eb6c75ae69d076dea9e32356d51bc4b`.

## Evidência técnica posterior — sem nova decisão

A [preparação de conformidade factual AA01/CRA1](Preparacao_M1_Conformidade_AA01_CRA1_20261127.md) registra 141 casos de schema avaliados offline e um confronto NUnit candidato com o validador real, ainda não compilado/executado. Essa evidência não amplia contratos, não altera autorização, não acrescenta gate obrigatório e não declara M1/E2E PASS. Os apontadores foram acrescentados nesta PR; a integração documental protegida permanece pendente.

## Continuidade da preparação enquanto o Codex avalia o V9

**Ratificação do PO em 27/11/2026 (BRT).** Fonte: “Mandei o comando para avaliar antes da validação independente e pode continuar aí o que deve ser feito na M1 enquanto isso. Concorda?” A autorização de continuidade da preparação M1 é expressa. O relato sobre o comando enviado ao Codex não é tratado aqui como aprovação de validação independente, da variante de identidade ou dos gates da Fase A.

**Contexto e alcance.** Avançar na integração HTTP já delimitada, enquanto o executor local conduz a avaliação V9. Ratifica os itens 1–5 desta decisão; não altera a ordem formal de integração, não cria outro executor na #1940, não amplia contratos ou autoriza escrita no ambiente protegido. GESTOR preservado; nenhum fallback GESTOR no Apoio. A revisão técnica abaixo orienta o aproveitamento dos materiais existentes, sem nova regra de negócio aprovada. Os dois índices já apontam para este documento e mantêm o mesmo alcance e precedência.

### Revisão focal da primeira entrega funcional da #1194

**Base reaberta:** master `250aedc5f71aec2fd54f7af80b404450d800ce08`; #1940 em `a651d6fa2cf434bcce08187528fb0211dd4f0fac`; #1946 em `c1a132093edb8c6ee9ea776f497682390dd487f7` antes deste registro. Reaberta também a especificação técnica da #1194 já entregue no kit, para não recriar roteiro ou suíte paralelos. Natureza: inspeção estática dirigida, não teste de execução ou demonstração de exploração.

| Caminho conferido | Evidência no código | Consequência para o primeiro incremento |
|---|---|---|
| `JornadaAccessSecurity.Permissions` | `ingestao.write/status` continuam com `AllowType=false`. | Não abrir isoladamente o handler; a habilitação precisa acompanhar o isolamento abaixo. |
| `IngestionApi` e `MunicipalAccessPolicyEngine` | Pré-checagem com recurso nulo; depois manifesto mínimo e checagem por `CodigoTipo`. A política não cruza por si só toda a natureza/versão/origem. | Reaproveitar a rejeição precoce e vincular a autorização do recurso ao contexto autenticado e ao catálogo, sem mudar a política global de Pessoa. |
| `SqlIngestionService.GetStatusAsync` | Filtra por ID da Entrega e Gestor, sem recorte por Tipo/natureza; lê erro de lote separadamente. | Antes de permitir Tipo, restringir o domínio da Entrega; preservar separação de leituras e regressões de concorrência. |
| `ReceivePackageAsync` — replay | Busca a chave por Gestor; SHA e bytes iguais retornam o recibo existente. | Conferir autoridade sobre a Entrega persistida antes de devolver recibo; não trocar o domínio da idempotência por conveniência. |
| `ReceivePackageAsync` — armazenamento | `PutIfAbsentAsync` ocorre antes de `ResolveContextAsync`; o código admite órfão Bronze para GC em falha SQL. | Distinguir negativa de autorização de falha posterior de catálogo/armazenamento. Revisar o ponto da guarda de recurso e testar seus efeitos. Não remover a coordenação Shared/Exclusive com GC nem prometer ausência de objeto em toda resposta de erro. |
| `ResultadoAccessVerifier` | Rejeita não GESTOR, encaminha `X-Jornada-Gestor`; lookup e contagem são por Gestor/nome. | Adaptar a delegação com o tipo real e restringir seleção/contagem antes do retorno; não fabricar contexto GESTOR. |

**Sequência técnica recomendada, sem habilitação nesta revisão:** preparar o isolamento de status e replay junto à autorização de recepção; executar os testes positivos/negativos do recurso e regressão GESTOR; somente então integrar a abertura das duas permissões e a atualização coerente de matriz/contratos. Coordenar Resultado.Api e o cliente Apoio nos requisitos existentes. `enviar=false` permanece até as condições da #1194. Não tratar esta sequência como nova autorização de merge ou de execução paralela no produto.

**Regressões a aproveitar e completar:** os três testes existentes em `IngestionStatusConcurrencyTests` cobrem leitura durante locks, ausência de deadlock e preservação do erro, com contexto GESTOR. Acrescentar ao ciclo da #1194 os positivos de AA01/CRA1, recusas de scope e recurso, replay autorizado com mesmo ID, conflito de payload e impossibilidade de ler Entrega alheia. As negativas precisam de pré-condições válidas para não passar apenas porque a credencial foi rejeitada na borda. Lacunas de isolamento entre dois recursos do mesmo Gestor continuam as já declaradas acima; não inventar terceiro contrato. Separar efeitos em Staging, objeto Bronze, Entrega/lote e Gold; a auditoria sanitizada de uma negativa não é escrita de negócio proibida. Não alterar implicitamente cadastro puro, versões ENCERRADA, códigos HTTP, chaves existentes ou demais decisões.

**Fontes exatas no SHA-base:** [handler](https://github.com/lucianox777/Jornada/blob/250aedc5f71aec2fd54f7af80b404450d800ce08/Solution/src/Jornada.Access.Security/JornadaAccessSecurity.cs) (blob `ad016cce5df98c0d20ca4832ff85dee25543d29e`); [borda HTTP](https://github.com/lucianox777/Jornada/blob/250aedc5f71aec2fd54f7af80b404450d800ce08/Solution/src/Jornada.Api/IngestionApi.cs) (`5c9d24c0092a2b71b2ca0a418826d0a576557e10`); [política](https://github.com/lucianox777/Jornada/blob/250aedc5f71aec2fd54f7af80b404450d800ce08/Solution/src/Jornada.Api/AccessPolicy.cs) (`2a1ccd4fa32e2c98d68ad6353b3d2aefb364d082`); [serviço SQL](https://github.com/lucianox777/Jornada/blob/250aedc5f71aec2fd54f7af80b404450d800ce08/Solution/src/Jornada.Api/SqlApiServices.cs) (`64feee826e8fd92aba8149abfae29c4a5493ef24`); [Resultado.Api](https://github.com/lucianox777/Jornada/blob/250aedc5f71aec2fd54f7af80b404450d800ce08/Solution/src/Jornada.Resultado.Api/Program.cs) (`3cb074c389131c85623a41e8fefe572ec45fe4a2`); [regressões de status](https://github.com/lucianox777/Jornada/blob/250aedc5f71aec2fd54f7af80b404450d800ce08/Solution/tests/Jornada.Integration.Tests/Integration/IngestionStatusConcurrencyTests.cs) (`e7d0c95c5e239b1d99ab171be23dded9184931b3`).

**Estado desta continuidade:** revisão e ratificação documentadas em PR; nenhum código funcional alterado, nenhum teste .NET/HTTP/SQL executado e nenhum M1/E2E PASS novo. Não há acesso à sessão WSL do PO. A #1946 permanece documental e com integração protegida pendente. A etapa útil seguinte é transformar o recorte de autorização em alteração funcional testada no ciclo autorizado, não produzir novos kits sobre os mesmos requisitos.
