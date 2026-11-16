# GOV-V9-FLUXO-PADRAO-PROVISORIO-20261116 — Fluxo padrão científico V9

**Data:** 16/11/2026. **Autoridade:** Product Owner. **Estado:** decisão funcional/arquitetural **aprovada provisoriamente**, sujeita a revisão; documentação proposta em PR, sem implementação ou integração na master neste registro.

## Decisão aprovada

O procedimento **padrão** da campanha científica V9 segue a sequência **ADMISSION → PREPARE → TRAIN → AUDIT → VALIDATION → TEST opcional**. ADMISSION é a etapa de autorização/seleção e permanece anterior ao PREPARE. O PREPARE padrão **sempre gera uma base sintética** para a calibração; não oferece, neste fluxo, seleção de base TRAIN informada pelo operador nem seleção de base TEST informada. O TRAIN recebe base e plano preparados e executa as 324 hipóteses TF4. AUDIT e VALIDATION permanecem etapas distintas, posteriores ao TRAIN. Se TEST não for solicitado, o fluxo encerra após a cadeia obrigatória e o closeout pertinente.

A organização desejada é concentrar a geração sintética, validações e preparação de artefatos **antes** do job físico de TRAIN, evitando gastar seu orçamento de tempo com geração. A preparação independente dos dados necessários ao TEST, quando aplicável, deve preservar seu isolamento científico e a condição de uso apenas após VALIDATION. O PO **priorizou implementar a separação do PREPARE em job independente do TRAIN** no workflow científico reutilizável, preservando ADMISSION e a cadeia posterior. A composição interna exata do PREPARE e uma eventual bifurcação anterior a ele permanecem para revisão futura.

## Arquitetura operacional aprovada e prioridade

1. Preservar o workflow de entrada `.github/workflows/v9-tf4-execucao-testes.yml`: job `admission` valida SHA exato da master, autorização owner-only, preflight e seleção do TEST; `calibrate-audit-validate` invoca o workflow reutilizável. Não duplicar o motor científico em um novo workflow agora.
2. No reutilizável `.github/workflows/v9-v3-four-name-100k.yml`, extrair do `physical-100k` para um job PREPARE anterior a geração sintética obrigatória TRAIN (100k, seed 42), a validação da base e o plano TF4. TRAIN recebe os artefatos preparados e executa as 324 hipóteses.
3. Estabelecer handoff PREPARE → TRAIN por artifacts com manifesto, allowlist, SHA-256, vínculo a commit/run/attempt, validação fail-closed e limpeza. Reutilizar o **padrão de integridade**, não simplesmente copiar a allowlist, da transferência TRAIN → AUDIT → VALIDATION. A base sintética bruta exige contrato próprio, limites de tamanho e isolamento de TEST.
4. Preservar AUDIT, VALIDATION, TEST opcional e E2E CLOSEOUT. Não deslocar nem desbloquear o TEST científico; não alterar sua preparação lacrada, preflight ou os gates atuais nesta frente.
5. **Não criar workflow alternativo agora**: avaliar posteriormente a bifurcação anterior ao PREPARE, se surgir necessidade operacional concreta.

**Prioridade aprovada:** reorganização ADMISSION/PREPARE/TRAIN com documentação, testes de contratos e integração controlada; a decisão não autoriza executar campanha 100k automaticamente. A implementação técnica continua pendente nesta PR documental.

## Contexto e justificativa

Na master consultada em 16/11/2026, o workflow `.github/workflows/v9-v3-four-name-100k.yml` contém `physical-100k`, `audit-100k`, `validation-100k`, `prepare-test-100k` e `test-database-preflight`. A geração sintética TRAIN seed 42 / 100k e o binding do plano TF4 ainda ocorrem como steps do `physical-100k`. A opção `test_database` existente é do TEST externo, **não** uma fonte de TRAIN. Portanto, o fluxo aqui registrado é uma **diretriz aprovada**, não uma afirmação de que o PREPARE unificado já está implementado.

A separação PREPARE/TRAIN pode liberar mais tempo do limite nativo do job TRAIN; deverá ser medido o custo de upload/download de artifacts e preservada a integridade SHA, a proveniência e a independência de TEST.

## Impactos e limites

- **Sem alteração científica aprovada:** 324 hipóteses, TF4, seleção, auditoria, validação, segurança e regras de publicação permanecem intactas.
- **Sem execução ou desbloqueio do TEST científico:** continua sujeito à paridade produtiva e aos gates vigentes, inclusive `V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED`.
- **Implementação da separação PREPARE/TRAIN autorizada como prioridade**, mas ainda não realizada nesta PR; a integração exige testes e gates de CI. Não disparar campanha científica automaticamente. O PO poderá decidir posteriormente por outro workflow para casos especiais.
- O workflow atual e as opções operacionais preexistentes não são revogados por este registro; a definição do padrão não elimina o histórico nem equivale a uma migração técnica concluída.

## Precedência e rastreabilidade

Complementa GOV-V9-CADEIA-OBRIGATORIA-E2E-PREFLIGHT-20261114, GOV-V9-TF4-JOBS-INDEPENDENTES-TIMEOUT-NATIVO-20261114 e GOV-V9-TRAIN-100K-GERACAO-LOCAL-SEM-REF-CORPUS-20261116. Não revoga o isolamento de TEST nem as restrições da decisão GOV-V9-TF4-DISPATCH-SELETOR-TEST-20261115. Origem: aprovação provisória expressa e confirmação da prioridade de implementação pelo PO em conversa do projeto em 16/11/2026. Implementação, provas e eventual decisão de workflow alternativo: **pendentes**.

## Frente paralela: TEST científico

Pesquisa de PRs abertas em 16/11/2026: [PR #1576](https://github.com/lucianox777/Jornada/pull/1576) documenta critérios de paridade, **não é identificada como a PR única de implementação**. A implementação de paridade está rastreada nas issues [#1584](https://github.com/lucianox777/Jornada/issues/1584) e [#1575](https://github.com/lucianox777/Jornada/issues/1575), com incrementos em PRs anteriores. Nenhuma PR aberta de implementação integral do TEST foi identificada na listagem consultada; verificar a PR específica com o PO ou com busca adicional antes de atribuir propriedade. Esta frente permanece independente do PREPARE/TRAIN.
