# Lifecycle do corpus sintético de bootstrap

O corpus sintético é entrada transitória da calibração. Nenhum ambiente pode ficar pronto com registros de bootstrap pendentes. Em Development, depois de processado e calibrado, o domínio é publicado/preservado na Gold; em Homologation/Production ele é descartado integralmente, preservando somente o resultado aprendido.

## Estados

1. **ABSENT** — nenhum corpus de bootstrap materializado.
2. **PENDING_DISCARD** — o corpus `SCALE-*` foi explicitamente reservado para bootstrap/calibração.
3. **DISCARDED** — existe modelo calibrado ativo e o corpus de bootstrap foi removido do estado operacional.

A transição `PENDING_DISCARD → DISCARDED` é executada por `scripts/bootstrap-corpus-lifecycle.ps1` e `database/Jornada_BootstrapCorpus_Discard.sql`.

## Política por ambiente

| Ambiente | Corpus de bootstrap | Após ativar modelo | Massa sintética funcional |
|---|---|---|---|
| Development / Console DEV | processa integralmente | preserva/publica o domínio na Gold; zero pendências | a própria Gold inicial pode ser expandida |
| Homologation | processa integralmente para calibrar | descarte obrigatório do domínio antes de liberar o ambiente | não permanece como domínio operacional |
| Production | processa integralmente para calibrar | descarte obrigatório do domínio antes de liberar ingestão | proibida como dado operacional |

Em Homologation e Production o wrapper exige `-EnvFile` explícito. Ele nunca assume credenciais ou banco a partir do DEV.

## Invariantes de segurança

O descarte é exclusivo de Homologation/Production e somente começa quando:

- `Jornada.EnvironmentProfile` coincide com o perfil explicitamente informado;
- `Jornada.BootstrapCorpusLifecycle=PENDING_DISCARD`;
- existe exatamente um modelo calibrado `ATIVO` (seed fixo não conta).

Durante o descarte, o conjunto de origens é congelado pelos códigos `SCALE-*`. A rotina remove as identidades/observações correspondentes e reabilita constraints com `WITH CHECK CHECK CONSTRAINT`. Qualquer referência esquecida impede o commit e causa rollback.

Antes e depois são conferidos o ID/versão do modelo ativo, a quantidade de `identidade.parametro_linkage` e de `identidade.linkage_ruleset`. A referência IBGE não é removida.

## Console DEV

A subida da Console materializa/processa o corpus, publica a Gold DEV, calibra/ativa o modelo e verifica zero pendências. Em Development o corpus processado não é descartado. A ação **Adicionar mais 5.000 registros** expande essa Gold preservada (30k → 35k → 40k). HML/PROD, ao contrário, eliminam o domínio sintético após a calibração e deixam somente modelo/parâmetros/configuração necessários ao linkage.
