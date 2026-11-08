# GOV-V9-PO-P95-LOCK-D2-20261108 — decisões vigentes do PO: manter P95, manter LOCK, cancelar D2

**Autoridade:** Product Owner, 08/11/2026 (America/Sao_Paulo). **Estado:** DECIDIDO / VIGENTE, sem mudança de código nem de `plan.json`. **Precedência:** esta decisão superveniente substitui o enquadramento transitório de P95 e LOCK como dívida técnica registrado anteriormente na issue [#1341](https://github.com/lucianox777/Jornada/issues/1341) e na antiga proposta documental de adiamento; cancela o passo 3 (D2), mantendo os outros passos do plano. **GOV-DOC-01:** registro documental na PR [#1318](https://github.com/lucianox777/Jornada/pull/1318), cuja integração em `master` ainda deve observar os gates da fila.

## 1. Passo 3 (D2) cancelado — decisão expressa do PO

Não implementar **gate de comparação automática de resultados**, não separar o **P95 da seleção**, não acrescentar `LatencyAffectsSelection` e não criar ressalva adicional de tempo nos relatórios. Não iniciar PR/branch para D2. Qualquer PR ou branch **especificamente** destinada a esse passo, se encontrada, deve ser encerrada **sem merge**. Avançar **do passo 2 diretamente ao passo 4**, preservando a lógica científica, o código e a telemetria existentes, sem transformar D2 em dependência das frentes vigentes.

**Verificação nesta rodada (08/11):** entre as PRs abertas consultadas, buscas por D2 e `LatencyAffectsSelection` e buscas de branches por D2, não foi encontrada PR aberta nem branch identificável dedicada a esse passo. Isso não altera o histórico de PRs já concluídas ou antigas medições da frente D; não executar reversão de código comprovadamente válido sem motivo.

## 2. P95 mantido como requisito de elegibilidade da seleção

**Decisão vigente:** `SelectionPolicy.MaximumP95Milliseconds = 60000` no `plan.json` do estudo histórico verificado (60 segundos). O P95 físico **continua critério de elegibilidade** do seletor, do congelamento e do recebimento de TEST nos caminhos que já exigem tal verificação. A presença de proposta exploratória puramente diagnóstica no software não autoriza retirar o filtro de P95 da escolha elegível existente. Nenhum seletor, campo booleano ou gate novo é criado.

**Justificativas aprovadas pelo PO:** uma configuração excessivamente lenta não serve operacionalmente, mesmo com bom resultado. O teto atual é folgado: o PO informa P95 observados entre **1,2 e 1,6 segundos**, muito abaixo dos 60 segundos, sem efeito prático na escolha atual. O tempo pode variar entre máquinas e execuções, mas **TP, FP, FN, conflitos e fits** devem permanecer invariantes, conforme o contrato de determinismo e a prova da frente D, sem alegar equivalência universal além do ensaio realizado. P95 **não é timeout**: é apurado após a execução. Prevenção de execuções indefinidas/perigosas é tarefa separada dos `CommandTimeout` dos comandos SQL, timeout dos jobs e limite de fanout (incluindo `FANOUT_P99_ACIMA_DO_ORCAMENTO`). Telemetria já disponível de duração total/fases, processadores, memória, SO e .NET é suficiente; diferença futura material de tempo entre alternativas exige **decisão de governança**, não novo algoritmo automático nem ressalva de relatório.

**Impacto:** manter integralmente o `V9BootstrapPareto2D`, `V9NestedCalibrationCli`, `V9CalibrationFreezeService`, os limites do plano, a telemetria atual e os contratos/testes existentes. Não desligar, transformar em alerta ou contornar P95 para aprovar configuração.

## 3. LOCK científico mantido como condição obrigatória

**Decisão vigente:** toda campanha científica — novas seeds, validação independente, Gold ou TEST — **exige manifesto LOCK pré-registrado**, com **SHA-256 fixado no workflow** e verificado pelo **lock gate antes da execução**. O LOCK fixa antecipadamente modelos, seeds, partições, método estatístico, hipóteses, critérios e regra de parada. Não permite mudar seed, reajustar aceitação após observar resultados ou repetir até obter resultado conveniente.

**Justificativas aprovadas pelo PO:** pré-registro dá rastreabilidade e proteção contra escolha posterior de evidência; tem custo operacional baixo; distingue estudo confirmatório de exploração técnica. O PO indicou como exemplo o protocolo **3σ de ONE_MF**, seeds **48–57**, com LOCK de prefixo **`3c2a1119…`**. O prefixo é referência comunicada pelo PO, **não** substitui o SHA-256 completo exigido no workflow nem constitui nesta documentação novo atestado independente de execução. Permanecem os gates estatísticos, de amostragem, proveniência, FDR e aprovação já existentes; não se acrescenta critério de sucesso novo por iniciativa desta decisão.

**Impacto:** não afrouxar locks, hash de manifesto, parada, execução única ou controles de TEST/Gold. O 30k seed42 conhecido continua somente regressão técnica e **não** se converte em campanha científica confirmatória. LOCK não é ato de ativação/DT-15.

## 4. Remoção de dívida técnica e rastreabilidade

O PO determinou **retirar P95 e LOCK da lista de dívida técnica**: ambos são **decisões vigentes de manter o contrato atual**, e o passo 3/D2 está **cancelado**, não pendente de implementação. O registro transitório anterior da issue #1341 fica **superado/encerrado como classificação de dívida**, sem apagar o histórico GitHub; não criar novo item DT para os mesmos assuntos. O antigo arquivo de adiamento era inclusão recente na PR #1318, ainda não mesclada, e deve ser **retirado da árvore vigente da PR**; o histórico de commits continua auditável. `Dividas_Tecnicas.md` e demais itens DT de outros temas são mantidos — menções genéricas a P95 em SLA/BI e a LOCK operacional de outras áreas não significam essas duas dívidas.

**Documentos oficiais sincronizados na PR #1318:** esta decisão temática, `Solution/docs/Indice_Decisoes_Vigentes.md` e a especificação v5.00 unificada (seções 7.16, 7.21 e inventário do capítulo 21). Sem outra PR.

**Estado de implementação:** registrar a decisão não equivale a certificar nova campanha, a liberar TEST/Gold, a homologar HML/produção ou a declarar todas as frentes V9 concluídas. Manter workflow técnico/CI e fila de merge do mandato vigente; a PR documental aguarda reconciliação com a `master` e verificação no SHA exato.
