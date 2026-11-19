# GOV-V9-CAMPANHA-10K-VALIDADE-CONDICIONAL-20261118

**Autoridade:** Product Owner, 18/11/2026. **Estado:** APROVADA; execução ainda não realizada. **Política:** GOV-DOC-01.

## Contexto e decisão

O PO aprovou iniciar a próxima execução do workflow científico padrão V9 com **10.000 identidades sintéticas por corpus independente**, utilizando o input manual `synthetic_population=10000` introduzido pela PR #1722 (integrada à master em `a2362e8028b2d581d81f64330ebbca9529e510c9`). O valor de 10 mil é escolha inicial operacional, **não mínimo estatístico universal** nem declaração antecipada de sucesso. Não exigir nova execução de 100 mil apenas por tradição quando os critérios de aceite e o protocolo aplicáveis forem efetivamente satisfeitos pela campanha reduzida.

A campanha mantém ADMISSION → PREPARE → TRAIN → AUDIT → VALIDATION → TEST (quando habilitado), 324 hipóteses e parâmetros científicos vigentes, coortes independentes, partição de identidade-base 60/20/20 e regras de integridade, SHA, seed, segurança, publicação e isolamento. Os tamanhos efetivos de VALIDATION e TEST devem ser lidos dos artefatos e avaliados contra seus contratos, sem assumir que 10 mil identidades equivalem a 10 mil observações ou pares.

## Critérios de interpretação e escalonamento

- **Sucesso técnico:** jobs obrigatórios, transferências, integridade, relatórios e gates aprovados no SHA exato; falha de implementação exige correção, não aumento automático da amostra.
- **Validade científica condicional:** métricas, intervalos de incerteza, seleção de finalistas, auditoria, VALIDATION e TEST, quando habilitado, devem satisfazer os critérios científicos pré-existentes aplicáveis. O tamanho 10 mil, isoladamente, não invalida resultados; tampouco assegura poder estatístico, FDR ou certificação.
- **Evidência insuficiente:** analisar a razão e considerar nova campanha de tamanho justificado (por exemplo, 30 mil), sem ajuste oportunista de parâmetros com base no TEST exposto. Uma nova avaliação confirmatória requer independência e protocolo congelado.
- **Encerramento de PRs:** decidir individualmente por critérios de aceite e evidências reais; PRs que exigem expressamente 100 mil ou certificação específica não são automaticamente dispensadas. Registrar exceções ou revisões de protocolo de forma explícita antes de declarar conformidade.

## Precedência e limites

Esta decisão complementa GOV-V9-OVERRIDE-POPULACAO-CLI-20261118 e revisa a **preferência operacional** pela população de 100 mil para a próxima tentativa, mas **não revoga silenciosamente** os requisitos específicos de 100 mil do protocolo histórico do TEST e das decisões GOV-V9-TEST-COHORT-20PCT-20261118 e GOV-V9-100K-AUTO-RUN-20261110. Havendo conflito entre critérios vinculantes de certificação e o novo tamanho, exigir revisão explícita do protocolo ou classificar o resultado como evidência limitada; nunca apresentar o TEST reduzido como certificação 100k sem base documental. Nenhuma publicação/ativação automática.

## Condições operacionais e rastreabilidade

Execução exclusivamente por `workflow_dispatch` do `.github/workflows/v9-standard-linear-100k.yml`, a partir da master, com `expected_sha` correto e `security_preflight_approved` confirmado de fato pelo operador autorizado; não falsificar aprovação nem contornar gates. O workflow não deve ser disparado automaticamente por esta decisão. Antes da execução, verificar pressupostos fixos remanescentes no fluxo e eventual incompatibilidade do TEST com 10 mil. Registrar posteriormente run ID, SHA, parâmetros, hashes, volumes efetivos, resultados, limitações e decisões individuais de fechamento de PR.

**Rastreabilidade:** aprovação do PO em conversa de 18/11/2026 (“Concordo, continue.”), após proposta explícita de campanha inicial com 10 mil e avaliação condicional de resultados; PR #1722 e documentos citados. **Evidência física:** pendente.
