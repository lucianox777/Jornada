# Governança de finalidade de acesso

Este documento registra um **gate de decisão institucional**. Ele não cria política pública, não resolve a **decisão institucional de finalidade, necessidade e base legal** e não autoriza ampliar ou reduzir compartilhamento municipal por inferência técnica. A governança formal do Programa Reencontro, conforme arts. 7º–9º do Decreto municipal nº 62.149/2023, é o **Núcleo Gestor do Programa Reencontro**, coordenado pela **SGM/SEPE**, com suporte de um Núcleo Técnico. Foi anunciada a publicação de **portaria que atribuirá competência ao Núcleo Gestor sobre o Projeto Jornada**. Até a publicação e conferência de seu alcance, essa competência adicional é **prevista, não vigente**; a decisão institucional de finalidade/base legal permanece pendente.

**Encaminhamento institucional:** **Núcleo Gestor do Programa Reencontro**, por sua coordenação **SGM/SEPE**. **Interlocução técnica:** Núcleo Técnico do Programa Reencontro. Essas referências não substituem as autorizações dos respectivos gestores de dados nem antecipam o conteúdo da portaria ainda não publicada.

**Limite funcional — RN de negócio, linha 23:** [01_Requisitos_de_Negocio_Jornada_v1.1.md](../../Documentos/Requisitos/01_Requisitos_de_Negocio_Jornada_v1.1.md): “a Jornada referencia, integra e informa; não concede benefício nem altera automaticamente o sistema finalístico”. A governança do acesso e a resolução de identidade não autorizam alteração de decisões dos sistemas de origem.

## Instâncias e fundamento normativo

- **Núcleo Gestor do Programa Reencontro** — Decreto municipal nº 62.149/2023, arts. 7º–8º: coordena planejamento e execução do Programa, sob coordenação SGM/SEPE. Receberá competência específica sobre o Projeto Jornada conforme portaria anunciada, **ainda não publicada**.
- **Núcleo Técnico** — art. 9º do mesmo decreto e Portaria SGM/SEPE nº 2/2026: presta suporte técnico ao Núcleo Gestor; não é instância autônoma de aprovação jurídica.
- **Integração do Programa** — art. 10 do Decreto nº 62.149/2023: a SEPE promove a integração de dados dos sistemas sobre a população em situação de rua; o dispositivo, isoladamente, não autoriza estender a finalidade a benefícios de outros públicos.
- **CCGD** — Decreto nº 60.663/2021: instância municipal de governança de dados, separada da SEPE e do Programa Reencontro. A [Resolução SGM/CCGD nº 2/2022](https://legislacao.prefeitura.sp.gov.br/resolucao-secretaria-de-governo-municipal-sgm-ccgd-2-de-22-de-maio-de-2022) disciplina categorização, sigilo, segurança, auditabilidade e compartilhamento. O art. 11 exige permissão expressa do gestor de dados para compartilhamento específico.

**Estado institucional em 28/09/2026:** portaria de competência sobre a Jornada **anunciada, mas ainda não publicada**. Não antecipar sua vigência nem presumir base legal uniforme para todas as Secretarias; conservar o gate de finalidade/base legal **PENDENTE**.

## Estado corrente da Fase 1

A implementação vigente autoriza consultas por **credencial autenticada, scopes e recurso aplicável**. Não existe, no contrato corrente, parâmetro livre `X-Jornada-Finalidade`, catálogo de finalidades escolhido pelo consumidor ou allowlist de finalidade enviada em cada requisição.

Essa ausência é deliberada enquanto permanece pendente a decisão normativa sobre como compatibilizar compartilhamento municipal, finalidade, necessidade e base legal. Portanto, a engenharia não deve introduzir silenciosamente um motivo textual livre por chamada como novo requisito de autorização.

## Regra de mudança

Qualquer exigência futura de finalidade depende de deliberação institucional formal sobre o Projeto Jornada, cuja atribuição ao Núcleo Gestor está prevista em portaria **a publicar**, e deve chegar à Solution por mudança versionada de contrato, modelo e auditoria. A futura competência sobre o projeto não dispensa identificar responsabilidades legais e autorizações dos gestores de dados de cada Secretaria conforme a categoria do compartilhamento. Até essa decisão existir, a política corrente deve permanecer estável e verificável por teste.

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
**Rastreio de nomenclatura e compatibilidade:** [Decreto nº 62.149/2023, arts. 7º–10](https://legislacao.prefeitura.sp.gov.br/decreto-62149-de-24-de-janeiro-de-2023); [Portaria SGM/SEPE nº 2/2026 (Núcleo Técnico)](https://legislacao.prefeitura.sp.gov.br/portaria-secretaria-de-governo-municipal-sgm-sepe-2-de-15-de-setembro-de-2026/consolidado); [Decreto nº 60.663/2021](https://legislacao.prefeitura.sp.gov.br/decreto-60663-de-25-de-outubro-de-2021); [Resolução CCGD nº 2/2022](https://legislacao.prefeitura.sp.gov.br/resolucao-secretaria-de-governo-municipal-sgm-ccgd-2-de-22-de-maio-de-2022); [issue #503](https://github.com/lucianox777/Jornada/issues/503). A chave legada de máquina `GTPR_PURPOSE_LEGAL_BASIS_DECISION` permanece temporariamente como identificador técnico de gate no manifesto/RC; **não** é nome da instância competente. A referência humana é o **Núcleo Gestor do Programa Reencontro**, por sua coordenação SGM/SEPE, com interlocução técnica do Núcleo Técnico. A competência específica da Jornada aguarda publicação da portaria; a decisão de finalidade/base legal permanece pendente. Renomear chave apenas em mudança coordenada de contratos, testes e evidência RC; manter gate PENDENTE.
