# CI — Watchdog horário e recuperação estritamente infraestrutural

**Escopo de engenharia (12/10/2026):** incremento incremental de autorrecuperação de CI, sem alterar decisões canônicas de identidade/linkage, sem reabrir DT-10 ou substituir gates técnicos. A refatoração completa de builds e um agente de correção de código **não** estão concluídos por esta entrega.

## Funcionamento

- `.github/workflows/ci-watchdog.yml` executa a cada hora (minuto 17 UTC, sujeito a atrasos de agendamento do GitHub Actions). `workflow_dispatch` opera inicialmente em **diagnóstico**; o parâmetro `apply=true` habilita a mesma política estrita no disparo manual.
- Usa exclusivamente `github.token` efêmero da execução, com `actions: write` / `contents: read`, e não instala nem solicita PAT duradouro. O workflow só roda no repositório canônico e em `master`; o checkout desabilita persistência de credenciais.
- Ao verificar a CI, consulta os últimos 30 runs; restringe-se a execuções `jornada-ci` **concluídas em failure**, criadas há no máximo 24 h, primeira tentativa, HEAD ainda atual de `master` ou de PR aberto originado no próprio repositório. Um novo run do mesmo SHA prevalece sobre falha histórica.
- Requer listagem **completa** dos jobs, ausência de jobs em andamento, e marca explícita de interrupção de runner GitHub nos logs de **cada job com failure**. `timed_out`, `cancelled`, `action_required`, erro de teste, compilação, segurança, política ou log indisponível **não** autorizam rerun.
- Só então chama a API `rerun-failed-jobs`, uma vez por run (`run_attempt == 1`); não inicia build completo desnecessário, nem cancela job em execução. Concorrência serial evita duas instâncias do watchdog.
- Quando a API entrega URL assinada dos logs, o token de API **não** é enviado ao host do download; somente destinos HTTPS permitidos são aceitos. Log parcial não vira prova.
- Publica evidência em `GITHUB_STEP_SUMMARY`, com SHA, ID e decisão sem reproduzir segredos ou conteúdo dos logs. Testes simulados cobrindo replay, erro de produto e SHA obsoleto antecedem a ação.

## Limites e continuidade

O watchdog é infraestrutura de supervisão/reexecução, **não agente generativo de reparo**. Correção autônoma de código exige executor isolado, escopo de branch/PR, verificação de proveniência dos diffs, separação de permissões, proteção contra loops, teto de tentativas, aprovação de políticas e gates **no SHA exato**. Não misturar autorização `actions: write` do watchdog com `contents: write`/PR/merge.

A validação do PR depende da CI integral do repositório, conforme `Solution/tests/scripts/test_ci_required_gates.py` e `DT10_CI_Extracao_Reutilizavel.md`. Os dez gates, inclusive integração SQL, E2E e segurança, continuam obrigatórios para alterações de código. Em caso de erro não classificado, este watchdog **falha fechado** e aguarda a correção por PR testada; não falsifica êxito.

**Isolamento:** somente GitHub Actions, scripts Python e REST GitHub. Nenhuma intervenção no banco `JornadaLocal`, NAS, referência IBGE, NODE1/NODE2, HML/PROD, volumes ou contêineres locais. O monitor horário do ChatGPT é complementar e não substitui a execução persistente agendada no GitHub.
