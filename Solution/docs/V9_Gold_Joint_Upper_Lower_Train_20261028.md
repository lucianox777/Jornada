# V9 Gold — calibração conjunta dos limiares superior e inferior (28/10/2026)

**Estado: contrato C# de exploração TRAIN implementado em PR técnica DRAFT (V2 sem veto percentual prévio); executor físico conjunto ainda NÃO integrado. Testes desta revisão dependem de CI no SHA exato. Não aprovado, não ativado.**

## Decisão

O calibrador C# único `V9LowerNonMatchTrainCalibrator` passa a expor `SearchJoint` com grade declarada de limiares **superior × inferior**. O superior não é um parâmetro manual fixo da solução final. Cada par exige `0 < lower < upper <= 1`; ambos aparecem na sugestão e nos fingerprints do estudo. O `Search` anterior com `FixedUpperThreshold` permanece somente como reprodução de experimentos congelados, com versão histórica inalterada.

A grade, o plano de risco e a regra de seleção precisam ser pré-registrados e congelados **antes** de gerar medições. O contrato exige SHA-256 do plano, grade, fit Gold, snapshot Gold, evidência TRAIN e execução física. Um hash de plano, sozinho, não comprova sua anterioridade: o executor deve anexar manifesto assinado/timestamp e verificar proveniência externa.

## Medição obrigatória e limitações

Cada célula upper × lower deve ser medida no **mesmo Rank/Resolve FS/TF Gold congelado**, recalculando fisicamente a decisão superior para cada upper, em LocalDB descartável e nas três ordens `G0_G3`, `G3_G0`, `G1_G3_G0_G2`. O contrato recebe métricas agregadas: falsos vínculos, falsas criações, pendências, B³ F1 e grupos independentes. Recusa TEST, VALIDATION, grade incompleta, replay de ordem ausente, **reutilização do mesmo SHA de execução física em células/ordens distintas**, busca física incompleta, SHA divergente e decisão superior não recalculada. SHAs distintos são necessários, mas não bastam: o runner ainda deve provar a execução real e sua proveniência por artefatos verificáveis.

A API não recalcula por si a decisão superior: **o runner físico deve produzir e atestar os dados**. Não adaptar os resultados do `FsDecisionThresholdCalibrator` V6 (seleção em VALIDATION) ao Gold V9 TRAIN; particionamento e evidência são distintos. O `V9LowerNonMatchStudyCli` atual ainda usa superior congelado do fit e não é um executor da busca conjunta.

Não há teto percentual pré-declarado de falsos vínculos: todas as células válidas entram na exploração TRAIN. A fronteira considera falsos vínculos, falsas criações, pendências e B³ F1; a sugestão determinística minimiza a distância normalizada ao ideal da fronteira, sem preferências percentuais prévias. Taxas com denominador zero são registradas como indefinidas, não como 100%. A sugestão é apenas descritiva, não equivale a limite estatístico de FDR, validação independente, DT-15 ou ativação. Nenhum limiar é publicado ou ativado.

## Gates antes de merge técnico desta frente

- Integrar ao runner físico o replay de todas as células, recalcular superior, comprovar busca completa e proveniência por identidade-base sem TEST na seleção.
- Congelar grade, regra de seleção e plano de evidência antes dos resultados, sem teto de falso vínculo antecipado; medir incerteza e FDR dependente posteriormente, testar casos positivos e negativos e as três ordens reais, não apenas registros agregados fabricados.
- Verificar compatibilidade com loader REF e snapshot versionado (#1089), documentação transversal, CI no SHA exato e revisão sem bloqueios.
- TEST independente **novo**, somente após congelar a seleção TRAIN; não reutilizar seeds 42/43/44, 20261019001 ou holdout 20261025001. Aprovação institucional e ativação são gates separados.

A PR #1199 permanece independente, default OFF e bloqueada pelos gates positivos DT-15/digest/atomicidade. Esta frente não modifica SQL operacional, REF ativa, Gold real, V8, IBGE, NAS, HML ou PROD.
