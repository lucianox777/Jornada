# Bronze externa - operação Fase 1 v3.50

## Regra de integridade

A Bronze é content-addressed por SHA-256 (`sha256/ab/cd/<sha256>.zip`). A chave no SQL é relativa e o objeto é imutável. Objeto canônico já existente é re-hasheado antes de ser aceito como idempotente; divergência de tamanho ou SHA-256 é falha de infraestrutura (`BRONZE_INTEGRIDADE_DIVERGENTE`), não erro de contrato do Gestor.

O Processor recalcula o SHA-256 do ZIP aberto por `objeto_chave` antes de materializar Silver/Gold. Objeto ausente ou divergente entra em QUARENTENA; indisponibilidade temporária do storage é reagendada.

## Indisponibilidade HTTP

Se a API não conseguir gravar/verificar a Bronze por indisponibilidade de storage, ou detectar corrupção do objeto canônico existente, responde `503 Service Unavailable` com `Retry-After: 60`. O Gestor pode reenviar com a mesma `Idempotency-Key`; a operação é idempotente.

## Pacote determinístico

A deduplicação física é garantida para ZIPs byte a byte idênticos. Para que uma base lógica inalterada gere o mesmo SHA-256, o gerador homologado deve seguir perfil determinístico: mesma ordem das entradas (`manifest.json`, `pessoas.jsonl`, `registros.jsonl`), nomes canônicos, bytes UTF-8 estáveis, timestamp ZIP fixo e versão homologada do algoritmo/runtime de compressão. `Jornada.Ingestion.DeterministicIngestionZipWriter` é a implementação de referência e possui teste de reprodutibilidade.

## GC de órfãos e temporários

`Jornada.Bronze.Maintenance.Worker` executa coleta periódica com `OrphanGraceHours` (default 24 h). Temporários `.tmp` mais antigos que a carência podem ser removidos diretamente, pois nunca são referenciados pelo SQL.

Para objetos canônicos, a exclusão é protegida contra a janela objeto-primeiro -> linha SQL:

1. API adquire `sp_getapplock` Shared em `Jornada.Bronze.Object.<sha256>` antes de `PutIfAbsentAsync` e o mantém até o commit da referência em `bronze.entrega_arquivo`.
2. O mantenedor adquire o mesmo recurso em modo Exclusive.
3. Sob o lock Exclusive, confirma `COUNT_BIG(*)=0` para `objeto_chave`.
4. Somente então remove o objeto físico.

Se houver referência `DISPONIVEL` ou o lock não puder ser adquirido, o GC não apaga o objeto. A v3.46 implementa retenção por Entrega no `Jornada.Operations.Maintenance.Worker`: a referência passa por `DISPONIVEL -> EXPURGO_PENDENTE -> EXPURGADO`, preservando a linha SQL e a proveniência. O objeto físico só é removido quando não resta nenhuma referência `DISPONIVEL`; objetos deduplicados compartilhados permanecem enquanto pelo menos uma Entrega ainda estiver dentro da retenção.

## Backup e restauração

Não é necessário que snapshot SQL e storage tenham o mesmo instante exato, porque objetos são imutáveis e objetos extras são inofensivos. A propriedade obrigatória é:

> Toda `objeto_chave` referenciada por qualquer backup SQL ainda restaurável deve continuar recuperável no armazenamento Bronze.

A infraestrutura deve, portanto, alinhar janela de retenção do banco e do storage, backup/replicação/soft-delete/versionamento conforme tecnologia adotada e testes periódicos de restore. Após restauração, executar verificação referencial: toda linha de `bronze.entrega_arquivo` deve apontar para objeto existente; divergências bloqueiam replay e devem gerar incidente operacional.

## Configuração do mantenedor

- `BronzeMaintenance:Enabled` - habilita o ciclo.
- `ScanIntervalMinutes` - periodicidade.
- `OrphanGraceHours` - carência mínima antes de considerar objeto/temporário órfão.
- `MaxObjectsPerCycle` - limite de trabalho por ciclo.
- `ObjectLockTimeoutSeconds` - espera máxima pelo lock Exclusive por objeto.

Em HML/Produção, `BronzeStorage:RootPath` permanece absoluto, compartilhado e durável para API, Processor e Maintenance Worker. Somente a identidade do Maintenance Worker deve possuir permissão de delete físico.


## Varredura justa e métricas - v3.38

O Maintenance Worker percorre os 65.536 buckets `ab/cd` com cursor persistente (`controle.bronze_manutencao_estado`) e registra cada ciclo em `controle.bronze_manutencao_ciclo`. Isso impede que `MaxObjectsPerCycle` faça o GC recomeçar eternamente do início. O BI usa `serving.v_bi_manutencao_bronze`.

## Retenção de Entregas/Bronze - v3.46

A política é deliberadamente **desabilitada por default**. `DeliveryBronzeRetention:RetentionDays` deve ser maior que zero e aprovado pela governança antes de `Enabled=true`. Somente Entregas `PROCESSADA` ou `REJEITADA` são elegíveis; `QUARENTENA` não é expurgada automaticamente. O payload pode expirar, mas `ingestao.entrega` e `bronze.entrega_arquivo` permanecem como trilha de auditoria, com `estado_armazenamento`, timestamps e motivo.

O mesmo app lock `Jornada.Bronze.Object.<sha256>` protege API, GC e retenção. Uma falha de storage deixa a referência em `EXPURGO_PENDENTE` para retry; não há transação distribuída entre SQL e filesystem. `serving.v_bi_retencao_bronze` expõe os ciclos sem PII.

## Verificação pós-restore - v3.46

`Jornada.Bronze.Verify` é um utilitário fail-closed para o restore drill. Ele percorre todas as referências `DISPONIVEL`, verifica existência, tamanho e SHA-256 no storage e retorna exit code diferente de zero se houver objeto ausente, divergente ou storage indisponível. Desde a engenharia v3.70, o drill local/CI exige também dois testes negativos após restaurar SQL+storage: remoção física do objeto deve produzir `MISSING`, adulteração dos bytes deve produzir `DIVERGENT`, e a reposição do objeto íntegro deve restaurar o PASS. `bronze-restore-evidence-gate.py` impede que o relatório seja aceito sem os cinco estágios (origem, restore, missing, corrupção e verificação final). Assim, a propriedade de backup/restore deixa de ser apenas um procedimento escrito e passa a ter verificador executável e fault injection de storage.


## Três hashes com finalidades distintas — v3.50

A Fase 1 usa SHA-256 em três contextos que não devem ser confundidos:

1. **Contrato/schema publicado**: o catálogo guarda o SHA-256 dos bytes do JSON Schema aprovado para impedir substituição *in-place* da mesma versão. Alterar o schema exige nova versão do contrato.
2. **Objeto Bronze/ZIP recebido**: a API calcula SHA-256 dos bytes físicos do ZIP para endereçamento, idempotência e verificação posterior de integridade. Formatação diferente de um novo payload produz naturalmente outro hash e não constitui erro; conflito existe quando a mesma chave idempotente é reutilizada para conteúdo diferente ou quando os bytes Bronze não correspondem ao digest gravado.
3. **Conteúdo factual canônico**: o versionamento lógico usa hash semântico canônico, com normalizações definidas pela Jornada. Ordem de propriedades JSON, representação numérica equivalente e outras diferenças puramente sintáticas cobertas pela canonicalização não criam versão factual artificial.

`BRONZE_INTEGRIDADE_DIVERGENTE` indica divergência dos bytes físicos armazenados em relação ao digest registrado, e não mera diferença de serialização entre duas Entregas distintas.
