# DC-V9-PAR-DET-20261107 — Paralelismo local e determinismo científico

**Aprovada pelo Product Owner em 07/11/2026, 19h12 (America/Sao_Paulo).** Precedência: decisão superveniente sobre a proposta de comparar fisicamente dois runs serial × paralelo (#1321 e #1322, fechadas sem merge; #1323, diagnóstica e não autorizada a merge). Vinculada à política GOV-DOC-01.

## Decisão e justificativa

O `MaximumParallelRuleSets` controla **exclusivamente a execução local** no mesmo host: 1 mantém o default histórico serial V2; 0 usa as vCPUs visíveis para o processo .NET; N positivo fixa N tarefas concorrentes, geridas em C#/.NET e escalonadas pelo SO. Não existe distribuição entre máquinas. Não alterar nenhum valor padrão existente para fazer uma demonstração.

**Regra científica mandatória:** TP/FP/FN, conflitos e pendências por motivo, conteúdo de cada snapshot/fit e opção selecionada devem permanecer idênticos entre 1, 0 e 8 e entre ambientes. Deve existir um teste C# em todas as PRs que compare um hash canônico de resultado, separado dos parâmetros de execução. Em caso de falha, corrigir ordenação/redução e desempate; nunca relaxar o teste. A comparação física serial × paralelo previamente proposta foi **dispensada por decisão explícita**, não considerada aprovada.

**Telemetria obrigatória de calibração:** tempo total e por fase, vCPUs visíveis, paralelismo efetivo, memória de pico, SO, .NET e máquina/runner. O tempo é apenas informativo; entre máquinas, SOs e runners não é comparável, e mesmo no mesmo host sofre a carga de processos concorrentes. A escolha do modelo e qualquer resultado científico **não podem depender** do tempo. A restrição pré-existente de P95 operacional precisa ser reconciliada com esta regra antes de declarar o determinismo completo.

## Estado verificável (não converter intenção em evidência)

- Master de partida: `65659c681eb02620e49acd96196c5a53db54be9a`; `jornada-ci` run 42418105656 e probe 30k run 42418105663 concluídos com sucesso no SHA; job físico 30k também success.
- Nesta PR inicial, o hash científico canônico e o teste de orquestração 1/0/8 foram **propostos**, mas não substituem um teste end-to-end do mesmo plano físico com todas as saídas, nem demonstram invariância entre SOs. O relatório de telemetria foi conectado ao CLI adaptativo; outros CLIs requerem cobertura.
- **Gate de conclusão PENDENTE:** completar o seam para execução do pequeno plano real sem infraestrutura, cobrir a seleção e suas dependências de P95, CI rápida verde na PR e, após merge permitido, CI e probe físico verde no SHA integrado. Até lá, nenhuma liberação de merges subsequentes atribuída a esta PR.
- Campanhas confirmatórias, TEST, Gold, DT-15, HML e ativação continuam não autorizados.

## Demais decisões do mesmo mandato (escopo reservado)

Executar sequencialmente: #1308 (paridade e sanitização de todas as bordas/segredo canário), #1310 (λ=0 testemunha em ambos os modos), #1304 (plano científico autocontido), campanha U-raro no SHA integrado; remover disparo manual **somente do gate técnico** seed42; depois consolidar **uma única PR documental #1318**; neutralização literal de toda grafia do fornecedor por `DESENVOLVEDOR` inclusive binários, resguardando baselines hash-fixados e migração de domínio nova; motor V2/V3/misturas multiparâmetro com CANDIDATE/WITNESS e limite CP somente candidatas; ciência com seed inédita sob LOCK manual; FDR estimado sem gabarito em base real somente documentado, sem qualquer dado real no Git. Estes itens são **decisões aprovadas e tarefas pendentes**, não execução comprovada.

Regras de precedência: bloqueio ativo proíbe merge inclusive documental; master vermelha exige correção/reversão antes de novo merge; checks rápidos em PR e CI completa/probe 30k cumulativo no push de master; sem `workflow_dispatch` no gate técnico final; nenhuma alteração retrospectiva no histórico Git. Copyright exclusivo do PO conforme LICENSE. Sobrenome, TF e resultados previamente aprovados não são alterados por este registro.
