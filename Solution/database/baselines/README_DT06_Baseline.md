# DT-06 — baseline versionada, upgrade preservando histórico e recuperação

**Snapshot congelado:** commit-fonte `42470cc1aa2166435e6776948a84b4baf824c0d7`, manifesto com 51 migrations e `SolutionSchema=3.70`. Os novos arquivos são aditivos: não substituem, apagam ou alteram uma migration histórica. O baseline referencia os arquivos existentes e só é válido com o checkout cujos 54 SHA-256 constam em `DT06_SHA256SUMS.txt`; futuras evoluções exigem outro snapshot, não atualização retroativa dos hashes.

## Nova instalação: banco explicitamente escolhido e vazio

1. Fazer checkout do commit que contém esta baseline e seus arquivos-fonte congelados. Na pasta `Solution/database`, **antes de qualquer sqlcmd**, executar `sha256sum -c baselines/DT06_SHA256SUMS.txt`. Interromper diante de qualquer divergência. Para PowerShell, comparar os 54 hashes com `Get-FileHash -Algorithm SHA256`.
2. Preparar o banco de destino vazio sob controle DBA, com collation, permissões, plano de backup e ambiente corretos. Não usar bases compartilhadas nem assumir `JornadaLocal`; a baseline não cria, derruba ou reseta banco.
3. A partir de `Solution/`, com credenciais no ambiente (nunca na CLI ou artifacts), executar: `sqlcmd -S <servidor> -U <usuario> -C -b -I -d <banco_novo> -i database/baselines/DT06_Instalacao_Nova_370.sql`. O SQL recusa tabela de usuário preexistente, executa o wrapper 3.70 canônico e só então registra os 51 checksums no ledger.
4. Executar `sqlcmd -S <servidor> -U <usuario> -C -b -I -d <banco_novo> -i database/tests/DT06_Verificar_Schema_370.sql`. Executar `scripts/apply-migrations.sh` apontando **explicitamente** `JORNADA_SQL_DATABASE=<banco_novo>`: o ledger já preenchido deve fazer o runner pular todas as migrations, sem replay.

**Falha parcial:** como o instalador pode aplicar DDL antes de chegar ao ledger, nunca continuar uma instalação falhada por tentativa cega sobre o mesmo banco. Investigar, restaurar snapshot anterior/descartar apenas banco descartável autorizado e repetir em banco vazio.

## Upgrade de banco já existente, com histórico aplicado

1. Estabelecer janela de manutenção, escolher um **clone restaurado isolado** e registrar o commit-fonte, versão, inventário de `jornada.schema_migration(migration_name,sha256,applied_at)`, `Jornada_Upgrade_Invariants.sql` (contagens/violações) e evidências do backup. Não reclassificar instalações antigas retroativamente por adivinhação de histórico.
2. Em `Solution/`, configurar `JORNADA_SQL_DATABASE`, `JORNADA_SQL_SERVER`, `JORNADA_SQL_USER` e `JORNADA_SQL_PASSWORD` no ambiente seguro. Executar `bash scripts/apply-migrations.sh` no **mesmo clone**. O executor valida SHA-256 de cada migration já registrada e aplica só as pendentes. Se divergirem hashes: abortar, investigar e nunca `UPDATE/DELETE` de linha aplicada.
3. Reexecutar o mesmo runner. Verificar 51 registros, `SolutionSchema=3.70`, sentinelas e invariantes antes/depois. Para comprovar preservação do histórico real já aplicado, capturar `database/tests/DT06_Exportar_Historico.sql` antes da segunda passagem e após ela, e comparar arquivos sob acesso restrito (mesmos SHA, `applied_at`, mesma sentinela). O runner deve informar somente `OK já aplicada` na segunda passagem.

## Dois ensaios reprodutíveis, sem apagar banco de outros ambientes

- **Fresh:** criar `JornadaDT06Fresh` vazio; rodar instalação, verificador e runner conforme acima. Conferir explicitamente que o segundo comando não executa migrations.
- **Upgrade:** criar `JornadaDT06Upgrade` descartável e aplicar `database/baselines/Jornada_Fase1_v3.65.sql` (e seed v3.65 se adequado). Inserir somente nesse teste `ref.gestor(codigo,nome,ativo)=('DT06_SENTINELA',N'Gestor sintético DT06',1)`. Executar runner uma primeira vez (produz histórico aplicado), verificador e exportador do histórico `-o <antes.txt>`; executar o runner novamente, verificador e exportador `-o <depois.txt>`; comparar os dois arquivos com `cmp`/`Compare-Object`. Testar alteração de checksum apenas em **cópia de arquivo num checkout descartável**, nunca na árvore rastreada. A falha deve ocorrer antes da aplicação do arquivo adulterado. Uma base original com histórico parcial exige clone específico da situação real; não apagar registros para forçar replay.

## Backup e rollback

Antes de qualquer upgrade real, executar backup completo com `CHECKSUM` para destino seguro e `RESTORE VERIFYONLY ... WITH CHECKSUM`; preferir ensaio de restauração integral em outro banco. Preservar localização e digest do backup sob controle de acesso. Rollback de DDL/dados não é `down` automático nem `DELETE` do ledger: suspender escritores e consumers, restaurar **em banco distinto** o backup validado, conferir invariantes e só promover sob procedimento de DBA. Recuperar mudanças ocorridas após o backup requer PITR/reconciliação institucional. Nunca executar `DROP DATABASE` implícito ou reset de `JornadaLocal`, `JornadaE2E` ou `JornadaSyntheticDev`.

**Validação:** o CI existente já cobre instalação do wrapper e upgrade do snapshot v3.65 com runner aplicado duas vezes. Os novos arquivos DT-06 ainda exigem execução SQL específica em clone; CI verde por si só não equivale a evidência de sua execução nem a backup restaurado.

## Ensaio adicional de histórico PARCIAL efetivamente aplicado (opt-in)

O script [test-history-upgrade.sh](test-history-upgrade.sh) cria **somente** um banco de teste ainda inexistente, com prefixo `JornadaDT06_`, instala o snapshot 3.65, registra uma sentinela e aplica as três primeiras migrations com SHA de arquivo no ledger. Em seguida roda o executor de upgrade sobre essas três entradas preexistentes (sem reaplicá-las), completa as restantes e repete o executor sobre o histórico já consolidado. Exige contagens do ledger e preservação da sentinela. Não faz `DROP`, não recria banco preexistente nem altera dados dos três perfis operacionais.

```bash
export JORNADA_DT06_TEST_DATABASE=JornadaDT06_Historico01
export JORNADA_DT06_CONFIRM_CREATE=YES
bash database/baselines/test-history-upgrade.sh
```

A suíte SQL do workflow de consolidação é disparada neste PR por `database/migrations/DT06_BASELINE_README.md`. Ela valida a migração e o replay em SQL Server real; o novo smoke de histórico parcial continua uma operação explícita e não deve ser declarado executado sem o resultado do ensaio.
