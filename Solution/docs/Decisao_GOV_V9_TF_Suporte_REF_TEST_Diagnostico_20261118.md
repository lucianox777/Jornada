# GOV-V9 — Tratamento de suporte TF, REF congelada e repetições TEST

**Autoridade:** Product Owner, 18/11/2026. **Status:** decisão aprovada, implementação em andamento na PR #1723; não declarar concluída antes da integração e execução de evidências. **Política:** GOV-DOC-01.

## Contexto e evidência
Run #44411153793, SHA 2bc76e84f581aa6b20f500637614999fc1e12f6b, população 1000: ADMISSION/PREPARE passaram e TRAIN físico falhou com `System.IO.InvalidDataException: TF ativa sem suporte TRAIN U: Mother/LastContentSurname`. AUDIT/VALIDATION/TEST foram pulados. A falha não demonstra defeito da fonte IBGE nem autoriza inventar suporte U.

## Decisões aprovadas
1. **TRAIN:** avaliar cada hipótese com falha estatística identificável isoladamente, sem abortar indevidamente as demais; preservar fail-closed para falhas estruturais e não mascarar ausência de suporte U. Exigir evidência de 324 resultados/estados auditáveis antes de liberar AUDIT.
2. **REF/IBGE:** reutilizar fonte canônica congelada e conferir ID/hash/versão; evitar downloads e recomputações redundantes. A decisão vigente [IBGE congelado sem regenerar 30k](Decisao_GOV_V9_100K_IBGE_Congelado_Sem_Regenerar_30K_20261113.md) já proíbe regenerar o bootstrap histórico. Reidratar a REF local no SQL efêmero de cada job é diferente de download ou recomputação; preservar SQL independente entre TRAIN, VALIDATION e TEST.
3. **TEST:** introduzir parâmetro de repetições, com 3 apenas para diagnóstico DEV explicitamente não científico, preservando 20 como padrão e mínimo do protocolo científico vigente. Não publicar ou certificar resultado diagnóstico. Manter vencedor SHA-pinado, corpus TEST independente, nenhuma refit ou seleção.

## Implementação, riscos e precedência
Primeira entrega na PR #1723: argumento CLI opcional `--test-repetitions N`, exposto como input `test_end_to_end_repetitions` no workflow manual, com default 20 e guarda explícita `JORNADA_V9_DIAGNOSTIC_NON_SCIENTIFIC=true` e `JORNADA_RUNTIME_MODE=DEV` para menos de 20. A propagação de 3 pelo workflow foi implementada; a revisão de contratos de latência e critérios científicos depende de testes de integração: não declarar a modalidade 3 operacional antes disso. As correções de tratamento por hipótese e auditoria da carga REF ainda precisam ser concluídas e testadas. Não substituir uma falha estatística por zero, suavização arbitrária ou resultado aprovado.

## Rastreabilidade
- Run: https://github.com/lucianox777/Jornada/actions/runs/44411153793
- PR: https://github.com/lucianox777/Jornada/pull/1723
- Precedência: GOV-DOC-01 e decisão IBGE congelado de 13/11/2026.

## Retificação do Product Owner — 18/11/2026

O PO revogou a autorização para **rejeitar isoladamente hipóteses por ausência de suporte U** até esclarecimento do significado estatístico. A implementação correspondente foi revertida na PR #1723 (commit 6034e437). **Precedência:** esta retificação substitui somente o item 1 acima; as decisões REF e parametrização TEST permanecem vigentes. O TRAIN volta a falhar fechado em suporte U insuficiente. Não executar nem certificar campanha presumindo que a hipótese é simplesmente descartável.

**Verificação do código:** `V9TrainSqlNominalUCalibration.Frozen.HasSufficientSupport` exige simultaneamente `UnmatchedCandidatePairs >= 2` e `MatchingNominalPairs >= 2`. A implementação conta **pares candidatos não correspondentes no TRAIN**, não pessoas/observações únicas. Portanto a falha pode decorrer de zero ou um acordo nominal U entre pares não correspondentes, ou de menos de dois pares não correspondentes totais; não prova, por si só, que exista exatamente uma observação no corpus. Exige-se diagnóstico dos contadores por lado/unidade antes de definir nova política.

## Decisão posterior do Product Owner — 18/11/2026: cálculo nominal em populações pequenas e nomes inéditos

**Precedência:** substitui a política de rejeição por suporte mínimo U, inclusive o bloqueio por menos de dois pares ou menos de duas concordâncias; substitui também a exigência de presença do termo na referência IBGE como condição para prosseguir com TF. Revoga a proposta de descartar hipóteses individualmente por falta de suporte. Preserva as demais decisões REF e TEST deste documento.

**Regra funcional aprovada:** o sistema deve prosseguir normalmente com nomes inéditos, inclusive ausentes simultaneamente de TRAIN e IBGE, para pessoa e mãe, prenome e sobrenome. Não condicionar execução à quantidade de pares não correspondentes, acordos nominais, registros da população ou presença do nome em qualquer das duas fontes. A escassez de dados pode produzir resultados estatisticamente pouco representativos; isso é efeito do volume da amostra e não motivo para rejeição, penalização, limitação artificial da força TF ou interrupção. Demais bloqueios continuam responsáveis por seus próprios critérios. Situações de divisão por zero devem gerar aviso observável, sem abortar a campanha **por esse motivo**; não produzir infinito/NaN nem inventar valor estatístico como se fosse calculado. Preservar integridade, segurança, proveniência e paridade TRAIN/VALIDATION/TEST/produção.

