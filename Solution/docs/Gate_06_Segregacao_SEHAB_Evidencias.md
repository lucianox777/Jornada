# Gate 6 — segregação da SEHAB e evidências pré-Ensaio

**Data:** 27/09/2026. **Estado:** EM VALIDAÇÃO — não iniciar Ensaio, não declarar HML aprovado. **SHA da migração inicial:** [`2def00bf927b15966e924164810bb807dbe6b6cb`](https://github.com/lucianox777/Jornada/commit/2def00bf927b15966e924164810bb807dbe6b6cb). **SHA do merge/reconciliação com `master`:** [`baea6928505fb53352b3dbb254236ad8058682d4`](https://github.com/lucianox777/Jornada/commit/baea6928505fb53352b3dbb254236ad8058682d4).

## Segregação verificada no diff

- Projeto C# (cinco arquivos), testes do transmissor (três), sete contratos de Pessoa v1–v5, metadado específico AA01 e configuração ilustrativa SEHAB: **17 arquivos movimentados**, preservando bytes dos contratos.
- `ApoioSecretarias/SolucaoApoioSecretarias.sln` tem build e locks próprios, preparador CSV explícito, transmissor C# e suíte de testes. `Solution/Jornada.sln` continua sem ProjectReference ao integrador e o instalador/container/cluster não o distribuem.
- Os sete contratos Pessoa e o metadado específico preservam oito hashes no inventário independente `ApoioSecretarias/config/governance/schema-approvals.SEHAB.json`. O inventário principal tem 38 recursos restantes, incluindo os 21 arquivos Pessoa de SMADS, SMDET e SMS (v1–v5). Status de aprovação institucional continua **PENDENTE**.
- A migração operacional `Solution/database/migrations/20260921_Pessoa_V5_Contrato_371.sql` registra **somente SMADS, SMDET e SMS v5**. `ApoioSecretarias/database/migrations/Registrar_SEHAB_Pessoa_v5.sql` cadastra externamente SEHAB v5 em **RASCUNHO** e exige o digest canônico exato, sem ativação e sem aprovação fabricada. O preparo E2E usa **v4 ativo** até governança ativar v5.
- `Solution/src/Jornada.Ensaio/EnsaioPlan.cs` parametrizado/genericizado; configurações Test e Production do cluster não incluem integrador. A reconciliação com o `master` preserva o acesso semicego recentemente integrado à API, sem alterar `Jornada.Api/*`.
- A separação atual é uma **solução independente dentro do mesmo repositório Git**; um repositório remoto separado, se institucionalmente exigido, ainda não foi criado.

## Provas de compilação e compatibilidade

A [Action `apoio-secretarias` no commit `4171109f91adaae649b523b6fa5cf0d57969cf57`](https://github.com/lucianox777/Jornada/actions/runs/36302216640) concluiu com **success**: hashes específicos, quatro testes de preparação, compilação e testes C# do transmissor, e regressão de 21 schemas/metadados remanescentes de SMADS/SMDET/SMS. [`jornada-schema-inventory`](https://github.com/lucianox777/Jornada/actions/runs/36302216639) e [`jornada-windows-production-installer`](https://github.com/lucianox777/Jornada/actions/runs/36302216706) também passaram nesse SHA. Isso **não** comprova a operação ponta a ponta.

## Fluxo real do harness isolado e falha observada

O `Solution/scripts/local-e2e.sh` usa SQL `JornadaE2E`, staging/Bronze isolados e **apenas** massa sintética. Os schemas específicos são disponibilizados temporariamente a partir de `ApoioSecretarias/`, sem cópia versionada para o produto principal. O roteiro prova: preparo v4 (CSV → três JSONL/manifest no ZIP) → transmissor C# → recibo HTTP → Processor → estado `PROCESSADA` → Bronze/Silver/vínculo, preservando SHA e artefato JSON. Também prepara testes HTTP/SQL de **SMADS (CRA1), SMDET (cadastro) e SMS (cadastro)** com respectivos recibos, estados, contagens e digests v4. Um segundo passo externo registra e confere **SEHAB v5 RASCUNHO** antes do envio v4; nunca promove v5.

As primeiras tentativas no [run `36302214518`](https://github.com/lucianox777/Jornada/actions/runs/36302214518) e [run `36302216632`](https://github.com/lucianox777/Jornada/actions/runs/36302216632) falharam **antes da transmissão** no cadastro externo v5: SQL Server erro **1934** — `INSERT` com `QUOTED_IDENTIFIER` inadequado na sessão SQLCMD. O carregador externo foi corrigido para fixar todas as opções SQL exigidas por índices computados/filtrados e o harness passa a imprimir o log SQLCMD se a etapa falhar. **A correção exige novo run E2E no SHA posterior; não converter tentativas falhas em evidência verde.**

## Resíduos e decisões pendentes

- `Solution/database/Jornada_Seed_Dev.sql`, fixtures, scripts locais/escala e fontes de diagnóstico com `SCALE-SEHAB`: exceção **sintética DEV**, não dependência do contrato externo na solução principal. O inventário contratual, a migração v5 e as configurações operacionais Production não contêm inscrição da SEHAB.
- O monitor genérico da API não exibe mais `SEHAB` como placeholder: o campo apresenta `Código do gestor`; isso não altera handler, política nem APIs. A alteração remove o único hardcode visual pendente identificado fora de fixtures/scripts de desenvolvimento. Documentos AS-IS em `Documentos/Testes/SEHAB` são históricos e não integram a distribuição de suporte.
- O preparador da SEHAB contém **mapeamento CSV sintético**; faltam homologação de layout real, normalização dos registros efetivos e autorização de uso/distribuição da base real. Nenhum dado pessoal real foi migrado.
- Para encerrar o gate: obter CI/SQL/HTTP/cluster verdes no **SHA exato**, anexar JSON de evidência de todas as quatro cadeias, verificar rejeição/reatentativa/idempotência/autorização e os controles HML do Plano; decidir a criação de repositório Git separado se requerida pelo modelo de distribuição. A edição da documentação não substitui prova.

**Regra de aceite:** o item 6 de `Plano_Desenvolvimento.md` permanece **não concluído** até que todas as provas exigidas existam. Um workflow estático verde ou documentação de passos não autoriza iniciar o Ensaio.

## Evidências E2E confirmadas e fechamento de regressão SQL

A [rodada 36302605573](https://github.com/lucianox777/Jornada/actions/runs/36302605573) produziu evidência real de E2E sintético na infraestrutura CI: o preparador da Solução de Apoio gerou o ZIP SEHAB v4, o transmissor C# enviou o pacote, o receptor respondeu com recibo e a entrega alcançou `PROCESSADA`. O verificador registrou 1 objeto Bronze, 2 observações Silver, 1 vínculo progressivo e hash do ZIP; a retransmissão não duplicou a concessão Gold vigente.

Na mesma rodada foram verificados os gestores SMADS, SMDET e SMS, também com recibos, hashes v4, 1 objeto Bronze cada e status `PROCESSADA`; os detalhes ficam no artefato `local-e2e-evidence` (42 arquivos) do run. Assim, a falha anterior do cadastro externo com SQLCMD/`QUOTED_IDENTIFIER` foi efetivamente superada em E2E, e não apenas descrita.

Essa rodada teve 130 testes SQL de integração aprovados, mas 1 falhou porque o replay histórico SEHAB não encontrava o schema de Pessoa depois da migração. A correção no commit [`8391197`](https://github.com/lucianox777/Jornada/commit/839119706ccbda69e54dad8a049560e32dc2c082) inclui o schema externo SEHAB **somente como fixture de build do projeto de testes**, sem recolocá-lo nos contratos distribuídos pela solução principal. O novo CI do HEAD deve comprovar essa correção antes do merge técnico.

Esta evidência **não** equivale a homologação dos layouts reais da origem, autorização de HML, criação de outro repositório remoto ou liberação para o Ensaio. O item 6 permanece EM VALIDAÇÃO até os controles e aprovações institucionais descritos no Plano.
