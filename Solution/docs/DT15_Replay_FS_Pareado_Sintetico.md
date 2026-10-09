> **Separação de responsabilidades — 09/10/2026:** esta
> conferência sintética pareada de modelos para a DT-15
> não é revisão/publicação dos RESOLVIDOS do banco operacional.
> A reavaliação extraordinária com modelo novo consta como
> [DT-22](DT22_Reavaliacao_Governada_Resolvidos.md) aberta
> e postergada. `REPLAY` histórico mantém modelo de origem;
> `ACTIVATE` não dispara DT-22 automaticamente.
>
# DT-15 — replay FS agregado pareado no mesmo corpus sintético

**Estado:** etapa implementada em Desenvolvimento. Evidência exclusivamente de engenharia; **não é** o dossiê decisório completo, validação estatística da população de São Paulo ou parecer do operador master.

## Objetivo

Complementar o diagnóstico de blocking no treino e a prévia somente leitura da página master. O `Jornada.Linkage.Evaluation` executa o **mesmo avaliador C# já existente** duas vezes, em sequência: uma com o modelo ATIVO-base e outra com o RASCUNHO, sobre os **mesmos arquivos do corpus sintético congelado**. Cada modelo gera seu próprio candidate set/ruleset; ambos compartilham o universo de observações, a verdade e a partição determinística de VALIDATION/TEST.

O comparativo mede o efeito agregado no FS real de parâmetros, regras de blocking, threshold e guardas **congelados de cada modelo**, sem estimar novo threshold a partir dos resultados. Constrói um arquivo externo imutável por invocação, acompanhado do hash SHA-256. Não grava dados no SQL, não muda a Gold ou estados dos modelos, não dispara `linkage_run` nem registra aprovação.

## Invocação Development

Na pasta `Solution/`, com `ConnectionStrings__Jornada` apontando exclusivamente para SQL Server isolado **com marcador residente** `Jornada.EnvironmentProfile=Development`:

```bash
dotnet run --project src/Jornada.Linkage.Evaluation -- \
  --dt15-compare-synthetic <guid-ATIVO> <guid-RASCUNHO> \
  <diretorio-corpus-gerado> <novo-dossie.json> \
  --max-candidate-pairs 250000
```

O limite opcional de pares é 1.000 a 1.000.000, padrão 250.000. Ausência dos arquivos de corpus ou ultrapassar o limite de candidatos aborta; **nunca** trunca silenciosamente. O destino deve ser novo: arquivos anteriores não são sobrescritos. A CLI retorna o estado do dossiê e o SHA256, sem PII nem dados por observação.

## Controles de comparabilidade

Antes de abrir arquivos sintéticos, o comando verifica o marcador residente Development, **um único ATIVO**, o RASCUNHO solicitado e seed/basis-points de TRAIN/VALIDATION/TEST idênticos. Captura os fingerprints canônicos via `auditoria.sp_calcular_fingerprint_modelo_linkage`, refaz o teste depois das duas execuções e descarta o resultado se alguma versão/hashes/estados mudaram durante o replay.

O contrato `DT15_SYNTHETIC_FS_PAIRED_V1` usa `COMPARAVEL_APENAS_FS_SINTETICO_AGREGADO` apenas para **mesmos** hashes de `generation-manifest`, `observacoes.csv`, `bridge-truth.jsonl` e `bridge-manifest`, mesmo seed, mesmo algoritmo/normalização, população de pares verdadeira e não-vínculos igual, mesmo contrato de partição e os mesmos IDs implícitos de cenários determinísticos. A verificação requer que cada modelo produza métricas completas da **política FS persistida** para VALIDATION e TEST sobre os mesmos totais e os mesmos estratos com suporte verdadeiro igual.

Status `NAO_COMPARAVEL` e `INCOMPLETO` **não contêm deltas**. Ausência de histórico, modelo inicial sem ATIVO, marcador não-DEV, mudança concorrente de base ou erro de execução não se transformam em diferenças zero.

## Conteúdo do dossiê

Identidade e hash de ambos os modelos/rulesets, proveniência de corpus/seed, verdade congelada e método; recall de candidatos, redução de não-vínculos e candidatos D por modelo; resultados congelados do FS em VALIDATION/TEST (TP, TN, FP, FN, inconclusivos e deltas RASCUNHO–ATIVO); recortes comparáveis por estrato. O FPS do scorer operacional continua float64 com fronteira decimal; são reutilizados os comparadores C#, `FellegiSunterScoring` e `FsDecisionThresholdCalibrator.EvaluateFrozen`. As saídas do oráculo de seleção não entram nos deltas de decisões do modelo.

`FP` pode se sobrepor a `FN` quando um cenário positivo é resolvido para a pessoa errada. Por isso a prova de suporte positivo usa `TP+FN`; **TN+FP não é denominador negativo estável** nesse contrato. Cenários positivos e leave-truth-out continuam emparelhados pela identidade sintética base, e as políticas de blocking podem gerar conjuntos candidatos diferentes.

## Fronteiras obrigatórias

- Apenas **sintético e agregado**; ainda faltam diferenças **por cenário** (ex.: RESOLVIDO→CONFLITO), custos e latência SQL pareados, intervalo de incerteza, classificação de amostra municipal e comprovação de fidelidade temporal em ondas.
- O resultado externo **não é ingerido automaticamente** pela página DEV ` /governanca/modelos ` e não concede permissão de promoção.
- Em HML/PROD o comando se recusa a executar; nenhuma rota master mutável ou IdP corporativo foi criada.
- `VALIDATE` e `ACTIVATE` diretos ainda precisam de parecer individual vinculado por hash, ledger append-only e controle transacional anti-TOCTOU. A DT-15 **permanece aberta** até esses gates serem implementados.

Referência: [DT-15 — dossiê e página master](DT15_Governanca_Decisao_Modelo.md).
