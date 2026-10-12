# Linkage V9 — diagnóstico inicial de calibração nominal composta

**12/10/2026.** Continuação da [PR #947](https://github.com/lucianox777/Jornada/pull/947) (fundação V9). Este incremento ainda **não** conecta os parâmetros a `GENERATE_DRAFT`, `Rank`, `Resolve`, `VALIDATE` nem `ACTIVATE`. A fonte operacional V8 permanece reproduzível, sem novas correções conceituais.

## Implementação pura
`CompositeNominalCalibrationEstimatorV9.Estimate` recebe pares TRAIN rotulados `match`/`non-match` e metadados de origem dos pares (fingerprint de blocking, fonte de rótulos e fingerprint da partição TRAIN). Calcula frequências **agregadas** e suavização de Laplace por nível composto V9 para **pessoa e mãe separadamente**. Cada par contribui para exatamente um estado por campo.

A ausência de um campo (`Missing`) entra nos contadores de suporte, **não** nos parâmetros `m/u` dos estados observados. Se match ou non-match não apresentar qualquer nome materno observado, a estimativa falha explicitamente; o sistema não fabrica distribuição com pseudocontagens apenas. O contrato do scorer para esse tipo de falta continua PENDENTE de calibração governada. Nenhum token nominal é emitido na distribuição; os identificadores de proveniência fornecidos pelo chamador devem ser previamente verificados como livres de dados pessoais.

O retorno é **diagnóstico**, não certificado. A presença de um fingerprint declarado pelo chamador **não prova** que os pares foram realmente recuperados pelo blocking, que rótulos são independentes nem que a amostra representa o universo candidato. Não conectar esses valores ao modelo ATIVO sem esses gates.

## Passos obrigatórios antes de usar `m/u` na V9
1. Ligar o estimador ao **leitor independente de pares do universo bloqueado**; registrar manifesto/fingerprint do frame e do ruleset, efetividade de amostragem, pesos de desenho e independência entre grupos.
2. Separar TRAIN, VALIDATION e TEST sem vazamento de identidades/grupos; suporte por estrato (nomes comuns/raros, mãe presente/ausente, nomes completos/abreviados).
3. Estimar `m/u` **condicionados ao blocking**, com peso/desenho, incerteza e limites de suporte; os contadores simplificados desta PR **não substituem** essa etapa.
4. Investigar dependência entre nome de pessoa e mãe. A separação por campo para diagnóstico não autoriza independência na soma do FS.
5. TF: frequências de **nome integral** e/ou combinação **primeiro + último** com proveniência e população adequada. IBGE fornece marginais, **não** distribuição conjunta observada; sem base compatível, TF daquele nível deve ficar neutra. Peso TF estimado/validado, não fixo por conveniência.
6. Conferir pares, níveis, m/u e LLR matematicamente com implementação Splink independente; validar falsos vínculos/recall/FDR antes de habilitar o scorer V9.
7. Somente modelos V9 aprovados em DT-15 podem ser ativados. A aprovação desta PR de diagnóstico não altera o modelo ATIVO nem publica V9.

## Referências
- Issue de migração [#946](https://github.com/lucianox777/Jornada/issues/946).
- Risco estatístico V8 documentado na [#944](https://github.com/lucianox777/Jornada/issues/944).
- Splink (biblioteca de comparações): https://moj-analytical-services.github.io/splink/topic_guides/comparisons/out_of_the_box_comparisons.html
