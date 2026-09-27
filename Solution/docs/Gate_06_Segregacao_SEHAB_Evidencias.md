# Gate 6 — segregação do integrador e contratos da SEHAB

**Data:** 27/09/2026. **Resultado técnico:** SEGREGAÇÃO E E2E SINTÉTICO CONCLUÍDOS; [PR #541](https://github.com/lucianox777/Jornada/pull/541) mergeado (`90d24f9`) com os seis workflows pós-merge verdes. **Não equivale a aprovação HML, homologação do mapeamento real da Secretaria nem liberação automática do Ensaio.**

## Inventário e fronteira

- O [commit inicial `2def00bf927b15966e924164810bb807dbe6b6cb`](https://github.com/lucianox777/Jornada/commit/2def00bf927b15966e924164810bb807dbe6b6cb) iniciou a transferência byte a byte dos cinco arquivos de `Jornada.Integrador.CSharp`, seus três arquivos de testes, sete contratos Pessoa SEHAB v1–v5, metadado específico AA01 e exemplo de configuração. Destino: `ApoioSecretarias/`. Os contratos permaneceram com os hashes do inventário original, sem aprovação inventada.
- A solução independente `ApoioSecretarias/SolucaoApoioSecretarias.sln` compila o transmissor e os testes, e entrega o preparador `preparador/preparador.py` para CSV de origem com mapeamento explícito. O preparador valida JSON Schema e SHA-256, requer motivo de ausência de CPF **declarado pela origem** e produz ZIP determinístico com os três arquivos canônicos. `Solution/Jornada.sln` não contém o transmissor; perfis Production e imagens de cluster não o incluem.
- `Solution/config/governance/schema-approvals.json` retém 38 itens dos demais gestores e tipos de registro. `ApoioSecretarias/config/governance/schema-approvals.SEHAB.json` guarda os sete contratos Pessoa e o metadado AA01. A migração operacional `Solution/database/migrations/20260921_Pessoa_V5_Contrato_371.sql` registra somente SMADS, SMDET e SMS v5; `ApoioSecretarias/database/migrations/Registrar_SEHAB_Pessoa_v5.sql` registra SEHAB v5 externamente em **RASCUNHO**, após validar digest, sem ativar contratos pendentes.

A separação exigida é de **solução/build/distribuição**: as duas soluções coexistem no mesmo repositório Git. Se o modelo institucional exigir também outro Git remoto, sua criação e configuração são entregas de publicação independentes; não se deve distribuir `ApoioSecretarias/` como parte do instalador da Jornada.

## Evidência executada com SQL/HTTP reais e massa exclusivamente sintética

[Workflow `jornada-ci` no commit `1a34ebd24c4518e1da8580e2c4a873f837045937`](https://github.com/lucianox777/Jornada/actions/runs/36302605573), job `e2e` **success**. O artefato `local-e2e-evidence` é anexado ao workflow; todos os resultados foram obtidos no receptor SQL `JornadaE2E`, staging/Bronze isolados, sem dados de cidadãos:

| Gestor | Fluxo comprovado | Resultado | Bronze | Silver Pessoa | Silver Registro |
|---|---|---|---:|---:|---:|
| SEHAB | CSV mapeado → ZIP v4 → transmissor C# → HTTP autenticado → Processor → vínculo | `PROCESSADA` | 1 | 2 | 0 |
| SMADS | CRA1 v4 → HTTP → Processor | `PROCESSADA` | 1 | 1 | 1 |
| SMDET | cadastro v4 → HTTP → Processor | `PROCESSADA` | 1 | 1 | 0 |
| SMS | cadastro v4 → HTTP → Processor | `PROCESSADA` | 1 | 1 | 0 |

A remessa SEHAB registrou **um vínculo de identidade progressiva**, com ZIP SHA-256 `d836c24dce0bdf50c073b091520e5cebf411e7e491b022fe4b7fd31107df95c5`; o SHA transmitido foi comparado ao do arquivo preparado. Recibos `entregaId` independentes, estados finais e hashes Pessoa v4 de SMADS/SMDET/SMS constam nos JSONs de evidência. O E2E original da Jornada também verificou recebimento, retransmissão idempotente, Bronze, Gold, Serving e retorno HTTP.

[Workflow `apoio-secretarias` no mesmo commit](https://github.com/lucianox777/Jornada/actions/runs/36302605596) **success**: sete contratos Pessoa SEHAB e metadado AA01 íntegros, preparador e regressões positivas/negativas, build .NET bloqueado e testes C# do transmissor; além de verificar os **21 arquivos contratuais Pessoa** v1–v5 remanescentes de SMADS, SMDET e SMS e seus SHA-256. A [Action do schema inventory](https://github.com/lucianox777/Jornada/actions/runs/36302605599), [installer Windows](https://github.com/lucianox777/Jornada/actions/runs/36302605671) e [cluster](https://github.com/lucianox777/Jornada/actions/runs/36302605662) passaram no mesmo SHA.

## Auditoria de referências SEHAB e regressão geral

As referências de execução, registro de versão v5, inventário e configuração de produção foram removidas da solução principal. Rótulos `SEHAB` permanecem em `Jornada_Seed_Dev.sql`, testes, fixtures, avaliação sintética e scripts de DEV (não representam dependência operacional). O único literal em `Jornada.Api/*` é `placeholder="SEHAB"` no campo visual do monitor: não define credencial, autorização, schema, encaminhamento ou seleção de Gestor. Esse arquivo é protegido pela solicitação; nenhuma alteração foi realizada nele. Documentos históricos AS-IS não são incluídos na distribuição externa.

A primeira rodada da suíte geral obteve 130/131 testes SQL com uma falha `Contrato referenciado pelo banco não encontrado` no replay Bronze: um teste ainda não importava o contrato externo no diretório de saída. O [commit `839119706ccbda69e54dad8a049560e32dc2c082`](https://github.com/lucianox777/Jornada/commit/839119706ccbda69e54dad8a049560e32dc2c082) corrige **somente a fixture** `Solution/tests/Jornada.Integration.Tests.csproj`, fazendo link de leitura do contrato do suporte ao output de teste, sem copiá-lo ao código-fonte principal. A [revisão final do PR #541](https://github.com/lucianox777/Jornada/actions/runs/36303728023) passou 131/131 testes SQL e 2/2 testes de fault injection; o [CI pós-merge em `master`](https://github.com/lucianox777/Jornada/actions/runs/36304149541) e os outros cinco workflows do mesmo SHA concluíram com sucesso. Tentativas anteriores canceladas não contam como aceite.

## Guarda contra regressão da separação

O gate estático `ApoioSecretarias/scripts/check-segregation.py` executa no workflow `apoio-secretarias`, inclusive quando a PR altera projetos C#, contratos, migração v5 ou distribuição da solução principal. Verifica a titularidade dos oito contratos externos, a ausência do integrador e de referências ao suporte no projeto de produção, e o registro SEHAB v5 exclusivamente no apoio. Testes negativos criam reintroduções deliberadas em árvore temporária para exigir falha; a leitura externa de schema em `Solution/tests` é exceção intencional, sem retorno à distribuição. Este guard é **regressão estática**, não substitui E2E SQL/HTTP nem aprovação HML.

## Limites e próximos gates distintos

O preparador demonstrado emprega CSV e mapeamento **sintéticos**; não há homologação de layout real SEHAB nem mapeador aprovado de seus registros finalísticos. O contrato v5 permanece RASCUNHO e as autorizações institucionais não são implicitamente concedidas pelo teste DEV. A publicação em HML depende ainda dos demais contratos, funcionalidades e controles integrais previstos para HML, inclusive autenticação institucional PRODAM, ambientes separados, classificação/autorização dos dados e massa fiel à origem. A conclusão do gate técnico de segregação **não substitui esses requisitos e não autoriza, isoladamente, iniciar o Ensaio**. O [Plano de Desenvolvimento](Plano_Desenvolvimento.md) registra a prioridade e o aceite separado.

## Complemento de regressão negativa da ingestão sintética

O harness isolado `JornadaE2E` exercita antes da primeira Entrega válida três rejeições: sem credencial (HTTP 401), credencial sintética BENEFICIO sem escopo `jornada.ingestao.write` (403) e Gestor autorizado com SHA-256 adulterado apenas no nome de um ZIP válido (400). A verificação exige exatamente um evento na auditoria SQL para cada `X-Correlation-Id`, nenhum registro `ingestao.entrega` para essas requisições, Staging sem arquivos residuais e respostas sem atributos pessoais do fixture. `negative-security-evidence.json` integra o artefato do E2E; nenhuma access key entra no JSON. Esta cobertura é DEV sintético e não substitui autorização HML, layout real nem decisões institucionais da issue #496.
