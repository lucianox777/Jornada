# GOV-CI-V9-P0-COORTE-TLS-20261113 — restaurar os dois gates da CI completa

**Autoridade:** Product Owner, 13/11/2026, America/Sao_Paulo. **Estado:** APROVADO; implementação em PR corretiva, sem alegar CI completa verde até sua execução no SHA integrado. **Política:** GOV-DOC-01.

## Contexto e falhas verificadas no SHA `785d4d2c6e7559618c1eb287ee8a3ce28c0d9c02`

Após uma sequência de integrações na `master`, o PO aprovou **priorizar a correção das duas incompatibilidades de CI completa**, sem modificar as campanhas físicas em andamento, o algoritmo de calibração ou o protocolo científico. As horas e o número de runs vermelhos apresentados em revisão são informações históricas a confirmar no GitHub Actions; **o código comprova as duas incompatibilidades**, mas não prova por si só o histórico de falhas.

1. `Solution/tests/Jornada.Tests/Unit/V9FellegiSunterAdaptiveCalibrationAdapterTests.cs`, teste `Source_recall_uses_eligible_observations_and_oracle_gold_collapses_duplicate_identity`, ainda esperava trecho `sem TEST`. O método `V9FrozenSourceResolveReplay.ReplayForPartition` passou a usar um erro padronizado de **coorte única com finalidade consistente e IDs distintos**; o método de VALIDATION já rejeitava toda coorte TEST. Corrigir **somente a asserção**, ampliando a prova negativa para mistura parcial TEST/VALIDATION, **sem mudar a rejeição científica ou as saídas**.
2. O workflow `.github/workflows/v9-operational-model-sql-smoke.yml`, introduzido com o conversor do vencedor, usa SQL Server 2022 **efêmero e privado ao loopback**, três ambientes `DEV`, senha SQL aleatória e mascarada e imagem fixada por digest. A política `source-sanity-gate.py` recusa deliberadamente TLS não validado fora de caminhos Development expressamente autorizados; faltava enquadrar esse workflow no mecanismo restritivo existente. A correção deve **manter o gate ativo**, aceitar exceção **exata** somente nesse smoke DEV e adicionar **prova fail-closed** dos controles essenciais (três ambientes DEV, runner hospedado, mapeamento só em 127.0.0.1, cliente loopback, senha aleatória mascarada antes do Docker, imagem SQL pinada, `--rm` e cleanup). Não adicionar exceção genérica, não permitir TLS enfraquecido em HML/PROD e não substituir o teste SQL físico por simulação.

## Decisão e precedência

Aprovada **uma única PR P0** para corrigir o contrato de teste do replay e enquadrar o TLS exclusivamente no ambiente sintético descartável do conversor. A segurança mantém o critério de rejeição fora da allowlist; a exceção é condicionada em código ao próprio conteúdo do workflow (mutações negativas são testadas). A conexão é confinada ao runner e o risco residual de SQL com TLS não autenticado é aceito **somente** para este smoke DEV local com dados artificiais, jamais como precedente para serviços de HML/PROD.

A decisão complementa a política de segurança `Solution/config/security/source-sanity-policy.json` e o contrato GOV-DOC-01. **Não substitui** as decisões do TEST único, do vencedor congelado, da paridade operacional e dos limites 315/360 minutos. Preserva TF4/324, CP/FDR/P95, SQL e replay do calibrador, TEST lacrado, runner GitHub hospedado, credenciais operacionais e autorização de disparo exclusivamente manual. As execuções #43499315560 e #43523989957 não são canceladas nem reprocessadas por esta correção.

## Critérios de aceite e rastreabilidade

- CI rápida: `unit-runtime`, `security-guards` e `deterministic-build` aprovadas na HEAD da PR.
- CI completa `jornada-ci`: executar os testes unitários de coorte, `source-sanity-gate.py --self-test`, detecção de regressões por remoção de DEV/loopback/mascaramento/efemeridade, compilação Release e demais gates sem relaxamentos.
- O smoke SQL real do conversor continua testável com a configuração DEV, sem expansão para produção; suas verificações materiais não são puladas.
- Após merge por squash: verificar o **SHA exato da master** e confirmar o resultado do `jornada-ci` de push antes de declarar restabelecimento da CI completa. CI rápida verde, isoladamente, **não é suficiente**.
- Entrega somente após atualização da documentação oficial e `Indice_Decisoes_Vigentes.md`, com PR, commit, gates e estado factual. Nenhuma execução física manual 100k é disparada pela correção.

**Implementação proposta:** `Solution/tests/Jornada.Tests/Unit/V9FellegiSunterAdaptiveCalibrationAdapterTests.cs`, `Solution/config/security/source-sanity-policy.json`, `Solution/scripts/source-sanity-gate.py`, `.github/workflows/ci.yml`, `.github/workflows/jornada-pr-required-fast.yml`. **Branch:** `fix/p0-master-full-ci-test-and-tls-20261113`. A evidência de CI completa e o SHA de merge devem ser anotados após execução, sem antecipar sua conclusão.

## Recibo da primeira correção e terceiro bloqueio revelado pela CI completa

A [PR #1499](https://github.com/lucianox777/Jornada/pull/1499) foi integrada por squash no SHA `0d1816112a93fef782f81ee8f24bd9e9bdd16b3a`. A [CI rápida #43533009703](https://github.com/lucianox777/Jornada/actions/runs/43533009703) passou nos três jobs (`unit-runtime`, `security-guards`, `deterministic-build`), incluindo o teste específico do replay e as provas adversariais do smoke DEV.

A [primeira jornada-ci de push nesse SHA, #43533273927](https://github.com/lucianox777/Jornada/actions/runs/43533273927), **aprovou o gate `security-guards` e os 2.225 testes unitários efetivamente executados**, mas revelou outro bloqueio preexistente, antes encoberto pelas duas falhas principais: **um teste `SQLSmoke` ignorado sem SQL descartável**. O job `unit-runtime` registrou `Passed: 2225; Skipped: 1; Failed: 0`, e o gate `--forbid-skipped` rejeitou corretamente a evidência. O teste `Operational_SQL_roundtrip_uses_immutable_one_winner_row_and_same_frozen_FS_decisions` está marcado `Category("SQLSmoke")` e só pode executar corretamente no workflow físico dedicado `v9-operational-model-sql-smoke.yml`, que cria SQL 2022 local efêmero, migra schema, e seleciona explicitamente essa fixture.

**Correção complementar necessária para cumprir a aprovação original de CI completa verde:** na seleção de `Jornada.Tests.csproj` do `unit-runtime` geral, excluir **somente `TestCategory=SQLSmoke`** além de `Integration`, preservando `--forbid-skipped`, quantidade mínima, evidência TRX, cobertura e a execução explícita obrigatória no workflow físico SQL. Não alterar o teste para `Pass`, não remover `Assert.Ignore`, não suprimir o teste físico, não liberar TEST ou produção. Um teste de contrato garante que o filtro e o workflow real continuem conectados.

A remediação complementar foi preparada na branch `fix/p0-ci-exclusive-sql-smoke-category-20261113`, em PR corretiva separada porque a #1499 já foi integrada. **Restabelecimento do `jornada-ci` permanece PENDENTE** de uma execução completa aprovada no SHA da master após essa integração; a CI #43533273927 não pode ser declarada verde.
