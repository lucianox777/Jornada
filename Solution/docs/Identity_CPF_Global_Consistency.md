# Consistência global de CPF

Este documento congela a semântica vigente para CPF válido na Jornada e prevalece sobre descrições históricas anteriores que tratavam `CPF_CORE_CONSISTENCY_V1` como bloqueio da atribuição factual.

## Regra de identidade

Um CPF estruturalmente válido resolve deterministicamente pela âncora permanente:

```text
CPF -> identidade.cpf_ancora -> pessoa_uuid
```

A política de consistência de núcleo não escolhe outro UUID, não transfere a âncora e não envia CPF válido ao linkage probabilístico.

## Regra de consistência

`CPF_CORE_CONSISTENCY_V1` compara núcleos observados associados ao mesmo CPF. Na V1, uma divergência forte é detectada quando coexistem dois sinais independentes:

- nome em estado `LOW`;
- data de nascimento diferente.

A detecção não determina qual observação está errada. A observação que torna a divergência visível é apenas evidência do conflito global do identificador.

Quando a política dispara, o estado pertence ao próprio CPF:

```text
identidade.identity_map.estado = EM_CONFLITO
identidade.identity_map.estado_motivo = CPF_COMPARTILHADO_SUSPEITO
```

`CPF_COMPARTILHADO_SUSPEITO` descreve uma hipótese operacional de coexistência de núcleos incompatíveis sob o mesmo CPF. Não afirma fraude, culpa, uso indevido nem qual registro é incorreto.

A transição é auditada em `identidade.identity_map_estado_evento`.

## Efeito sobre observações e fatos

O conflito global de consistência não rompe a resolução determinística:

- a observação continua `RESOLVIDO` para o UUID da âncora;
- `identidade.vinculo_fonte.pessoa_uuid` permanece com o UUID ancorado;
- fatos válidos continuam `ATRIBUIDA` para o mesmo UUID;
- `cpf_declarado` continua sendo o snapshot da declaração recebida da fonte;
- fatos anteriormente materializados não têm `pessoa_uuid` anulado por esse conflito;
- `identidade.pessoa` não muda para `EM_CONFLITO` somente por esse motivo;
- `gold.pessoa` não é removida somente por esse motivo.

A Gold pode continuar expondo divergência entre observações por seus mecanismos próprios de concordância cadastral, sem transformar a consistência em uma segunda chave de identidade.

## Consulta do identificador

A condição global pode ser sinalizada ao consumidor da resolução, mas sempre preservando o UUID permanente do CPF. Portanto, uma superfície de consulta pode informar `CONFLITO` / `CPF_EM_CONFLITO_IDENTIDADE` como condição do identificador e simultaneamente devolver o UUID da âncora.

Isso não significa que a atribuição factual foi perdida. Significa que o identificador possui uma inconsistência global registrada que pode exigir análise governada.

## Casos que continuam distintos

Esta regra não altera:

- CPF inválido: continua sendo erro/conflito próprio e não constitui âncora;
- CPF ausente em hipótese admitida: continua seguindo o fluxo de pendência/linkage definido para ausência de CPF;
- divergência entre `cpf_ancora` e `identity_map`: continua sendo falha de integridade, nunca resolvida por score de nome/data;
- correções, separações e fusões governadas: continuam preservando histórico e não criam duas âncoras concorrentes para o mesmo CPF.

## Invariantes de implementação

SQL Server e PostgreSQL devem provar os mesmos invariantes:

1. um CPF possui uma única âncora permanente;
2. a consistência nunca muda o UUID ancorado;
3. nenhuma observação é automaticamente eleita como incorreta;
4. o conflito é persistido no estado do identificador e em seu histórico;
5. observação, fato corrente e âncora preservam o mesmo UUID após a detecção;
6. Gold e Serving preservam a atribuição factual válida;
7. CPF válido não cai no linkage probabilístico por divergência de núcleo.
