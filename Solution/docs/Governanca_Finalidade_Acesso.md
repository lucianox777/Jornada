# Governança de finalidade de acesso

Este documento registra um **gate de decisão institucional**. Ele não cria política pública, não resolve a **decisão institucional de finalidade, necessidade e base legal** e não autoriza ampliar ou reduzir compartilhamento municipal por inferência técnica. A governança formal do Programa Reencontro, conforme arts. 7º–9º do Decreto municipal nº 62.149/2023, é o **Núcleo Gestor do Programa Reencontro**, coordenado pela **SGM/SEPE**, com suporte de um Núcleo Técnico. A competência específica para aprovar a política de compartilhamento da Jornada **não foi comprovada** por esse decreto e depende de identificação e ato institucional próprios.

**Interlocução administrativa:** Coordenação do Programa Reencontro (SEPE). A denominação não cria órgão novo nem transfere a competência de autorização das Secretarias ou da instância que venha a ser formalmente definida.

**Limite funcional — RN de negócio, linha 23:** [01_Requisitos_de_Negocio_Jornada_v1.1.md](../../Documentos/Requisitos/01_Requisitos_de_Negocio_Jornada_v1.1.md): “a Jornada referencia, integra e informa; não concede benefício nem altera automaticamente o sistema finalístico”. A governança do acesso e a resolução de identidade não autorizam alteração de decisões dos sistemas de origem.

## Estado corrente da Fase 1

A implementação vigente autoriza consultas por **credencial autenticada, scopes e recurso aplicável**. Não existe, no contrato corrente, parâmetro livre `X-Jornada-Finalidade`, catálogo de finalidades escolhido pelo consumidor ou allowlist de finalidade enviada em cada requisição.

Essa ausência é deliberada enquanto permanece pendente a decisão normativa sobre como compatibilizar compartilhamento municipal, finalidade, necessidade e base legal. Portanto, a engenharia não deve introduzir silenciosamente um motivo textual livre por chamada como novo requisito de autorização.

## Regra de mudança

Qualquer exigência futura de finalidade depende de decisão normativa explícita da **instância institucional competente, a confirmar formalmente com SGM/SEPE**, e deve chegar à Solution por mudança versionada de contrato, modelo e auditoria. O Núcleo Gestor coordena o Programa Reencontro; não presumir, sem delegação/documento específico, que isso lhe atribua competência exclusiva para determinar a base legal de todas as Secretarias. Até essa decisão existir, a política corrente deve permanecer estável e verificável por teste.

Se a decisão institucional vier a exigir finalidade, a implementação preferencial deve evitar texto livre controlado pelo chamador. A finalidade governada deve ser vinculada à **credencial/contrato de projeção autorizado**, com identidade/versionamento persistente e trilha de auditoria capaz de demonstrar qual regra estava vigente no instante do acesso.

Esse desenho preferencial é uma restrição de engenharia contra bypass e ambiguidade; não antecipa quais finalidades serão permitidas, quem as aprovará, qual base legal será aplicável ou se finalidade será de fato exigida.

## Invariantes protegidas

Enquanto a decisão institucional estiver pendente:

1. o OpenAPI não deve expor `X-Jornada-Finalidade` como parâmetro de requisição;
2. o runtime não deve ler `X-Jornada-Finalidade` para autorizar consultas;
3. fixtures de Development não devem criar catálogo/allowlist de finalidade;
4. autorização continua sendo expressa por credencial, scopes, recurso e restrições versionadas de projeção;
5. eventual evolução deve ser explícita, versionada e acompanhada de auditoria persistente — nunca uma alteração silenciosa no comportamento das APIs.

## Limites

Este registro não fecha a **decisão institucional pendente de finalidade/base legal**. Não altera DDL, OpenAPI, runtime, política de compartilhamento, Fabric, `RELEASE_INFO.txt`, tag ou release. Também não define finalidade institucional, necessidade, base legal, owner de aprovação ou SLA.
**Rastreio de nomenclatura e compatibilidade:** [Decreto nº 62.149/2023, arts. 7º–9º](https://legislacao.prefeitura.sp.gov.br/decreto-62149-de-24-de-janeiro-de-2023); [Portaria SGM/SEPE nº 2/2026 (Núcleo Técnico)](https://legislacao.prefeitura.sp.gov.br/portaria-secretaria-de-governo-municipal-sgm-sepe-2-de-15-de-setembro-de-2026/consolidado); [issue #503](https://github.com/lucianox777/Jornada/issues/503). A chave legada de máquina `GTPR_PURPOSE_LEGAL_BASIS_DECISION` permanece temporariamente como identificador técnico de gate no manifesto/RC; **não** é nome da instância competente. A referência humana neste documento é **Coordenação do Programa Reencontro (SEPE)**, mantida distinta do Núcleo Gestor formal e da aprovação institucional ainda pendente. Renomear chave apenas em mudança coordenada de contratos, testes e evidência RC; manter gate PENDENTE.
