# V9 — consolidação local, execução e condições de merge

Base integrada: master `7f68f5e8` (PR #1157), mais os cinco commits da PR #1134.
Branch: `feat/v9-consolidacao-pareto-20261019`, no worktree irmão
`Jornada-v9-consolidacao-20261019`. O checkout original, seus arquivos não rastreados
e o stash anterior foram preservados. Não houve push, merge ou ativação nesta rodada.

## Implementação efetiva

| Componente | Comportamento entregue | Limite explícito |
|---|---|---|
| Descoberta de blocking | Somente TRAIN; positivos completos anteriores ao blocking; amostra U reproduzível para propor regras; retorna todas as uniões do pool declarado | A amostra não estima m/u; pool limitado não é busca exaustiva de todos os primitivos. O modo exaustivo exige orçamento suficiente. |
| Projeção | Contrato opt-in, normalização compartilhada, prefixos pessoa/mãe K=1..64; valores curtos/ausentes não geram chave | Índice histórico V2 não é materialização V9; não foi alterado nem reprocessado. |
| Coordenador único | Reutiliza adaptador FS C#, SQL completo por ruleset, TRAIN específico do frame, VALIDATION pós-Resolve e Pareto global | Não consulta TEST nem escolhe um vencedor precoce por recall de pares. |
| Orçamento computacional | Rulesets, opções, margens, assinaturas, incidências e repetições explicitamente configuráveis; lotes 64×8 não reduzem a família | Exceder orçamento falha explicitamente. Não se usa TOP N; configurações fisicamente inviáveis permanecem na tabela com motivo. |
| Multiplicidade | Número global inclui todos os rulesets×opções×margens e comparações anteriores declaradas, inclusive configurações rejeitadas | Não autoriza adaptar propostas após observar VALIDATION nem comprova histórico não declarado. |
| m/u e TF | 81 células conjuntas, nascimento e prior estimados pelo adaptador existente; TF positiva exige referência imutável por frame, carregada uma vez antes dos lotes | TF portátil atual é ExactFirst, não todas as unidades do catálogo. A CLI sem provedor de referência recusa TF positiva. |
| Pareto | Recall global por fonte e FDR final; todos os motivos de rejeição, dominadores, equivalentes, fronteira e distância min–max ao ideal; desempate por hash/ID | P95 é guardrail versionado inicial de 60.000 ms, não terceiro objetivo nem SLA. |
| Snapshot | JSON canônico/SHA, passes, 81 células, nascimento/prior/TF, threshold/margem e parâmetros efetivos de agrupamento | Artefatos V2 antigos sem agrupamento continuam legíveis para preview; não podem virar novo congelamento integral. |
| Replay portátil | Recarrega bytes fixados e executa FS/TF→Rank→complete-link cego→Resolve, usando posterior/cap embutidos | Exige coorte VALIDATION e união física completa. Não opera produção. |
| P95 físico | Pelo menos 20 repetições: projeção, criação/bulk das temporárias, união SQL completa, leitura, fit congelado recarregado, agrupamento, Resolve e métricas | Exclui leitura do CSV, abertura da conexão e fitting TRAIN. Não mistura o P95 antigo de apenas materialização SQL. Novo resultado físico ainda não medido localmente. |
| Congelamento | Envelope completo, tabela reproduzível, catálogo, política, seleção, proveniência; create-only/idempotente; bootstrap elegível e sucessores manuais | Exige evidência elegível; congelar é diferente de publicar/ativar. |
| TEST | Reserva exclusiva antes de abrir labels; corpus/seed/identidades/snapshot fixados; bloqueia seeds 42/43/44 e reroll do mesmo fit; recalcula bound por identidade | Nenhum novo TEST foi aberto. Hashes externos não autenticam por si a hipótese IID. |
| Consumidor de consulta | Projeta/plana passes exatos e SQL parametrizado; rejeita materialização legada, truncamento e fit trocado | Resolve de registro isolado recusa snapshot que exige agrupamento de coorte. |
| API/Processor/SQL | Auditoria do CPF, fila probabilística, reavaliação incremental e persistência governada documentada | Loader SQL operacional não consome o envelope completo; V9 permanece bloqueada por DT-15/certificação. Não foi criada rota que ignore esse gate. |

O bound CP/Hoeffding permanece **condicional**, com `IID=false` no produtor real.
Registros base independentes não tornam decisões de Rank/Resolve independentes:
a competição por candidatos pode ligar identidades. Nenhuma alteração de orçamento
ou compra de computação resolve essa premissa por si.

## Validação desta rodada

- SDK efetivo e `global.json`: **10.0.113**, roll-forward desabilitado.
- `System.Security.Cryptography.Pkcs`: única referência direta **9.0.21** em Operational.Sql.
- Restore locked e build Release da solution: **0 erros, 0 avisos**.
- Suíte unitária completa: **1.769 aprovados, 0 falhas, 0 ignorados**.
- Bateria integrada V9/blocking: **458 aprovados**, incluindo equivalência ponderada,
  ausência de atributos, replay recarregado, alteração real de resultados por
  posterior/cap, independência de gabarito, TF e imutabilidade.
- Gate de evidência TRX, NuGet/proveniência dos **22 projetos**, cache CI e **67**
  referências de actions fixadas por SHA aprovados.
- Python amplo: **66/68 aprovados**. Dois casos anteriores exigem symlink autorizado
  e bits POSIX executáveis; falham neste Windows sem esses recursos. Não foram
  pulados nem alterados para aparentar CI verde.
- Integração SQL tentada: **2 falhas no OneTimeSetUp** por Docker indisponível
  (`npipe://./pipe/docker_engine`), antes dos testes. Não é execução aprovada do SQL.
- CI do novo HEAD não executada: a restrição vigente exige autorização para push.

TRX e inventário locais: `Solution/.local/v9-consolidation-tests/`.
Os resultados serão reconferidos no commit final; a CI histórica verde da PR #1157
não é aprovação das alterações desta rodada.

## CPU e GPU

RTX 3060 Laptop, 6 GiB, driver 610.62, compute capability 8.6; enumeração CUDA
funcional. Não há toolkit nvcc nem bibliotecas Python de computação GPU instaladas.
Não houve instalação ou alegação de speedup GPU.

Otimização exata de blocking: **19,69 s→4,32 s (4,56×)** na comparação controlada
antes/depois, alocações **8.804→1.193 MiB (−86,45%)**. Os arquivos de prova das
uniões, contagens e fingerprints são byte a byte iguais, SHA
`cf7426f411663d559804694b377c4e59d728b6cf073e5c96d8d0910c60364a83`.
Isso mede workload artificial de 30 mil observações TRAIN, não aprovação
estatística da massa IBGE de 30 mil pessoas.

O benchmark reconferido na consolidação usa **Pareto V2**, política artificial
explícita e flags sintéticos somente para exercitar o algoritmo: 8.000 pontos
na fronteira, mediana **864 ms**; descoberta de 30.000 observações, **3.294 ms**.
O JSON registra assemblies, amostras e alocações. A tabela anterior de Pareto V1
é histórica e não deve ser apresentada como tempo da V2. Tempos variam com JIT/GC
e atividade concorrente. A saída completa de relações pode ser quadrática;
GPU não elimina o tamanho dessa saída. Nenhuma configuração é truncada.

## Evidência estatística e amostra

Permanece válido como diagnóstico histórico, sem nova certificação: seed42,
30 mil pessoas-base, 58.148 observações; exemplo FS cego com threshold .95/margem
.5: **TP=7.505, FP=0, recall por fonte≈95,715%**, FDR observado=0 e limite
condicional≈**2,223%** com 36 comparações. Precisão observada=100% nos aceitos;
isso não comprova precisão populacional. FPR de pares ou oportunidades negativas
não deve ser inventado a partir do denominador de fontes elegíveis.

Com cap16, alpha .05, zero FP e aceitação média histórica, a projeção para 65.536
configurações exige **22.310 identidades independentes de VALIDATION** para bound
≤1%; havia 5.556, déficit 16.754. A estimativa não garante poder, representatividade
ou independência e não justifica consumir TEST. A calculadora e premissas estão em
`V9_Revisao_Independencia_Multiplicidade_Amostra_20261019.md`.

## Executar em SQL descartável

Usar exclusivamente um SQL Server isolado com database **JornadaHarness**. O
programa confere o catálogo na connection string e `DB_NAME()` no servidor.
Manipula somente tabelas temporárias da sessão; não cria/reseta o banco original.
Não iniciar Docker Desktop sem conferir os containers persistentes com restart automático.

Na pasta `Solution`, com conexão do harness já configurada sem gravar senha no repo:

```powershell
python scripts/v9-adaptive-pipeline-smoke.py prepare .local/novo-smoke-input
dotnet run --no-build -c Release --project src/Jornada.Linkage.Parameters.Worker -- `
  --v9-adaptive-study .local/novo-smoke-input/plan.json `
  .local/novo-smoke-input/observacoes.csv .local/novo-smoke-result
python scripts/v9-adaptive-pipeline-smoke.py verify .local/novo-smoke-result
```

Esse fixture pequeno testa encanamento: 27 passes, 54 configurações, rejeições
mantidas, prefixos SQL reais, P95 completo e fit portátil. **Não é validação
estatística nem TEST.** A CI física foi ampliada para executá-lo no SQL efêmero.
Depois, fornecer plano pré-declarado e massa-base contratada; preservar TEST e
ajustar somente os orçamentos computacionais explicitamente justificados.

TF positiva via API C#: passar provedor assíncrono que carrega
`FrozenIbgeNominalTfV9.LoadAsync` com versão publicada e
`V9PhysicalSqlAdaptiveCalibrationProbe.ComputeBlockingFingerprint(passes)`.
O coordenador fixa esse objeto uma vez por ruleset e o snapshot incorpora as
frequências e U TRAIN; não recarrega a referência ATIVA por lote/margem.

## Condições que ainda impedem o merge final/ativação

1. Executar o novo HEAD na CI, smoke físico e estudo TRAIN/VALIDATION no SQL isolado.
2. Resolver/atestar a dependência dos resultados finais e dimensionar a amostra
   pela família completa; ampliar pessoas sem resolver IID não basta.
3. Somente com candidato plenamente elegível, congelar envelope integral;
   reservar um TEST novo e lacrado, executá-lo uma única vez sem rerank/refit.
4. Implementar publicação SQL da referência integral aprovada e materialização V9,
   com proveniência e compatibilidade; exigir aprovação humana DT-15 para ativação.
   O fluxo operacional protegido não foi habilitado sem esses pré-requisitos.
5. Autorização explícita para push/PR/merge conforme o roteiro vigente. Esta rodada
   prepara commits e descrição local; não concede aprovação estatística nem operacional.

Referências complementares: `V9_Pareto_2D_Integracao_20261019.md`,
`V9_Congelamento_Local_TEST_Independente.md`,
`V9_Snapshot_Runtime_Operational_Audit_20261019.md`,
`V9_TRAIN_Cached_Primitive_Optimization_20261019.md`.
