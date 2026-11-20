# Implementação proposta — inventário cumulativo read-only de CI

**ID:** GOV-CI-INVENTARIO-EXECUTAVEL-20261120. **Estado:** PROPOSTA TÉCNICA, não aprovada para integração. **Origem:** mandato do PO de acompanhar CI acumulativa por interação, documentado em GOV-OPS-INTERACAO-IA-CI-ACUMULATIVA-20261119 (PR #1757). **Escopo:** somente consulta de Actions e saída JSON local; não altera gates científicos nem workflows físicos.

## Contexto e justificativa

A consulta disponível no conector GitHub nesta sessão retorna runs por commit, sem inventário global paginado; portanto, contagens consolidadas do repositório não são demonstráveis por essa consulta. Este utilitário independente fornece um caminho reproduzível **quando executado com credencial de leitura e acesso à API Actions**. Não representa uma coleta já executada.

## Implementação

- `Solution/scripts/ci-cumulative-inventory.py` consulta `/actions/runs` com filtro `created` e paginação de 100, rejeita alterações de total durante coleta, IDs duplicados, resposta incompleta, janela inválida e limite GitHub de 1.000 runs por filtro. Para períodos maiores, dividir a janela e reconciliar por ID.
- Classifica `approved`, `running`, `queued`, `failed` e `other`, garantindo a soma dos estados e evitando contar `cancelled`/`skipped` como sucesso.
- Inspeciona jobs reprovados em runs `failed` ou `running`, sem baixar logs sensíveis; captura IDs, nomes e conclusões.
- Gera JSON com `run_id`, `run_attempt`, SHA, timestamps, URL, workflow, evento e categoria. **Não** persiste token; saída local deve ser tratada como relatório operacional, não decisão.
- Testes offline de paginação, reconciliação, categorias, jobs e falhas fechadas são executados pelo `classify-impact` da CI obrigatória.

## Execução manual segura (exemplo, não realizada)

```sh
GH_TOKEN=<token-somente-leitura> python3 Solution/scripts/ci-cumulative-inventory.py \
  --since 2026-11-19T00:00:00Z --until 2026-11-20T00:00:00Z \
  --output /tmp/jornada-ci-snapshot.json
```

Usar token por variável de ambiente, jamais escrever valor real no comando versionado, logs ou chat. Para reconciliação histórica, comparar snapshots sucessivos por `run_id` e `run_attempt`; este utilitário **não implementa ainda** persistência automática de deltas, monitoramento agendado ou listagem de todas as PRs.

## Limites e aceite

**Não** executar workflow científico, retry, merge, alteração de branch protection, upload, escrita GitHub ou automação persistente. O filtro de tempo é por criação do run; execuções anteriores ainda ativas exigem janela retrospectiva adequada. Não afirmar inventário global se API negar acesso, a paginação mudar ou o intervalo exceder limite. A CI do PR comprova somente testes offline, não uma coleta autenticada real. Aceite operacional exige execução autenticada, escopo e janela declarados, contagens reconciliadas, jobs verificados e comparação de snapshots.

**Precedência:** implementação proposta subordinada a [protocolo canônico](Decisao_GOV_OPS_Correcao_Erros_Squash_CI_Master_20261111.md), [mandato detalhado](Mandato_IA_Ciclo_Desenvolvimento_Continuo.md) (PR #1757) e GOV-DOC-01. Não modifica as decisões já aprovadas.

## Reconciliação incremental proposta
A opção `--previous <snapshot.json>` compara dois snapshots **da mesma janela e repositório**. Reporta novos `run_id` e transições de categoria ou tentativa, sem dupla contagem; rejeita mudança de janela, desaparecimento de run, SHA alterado, IDs duplicados e total inconsistente. Para acompanhar janelas móveis, será necessário um checkpoint persistente/algoritmo de sobreposição independente; não declarar que esta comparação já implementa monitoramento global contínuo. A comparação não executa ações no GitHub.
