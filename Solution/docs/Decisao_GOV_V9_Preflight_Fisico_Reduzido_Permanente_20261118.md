# GOV-V9-PREFLIGHT-FISICO-PERMANENTE — decisão do Product Owner (2026-11-18)

**Estado:** REVOGADA pelo Product Owner em 18/11/2026, antes de implementação; substituída por validação pontual pré-workflow. **Política:** GOV-DOC-01.

## Contexto e motivação

Duas campanhas científicas V9 de alto custo falharam; a execução #44235715333 concluiu VALIDATION mas o TEST falhou por divergência entre a projeção do snapshot e a do protocolo. A correção foi integrada nas PRs #1686 e #1687 (merge `878d64f91ea4d87ef6773e32978fdb7d81e313c6`). A CI não equivale a uma execução end-to-end do TEST. O leitor científico atual exige exatamente 100.000 identidades e o protocolo fixa 20 repetições: não se deve reduzir silenciosamente esse TEST.

## Decisão histórica do PO — revogada

Criar **mecanismo permanente de diagnóstico físico reduzido**, separado do TEST científico oficial, para detectar antecipadamente erros de integração do vencedor congelado, fingerprint/projeção, SQL e fluxo físico. Deve ser repetível como proteção pré-campanha e possuir testes e CI próprios.

## Requisitos históricos — não vigentes

1. **Entrada independente e inequívoca:** nome de comando, relatório, logs e estados diferentes de `--v9-independent-test`; identificação explícita `DIAGNOSTIC_ONLY_NOT_SCIENTIFIC_TEST`.
2. **Fidelidade do caminho técnico:** consumir vencedor operacional SHA-pinned, validar round-trip byte-exato, carregar do SQL efêmero `JornadaHarness`, usar o resolvedor compartilhado da projeção congelada e exercitar a avaliação física real, com coorte sintética reduzida e limites explícitos.
3. **Isolamento:** nenhum acesso a TRAIN/VALIDATION para ajustar, escolher ou refazer o vencedor; nenhuma publicação, ativação, promoção, refit ou certificação; não consumir seed reservada do TEST oficial.
4. **Fail-closed:** erro de SHA, projeção, SQL, integridade de dados ou invariantes deve falhar antes de registrar sucesso; logs e relatório devem distinguir diagnóstico de ciência.
5. **Automação permanente:** testes de regressão, integração física em SQL descartável quando infraestrutura disponível e verificação em CI/preflight; evidência com SHA do commit e do vencedor, parâmetros, hashes do corpus, resultados e saída de processo.
6. **Aceite:** implementação revisada, CI aprovada e **execução física observada**. Sem essa evidência, não marcar o mecanismo como operacional.

## Impactos, limites e precedência (históricos)

O diagnóstico reduz custo de detecção de regressões, mas **não substitui nem reduz** o TEST científico de 100 mil identidades e 20 repetições. Sua aprovação **não libera isoladamente a terceira campanha**. Mantêm-se as decisões de TEST independente, seed inédita, vencedor imutável e bloqueio de publicação. Complementa [GOV-V9-TEST — Projeção congelada](Decisao_GOV_V9_TEST_Projecao_Congelada_20261118.md) e [roteiro de admissão física](V9_TEST_Fisico_Preflight_Admissao_20261118.md).

## Revogação posterior, escopo vigente e rastreabilidade

**Revogação:** manifestação explícita posterior do PO em 18/11/2026: “Não, eu acho melhor revogar que seja feito isso de forma permanente. Acho que é bom fazer agora os testes para liberar a execução do workflow.” **Precedência:** esta revogação prevalece sobre a aprovação anterior apenas quanto à permanência, automação recorrente e implementação de um novo mecanismo permanente. **Escopo vigente:** executar verificações pontuais adequadas ao código atual para decidir se o workflow científico pode ser iniciado; manter as proteções existentes e não flexibilizar o TEST científico. Nenhuma execução física ou liberação foi comprovada até aqui. **Aprovação histórica:** manifestação explícita do Product Owner em 18/11/2026: “Sim, devemos criar como um mecanismo permanente.” **Documentação:** PR #1688. **Código, workflow, CI e execução:** pendentes; devem ser acrescentados neste registro conforme entregues. O histórico da conversa não substitui este documento.
