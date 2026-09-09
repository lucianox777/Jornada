# Consistência global de CPF

Este documento congela a semântica vigente para CPF válido na Jornada e prevalece sobre descrições históricas anteriores que tratavam `CPF_CORE_CONSISTENCY_V1` como bloqueio da atribuição factual.

## Três níveis que não podem ser confundidos

A implementação separa explicitamente três níveis:

| Nível | Estrutura/estado | Semântica | Efeito sobre `pessoa_uuid` |
|---|---|---|---|
| Âncora do CPF | `identidade.cpf_ancora` | relação permanente `CPF -> UUID` | determina o UUID para CPF estruturalmente válido |
| Consistência do identificador | `identidade.identity_map.estado` / `estado_motivo` | condição operacional do CPF, inclusive `EM_CONFLITO / CPF_COMPARTILHADO_SUSPEITO` | nenhum; não rompe a âncora nem a atribuição |
| Atribuição da observação/fato | `identidade.vinculo_fonte` e `estado_atribuicao_identidade` em Gold/Serving | informa se a Pessoa do registro pôde ser determinada | `ATRIBUIDA` exige UUID; `PENDENTE_IDENTIDADE` e `CONFLITO_IDENTIDADE` ficam sem UUID |

`EM_CONFLITO` e `CONFLITO_IDENTIDADE` não são sinônimos. O primeiro pode coexistir com um registro `ATRIBUIDA`; o segundo significa que a atribuição daquele registro não pôde ser determinada.

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

A transição é auditada em `identidade.identity_map_estado_evento`. O evento registra quando a inconsistência se tornou detectável; não transforma a observação que chegou por último na observação culpada.

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

## Estados de atribuição do registro

Os estados factuais permanecem necessários e têm significados próprios:

- `ATRIBUIDA`: existe UUID suficientemente determinado. Para CPF estruturalmente válido, a âncora determinística satisfaz essa condição mesmo se o `identity_map` estiver `EM_CONFLITO` por inconsistência cadastral global;
- `PENDENTE_IDENTIDADE`: não há evidência suficiente para determinar uma Pessoa, por exemplo no caminho probabilístico `NAO_RESOLVIDO`; `pessoa_uuid` é nulo;
- `CONFLITO_IDENTIDADE`: há conflito sobre qual Pessoa deve receber a atribuição, portanto `pessoa_uuid` é nulo. Na Fase 1 isso inclui CPF informado que falha na validação estrutural e conflito probabilístico entre candidatos concorrentes.

CPF informado que falha localmente em quantidade de dígitos, sequência repetida ou dígitos verificadores usa o motivo canônico:

```text
CPF_ESTRUTURALMENTE_INVALIDO
```

Esse motivo não representa situação cadastral na Receita Federal. Estados externos como situação cadastral irregular ou titular falecido não são inferidos pela validação estrutural da Fase 1.

Em todos os estados de atribuição, um fato finalístico válido continua podendo ser materializado em Gold/Serving; o estado de identidade não altera a existência do fato declarado.

## Consulta do identificador

A condição global pode ser sinalizada ao consumidor da resolução, mas sempre preservando o UUID permanente do CPF. Portanto, uma superfície de consulta pode informar `CONFLITO` / `CPF_EM_CONFLITO_IDENTIDADE` como condição do identificador e simultaneamente devolver o UUID da âncora.

Isso não significa que a atribuição factual foi perdida. Significa que o identificador possui uma inconsistência global registrada que pode exigir análise governada.

## Casos que continuam distintos

Esta regra não altera:

- CPF estruturalmente inválido: `CONFLITO_IDENTIDADE / CPF_ESTRUTURALMENTE_INVALIDO`, sem UUID e sem criação de âncora;
- CPF ausente em hipótese admitida: continua seguindo o fluxo de pendência/linkage definido para ausência de CPF;
- divergência entre `cpf_ancora` e `identity_map`: continua sendo falha de integridade, nunca resolvida por score de nome/data;
- correções, separações e fusões governadas: continuam preservando histórico e não criam duas âncoras concorrentes para o mesmo CPF.

## Invariantes de implementação

SQL Server e PostgreSQL devem provar os mesmos invariantes:

1. um CPF possui uma única âncora permanente;
2. a consistência nunca muda o UUID ancorado;
3. nenhuma observação é automaticamente eleita como incorreta;
4. o conflito é persistido no estado do identificador e em seu histórico;
5. observação, fato corrente e âncora preservam o mesmo UUID após a detecção de conflito global;
6. Gold e Serving preservam a atribuição factual válida;
7. CPF válido não cai no linkage probabilístico por divergência de núcleo;
8. `identity_map.EM_CONFLITO` não produz `CONFLITO_IDENTIDADE` factual por si só;
9. `CONFLITO_IDENTIDADE` exige `pessoa_uuid` nulo e permanece reservado a conflito real de atribuição;
10. CPF estruturalmente inválido usa `CPF_ESTRUTURALMENTE_INVALIDO` e não constitui âncora.
