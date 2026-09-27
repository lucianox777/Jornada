# Checkpoint técnico — 2026-09-27

**Escopo:** revisão documental independente do PR #526. **Decisão:** `ATUALIZACAO_DOCUMENTAL`; não representa homologação, release nem evidência de HML.

## Base e alterações

- Baseline: `master` no SHA de criação desta branch (conferir no histórico Git do PR).
- Desde o checkpoint anterior de 2026-09-21: consultar o histórico Git para a lista exaustiva de merges; este checkpoint não declara uma lista que não foi auditada.
- PR #526, blocking combinado e bootstrap IBGE: **aberto**, não integra esta baseline; seus testes, evidências e diagramas devem ser revisados no checkpoint posterior ao merge.
- Dependabot: PRs abertos de atualização de pacotes; não presumir compatibilidade entre versões maiores.

## Invariantes e rastreabilidade

- `INV-LINK-001`: blocking produz candidatos, nunca identidade. Prova atual: `Solution/docs/Conformidade_Invariantes.md` e testes do Linkage.
- `INV-LINK-003`: regras e parâmetros devem preservar versão e fingerprint. Prova atual: gates de proveniência/modelo.
- `INV-LINK-004`: execução incompleta não equivale a ausência de candidato. Prova atual: testes de timeout/limites.
- `INV-REPLAY-001`: replay preserva histórico e idempotência. Prova atual: `IdentityReplayInvariantTests.cs`.
- Nenhum invariante novo foi aprovado neste checkpoint. Alterações futuras de blocking devem preservar essas quatro propriedades.

## Schema, diagramas e gates

- Este PR não altera schemas, migrations, contratos executáveis ou diagramas.
- `Solution/docs/Diagramas_Rastreabilidade.json` permanece a referência de rastreabilidade dos diagramas. Não se declara freshness automática sem fonte verificável.
- Nenhum gate automático novo é criado; a moratória de `Documentos/Revisoes/README.md` permanece vigente.
- Os testes e CI deste PR são os checks efetivamente exibidos no GitHub; este checkpoint não atribui aprovação a jobs ainda não concluídos.

## Pendências externas e técnicas

- #378: autenticação PRODAM e separação ambiental; decisão externa.
- #379: políticas institucionais pendentes.
- #31: evidência com dados reais e validação independente do Linkage.
- #404: rastreabilidade documental e revisão dos diagramas seguem como trabalho contínuo.
- #494: transições semânticas de Linkage sem perda de auditoria por run.
- #526: avaliar separadamente após CI, benchmark sintético e validação independente.

## Divergências e próxima revisão

A documentação de blocking combinado está em branch aberta e **não** deve ser tratada como política operacional publicada. Na próxima revisão, conferir merge SHA, testes efetivamente verdes, evidências congeladas e mudanças de diagramas. Próximo checkpoint ordinário: 2026-10-04, ou antes se ocorrer mudança estrutural material.
