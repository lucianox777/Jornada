# Linkage V9 — diagnóstico conjunto de nome da pessoa e da mãe

**12/10/2026 — etapa 6, issue #946.** A decisão arquitetural aprovada mantém V9 como única direção de evolução, mas preserva V8 histórica para replay. Esta PR **não ativa** V9, não altera decisões ou scores, nem certifica `m/u` ou TF.

## Motivação

As etapas #947–#951 criaram níveis nominais mutuamente exclusivos, `m/u` separados de pessoa/mãe, condicionamento lógico dos pares M ao blocking e amostra U derivada das chaves materiais, além da guarda contra unidade TF inadequada. **Somar `ln(m_p/u_p)` de pessoa com `ln(m_m/u_m)` de mãe sem medir sua dependência pressupõe independência condicional injustificada.** A V9 precisa medir também a distribuição **conjunta**.

## Implementação nesta etapa

`CompositeNominalJointCalibrationEstimatorV9.Estimate` calcula no TRAIN a tabela completa de estados `(nível_nome_pessoa, nível_nome_mãe)`, 9 × 9 = **81 células mutuamente exclusivas**. Cada par rotulado independentemente contribui para **uma célula somente** em M ou U. `Missing` é preservado como parte da célula: a falta da mãe não é imputada como `Other`/discordância. Contagens observadas são independentes das pseudocontagens de suavização.

Os 81 níveis têm frequências `M` e `U` *diagnósticas*, normalizadas separadamente, com `smoothingAlpha` explícito. Os contadores `BothClassesObservedCells` e `ZeroClassSupportCells` revelam esparsidade; `RequireObservedInBothClasses` impede que uma célula com suporte exclusivamente de pseudocontagem seja interpretada como evidência observada. **Mesmo células com suporte nas duas classes não estão liberadas para scoring**: esse método só atesta contagens no frame que recebeu, não calibração populacional, incerteza ou validade amostral.

`CompositeNominalBlockingAuditBuilderV9` agora anexa o diagnóstico conjunto à auditoria atual do `GENERATE_DRAFT` quando o frame nominal por campo dispõe de suporte. O worker registra somente `matchedPairs`, `unmatchedPairs`, `exclusiveJointCells`, células apoiadas em ambas as classes e células sem suporte em uma classe. Nenhuma string nominal, identificador civil, token ou hash de nome é registrado. As 81 células expostas no retorno diagnóstico representam apenas enumerações, contagens e probabilidades derivadas, **não nomes ou frequências de valores**.

## Por que ainda NÃO publicar parâmetros V9

1. Pares M são recuperados pela projeção lógica atual, mas não têm pertencimento SQL histórico materializado `VERSIONED_ALIAS` certificado; pares U vêm de uma amostra do pool candidato SQL.
2. Não há pesos de inclusão ou representação de indivíduos em universo populacional demonstrados, nem intervalos de incerteza. Cada uma das 81 células exigirá suporte suficiente ou tratamento rigoroso de esparsidade; estimar 81 probabilidades com uma amostra pequena pode produzir evidência espúria.
3. Não há estimativa independente de TF para `FullName`/`FirstLastJoint` na população compatível. O produto das marginais IBGE continua proibido.
4. Não há calibração condicional ao blocking e critérios auditáveis de `TRAIN/VALIDATION/TEST`, comparação matemática com Splink, orçamento de FP/FDR/recall ou DT-15 concluídos.
5. A ausência da mãe pode ser informativa sobre mecanismos de coleta, mas **não** é uma evidência negativa automática; o tratamento decisório depende de calibração aprovada de ausências.

**Status:** `DIAGNOSTIC_ONLY_JOINT_MU_NOT_CERTIFIED`. Esta PR integra apenas fundação estatística **não decisória**. V8, ativos, parâmetros persistidos, bancos, APIs e ledger seguem intocados.
