# DT-06 — Evidência de aceite do upgrade das migrations

Base inventariada: `master` em `d33eb3b40a769f5f884c095425e091d1fc225042`.

## Escopo e limites

Esta frente é exclusivamente de teste/evidência. Não altera migrations, baselines, DDL canônico, `database/migrations/manifest.txt`, workflows, versões de pacotes, `Plano_Desenvolvimento.md` ou `Dividas_Tecnicas.md`. O ensaio pesado é opt-in e recusa bancos fora do prefixo reservado `JornadaDT06_`; `JornadaLocal`, `JornadaE2E` e `JornadaSyntheticDev` são explicitamente proibidos.

Resultados numéricos e aprovação só podem ser registrados após execução real. Este documento, enquanto não houver execução anexada, registra inventário, critérios objetivos e procedimento reprodutível.

## Etapa 0 — o que já estava provado

### Baselines e checksums

O repositório contém snapshots históricos `Jornada_Fase1_v3.55.sql`, `Jornada_Fase1_v3.57.sql` e `Jornada_Fase1_v3.65.sql`. O estado 3.70 é representado pelo instalador corrente `database/Jornada_Fase1_v3.70.sql` e pelos artefatos DT-06 de instalação/ledger. `database/baselines/DT06_SHA256SUMS.txt` fixa o snapshot DT-06 e os hashes dos arquivos referenciados.

**Prova:** existência/proveniência dos snapshots congelados e integridade dos arquivos cobertos pelo arquivo de hashes.

**Não prova:** que um banco construído de um predecessor histórico com dados foi realmente elevado ao estado corrente, nem que os dados sobreviveram, nem que um backup pré-upgrade foi restaurado.

### `Jornada_Upgrade_Invariants.sql`

Captura contagens de entidades relevantes e violações estruturais antes/depois, incluindo referências quebradas, CPF ativo duplicado e múltiplos vínculos ativos por observação.

**Prova:** fornece uma fotografia objetiva e comparável do estado lógico observado.

**Não prova:** sozinho não executa upgrade, não garante preservação byte a byte do ledger, não cria backup e não restaura banco.

### `scripts/local-ddl-upgrade.sh`

O harness existente cria banco descartável, parte por padrão do snapshot 3.65, injeta sentinela/dados legados, executa backfill/DDL corrente, runtime smoke, invariantes e reaplicação; compara fingerprints da primeira e segunda aplicação.

**Prova:** upgrade controlado 3.65 → estado corrente no caminho de DDL do harness, preservação das sentinelas exercitadas e idempotência do DDL medido pelo fingerprint.

**Não prova:** clone histórico via runner de migrations a partir de 3.57, preservação do ledger de migrations de um histórico real, nem rollback por restauração de backup.

### Job `ddl-upgrade` do CI

O job executa `scripts/local-ddl-upgrade.sh` em SQL Server real e publica `.local/ddl-upgrade/` como evidência.

**Prova:** automatiza no CI o contrato do harness acima.

**Não prova:** cenários que o harness não executa; em particular, não equivale a restauração de backup nem a clone histórico DT-06 opt-in.

### `.github/workflows/local-db-upgrade.yml`

O workflow exercita bootstrap local e `local-db.sh up` repetido sobre o mesmo volume, incluindo o backfill progressivo.

**Prova:** repetição segura do entrypoint local no escopo que o workflow declara.

**Não prova:** upgrade histórico DT-06, rollback, backup/restore ou imutabilidade do ledger de migrations.

### Evidência DT-06 já existente

`database/baselines/test-history-upgrade.sh` ensaia predecessor 3.65 com três migrations pré-aplicadas e SHA registrado, completa o ledger e repete o runner. `DT06_Exportar_Historico.sql` exporta ledger/sentinela para comparação; `DT06_Verificar_Schema_370.sql` exige 51 migrations, checksums válidos, `SolutionSchema=3.70` e objetos estruturais.

**Prova:** existe um caso reprodutível de histórico parcial e verificadores objetivos.

**Não prova:** a lacuna integral de aceite registrada na DT-06, sobretudo restauração de backup e retorno comprovado ao estado pré-upgrade.

## Novo ensaio opt-in

Arquivo: `scripts/dt06-upgrade-acceptance.sh`.

Pré-condições obrigatórias:

```bash
export JORNADA_DT06_EVIDENCE=1
export JORNADA_DT06_CONFIRM_CREATE=YES
export JORNADA_DT06_TEST_DATABASE=JornadaDT06_Aceite01
# credenciais/servidor conforme ambiente seguro
bash scripts/dt06-upgrade-acceptance.sh
```

Por padrão o clone parte de `database/baselines/Jornada_Fase1_v3.57.sql`. Outro snapshot sob `database/baselines/` pode ser selecionado por `JORNADA_DT06_BASELINE`, mas cada execução deve registrar qual foi usado.

O script nunca derruba um banco preexistente: se `JornadaDT06_Aceite01` já existir, aborta antes de qualquer mutação. Ao final bem-sucedido, preserva o banco no estado histórico restaurado para auditoria.

## Critérios objetivos de aceite

### 1. Clone histórico e preservação

O ensaio cria um banco novo com prefixo `JornadaDT06_`, aplica o baseline 3.57 e insere massa sintética DT-06. Antes do upgrade registra hash do manifesto, snapshot de `Jornada_Upgrade_Invariants.sql` e contagem da massa. O upgrade é executado exclusivamente por `scripts/apply-migrations.sh`; depois, `DT06_Verificar_Schema_370.sql` deve passar.

**Aceite:** schema 3.70 verificado; ledger completo/verificável; contagem da massa sintética igual à anterior; nenhuma migration aplicada é apagada ou reescrita pelo ensaio.

### 2. Rollback por backup/restauração

Antes do upgrade, o ensaio executa `BACKUP DATABASE ... WITH CHECKSUM` e `RESTORE VERIFYONLY ... WITH CHECKSUM`. Depois das verificações de upgrade/idempotência, restaura esse backup sobre o próprio banco sintético isolado.

**Aceite:** restauração conclui; snapshot de invariantes/contagens pós-restore é idêntico ao snapshot pré-upgrade; contagem da massa sintética volta exatamente ao valor pré-upgrade; SHA-256 do manifesto registrado antes e após a restauração é idêntico.

### 3. Idempotência

Após o primeiro upgrade aprovado, o ensaio captura exportação do histórico, fingerprint do DDL, invariantes/contagens e hash do manifesto. Reexecuta o mesmo `apply-migrations.sh` e captura novamente os quatro artefatos.

**Aceite:** comparação byte a byte do histórico é igual; fingerprint é igual; invariantes/contagens são iguais; hash do manifesto é igual. Qualquer diferença reprova o cenário.

## Política de parada

Qualquer divergência em clone histórico, preservação, verificação 3.70, idempotência ou restauração encerra o script com erro no primeiro caso mínimo observado. Esta frente não corrige migration, baseline, DDL ou runner; a correção pertence à frente integradora.

## Estado da execução

**NÃO EXECUTADO nesta alteração.** O novo cenário exige SQL Server com permissão de `BACKUP DATABASE`/`RESTORE DATABASE` e caminho de backup visível ao servidor. Não há resultado numérico inferido do código. O aceite integral da DT-06 permanece pendente até uma execução real produzir `.local/dt06-acceptance/<banco>/result.txt` com `DT06_ACCEPTANCE=OK`.
