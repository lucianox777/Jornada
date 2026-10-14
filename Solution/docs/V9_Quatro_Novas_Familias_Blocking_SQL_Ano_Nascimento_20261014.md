# V9 — quatro novas famílias SQL com tolerância ao ano de nascimento, somente TRAIN/VALIDATION

## Decisão e motivação (P0 #999)

A PR #1012 provou que nem a união dos **seis** passes preexistentes de #1001 recupera 95% dos pares M elegíveis do SQL físico C1+ATIVO nos corpora sintéticos TRAIN/VALIDATION. O recall máximo daquele conjunto fixo fica aproximadamente em 63–65%, não há razão para continuar reordenando as mesmas regras. As #1010/#1013 mediram as 56 combinações de até quatro passes e seus fan-outs. Esta entrega investiga, com **consultas SQL novas, não mera projeção lógica**, erros de dia/mês do nascimento e evidências fonéticas de nomes que não necessariamente coincidem com a data exata.

## Predicados experimentais pré-registrados

A fonte de verdade é `#v9obs` no banco efêmero `JornadaHarness`; são usadas as colunas originais normalizadas `nome`, `mae`, `nome_fon`, `mae_fon` e `nascimento`. Cada passe usa **dois sinais** e exige as duas datas preenchidas. `YEAR` é igualdade do ano civil da data entre as pontas; não permite DOB nula.

| Passe | Duas evidências exigidas | Regime de corrupção potencialmente coberto |
|---|---|---|
| `PERSON_EXACT_PLUS_BIRTH_YEAR` | Nome completo igual **e** ano de nascimento igual | dia/mês diferentes com nome preservado |
| `PERSON_PHONETIC_PLUS_BIRTH_YEAR` | Fonética do nome igual **e** ano de nascimento igual | alterações de nome não destrutivas da fonética e dia/mês alterados |
| `MOTHER_EXACT_PLUS_BIRTH_YEAR` | Nome completo da mãe igual **e** ano igual | nome próprio divergente e dia/mês alterados, mãe preservada |
| `PERSON_AND_MOTHER_PHONETIC_ANY_BIRTH` | Fonética pessoa igual **e** fonética mãe igual | datas divergentes com ambas as fonéticas preservadas |

Os predicados são compilados como constantes de código e **não** são opções de configuração nem regras publicadas. A análise usa a condição `a.part IN ('TRAIN','VALIDATION')` dentro de **cada consulta**, e não apenas no relatório. O espaço de pares é ainda restrito a **mesma partição**, **gestores distintos**, **ausência de CPF nas duas observações**, **nascimento presente nas duas pontas** e `a.obs_id<b.obs_id`. Pares já presentes no baseline físico SQL `#v9pairs` são excluídos de cada extensão.

## Medições com denominador correto

O novo `V9SqlBirthYearPassFamilyAudit` recebe a lista de observações elegíveis e a lista real e completa de pares M/U da união `ACTIVE+C1`, ambas em TRAIN/VALIDATION, mais os pares novos de cada consulta. Por passe e split publica:

- M e U recuperados pelo baseline e adicionais **distintos dentro do passe**, totais da união com baseline e **recall dividido por todos os M elegíveis independentemente enumerados antes do SQL**;
- M/U da **união das quatro novas famílias**, deduplicada entre si e com o baseline. Esse teto é válido apenas para **essas quatro famílias**, nunca um limite superior para todas as possíveis regras de blocking;
- número total de observações elegíveis **incluindo as com zero candidatos**, zero-hit, P95/P99/máximos do grau de candidatos totais e U por observação, com histogramas íntegros para verificação independente;
- `2*(M+U)` e `2*U` reconciliações exatas de incidências totais e falsas; as diferenças individuais entre passes não podem ser somadas sem deduplicar.

Todo conjunto de candidatos e a união dos quatro passes obedecem ao cap físico de **1 milhão de pares**. Superação aborta a execução; não há corte de U por amostragem, `TOP N`, imputação de ausentes, amostragem de TEST ou mudança de denominadores. Rótulos `M/U` provêm da identidade-base **apenas dentro do corpus sintético**; não são usados para inferir regras na operação.

Testes NUnit cobrem sobreposição de M e U, cap, proibição de TEST, ausência de endpoint elegível, inconsistência de rótulo e denominador zero-inclusive. Python na CI verifica por **três seeds**, de forma independente, contagem, baseline físico, recall, dedup, P95/P99 e histogramas, emitindo apenas agregados. O diagnóstico não exporta nomes, CPF, datas individuais, ordinais, IDs de pessoa-base nem arestas.

## Interpretação e próxima decisão

O experimento **não supõe** que essas regras recuperam automaticamente M faltantes ou que conseguem 95%. Seus ganhos e custo U devem ser inspecionados pelos resultados de TRAIN/VALIDATION depois da CI; a documentação não antecipa números. Mesmo uma extensão positiva não pode ser publicada sem orçamento de FP/FDR, validação das perdas por subpopulação, análise de rede de bases U, custo SQL e fan-out à escala, e recalibração `m/u`, prior e TF no **novo frame**. A política nunca é selecionada por TEST: `CanSelectOrPromotePolicy=false` sempre.

O piso de 95% do Calibrador ainda opera em frame e ruleset amostrais diferentes; não pode ser transposto para o SQL sem reconciliação de pessoas, pares, features, versões e fingerprints na mesma base. O campo `nascimento` presente neste experimento não altera a identidade progressiva ou o contrato de atributos mínimos das fontes. A decisão sobre massa de **30 mil identidades sintéticas por seed** ainda exige suporte inferencial e intervalos condicionados à dependência entre bases, não simples número de pares nem Kish descritivo.

**Barreira:** execução exclusivamente no banco sintético efêmero `JornadaHarness`, `DOTNET_ENVIRONMENT=Development`; não modificar policies ATIVAS, VERSIONED_ALIAS, V9 decisória, DT-15, Rank/Resolve, V8 histórico, JornadaLocal, Gold, NODE/NAS, HML/PROD nem marginais IBGE originais. Nenhum TF/fator Bayes é estimado aqui.
