# Preflight de segurança V9 — #1253 (09/11/2026)

**Resultado:** **MITIGAÇÃO DO CAMINHO ATUAL VERIFICADA EM CÓDIGO; AUDITORIA HISTÓRICA PENDENTE; issue #1253 PERMANECE ABERTA.** Este relatório não afirma ausência universal de segredos.

## Evidência

- A issue #1253 documenta oito exposições da senha `sa` **temporária** em logs de um run público de 02/11/2026. **Não reproduzir o segredo**, não presumir que `::add-mask::` posterior saneia logs anteriores.
- A [PR #1262](https://github.com/lucianox777/Jornada/pull/1262) foi integrada em `9d55a520ad6534b4ff3a20103cf860ba8d587eaf`; os runs [41845739456](https://github.com/lucianox777/Jornada/actions/runs/41845739456) e [41846520047](https://github.com/lucianox777/Jornada/actions/runs/41846520047) documentam preflight e campanha pós-mitigação. Início de SQL efêmero depois de senha randômica e máscara, conexão loopback, cleanup.
- O código atual `V9NestedCalibrationCli.TryWriteLastError` envia **somente `SafeErrorCode`** ao stderr. `V9CalibradorLastErrorLog.WriteLast` escreve também somente esse código no `calibrador.log`, não `Exception.ToString()`; conferir a regressão de canário na próxima mudança desse componente.
- Sem inspeção adequada da retenção histórica, não declarar logs saneados, revogação de segredos ou ausência comprovada de reutilização de credenciais. O caráter descartável dos contêineres é desenho de mitigação e **não prova de exclusão de todos os riscos**.
- Não há evidência nova de credencial persistente reutilizada **nesta análise**; isso não equivale a auditá-la.

## Condições para a frente C (100k)

Antes de campanha nova, revalidar **sem imprimir valores**: inexistência de segredos persistentes ainda ativos/compartilhados, política de retenção dos logs antigos, procedimentos de descarte do SQL, mascaramento na criação do contêiner e exportação segura de artefatos/diagnósticos. Se risco atual for encontrado, **BLOQUEAR** a campanha até mitigação. Sem essas verificações, registrar **PREFLIGHT PARCIAL — NÃO AUTORIZA INÍCIO DE 100k**. Não apagar logs unilateralmente e não fechar #1253 apenas por CI verde.

Sem alterar parâmetros científicos, TEST, Gold, ativação ou HML/Produção. GOV-DOC-01.

## Adenda de 09/11 — transporte de credencial e log histórico

Foi identificado uso de `docker run --env MSSQL_SA_PASSWORD=<valor>` que expunha o segredo em argumento de processo, em desacordo com mandato do PO. A [correção técnica](Decisao_V9_Seguranca_Credencial_Sem_Argv_20261109.md) troca para `--env-file` `0600` com `trap EXIT`, preserva SQL efêmero e inclui regressão. A leitura controlada do log histórico do job `125584814018` (run `41535062165`) confirmou 8 ocorrências com senha temporária ainda acessível, sem copiar valores. **Estado: bloqueio de preflight para campanha nova até decisão de retenção/reutilização e validação pós-merge.**

## Correção operacional posterior ao preflight — 09/11/2026

A [PR #1368](https://github.com/lucianox777/Jornada/pull/1368) foi integrada individualmente na master `04ca455c647fa4416df4c058529d01668a96882e`, após [fast CI 42612033252](https://github.com/lucianox777/Jornada/actions/runs/42612033252) **SUCCESS no HEAD `d6feb430...`**. O bootstrap `v9-tf-start-disposable-sql.sh` deixou de passar senha via argumentos Docker e usa `--env-file` temporário `0600` com remoção por trap, preservando senha aleatória, máscara antes da criação, porta loopback e SQL descartável. O pipeline de PR passou, mas a prova física pós-merge deve ser referida no SHA específico (não atribuir à PR runs anteriores).

Os logs públicos históricos ainda estão acessíveis no run `41535062165` (oito ocorrências), com serviço de SQL como container de um job `ubuntu-latest` concluído em 02/11, derivado de `github.run_id`; **nenhum segredo foi reproduzido**. Não há neste corte prova de reutilização em serviços persistentes. Essa ausência de achado não equivale a confirmação universal de descarte/expurgo. #1253 permanece **ABERTA** e frente C exige preflight atual e recursos aprovados.
