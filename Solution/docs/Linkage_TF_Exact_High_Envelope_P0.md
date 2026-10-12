# P0 — Envelope de coerência TF: EXACT e HIGH (pré-publicação)

**12/10/2026. Escopo: novos rascunhos V8, sem migração/replay de modelos históricos.**

## Fato reproduzido

PR #927, run 38199447430: no ranking sintético, `MARIA SILVA` (EXACT) fica atrás de `MARIA SILVAA` (HIGH), com restante da evidência igual/ausente. A fixture tinha `M_NOME_EXACT=U_NOME_EXACT=M_NOME_HIGH=U_NOME_HIGH=0,25`, por isso os dois LLR Fellegi–Sunter base são zero. O TF da primeira palavra publicada é `MARIA=0,6`; o runtime calcula para EXACT `ln(0,25/0,6)≈−0,87547`, enquanto HIGH não recebe ajuste. A matemática TF não é um erro de implementação de `SplinkCompatibleTermFrequency`; o modelo sintético não é calibrado e o contrato nominal pré-TF não comprova ordem após TF.

## Política mínima de segurança

**Não** alterar o peso TF, probabilidades calibradas, fórmula de Splink, `Rank`, `Resolve`, empates por UUID, limiares de conflito nem dados oficiais para deixar o teste verde. A política é impedir que **novos rascunhos** incorporem um envelope em que EXACT possa ser penalizado abaixo de HIGH com evidência restante igual, sem fingir recalibração.

O helper `LinkageTermFrequencyOrderingEnvelope` usa apenas os parâmetros já estimados e as duas listas do snapshot nominal por modelo. Para cada um dos atributos independentes `NOME` e `NOME_MAE`, calcula:

- `LLR_FS_EXACT = ln(M_EXACT/U_EXACT)`
- `LLR_EXACT_PIOR = LLR_FS_EXACT + weight × ln(U_EXACT/max(maxPublishedFrequency,minimumU))`
- `LLR_HIGH = ln(M_HIGH/U_HIGH)` (HIGH atualmente não tem ajuste TF no runtime V8)
- `folga = LLR_EXACT_PIOR − LLR_HIGH`, que deve ser **estritamente positiva**.

A frequência máxima publicada é o caso de **maior redução** do LLR EXACT. Se existir empate/inversão, o novo `GENERATE_DRAFT` falha fechado antes de buscar cenários de calibração/gravá-los: não publica um modelo inseguro nem cria evidência fictícia. O comportamento de modelos já persistidos **não muda** nesta PR. `weight=0` mantém a fórmula FS inalterada; ausência de frequências/parâmetros ou probabilidades inválidas bloqueiam a avaliação.

Esta condição não substitui a calibração TRAIN/VALIDATION/TEST, monitoramento do orçamento de falsos vínculos, amostras condicionadas ao blocking, modelo de independência, critérios de conflitos e aprovação humana. **Não certifica**, sozinha, que EXACT deva sempre vencer HIGH em situações com outras evidências divergentes; vale só para comparação nominal de mesmo contexto. A questão maior de decisão automática quando o modelo já persistido é incoerente permanece associada à PR #927. Não marcar #927 como corrigida só com este gate.

## Testes e implantação

A suíte NUnit `LinkageTermFrequencyOrderingEnvelopeTests` reproduz a inversão de Maria, o caso positivo com parâmetros sintéticos explicitamente mais fortes, o teste separado de mãe, neutralidade quando `weight=0`, desempate inseguro e inputs malformados. O pipeline deve executar **todos** os gates obrigatórios, incluindo unit, SQL, E2E real, CodeQL, harness e DT10 quando o classificador exigir, sobre o SHA exato. A PR fica draft se qualquer suíte falhar; não relaxar as provas para acelerar o merge. O gate não lê SQL local nem corpus IBGE original: usa somente snapshot já derivado em runners descartáveis.

## Limites

Amostras estatísticas são sintéticas; a validação do algoritmo completo em corpus DEV Windows, NAS/NODE, `JornadaLocal` e dados reais não é executada pelo GitHub CI. Se a nova condição bloqueia a geração de modelos operacionais atuais, **recalibrar parâmetros de forma governada** e comparar orçamento de falsos vínculos; não contornar o gate.
