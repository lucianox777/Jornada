# GOV-V9-CALIBRADOR-RECURSOS-INTERNOS-20261109 — orçamento adaptativo é responsabilidade do modo de calibração

**Autoridade:** Product Owner, 09/11/2026. **Situação:** decisão aprovada; implementação submetida em PR. **Classificação:** operacional/arquitetural; sem alteração científica. **Política:** GOV-DOC-01.

## Mandato e correção de arquitetura

O PO esclareceu após o início do run técnico [24×100k #42678462930](https://github.com/lucianox777/Jornada/actions/runs/42678462930): **o orçamento adaptativo não pode existir apenas como uma conveniência de CI, de runner GitHub ou do caso 100k. Quando o usuário entra no modo de calibração, o próprio Calibrador tem de consultar os recursos disponíveis e decidir o escalonamento operacional, registrar a decisão e bloquear recursos insuficientes**. Assim, qualquer população admitida pelo plano, não só 100k, usa a mesma regra da execução local.

Essa decisão é **superveniente** à [decisão de otimização do workflow 24×100k](Decisao_V9_100K_Orcamento_Adaptativo_Execucao_20261109.md) e especializa o comportamento **automático = 0** de [paralelismo/determinismo de 07/11](Decisao_V9_Paralelismo_Determinismo_20261107.md): em vez de usar incondicionalmente todas as CPUs visíveis, o modo físico de calibração respeita **simultaneamente o limite de CPUs e o orçamento de RAM observada**. Chamadas genéricas a `V9ParallelWork` que não são calibração permanecem inalteradas. Valores **positivos explícitos** `MaximumParallelRuleSets: 1, 2, 8, ...` permanecem íntegros por rastreabilidade e reprodução de ensaios; a decisão não altera o `plan.json` assinado.

## Fronteiras do orçamento — quem gerencia o quê

**Antes do SQL (orquestrador):** a inicialização de um contêiner de SQL Server é externa ao processo .NET. A via manual 24×100k continua preparando SQL com o orçamento calculado **após a geração e antes do `docker run`** via `v9-100k-execution-resource-budget.py`. Um Calibrador C# iniciado com a conexão SQL já aberta **não pode retroativamente aumentar/diminuir o limite de memória do Docker, tampouco criar/alterar SQL em produção**. Não fingir que essa etapa migrou para C#; o orçamento SQL e a credencial efêmera continuam no bootstrap, com guardas de preflight e mascaramento.

**Dentro de todo modo físico de calibração:** `--v9-calibrate` e `--v9-adaptive-study` invocam `V9CalibrationResourceGovernor.Capture()` **antes** de abrir conexões do fitting/avaliar configurações. O próprio processo mede `MemAvailable` do Linux (quando disponível), a memória remanescente de cgroup v2 (quando limitada), e o espaço livre real do volume onde criará seus resultados. Usa o **menor limite de RAM verificado**, não a memória nominal do runner nem o RSS anterior do gerador. Em Windows/macOS, memória host não mensurável por esse sensor significa **AUTO conservador de 1 worker** explicitamente sinalizado, nunca um valor inventado.

**Admissão e escalonamento:** o modo recusa em segurança disco não mensurável ou <3 GiB e RAM efetivamente medida <4 GiB (2 GiB host + 2 GiB de reserva por worker). Com `MaximumParallelRuleSets=0`, escolhe `min(CPUs_visíveis, floor((RAM_disponível_efetiva - 2 GiB)/2 GiB))`, mínimo 1 somente depois de admitido. Não elimina nem reordena nenhum ruleset, candidato, opção, TF, M/U ou margem; só altera a simultaneidade de tarefas independentes. Valores explícitos >0 são respeitados, com evidência separada; a reserva é estimativa de admissão, **não** garantia de memória/ausência de OOM. Para RAM desconhecida, o AUTO não a trata como infinita: usa 1 worker e sinaliza ausência de medição.

**Evidências:** cada execução física cria `calibration-resource-budget.json` no diretório novo dos resultados, contendo `schema`, estado, CPU configurada/visível/efetiva, valores RAM/cgroup/disco, reservas e flags `testUsed=false`, `goldUsed=false` e `activated=false`. O relatório `runtime-diagnostics.json` passa a informar o **número efetivamente executado**, em vez de repetir o máximo hipotético de CPUs. O relatório M/U compara limite teórico por CPU e o efetivo do modo de calibração. Esses diagnósticos não fazem parte do fingerprint científico, do critério de FDR nem da decisão de seleção. Evidência bruta não deve conter CSV, nomes, CPF, senha SA ou string de conexão.

## Precedências, compatibilidade e riscos

- Preserva **plan.json oficial**, 24/16/8, testemunhas V2 excluídas de seleção, 81 células, TF0/TF simétrico/assimétrico, sobrenome TF, M/U, teto FDR, TRAIN/TEST/Gold, CP, LOCK, p95, DT15 e corpus seed42 congelado. Nenhuma aprovação de estudo/TEST/HML/Produção é inferida.
- **Não** diminui população para caber na RAM e não utiliza o valor do estudo isolado do gerador 100k para ajustar o modelo.
- Em caso de falta de memória/disco mensurada, a calibração **falha antes do fitting**, com recibo de interrupção quando gravável; não promove PASS fictício. Falhas posteriores por OOM permanecem possíveis e exigem diagnóstico.
- A decisão anterior `0 = todas as CPUs` continua válida para os usos genéricos de `V9ParallelWork`, mas fica subordinada a este orçamento nos **modos físicos de calibração**. Em modo manual positivo, número explícito vence o redimensionamento automático; riscos de paralelismo explícito são registrados, não manipulados silenciosamente.
- A campanha 100k que já estava iniciada no [run #42678462930](https://github.com/lucianox777/Jornada/actions/runs/42678462930), SHA `dc3db33dffd6ddba6f65a57e1bfc07f4b7dc65b9`, **não pode receber código superveniente no meio da execução**. Seu resultado não comprova esta mudança. Isolar PR e preservar pin da master até a conclusão da campanha e dos gates dependentes. **#1253** continua aberta e checkbox de dispatch não equivale a auditoria de segredo; a solução nova não reabre a autorização dessa execução.
- Contratos próprios em NUnit, CI rápida no SHA exato, branch protegida e revisão dos relatórios; merging só com o run já disparado encerrado, sem sobrescrever evidência e sem mudança oportunista no plano.

## Critérios de aceite

1. O C# de ambos os modos físicos computa orçamento local independentemente de workflow externo e gera recibo.
2. `MaximumParallelRuleSets=0` fica restrito por CPU e RAM medida; `>0` permanece literal; no dado desconhecido não se declara RAM livre.
3. RAM/disco insuficientes falham sem número científico cortado, sem TEST/Gold e sem alteração do plano.
4. `runtime-diagnostics.json` e o relatório M/U informam o efetivo e preservam rastreabilidade; testes determinísticos/código científico não mudam.
5. PR com build, testes do orçamento e guardas; integração GOV-DOC-01 e acompanhamento da campanha anterior por SHA. 

**Status deste registro:** decisão aprovada, implementação em branch, **não implica que o run 24×100k tenha concluído nem que a segurança #1253 foi saneada**.
