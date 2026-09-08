# Âncora CPF no Processor — V1

Na V1, `identidade.cpf_ancora` é a autoridade permanente para a relação CPF→UUID. O `identity_map` continua sendo a projeção operacional usada pelos fluxos de atribuição, mas não pode transferir a referência permanente de um CPF para outro UUID.

O writer determinístico consulta e bloqueia a âncora antes do mapa corrente. Quando ambos existem, qualquer divergência falha de forma fechada. Quando existe apenas a âncora, a reaparição do CPF recompõe o mapa operacional usando o mesmo UUID. Quando existe apenas um mapa histórico válido, o primeiro uso do writer reserva a âncora para aquele mesmo UUID. Na primeira aparição absoluta, Pessoa, âncora e mapa são constituídos na mesma transação.

Conflitos de atribuição continuam independentes da referência permanente. Um CPF pode conservar sua referência CPF→UUID enquanto uma observação ou fato fica `CONFLITO_IDENTIDADE`, sem transferir ou excluir a âncora. O Linkage probabilístico não participa da constituição da âncora e permanece condicionado à homologação estatística independente.

## Correção governada

A correção institucional pode separar ou reassociar observações e fatos, inclusive criando UUIDs para grupos que não são titulares do CPF. Ela não pode escolher outro UUID para a âncora permanente. No SQL Server, `identidade.tr_correcao_identidade_cpf_ancora` valida o cabeçalho auditável `identidade.correcao_identidade`: quando `identity_map_origem_id` representa CPF, `pessoa_uuid_titular` deve ser exatamente o UUID registrado em `identidade.cpf_ancora`. Tentativa de transferência falha com o erro `51360` antes de a correção produzir efeitos posteriores.

A trava fica deliberadamente no ato auditável da correção, e não em `identity_map`. Isso protege qualquer chamador do procedimento governado sem interferir nos writers operacionais que usam `OUTPUT INSERTED`. A superfície de correção governada V1 existente é SQL Server; não se declara paridade PostgreSQL para um procedimento que ainda não existe nesse provider.

## Bootstrap

A âncora é infraestrutura obrigatória antes da execução do Processor. SQL Server aplica `database/Jornada_Fase1.sql` e, em seguida, `database/migrations/20260907_Cpf_Ancora.sql`; em DEV/Test, a massa histórica pode ser carregada antes da migração para que o backfill reserve também os CPFs já existentes. PostgreSQL aplica os cores operacionais e `database/postgresql/Jornada_Cpf_Ancora.sql` antes do Worker.

O bundle Windows de Produção compõe o baseline e a âncora no DDL instalável entregue pelo pacote, mantendo o arquivo de migração também no diretório `database/migrations` para auditoria e execução controlada.

## Evidência

O workflow `jornada-cpf-anchor-processor` executa SQL Server e PostgreSQL reais e prova primeira constituição, repetição idempotente, reaparição após encerramento do mapa, divergência âncora↔mapa com falha fechada e rollback sem resíduos de Pessoa, mapa ou âncora. A suíte Integration SQL prova adicionalmente que um cabeçalho de correção com titular diferente da âncora é rejeitado sem resíduo e que o mesmo ato é aceito quando preserva o UUID permanente.
