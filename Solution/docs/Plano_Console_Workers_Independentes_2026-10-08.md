# Plano da Console DEV — supervisor global e controles por worker

**Decisão mais recente: 08/10/2026.** Os testes históricos de ingestão e
RunOnce da Console (incluindo as PRs #837, #839 e #840 já integradas)
**permanecem válidos e executando na CI**. Este plano adiciona
controles contínuos e ensaios de recuperação, sem redesenhar o RunOnce.

## DTs vinculantes (ordem de precedência atualizada)

- [DT-18 — cada worker como serviço/processo independente](DT18_Servicos_Independentes_Console_DEV.md)
- [DT-19 — botões empilhados e habilitação conforme PID](DT19_Console_Acoes_Workers.md)
- [DT-20 — supervisor único OFF/ON com início automático dos três](DT20_Supervisao_Opt_In_Workers.md)
- [DT-21 — regressões preservadas + E2E de resiliência](DT21_Testes_Resiliencia_Workers.md)

Um **toggle global** `Supervisão: DESATIVADA / ATIVADA`,
inicialmente OFF **no ambiente DEV descartável novo**, controla
**exclusivamente os três workers**: Processor, Operations Maintenance
e Bronze Maintenance. Eles são independentes entre si e não levam
API/Resultado/SQL/NODE junto quando morrem.

## Layout aprovado — botões EMPILHADOS VERTICALMENTE

Por worker, nesta ordem:

```text
Processador                       ● PID/estado real
[Executar uma vez]                ativo somente em OFF
[Iniciar contínuo]                disponível em ON; inicia apenas se ausente
[Desligar processo]               disponível em ON E somente com PID vivo
[Status do processo]              consulta sempre possível
```

**OFF:** três RunOnce liberados, botões de contínuo desabilitados,
sem residentes sob controle da Console. Preservar os testes e rotinas
RunOnce atuais; respeitar apenas requisitos funcionais existentes.

**OFF → ON:** bloquear RunOnce primeiro; encontrar e **matar os
RunOnce ativos dos três workers** (exigir confirmação caso existam);
habilitar a supervisão independente de cada worker e **iniciar
automaticamente os três contínuos**; liberar controles de modo contínuo.
`Desligar processo` passa a habilitado **somente quando o respectivo
worker efetivamente subiu** — indicador complementar de processo vivo.
Se não subiu, apresentar ERRO/INICIANDO, não liberar Desligar nem
mostrar verde falso. `Iniciar contínuo` não cria duplicidade se
já está ativo.

**ON:** RunOnce desabilitado inclusive backend; desligar processo
selecionado é a antiga ação `Parar processo` com término abrupto
para teste de resiliência. O supervisor reinicia **apenas esse worker**.
Durante restart, desabilitar Desligar e expor status correto.

**ON → OFF:** desarmar **primeiro** as três políticas de restart,
encerrar os três residentes, verificar ausência e só então
reabilitar RunOnce. UI obtém estado real da API após refresh
ou reinicialização.

Não criar botão adicional de `Matar processo`,
`Simular falha`, `Parar contínuo` nem toggles por worker.
O botão `Desligar processo` corresponde à função antes chamada
`Parar processo`; **enquanto ON não é desligamento administrativo
permanente**, pois o supervisor o repõe. OFF global encerra os
residentes administrativamente.

## Etapas em PRs pequenas

1. **C3.1 — fundação isolada (PR #841):** entrypoint de worker
   individual com allowlist, DEV/`JornadaE2E`, contrato do modo
   e gate não destrutivo; manter ENTRYPOINT do cluster comum intacto.
2. **C3.2 — topologia de teste isolada:** três serviços independentes,
   processos/PIDs próprios e restart externo supervisionado em
   Compose CI descartável, sem contêineres, volumes, redes,
   credenciais ou bancos do `JornadaLocal`.
3. **C3.3 — API DEV:** supervisor global OFF/ON, RunOnce pré-existente,
   comandos individuais Iniciar/Desligar/Status, exclusão atômica,
   verificação real de processo e autorização fail-closed.
4. **C3.4 — UI e E2E:** botões verticais, Chromium real, estados
   e teste real de SIGKILL/retorno sem cascade, recuperação e
   não duplicação no banco descartável. Ver matriz DT-21.

**Regras:** gates obrigatórios SUCCESS na HEAD exata antes de
merge squash, acompanhamento horário ativo, sem ressuscitar
testes concluídos como trabalho novo. Nenhum reset ou ação em
`JornadaLocal`, IBGE original, HML/PROD, volumes compartilhados,
dados reais ou Trilha 4/reprocessamento de RESOLVIDOS.
