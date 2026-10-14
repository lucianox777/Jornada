# V9 — Plano memory-first e otimização de CI

## Sequência obrigatória
1. **Experimento em memória:** executar o explorador V9 de células com dados sintéticos reproduzíveis e sementes fixas. Separar TRAIN, VALIDATION e TEST por identidade. Cobrir campos ausentes, partículas, acentos, primeiro nome, todos os sobrenomes, último sobrenome, nome materno e nascimento. Medir pares M/U elegíveis, candidatos únicos, incremento marginal por passe, recall, comparações, tempo, memória e fanout P50/P95/P99/máximo. Registrar versão do algoritmo, corpus, semente, parâmetros e hash de evidência.
2. **Seleção somente TRAIN/VALIDATION:** comparar sequências adaptativas e limites de fanout com baseline; definir gates explícitos de ganho marginal, custo e recall. TF deve vir do snapshot por atributo já existente, com unidade estatística correta e cobertura comprovada. Não multiplicar marginais como se fossem conjuntas; distinguir TF para seletividade de TF no LLR.
3. **Gate para teste físico:** só iniciar SQL JornadaHarness efêmero após relatório em memória reproduzível e assertivas de correção; não usar TEST para escolher regras. O SQL deve reproduzir as mesmas projeções versionadas C#, células, contagens e pares deduplicados; verificar equivalência de resultado, plano de execução, índices, latência e fanout.
4. **TEST lacrado:** avaliar a configuração congelada, inclusive FDR/UCB, abstenção e suporte por estrato; não promover V9/DT15 sem decisão explícita.

## CI mais rápida sem perder proteção
- **Workflow V9 de PR por paths:** executar build mínimo de Contracts/Linkage.Evaluation e testes unitários de normalização, TF, monotonicidade, células e censo em memória; artefato JSON com métricas e hashes.
- **CI completa permanece obrigatória para merge:** workflow rápido é feedback antecipado, não substitui checks de segurança, schema ou integração existentes.
- **Cache seguro:** NuGet por lockfiles/SDK, compilação incremental quando reproduzível; evitar restauração repetida, jobs duplicados e cancelamento de runs necessárias. Paralelizar suites independentes com limite de recursos e publicar duração por job.
- **Experimentos longos sob workflow_dispatch:** matriz pequena de sementes/estratos em PR; benchmarks grandes sob acionamento explícito, fora do caminho crítico do merge. Não executar SQL físico em cada commit.
- **Medição antes/depois:** duração p50/p95 da CI, tempo até primeiro erro, cache hit, minutos de runner, taxa de flakiness e custo por evidência.

## Questões ainda abertas
- Qual corpus sintético e cobertura dos estratos são suficientes para gate memory-first?
- Qual limite de fanout e mínimo de ganho marginal justificam passar ao SQL?
- Quais dos quatro atributos TF têm entradas realmente publicadas? Como representar TF de todos os sobrenomes?
- O índice de sobrenomes requer tabela de tokens 1:N em vez de coluna escalar?
- A alegada degradação de 18% usa qual denominador e qual conjunto de pares?

## Não fazer
Não alterar V8, Gold, Rank/Resolve, IBGE original, JornadaLocal, HML/PROD; não ativar V9; não chamar FDR hipotético de FDR real; não reduzir checks obrigatórios para acelerar merges.
