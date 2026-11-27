# GOV-M1-ESCOPO-AA01-CRA1-20261127 — escopo factual inicial do M1

**Data:** 27/11/2026. **Autoridade:** Product Owner. **Estado:** DECIDIDO; registro documental em branch, sincronização dos índices e integração protegida pendentes. **Política:** GOV-DOC-01.

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
- **Documentação:** este arquivo deve ser incorporado com apontadores em `Indice_Decisoes_Vigentes.md` e `Indice_Acervo_Documental.md` no fluxo protegido. Não declarar registro integral concluído antes da sincronização e integração.
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

A [preparação de conformidade factual AA01/CRA1](Preparacao_M1_Conformidade_AA01_CRA1_20261127.md) registra 141 casos de schema avaliados offline e um confronto NUnit candidato com o validador real, ainda não compilado/executado. Essa evidência não amplia contratos, não altera autorização, não acrescenta gate obrigatório e não declara M1/E2E PASS. Os índices e a integração documental protegida permanecem pendentes.
