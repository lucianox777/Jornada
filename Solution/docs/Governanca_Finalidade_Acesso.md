# Governança de finalidade de acesso

Este documento registra um **gate de decisão institucional**. Ele não cria política pública, não resolve a deliberação do CCGD e não autoriza ampliar ou reduzir compartilhamento municipal por inferência técnica.

## Estado corrente da Fase 1

A implementação vigente autoriza consultas por **credencial autenticada, scopes e recurso aplicável**. Não existe, no contrato corrente, parâmetro livre `X-Jornada-Finalidade`, catálogo de finalidades escolhido pelo consumidor ou allowlist de finalidade enviada em cada requisição.

Essa ausência é deliberada enquanto permanece pendente a decisão normativa sobre como compatibilizar compartilhamento municipal, finalidade, necessidade e base legal. Portanto, a engenharia não deve introduzir silenciosamente um motivo textual livre por chamada como novo requisito de autorização.

## Regra de mudança

Qualquer exigência futura de finalidade depende de decisão normativa explícita e deve chegar à Solution por mudança versionada de contrato, modelo e auditoria. Até essa decisão existir, a política corrente deve permanecer estável e verificável por teste.

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

Este registro não fecha a decisão do CCGD. Não altera DDL, OpenAPI, runtime, política de compartilhamento, Fabric, `RELEASE_INFO.txt`, tag ou release. Também não define finalidade institucional, necessidade, base legal, owner de aprovação ou SLA.