**Base matemática:** U_ref=(A_U+α)/(N_U+2α), α>0, é definido para A_U=N_U=0 (U_ref=0,5); o limiar atual de dois pares e dois acordos é uma restrição de implementação, não exigência algorítmica. O TF atual também depende de frequência nominal IBGE; para termo ausente não há frequência publicada. A escolha de como obter a frequência desses termos e de como representar cálculo matematicamente indefinido **não foi especificada pelo PO** e não pode ser inventada silenciosamente. Requer solução única, versionada e auditável para todos os termos, sem bloquear a operação, a ser implementada e validada. Nenhuma certificação científica ou merge está implícita nesta decisão.

**Implementação:** pendente de alteração consistente nos contratos de fit, referência, auditoria de cobertura, scorer portável e testes. Rastreabilidade: PR #1723, discussão de 18/11/2026, decisão 19 e política GOV-DOC-01.

### Implementação incremental e pendência matemática (18/11/2026)

Commits a18449d e de9cb84 removem os mínimos arbitrários do cálculo e da validação de U_ref; bcf439a e 518aacb cobrem casos 0/1 e rejeitam contagens impossíveis. **Ainda não houve execução CI confirmada desses commits.**

Inspeção dos caminhos operacionais: `V9NominalTfUReference.ResolveTerm` rejeita ausência de frequência publicada; `V9TrainSqlNominalUCalibration.Preview` exige `FrequenciesForUnit`; `V9FittedSnapshot.CreateTf` exige `tf.Frequencies.TryGetValue`; `V9NominalTfPublishedCoverageAudit.RequireCompleteCoverage` rejeita termos inéditos em VALIDATION; o scorer `CompositeNominalTfPreviewEvaluatorV9` exige `TermFrequency>0`. O cálculo atual usa `ln(U_ref/f)`; para termo ausente do IBGE, `f` **não é conhecido** e a simples remoção dos throws não fornece valor numérico. Aviso de divisão por zero não substitui denominador. É necessário definir e versionar um estimador de frequência fora do domínio publicado, aplicado indistintamente aos termos publicados e inéditos, antes de alterar o scorer. Nenhum valor substituto ou neutralização foi aprovado; não presumir cobertura inédita concluída.

### Decisão PO — denominador populacional configurável para termos não publicados (18/11/2026)

O Product Owner escolheu a solução operacional simples: acrescentar ao arquivo de configuração do contexto de execução uma variável de **população de referência** `N_contexto`, inteira positiva, e utilizar, para termo nominal sem frequência publicada, a frequência substituta `f_inedito = 20 / N_contexto`. O valor populacional deve ser fornecido explicitamente pelo contexto operacional; não é obrigatório derivá-lo da REF IBGE congelada. A escolha substitui a proposta anterior de exigir denominador geográfico na REF. Justificativa: simplicidade, operação com nomes inéditos e eliminação de divisão por zero. Impactos: configuração, calibração, snapshot portável, auditoria de cobertura e scorer devem compartilhar a mesma regra, com rastreabilidade de valor e versão; não alterar a frequência publicada dos nomes conhecidos. Precedência: esta decisão prevalece sobre recomendações anteriores de denominador exclusivamente IBGE ou suavização de vocabulário aberto, sem alterar o U_ref TRAIN já aprovado. **Pendências de validação antes da implementação**: verificar que `N_contexto >= 20`, esclarecer a semântica do limite de publicação (20 como aproximação conservadora, não frequência observada) e impedir mistura silenciosa de referências geográficas. Esta seção documenta a decisão, não declara implementação ou testes concluídos.

### Aprovação PO — padrão populacional de São Paulo (18/11/2026)

O PO aprovou continuar a implementação da política de frequência substituta `20 / N_contexto` e solicitou pesquisa externa para definir o padrão de `N_contexto`. Fonte oficial consultada: IBGE, Cidades e Estados, município de São Paulo/SP, código 3550308, população estimada em **11.911.337 pessoas em 2026**, https://www.ibge.gov.br/cidades-e-estados/sp/sao-paulo.html ; estimativas municipais com referência 01/07/2026: https://www.ibge.gov.br/estatisticas/sociais/populacao/9103-estimativas-de-populacao.html . **Padrão aprovado: 11911337**; manter possibilidade de sobrescrita por contexto, congelamento em snapshot e proveniência IBGE 2026. A frequência substituta calculada é `20m / 11911337m` (aproximadamente 0,000001679071). Esta decisão prevalece sobre o exemplo ilustrativo anterior de 11 milhões. O número de 20 é uma convenção de imputação, não observação IBGE para um nome específico. **Implementação operacional ainda pendente de alteração de configuração, scorer, auditoria e testes**.

### Integração incremental da resolução de termos inéditos

Commits `f078c62` e `b0e8d03`: resolvedor de frequência e testes isolados; commits `7adb973`, `d4ffdd3` e `e4f33cd`: utilização do resolvedor nos caminhos `V9NominalTfUReference`, `FrozenIbgeNominalTfV9.CreatePreview` e `V9FittedSnapshot.CreateTf`. O comportamento vigente nesses pontos usa o padrão 11.911.337, **ainda não o valor configurável propagado**. Não afirmar conclusão end-to-end: auditoria de cobertura, política de abstenção de snapshot, serialização da população, passagem de parâmetros, testes legados e CI continuam pendentes. O uso do padrão fixo em caminhos operacionais é provisório e não atende à decisão completa do PO.
