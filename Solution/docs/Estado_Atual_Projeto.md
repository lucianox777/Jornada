# Estado atual — conferência técnica documental de 28/09/2026

**Escopo:** fotografia de implementação sobre a candidata técnica `master`, conferida com código, PRs e [CI verde do PR #568](https://github.com/lucianox777/Jornada/actions/runs/36348920753). Este arquivo não substitui consulta ao HEAD, aos gates institucionais nem ao [Plano de Desenvolvimento](Plano_Desenvolvimento.md).

## Publicação e candidato

`RELEASE_INFO.txt` continua sendo a autoridade para a **última release selada de engenharia v4.05 (SolutionSchema 3.69)**. `LEIA-ME.txt` declara **candidata técnica v5.00**, RC `v5.00-rc.1` cortada com efeito de release `NONE`, e **SolutionSchema técnico 3.70**. A Especificação Técnica v5.00 permanece **candidata**, não publicação normativa final; a versão materializada histórica permanece referenciada em `Documentos/README.md`.

## Estado técnico comprovado e limites

- **DT-04:** `Jornada.Access.Security` centraliza `X-Jornada-Access-Key`, `AuthenticationHandler`, scopes/policies nas duas APIs; [PR #510](https://github.com/lucianox777/Jornada/pull/510). Os gates de matriz e testes HTTP cobrem negações. **HML/Produção permanecem deny-by-default** até IdP corporativo e aceite da issue #378.
- **DT-03:** OpenAPI v1 contém **22 operações com contratos tipados** para sucesso/erro e regressão DTO/HTTP; [PR #517](https://github.com/lucianox777/Jornada/pull/517).
- **Busca síncrona:** `POST /api/v1/identidade/candidatos` está implementado em `Jornada.Api`, com scope dedicado, auditoria anterior à resposta, até cinco candidatos sem score, sem CPF/UUID visível e com `nenhumDestes`. **Restrito a Development e gate de modelo/feature elegível**; não equivale a endpoint FHIR `Patient/$match` publicado ou ativação institucional HML.
- **DT-05:** ledger semântico append-only, assinatura V1, guardas de transação/run `EXECUTANDO` e E2E de **três ondas e CPF tardio real em massa sintética** [PR #540](https://github.com/lucianox777/Jornada/pull/540) implementados. **Ainda faltam** manifesto NAS automaticamente vinculado, replay histórico determinístico, ensaios de concorrência/retenção e medição de custo.
- **DT-10:** Runner utiliza `identidade.sp_publicar_resolucao_progressiva_linkage_lote`; o cursor por origem foi retirado e há equivalência escalar/idempotência/precedência exercitadas em SQL. **O aceite integral ainda exige** concorrência adversarial, rollback sob falha e comparação física de plano/volumetria.
- **Blocking D/C/D∪C:** [PR #567](https://github.com/lucianox777/Jornada/pull/567) integrou diagnóstico sintético no mesmo universo; [PR #568](https://github.com/lucianox777/Jornada/pull/568) acrescentou auditoria SQL opcional somente leitura com contagens compartilhadas e latências observadas. **Não** publica passes C no Runner nem homologa equivalência populacional.

`codigoPessoaOrigem` permanece **opcional**. A aderência integral da Gold à hierarquia de quatro níveis por atributo, a reavaliação de referências candidatas, o dossiê decisório DT-15 e a validade estatística independente #31 continuam a exigir conferência/implementação específicas. A Gold é representação revisável, não certificação civil.

**Fases aprovadas:** DEV → Ensaio único → HML → Produção. O Ensaio técnico exige massa sintética, funcionalidades, contratos e controles já prontos; HML requer massa das Secretarias, condições institucionais e segurança homologadas. CI verde, massa sintética e página master somente leitura em DEV não autorizam HML/Produção.

## Próximos gates

Seguir [prioridades e dependências](Plano_Desenvolvimento.md), [inventário DT com critérios verificáveis](Dividas_Tecnicas.md), [contrato de Ensaio](Ensaio_Unico_Paridade_HML.md) e [governança de decisão do modelo](DT15_Governanca_Decisao_Modelo.md). DT-01/09 exigem evidência CONFORME por modelo para promoção; DT-14 esclareceu o runbook, **sem retirar o gate de código**. DT-02 (.NET 10), DT-05 replay NAS, DT-10 concorrência/desempenho e DT-15 HML/Produção continuam abertos. A [DT-07 teve auditoria documental concluída](DT07_Conferencia_Drift_20260928.md), sem encerrar a triagem histórica de segredos #405; a DT-08 já tinha aceite técnico no PR #508. A DT-16 [PR #580](https://github.com/lucianox777/Jornada/pull/580) foi revertida em 30/09/2026 antes de qualquer migração física: as cinco solutions auxiliares e o gate específico foram removidos, e `Jornada.sln` voltou a ser a única solution suportada. A matriz transversal foi renumerada **DT-17**.
