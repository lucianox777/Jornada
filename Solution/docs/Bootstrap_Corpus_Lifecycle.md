# Lifecycle do corpus sintético de bootstrap

O corpus sintético usado para obter o primeiro modelo de linkage é **efêmero**. Ele não é dado operacional e não deve permanecer em Silver/Gold depois que o modelo calibrado é ativado.

## Estados

1. **ABSENT** — nenhum corpus de bootstrap materializado.
2. **PENDING_DISCARD** — o corpus `SCALE-*` foi explicitamente reservado para bootstrap/calibração.
3. **DISCARDED** — existe modelo calibrado ativo e o corpus de bootstrap foi removido do estado operacional.

A transição `PENDING_DISCARD → DISCARDED` é executada por `scripts/bootstrap-corpus-lifecycle.ps1` e `database/Jornada_BootstrapCorpus_Discard.sql`.

## Política por ambiente

| Ambiente | Corpus de bootstrap | Após ativar modelo | Massa sintética funcional |
|---|---|---|---|
| Development / Console DEV | temporário | descartado automaticamente | opcional e deliberada, criada depois do bootstrap |
| Homologation | temporário | descarte obrigatório antes de liberar tráfego/testes operacionais | somente se houver cenário de teste explicitamente isolado |
| Production | temporário | descarte obrigatório antes de liberar ingestão operacional | proibida como dado operacional |

Em Homologation e Production o wrapper exige `-EnvFile` explícito. Ele nunca assume credenciais ou banco a partir do DEV.

## Invariantes de segurança

O descarte somente começa quando:

- `Jornada.EnvironmentProfile` coincide com o perfil explicitamente informado;
- `Jornada.BootstrapCorpusLifecycle=PENDING_DISCARD`;
- existe exatamente um modelo calibrado `ATIVO` (seed fixo não conta).

Durante o descarte, o conjunto de origens é congelado pelos códigos `SCALE-*`. A rotina remove as identidades/observações correspondentes e reabilita constraints com `WITH CHECK CHECK CONSTRAINT`. Qualquer referência esquecida impede o commit e causa rollback.

Antes e depois são conferidos o ID/versão do modelo ativo, a quantidade de `identidade.parametro_linkage` e de `identidade.linkage_ruleset`. A referência IBGE não é removida.

## Console DEV

A subida da Console usa a massa de 30 mil apenas para criar o modelo BOOTSTRAP. Ao terminar a calibração, marca e descarta esse corpus. Assim, os registros pendentes sintéticos não entram no linkage das cargas enviadas pelo desenvolvedor.

A ação **Adicionar 5.000 registros sintéticos** é um lifecycle diferente: cria massa funcional depois do bootstrap para testes de busca, semicega, blocking e escala. Essa massa não recebe `PENDING_DISCARD` e portanto não é removida pela rotina de bootstrap.
