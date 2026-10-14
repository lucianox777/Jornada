# Atividade técnica DT-V9 — Experimento em memória em lote e congelamento TF/LLR

**Situação:** em execução; issue #999; implementação inicial PR #1031; plano memory-first incorporado pela PR #1030. Não é certificação nem ativação de V9.

## Objetivo e fronteiras
Permitir que uma única execução de CI diagnóstica compare múltiplas combinações de passes de blocking com dados sintéticos projetados, mantendo scorer Fellegi–Sunter e TF fora do espaço de busca. Após estabilização, congelar explicitamente versões, hashes, parâmetros e fixtures TF/LLR; qualquer mudança neles exige ciclo separado de testes e aprovação.

## Entregas e aceitação
1. **Contrato do experimento:** JSONL com identificador único, truth_id sintético e campos já normalizados; JSON com configurações de passes e fanout cap. Declarar versão dos projetores C#, origem, semente, hash, população, partição TRAIN/VALIDATION/TEST e denominador M elegível. Rejeitar duplicatas, split misturado, campos inválidos e evidência ausente. **Pendente:** validação estrita de schema e segregação.
2. **Executor em memória:** avaliar todas as configurações num único job, sem alterar TF/LLR. Medir pares únicos e incremento M/U por passe, recall elegível, fanout P50/P95/P99/max, custo e duração. **Inicial na PR #1031; pendente:** testes de oráculo, limites de recursos, semântica de fanout e execução de fixture real.
3. **CI genérica:** workflow_dispatch parametrizado para arquivos versionados; job leve de sintaxe/contrato em PR e execução sob demanda para lote. Produzir artefato com SHA dos insumos e relatório. **Inicial na PR #1031; pendente:** prova de execução com fixture versionada, validação de segurança dos inputs e teste de regressão.
4. **Escolha e congelamento:** selecionar regras/caps somente em TRAIN/VALIDATION, com critérios de custo/recall, abstenção e limites de suporte. Congelar configuração e TF/LLR separadamente. TEST fica lacrado para avaliação final.
5. **Gate SQL:** executar banco efêmero JornadaHarness apenas após evidência reproduzível em memória; comparar pares e contagens, plano de índices e latência. Não modificar banco operacional.

## Questões técnicas a resolver
- TF: confirmar dados publicados para os quatro atributos do snapshot nominal; não inventar marginais de sobrenome nem frequências conjuntas.
- Evidências mínimas: quais pares de atributos contam como independentes e quando abster.
- Blocking: incluir todos os sobrenomes como tokens 1:N, não apenas campo escalar; determinar fanout cap e passes por padrão de ausência.
- Medidas: diferença entre U de candidatos, FDR da decisão FS e FDR hipotético; investigar denominador dos 18% de degradação.
- Robustez: medir consumo de memória e tempo total; evitar O(N²) do cálculo de verdade em corpus grande, com agrupamento por truth_id; tratar pares verdadeiros que cruzam partições e bloqueio por grupo.
- CI: não substituir checks obrigatórios de merge; evitar disparar benchmark pesado em toda alteração de parâmetros.

## Plano de execução
A. Validar #1031 com fixture sintética mínima e teste de saída esperada; corrigir se necessário.
B. Executar lote inicial em TRAIN/VALIDATION e publicar relatório com hashes, métricas e limites.
C. Refinar passes, componentes e TF para seletividade; congelar versão TF/LLR validada.
D. Selecionar candidatas com evidência e somente então iniciar SQL físico.
E. Teste final cego, documentação e decisão de promoção separada.

**Estado verificado em 14/10/2026:** #1030 merged; #1029 e #1031 com CI principal ainda em andamento na última consulta.
