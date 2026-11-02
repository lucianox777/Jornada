# V9 — TF bilateral: contrato e matriz de verificação (01/11/2026)

**Estado:** implementação integrada na PR #1241 (commits `72702b6` e `975f171`), **sem certificação e sem autorização para merge/ativação**. Esta especificação registra o código existente e seus gates pendentes; nunca transforma CI incompleta em evidência aprovada.

## Contrato nominal imutável

- **Uma única contribuição FS conjunta** sobre as 81 células `(PersonLevel, MotherLevel)` da tabela m/u TRAIN; não criar segundo scorer nem recalcular m/u na VALIDATION.
- **Seleção compartilhada de unidade:** `V9NominalTfUnitSelection.ForLevel`: `ExactFirst` e `ExactFull` selecionam `FirstName` (em `ExactFull`, exclusivamente proxy de prenome, não frequência do nome completo); `ExactLast` seleciona `LastContentSurname` (marginal publicada de sobrenome em qualquer posição, declarada proxy). Os seis demais estados não ativam TF. A seleção é independente em pessoa e mãe.
- **Quatro parâmetros independentes:** `(Person, FirstName)`, `(Person, LastContentSurname)`, `(Mother, FirstName)`, `(Mother, LastContentSurname)`. Cada slot tem peso em [0,1], modo `Symmetric` ou `AsymmetricPenaltyOnly`, distribuição condicional U ajustada em **TRAIN SQL** e referência de frequência IBGE tipada/fixada.
- **TF0:** peso zero implica ajuste logaritmo zero (Bayes factor 1), não é uma frequência desconhecida. **Simétrico:** usa o ajuste `LogBayesAdjustment` assinado. **Assimétrico:** usa `min(0, ajuste)`; não recompensa termo raro e não altera células m/u.
- **VALIDATION:** recebe os quatro fits U e os modos/pesos imutáveis de TRAIN. `EvaluateValidation` usa o scorer `FellegiSunterScoring.CalculateFrozenCompositeNominalV9BilateralPreviewScore`; as duas avaliações pós-Resolve usam o ranker bilateral `ProbabilisticLinkageDecisions.RankFrozenCompositeNominalV9BilateralPreview`. Não reestima U, não lê TEST e não reinterpreta ausência como TF0.
- **Runtime congelado (preview):** `V9FittedSnapshot.RankPreview` usa exatamente a seleção compartilhada, os pesos/modes persistidos e o mesmo scorer bilateral de 81 células. TF ativo com termo ou U TRAIN ausente falha fechado. A construção/carga confirma proveniência do frame blocking, versão e marginais, fingerprint TRAIN e serialização canônica SHA-256.

## Estados de cada slot no snapshot

| Estado | Peso/fit no snapshot | Significado |
|---|---|---|
| `Active` | calibração com `Weight > 0` | contribuinte TF autorizado pelo **fit**, não pela governança operacional |
| `ZeroChosen` | calibração e U TRAIN congeladas com `Weight = 0` | o calibrador considerou o slot e escolheu ajuste neutro; referência permanece auditável |
| `NotSelected` | sem calibração | slot não estava na configuração escolhida; não equivale a escolha explícita de zero |
| `NoSource` | sem calibração | marginal IBGE fixada indisponível; peso ativo é rejeitado |
| `InsufficientSupport` | sem calibração | suporte U TRAIN insuficiente; peso ativo é rejeitado |
| `BaselineTf0` | sem calibração | baseline integral sem qualquer TF positivo |

`UnitTfCalibrations` contém os quatro slots distintos; nunca coexistem com as fontes legadas `TfCalibration`/`MotherTfCalibration`. Configurações explícitas por unidade exigem `TfWeight` legado igual a zero para que não haja duas fontes concorrentes de peso. Uma configuração legada de um lado continua isolada enquanto os produtores que a utilizam existirem; nenhuma compatibilidade nova é criada.

## Seleção no calibrador

`V9AdaptiveCalibrationCoordinator` e `V9NestedCalibrationCoordinator` devem validar configurações de quatro unidades, exigir referência IBGE congelada quando **qualquer** peso de unidade for positivo, vincular ao frame blocking e preservar pesos/modes através dos refinamentos. O ranqueamento de Pareto escolhe entre as **opções declaradas** e respeita o orçamento/multiplicidade; este contrato **não** autoriza criar arbitrariamente grades ilimitadas de pesos nem selecionar pelo TEST. A opção selecionada não é automaticamente ativada em operação.

## Evidências da PR e limites

| Item | Código preparado/publicado | Verificação exigida |
|---|---|---|
| TF pessoa/mãe simultâneo e quatro U independentes | Implementado em branch | regressão bilateral e os quatro suportes em CI |
| `ExactFull` via FirstName e `ExactLast` via sobrenome | Implementado; seleção comum | 9 estados nominais, round-trip e falha fechada |
| Simétrico/assimétrico/TF0 por slot | Implementado | testes parametrizados com comparação de score do holdout e runtime |
| `Active`, `ZeroChosen`, `NotSelected`, suporte/referência ausentes | Implementado | snapshot SHA canônico e testes negativos |
| 81 células sem dupla contagem | Reuso do scorer existente | suíte C# de contratos e ranking |
| Busca aninhada com IBGE fixado, opção e orçamento | Corrigido no head `975f171` | CI e ensaio físico |
| CI na revisão `72702b6` | **Falhou** (três CA1806, reparados em `975f171`) | conferir **novo** SHA sem reaproveitar recibos |
| 30k TRAIN SQL + TEST independente | **Pendente** | campanha física manual no SHA final; gate `DEFERRED_NOT_CERTIFIED` não pode ser contornado |
| DC-V9-TF 0–21, Gold incremental e DT-15 operacional | **Não certificadas por esta PR** | evidência específica e autorização de governança |

Sem publicação em HML/PROD, sem auto-merge, sem alteração do watchdog original da `master`. **Merge depende de autorização explícita.**
