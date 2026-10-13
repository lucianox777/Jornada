# V9 — ablação física do blocking SQL (seis passes pré-registrados)

## Hipótese e discrepância dos resultados

O contraste entre os “~98% de acerto” da rodada anterior e os **9,1821% de vínculos errados entre os aceitos** no SQL físico é explicável por **amostragem e denominadores diferentes**, sem atribuir causalidade automaticamente ao SQL. O relatório histórico da seed 42 do proxy (PR #996) registra **3.414 TP, 39 FP, 67 FN e 5.833 TN** em TEST: **98,08% era recall** (`3414/3481`), não acurácia operacional; a precisão dentre aceitos era **98,87%** (`3414/3453`), portanto o erro dentre aceitos **1,13%** (`39/3453`). O FPR condicional aos U artificiais era **0,664%** (`39/5872`). Nesse primeiro experimento, U são vizinhos de mesma inicial e o FS foi ajustado contra esse proxy; o teste SQL recupera **todos** os pares da união C1 + ruleset ativo e aplica parâmetros e limiar do modelo antigo. Ambos tinham **TF=0**. O proxy avalia em geral o primeiro par verdadeiro por base, enquanto o SQL conta todos os pares verdadeiros elegíveis entre gestores; nem os denominadores de recall são os mesmos. O resultado de 1,13% de erros dentre aceitos **não é comparável diretamente** com os 9,18% SQL como se o algoritmo tivesse simplesmente regredido oito pontos.

Censo de referência: [PR #998 / CI #10465](https://github.com/lucianox777/Jornada/actions/runs/38451933667/artifacts/11840892682), seeds 42/43/44: **18.036 M elegíveis**, **7.786 M retornados pelo SQL**, **12.315 U retornados**, **7.784 TP**, **787 FP**, **1,5817 U/M recuperado**, **43,1581% recall contra M elegível**, **9,1821% de erros dentre vínculos aceitos**. Não confundir acurácia, FPR condicional em U recuperado e FDR observada em pares aceitos.

**Diagnóstico importante:** o blocking SQL atual perde **10.250 M** antes de o FS pontuar; portanto reduzir apenas o threshold ou mudar TF **não recupera os pares perdidos**. Já os **787 FP** ocorrem depois da recuperação: ampliar o blocking pode adicionar ainda mais U e FP se o scorer não for recalibrado no frame SQL. São dois problemas interligados mas distintos.

## Implementação de otimização controlada: seis candidatos físicos em paralelo

Esta PR instrumenta, no banco efêmero `JornadaHarness` **somente**, seis políticas alternativas ao union C1 + ruleset ativo, sob o mesmo universo de pares inter-Gestor, sem CPF, por partição e com nascimento observado. **Não altera `#v9pairs` e não promove nenhum novo passe.** Cada candidato executa um JOIN SQL indexado e é materializado numa `#v9opt` separada; `NOT EXISTS` exclui pares que já seriam retornados pelo baseline. O teste conta uma vez cada par distinto, exige ambos os lados compatíveis, preserva limite de **1 milhão por candidato (fail-closed)**, compara o mesmo scorer FS congelado e apaga o conjunto temporário antes do próximo passe.

Passes fixados **antes** da reexecução de TEST:

1. `PERSON_EXACT_PLUS_BIRTH_EXACT` — nome completo exato + nascimento exato, sem exigir mãe.
2. `MOTHER_EXACT_PLUS_BIRTH_EXACT` — nome completo da mãe exato + nascimento exato, sem exigir nome.
3. `PERSON_EXACT_PLUS_MOTHER_EXACT_ANY_BIRTH` — nomes integrais de pessoa e mãe exatos, tolerando data de nascimento diferente (somente dentro do frame de datas observadas).
4. `PERSON_PHONETIC_PLUS_BIRTH_EXACT` — fonética da pessoa + nascimento exato.
5. `MOTHER_PHONETIC_PLUS_BIRTH_EXACT` — fonética da mãe + nascimento exato.
6. `PERSON_AND_MOTHER_PHONETIC_PLUS_BIRTH_EXACT` — ambas fonéticas + nascimento exato, mais seletivo que fonética isolada.

As seis hipóteses são propositalmente heterogêneas. A igualdade de nascimento sozinha, sobrenome popular, primeiro nome sozinho, `TOP N` sem medida e relaxamento indiscriminado não são soluções. Em especial, `PERSON_EXACT_PLUS_MOTHER_EXACT_ANY_BIRTH` não significa que nascimento é obrigatório por contrato; o **frame diagnóstico**, e não o contrato da identidade, exige nascimento observado em ambas as pontas.

## Relatórios e limitações estatísticas

Por passe e por `TRAIN`, `VALIDATION` e `TEST`, reportar somente agregados:
`ExtraTruePairs`, `ExtraFalsePairs`, `ExtraAcceptedTruePairs`, `ExtraAcceptedWrongPairs`, `ExtraFalseCandidatesPerTrue` e `ExtraWrongLinkShareAmongAccepted`. Esses números são **incrementais relativamente ao mesmo baseline**, **não** incrementais uns aos outros; não somá-los como se fossem uma união, pois passes alternativos podem recuperar o mesmo par.

**A pontuação experimental permanece o FS proxy congelado (TF=0).** O cálculo de FP adicional identifica risco, **não autoriza** adotar o passe com mais recall ou escolher thresholds por observação de TEST. Critérios, limites de fan-out, custos SQL, orçamento de erro e seleção devem ser definidos em TRAIN/VALIDATION, com `m/u`, prior e TF recalibrados para o **SQL efetivo** na issue #999; somente então congelar a configuração e reportar uma avaliação independente em TEST 42/43/44.

O ruleset SQL físico do ensaio ainda não reproduz o histórico completo `VERSIONED_ALIAS`. Não se extrapolam taxas para a população municipal ou para clusters consolidados.

**Barreira operacional:** proibida ativação V9, DT-15, alteração de `Rank/Resolve` ou qualquer mudança em `JornadaLocal`, snapshots IBGE originais, NODE/NAS, Gold, HML/PROD e histórico V8. Verdes de CI aprovam instrumentação técnica, **não** desempenho estatístico.
