# ADR — REFERENCIA como estado da identidade progressiva

Estado: decisão aprovada em 2026-09-08; migração técnica em desenvolvimento. Complementa ADR_Identidade_Progressiva.md e ADR_CPF_Ancora_Imutavel.md.

## Decisão

O estado público `RESOLVIDA` da identidade progressiva passa a chamar-se `REFERENCIA`. O vocabulário é `PROVISORIA`, `REFERENCIA`, `INDEFINIDA`. REFERENCIA significa que uma origem possui referência canônica estabelecida segundo a política e as evidências declaradas. Não significa certeza absoluta de identidade civil, titularidade de CPF, correção de todos os registros ou autorização de compartilhamento.

A identidade inicial continua imutável. A referência canônica pode evoluir por decisão versionada e recomposição explícita. A âncora CPF→UUID permanece permanente e não pode ser transferida. Atribuições de observações e fatos continuam independentes, com seus próprios estados de pendência e conflito. Uma referência pode existir sem fatos ativos, e um fato válido pode permanecer sem atribuição canônica.

## Limites do vocabulário

Não renomear globalmente `RESOLVIDO` de `identidade.vinculo_fonte`, estados de Pessoa, estados de atribuição factual, resultados `NOVA_IDENTIDADE`/`ASSOCIACAO_EXISTENTE`/`INDEFINIDA`, nem o evento técnico `RESOLUCAO`. Esses conceitos possuem contratos e ciclos de vida distintos. A mudança não ativa scoring, fusão probabilística ou novas permissões de consulta.

## Migração e compatibilidade

O contrato novo publica REFERENCIA. O armazenamento corrente deve normalizar RESOLVIDA para REFERENCIA sem alterar UUID, versão, datas ou atribuição. Recibos históricos append-only preservam o valor original e continuam válidos por compatibilidade explícita; a leitura deve normalizá-los sem reescrever a história. Novos recibos devem usar REFERENCIA. A migração deve ser transacional, reentrante e falhar diante de estados desconhecidos. Não usar substituição textual global nem desabilitar permanentemente guardas de imutabilidade.

A instalação nova deve aplicar o vocabulário atualizado depois da persistência V1; o upgrade deve aceitar bancos V1 já povoados. Todos os escritores e leitores devem ser migrados antes de retirar compatibilidade de escrita legada. O corte definitivo exige testes de SQL Server e PostgreSQL, upgrade povoado, instalação dupla, rollback, histórico, API e Processor. Não executar em produção antes dessa validação.

## Próximas etapas

Concluir a migração e seus gates, integrar a âncora CPF aos escritores e às correções governadas, versionar as APIs de referência e atribuição, e validar E2E até Gold/Serving. A homologação estatística da issue #31 permanece independente e obrigatória antes de ativar Linkage probabilístico.
