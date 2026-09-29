> **EMENDADO/HISTÓRICO (29/09/2026):** este arquivo registra a **implementação inicial da V8**; não constitui veto a novas hipóteses de ausência. A política vigente é [DC-LK-01/02/03](Decisoes_Canonicas_Identidade_Linkage_20260929.md): apenas V8 como destino, remoção pendente do guard demográfico fixo, e **ausência de nome social como hipótese calibrável** (não neutralização/penalidade presumida). Menções V6/V7 abaixo são de replay e testes do HEAD anterior, não decisões operacionais futuras.

# Ausência neutra e contrato probabilístico V8

## Decisão

Na `FELLEGI_SUNTER_DECISION_EVIDENCE_NEUTRAL_MISSING_V8`, ausência de nome completo, nome da mãe ou data de nascimento em pelo menos uma das observações significa **evidência indisponível**: LLR = 0, probabilidades m/u ausentes e estado `MISSING_NEUTRAL` no breakdown. Ausência não é discordância, nem afirmação sobre filiação.

Comparações presentes preservam os comparadores de nome V1 (`WHOLE_NAME_JARO_WINKLER_V1`) e o nascimento semântico V5 da V6. A V7 experimental com guard nominal V2 não é ativada por esta decisão.

## Contrato e calibração

- A V8 exige `SCORING_MISSING_EVIDENCE_NEUTRAL_V1=1`, `SCORING_DECISION_EVIDENCE_V6=1` e margem log-odds. A V6/V7 mantém replay: nome da mãe ausente usa `M_NOME_MAE_MISSING`/`U_NOME_MAE_MISSING`. A V8 recusa esses parâmetros.
- O estimador V8 calcula m/u do nome materno **condicionados aos pares em que ambos os nomes estão presentes**, com suavização entre EXACT, HIGH, MEDIUM e LOW. Os suportes de ausência `SUPPORT_M_NOME_MAE_MISSING` e `SUPPORT_U_NOME_MAE_MISSING` continuam persistidos para auditoria.
- O bootstrap IBGE de u materno, quando usado, ocupa toda a massa condicional observável; não recebe multiplicador de probabilidade de ausência na V8. Threshold, margem e piso de conflito devem ser recalibrados: **não reutilizar os valores V6**.
- A migração SQL V8 preserva os contratos V5/V6, estende gates de nascimento e monotonicidade à V8 e recusa sua promoção sem proveniência neutra ou com probabilidades maternas MISSING.
- As configurações do Worker e do Ensaio apontam V8 **somente para novos rascunhos**; não modificam automaticamente modelos já ATIVOS.

## Gates antes de ativar

1. Rodar build, testes unitários do scorer, breakdown, estimator, política de versões e regressão V6/V7, além dos testes SQL de promoção.
2. Reexecutar `Jornada.Linkage.Conference` para cada modelo V8: a evidência histórica artesanal V6 não atesta o novo scorer. Preservar o fingerprint e a tolerância governada.
3. Gerar RASCUNHO em DEV preservando IBGE e bases originais; inspecionar suportes de ausência materna e suportes por passe. Calibrar T/margem/piso em TRAIN/VALIDATION sem usar TEST para selecionar parâmetros.
4. Comparar V6/V8 no mesmo corpus congelado, inclusive sem CPF, por Secretaria e padrão de ausência unilateral/bilateral. Medir falsos vínculos, recall, precisão e perdas de resolução; não afirmar melhoria estatística sem ensaio.
5. Somente `VALIDATE` e `ACTIVATE` explícitos após evidência governada `CONFORME` para o **novo** modelo. O bloqueio fail-closed permanece.

Esta mudança não altera a obrigatoriedade nos contratos de ingestão, não preenche valores ausentes e não muda a resolução determinística pelo CPF.
