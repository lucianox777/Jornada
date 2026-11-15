# GOV-V9-OBSERVABILIDADE-TIMEOUT-PARCIAL-20261115 — preservar diagnóstico no timeout

**Autoridade:** Product Owner, 15/11/2026, ordem explícita “Corrija” após investigar lentidão e receber aviso “A action terminou”. **Estado:** correção de observabilidade autorizada; integração e CI em PR. Política GOV-DOC-01.

## Contexto e evidência

A execução [#43631379628](https://github.com/lucianox777/Jornada/actions/runs/43631379628), SHA 814238795abccf5c8c64f61a83c871bd0374cc7b, terminou CANCELLED após seis horas, com **308/324** hipóteses, sem AUDIT ou VALIDATION; o E2E falhou com bloqueio correto. Na anterior [#43551194008](https://github.com/lucianox777/Jornada/actions/runs/43551194008) houve 324/324 em cerca de 306,7 min físicos. O comparador offline da [PR #1542](https://github.com/lucianox777/Jornada/pull/1542) registra aumento de cerca de 22% no tempo e não certifica causa.

O wrapper Python de evidências físicas já amostrava RAM e OOM durante a execução, mas **gravava o recibo apenas ao final do worker**. Quando o GitHub encerrou o processo por prazo, o arquivo final não foi gravado e a evidência de CPU/IO/pressão não estava disponível. Esta é uma falha de observabilidade verificável, distinta de correção de performance.

## Correção operacional autorizada

1. Atualizar o mesmo loop de monitoramento de 10 segundos para persistir um **checkpoint JSON atômico aproximadamente a cada minuto**, em results/physical-resource-checkpoint.json, sem criar previamente a pasta de saída que o calibrador C# deve criar por exigência de imutabilidade. Utilizar gravação temporária e substituição atômica. Ao cancelar, o último checkpoint completo sobrevive.
2. Registrar apenas números agregados e sanitizados: CPU cgroup usage/throttling, pressão CPU/memória/IO, CPU-steal do host, RAM C#/SQL/host e contador OOM, sem comandos SQL adicionais, credenciais, argv ou observações pessoais. Recurso não disponível, reset ou erro de leitura deve ser **null, nunca zero fabricado**.
3. Acrescentar o arquivo à allowlist **somente do artefato diagnóstico TRAIN** já protegido por if: always(). Não transferir a AUDIT/VALIDATION, nem permitir progresso incompleto como evidência científica.
4. Persistir no encerramento normal as métricas no recibo físico e um checkpoint FINAL_MONITOR; o parcial carrega PARTIAL_RUNNING e saída nula. A coleta é read-only, de custo muito baixo; seu impacto de desempenho não foi fisicamente medido.
5. Preservar o padrão replay_timing_diagnostics=false, definido anteriormente pelo PO. O diagnóstico individual opcional já existente fica ativável somente no dispatch manual; não adicionar impressões por hipótese.

## Precedência, impacto e limites

A correção **não melhora comprovadamente a velocidade**, nem atribui lentidão a região Azure, hardware, SQL ou código; apenas permite observar métricas no próximo incidente. Não modificar 324/81 TF4, SQL, worker C#, P95, FDR/CP, AUTO_0, TEST one-use/fechado, algoritmo científico, timeout nativo, jobs TRAIN/AUDIT/VALIDATION/CLOSEOUT, nem disparar 100K. Não efetuar rollback ou alterar infraestrutura sem evidências e aprovação específica.

Complementa [Investigacao_V9_TF4_324_Desempenho_Rollback_20261115.md](Investigacao_V9_TF4_324_Desempenho_Rollback_20261115.md) e GOV-V9-TF4-JOBS-INDEPENDENTES-TIMEOUT-NATIVO-20261114. Respeita GOV-V9-TIMESTAMPS-JOB-360-20261114 e GOV-V9-PROGRESS-ENTRADA-UNICA-CONSOLE-SEM-PIPE-20261113.

## Aceite e rastreabilidade

Exigir testes offline para contadores inteiros, null de indisponibilidade/reset, checkpoint atômico e repetido, preservação de saída inédita do C#, recebimento no artifact TRAIN após cancelamento, e CI 3/3 da PR. Só um futuro run físico explicitamente autorizado pode comprovar a origem da lentidão e a eficácia da captura.

Arquivos: Solution/scripts/v9-100k-physical-memory-evidence.py; .github/workflows/v9-v3-four-name-100k.yml; Solution/tests/scripts/test_v9_100k_physical_memory_evidence.py; Solution/docs/Indice_Decisoes_Vigentes.md; este documento. GOV-DOC-01.
