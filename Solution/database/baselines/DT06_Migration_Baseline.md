# DT-06 — baseline de migrations (SQL Server)

A autoridade de ordem permanece `database/migrations/manifest.txt`. O instalador SQLCMD canônico continua `database/Jornada_Fase1_v3.70.sql`; `Jornada_Fase1.sql` é somente a **base** incluída. O marcador de schema continua 3.70 enquanto migrações aditivas 3.71 aguardam seu fechamento. Este DT não altera migrations aplicadas, manifesto, wrapper, hashes nem proveniência RC.

## Instalação nova, com ledger desde o primeiro dia

A partir de `Solution/` (com `sqlcmd`, `sha256sum` e credencial autorizada):

```bash
export JORNADA_SQL_SERVER=localhost
export JORNADA_SQL_DATABASE=JornadaNova_DT06
# Fornecer JORNADA_SQL_USER/JORNADA_SQL_PASSWORD por ambiente protegido se necessário.
bash database/baselines/install-fresh.sh
```

O script recusa qualquer banco existente e nomes reservados. Executa a base **uma vez** e o runner existente `scripts/apply-migrations.sh` para aplicar todas as entradas em ordem e registrar seus SHA-256 reais em `jornada.schema_migration`. Não executa o wrapper completo **antes** do runner, o que duplicaria migrations sem ledger inicial. Não insere dados sintéticos, não faz reset nem remove banco parcialmente criado em caso de erro. Para diagnosticar uma interrupção, verificar objetos e ledger; só então retomar pelo runner de upgrade.

## Upgrade com histórico já aplicado

1. Fixar o commit, registrar `migration_name`, `sha256` e `applied_at` da tabela de histórico, o marcador `Jornada.SolutionSchema`, contagens de negócio e versões dos sistemas conectados. Não inventar registros de migração para instalações legadas sem ledger.
2. Parar writers/ingestão/linkage, fazer **BACKUP DATABASE** integral com `CHECKSUM` e testar `RESTORE VERIFYONLY`. Preservar também snapshots de objetos Bronze externos e configurações. Testar RESTORE real em ambiente isolado quando possível.
3. Executar `JORNADA_SQL_DATABASE=BancoExistente bash Solution/scripts/apply-migrations.sh` sem aplicar de novo a base ou o wrapper. O runner recusa qualquer checksum alterado para migração pré-aplicada, não apaga nem reescreve histórico.
4. Conferir marker/schema, referências, quantidades e ledger, e repetir o runner para prova de idempotência. Em erro parcial manter o banco isolado até conciliação ou restore.

## Ensaio SQL com histórico parcialmente aplicado

```bash
export JORNADA_DT06_TEST_DATABASE=JornadaDT06_Auditoria01
export JORNADA_DT06_CONFIRM_CREATE=YES
bash database/baselines/test-history-upgrade.sh
```

Exige nome reservado e banco inexistente. Instala a baseline real 3.65, grava sentinela, executa as três primeiras entradas do manifesto e as carimba com checksum de seus bytes. Em seguida aplica **somente migrações pendentes** usando o runner de produção, verifica histórico e sentinela, e executa novamente para confirmar preservação. Não remove o banco de teste automaticamente. O workflow existente `jornada-schema-consolidation-370` também cobre upgrade SQL real de 3.65 e 3.69, repetição sobre ledger e rejeição de checksum adulterado em bancos efêmeros.

## Backup/rollback

Exemplo de **backup SQL Server**, adequar destino protegido e credenciais ao ambiente:

```sql
BACKUP DATABASE [BancoExistente] TO DISK = N'/backup/BancoExistente-pre-DT06.bak'
  WITH COPY_ONLY, CHECKSUM, COMPRESSION, INIT;
RESTORE VERIFYONLY FROM DISK = N'/backup/BancoExistente-pre-DT06.bak' WITH CHECKSUM;
```

DDL distribuído em `GO` e migrações com efeitos em dados não admite downgrade genérico confiável. Em falha, desabilitar writers e **restaurar o backup integral validado** no destino correto, sincronizando os objetos externos Bronze/consumidores; nunca apagar linha do ledger, editar `applied_at`, reutilizar versão com novo SQL ou presumir que `ROLLBACK` de uma transação reverte toda a instalação. Alternativa quando restauração não for aceitável: nova migration aditiva compensatória com análise de impacto e testes, sem modificar/apagar migrations já aplicadas.
