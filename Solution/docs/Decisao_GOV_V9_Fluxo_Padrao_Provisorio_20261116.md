# GOV-V9-FLUXO-PADRAO-PROVISORIO-20261116 — Fluxo padrão científico V9

**Data:** 16/11/2026. **Autoridade:** Product Owner. **Estado:** decisão funcional/arquitetural **aprovada provisoriamente**, sujeita a revisão; documentação proposta em PR, sem implementação ou integração na master neste registro.

## Decisão aprovada

O procedimento **padrão** da campanha científica V9 segue a ordem obrigatória **PREPARE → TRAIN → AUDIT → VALIDATION**, seguida de **TEST opcional**. O PREPARE padrão **sempre gera uma base sintética** para a calibração; não oferece, neste fluxo, seleção de base TRAIN informada pelo operador nem seleção de base TEST informada. O TRAIN recebe base e plano preparados e executa as 324 hipóteses TF4. AUDIT e VALIDATION permanecem etapas distintas, posteriores ao TRAIN. Se TEST não for solicitado, o fluxo encerra após a cadeia obrigatória e o closeout pertinente.

A organização desejada é concentrar a geração sintética, validações e preparação de artefatos **antes** do job físico de TRAIN, evitando gastar seu orçamento de tempo com geração. A preparação independente dos dados necessários ao TEST, quando aplicável, deve preservar seu isolamento científico e a condição de uso apenas após VALIDATION. O PO indicou que as preparações podem vir a compor um único job PREPARE; **o detalhamento de jobs, dependências e isolamento não foi definitivamente aprovado**.

## Contexto e justificativa

Na master consultada em 16/11/2026, o workflow `.github/workflows/v9-v3-four-name-100k.yml` contém `physical-100k`, `audit-100k`, `validation-100k`, `prepare-test-100k` e `test-database-preflight`. A geração sintética TRAIN seed 42 / 100k e o binding do plano TF4 ainda ocorrem como steps do `physical-100k`. A opção `test_database` existente é do TEST externo, **não** uma fonte de TRAIN. Portanto, o fluxo aqui registrado é uma **diretriz aprovada**, não uma afirmação de que o PREPARE unificado já está implementado.

A separação PREPARE/TRAIN pode liberar mais tempo do limite nativo do job TRAIN; deverá ser medido o custo de upload/download de artifacts e preservada a integridade SHA, a proveniência e a independência de TEST.

## Impactos e limites

- **Sem alteração científica aprovada:** 324 hipóteses, TF4, seleção, auditoria, validação, segurança e regras de publicação permanecem intactas.
- **Sem execução ou desbloqueio do TEST científico:** continua sujeito à paridade produtiva e aos gates vigentes, inclusive `V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED`.
- **Sem autorização de implementação ou merge:** a presente aprovação é da sequência padrão, **provisória**. O PO poderá decidir posteriormente por outro workflow para casos especiais.
- O workflow atual e as opções operacionais preexistentes não são revogados por este registro; a definição do padrão não elimina o histórico nem equivale a uma migração técnica concluída.

## Precedência e rastreabilidade

Complementa GOV-V9-CADEIA-OBRIGATORIA-E2E-PREFLIGHT-20261114, GOV-V9-TF4-JOBS-INDEPENDENTES-TIMEOUT-NATIVO-20261114 e GOV-V9-TRAIN-100K-GERACAO-LOCAL-SEM-REF-CORPUS-20261116. Não revoga o isolamento de TEST nem as restrições da decisão GOV-V9-TF4-DISPATCH-SELETOR-TEST-20261115. Origem: aprovação provisória expressa do PO em conversa do projeto em 16/11/2026. Implementação, provas e eventual decisão de workflow alternativo: **pendentes**.
