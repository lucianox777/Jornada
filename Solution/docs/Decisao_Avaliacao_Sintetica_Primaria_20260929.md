# Decisão — avaliação sintética como evidência primária do linkage (29/09/2026)

**Estado:** decisão de priorização para implementação e Ensaio; não é aceite de resultados ainda não medidos. **Precedência:** atualiza a prioridade atribuída ao Splink na seção 2.1 de `Decisoes_Linkage_Calibracao_IBGE_20260926.md`, sem revogar o gate governado DT-01/09, a conferência de implementação DT-14 nem a validação populacional #31.

## Objetivo e separação das evidências

A evidência primária de comportamento do motor operacional C# será a **avaliação da base sintética com gabarito independente e conhecido**. A paridade C# × Splink sobre os mesmos pares é uma verificação opcional de implementação, não demonstra identificação correta e **não é pré-condição para o Ensaio**. O runner externo e a issue #506 permanecem opcionais/diagnósticos, sem introduzir Splink no build ou deploy, sem apagar artefatos existentes e sem representar ausência de execução como conformidade.

A **conferência matemática independente** permanece obrigatória conforme DT-01/09/14: casos determinísticos e resultados esperados calculados independentemente do scorer em produção, cobrindo comparadores, estados, `m/u`, LLR, thresholds, guardas e decisões nas fronteiras. Reutilizar a conferência decimal × float64 governada existente quando aplicável; não substituir sua independência por comparação do mesmo código consigo próprio. `VALIDATE`/`ACTIVATE` continuam sujeitos aos gates vigentes; mudança do método exige atualizar os contratos, testes e critérios antes de alterar código.

## Aceite da avaliação sintética

1. Fixar versão, seed, manifesto e hash do corpus, gabarito por pessoa/observação, contratos e partições TRAIN/VALIDATION/TEST; garantir que a verdade de referência não derive da decisão do próprio motor. Preservar o corpus de 200 mil para iteração enquanto a escala maior não tiver prova de capacidade.
2. Executar o motor C# operacional real, incluindo blocking e decisão, sobre vínculos verdadeiros e negativos: homônimos exatos, nomes semelhantes, abreviações, erros de data, mãe ausente, CPF ausente e tardio, conflitos, erros correlacionados entre sistemas e cenários leave-truth-out.
3. Medir por onda e estrato: verdadeiros vínculos, falsos vínculos (incluindo leave-truth-out), vínculos perdidos, abstenções, conflitos, precisão/PPV, recall, cobertura e custo/latência. Separar recall do blocking do recall da decisão; reportar denominadores e intervalos quando aplicáveis. Orçamento de FP declarado antes de VALIDATION; TEST congelado não pode ser usado para retunar.
4. Cobrir a Trilha 4: mudança de Gold, alias, normalização, passes e modelo; chaves antigas/novas; RESOLVIDOS afetados; fila persistida/deduplicada por run e versão; recuperação de falha, truncamento e idempotência de ledger; DT-05 somente para mudança da assinatura semântica V1.
5. Conservar provas SQL/HTTP/E2E de que ausência de referência IBGE ativa impede `GENERATE_DRAFT` sem criação de modelo; o teste SQL de pré-condição do PR #600, isoladamente, não substitui essa prova ponta a ponta.

**Fronteira:** resultado sintético demonstra apenas os cenários modelados. Não declara representatividade de cadastros reais nem quita a issue #31; contratos institucionais, credenciais, controles de HML e autorização humana DT-15 permanecem independentes.

## Sequência de execução

Trilha 4 e regressão multi-ondas → conferência matemática independente conforme gates existentes → avaliação sintética congelada e relatório por estrato → Ensaio único após os demais gates operacionais/institucionais aplicáveis → HML e validação real #31 em seus próprios critérios. O Splink pode ser executado posteriormente para diagnóstico sem bloquear essa sequência.
