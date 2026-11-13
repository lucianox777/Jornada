# GOV-V9-PROGRESS-UMA-LINHA-POR-HIPOTESE-20261113 — formato minimalista aprovado

**Autoridade:** Product Owner, 13/11/2026 (America/Sao_Paulo). **Estado:** DECISÃO APROVADA; implementação do novo formato ainda PENDENTE de PR/CI/integração. **Política:** GOV-DOC-01.

## Contexto e evidências

O PO comparou a execução [#43105369099](https://github.com/lucianox777/Jornada/actions/runs/43105369099), 324/324 sem mensagens individuais, `14.039.465 ms` (~234 min) e SUCCESS, às campanhas [#43159426682](https://github.com/lucianox777/Jornada/actions/runs/43159426682) (~303,1 min) e [#43309643251](https://github.com/lucianox777/Jornada/actions/runs/43309643251) (~303,7 min), com instrumentação individual. O salto de ~69 min é observacional; as execuções têm SHA e runner diferentes, portanto não comprova causa por `Stopwatch`, console, sincronização ou `Flush`. A análise corrigida está em [Baseline_Tempo_Decorrido_V9_100k_20261112.md](Baseline_Tempo_Decorrido_V9_100k_20261112.md).

O PO avaliou impressão por grupos de cinco e rejeitou o agrupamento: **manter exatamente uma mensagem por hipótese concluída**, sem segundo buffer, sem coordenação adicional entre grupos. Também confirmou que, no texto da mensagem, `|` é **caractere literal separador** e **não quebra de linha**; `Console.WriteLine` encerra cada registro com **um newline**, apenas após todos os campos.

## Decisão aprovada

Para a campanha V9 TF4 de 324 hipóteses, emitir **uma única linha física de console por conclusão**, contendo:

```text
[V9 PROGRESS] 38/324 | timestamp=2026-11-13T18:35:22Z | hipotese=047649776a35 | resultado=AVALIADA | restante=219min | limite=2026-11-13T22:15:00Z | status=DENTRO_DO_PRAZO
```

Valores ilustrativos, não um log real. A apresentação deve usar timestamp e limite com data e fuso inequívocos, preferencialmente UTC. O relógio limite da etapa física é referenciado no início do passo do workflow e corresponde **aproximadamente** ao prazo efetivo de `timeout-minutes: 315` (o prazo definitivo é aplicado pelo GitHub Actions). O teto adicional do job TRAIN de 360 minutos permanece efetivo; não anunciar prazo além do tempo realmente disponível. `restante` e status devem se referir a prazo, e não a uma extrapolação de ETA por produtividade.

Preservar **um único `lock` explícito do console**, **um único `Console.WriteLine`** e **um único `Flush()`** para a linha por hipótese; nenhuma linha vazia ou quebra intermediária. O contador já existente e os resultados `AVALIADA/REJEITADA` permanecem, sem agrupador de cinco. A linha de erro necessária permanece independente e não se deve suprimir falhas.

Manter **`V9_REPLAY_TIMING_DIAGNOSTICS=0` na execução normal**: a linha `[V9 PERF DIAG]` foi utilizada somente em investigação de desempenho, não alimenta FDR/CP/P95 ou a auditoria; preservar a opção somente para diagnóstico futuro explícito. Remover do progresso normal as estimativas operacionais de ETA, margem baseada em ETA e exposição duplicada de tempos de replay que não são necessárias ao formato aprovado. **Não remover relógios científicos do P95**, contadores/artefatos agregados obrigatórios, proteção de RAM/disco, segurança do SQL, prova física, auditoria ou a própria medição interna de cada hipótese caso ainda alimentem outros usos; auditá-los antes de qualquer exclusão. O objetivo é reduzir instrumentação redundante, não mudar ciência.

## Impactos, precedência e validação

**Precedência:** esta decisão substitui somente o **formato operacional** e a escolha de uma linha por hipótese das decisões [GOV-V9-PROGRESS-324-20261111](Decisao_GOV_V9_Progresso_Fisico_Hipoteses_20261111.md) e [GOV-V9-LOG-UNICO-PROGRESSO-REPLAY-20261113](Decisao_GOV_V9_Log_Unico_Progresso_Replay_20261113.md), **sem revogar os 324 resultados, a exclusividade do console, as travas científicas ou os timeouts 315/360**. A campanha [#43499315560](https://github.com/lucianox777/Jornada/actions/runs/43499315560), iniciada antes de implementação futura, não recebe alterações retroativas nem é cancelada por esta decisão.

**Aceite exigido:** testes de exatamente uma linha e um newline por hipótese, com colunas e delimitadores ` | `, 324/324 (inclusive rejeições e grupos concorrentes); evitar blocos extra de console por hipótese; demonstração de que a opção diagnóstica OFF não emite `PERF DIAG`; confirmar origem do prazo físico, limite real 315 minutos e job 360 minutos; comparar campanha manual de 100k futura contra baseline de ~234 min sem atribuição causal sem repetição controlada. Não iniciar execução manual automaticamente. A atividade só está IMPLEMENTADA após PR de código, CI verde, squash merge e atualização do índice; **este registro documental isolado NÃO equivale a implementação**.

## Esclarecimento posterior do PO — apresentação substituída

O PO esclareceu em 13/11/2026 que **uma informação por linha** significa **um campo por linha física**, e que o caractere `|` (pipe) **não é necessário**. A [decisão GOV-V9-PROGRESS-CAMPOS-MULTILINHA-20261113](Decisao_GOV_V9_Progresso_Campos_Multilinha_20261113.md) **substitui a exigência anterior de uma linha física por hipótese com pipes**, mas **preserva uma mensagem lógica por hipótese**, um `Console.WriteLine`, um `lock` e um `Flush`. Os sete campos passam a ser emitidos em sete linhas físicas consecutivas dentro de uma única escrita. A presente decisão permanece como histórico de deliberação e **não é a especificação visual vigente**. GOV-DOC-01.
