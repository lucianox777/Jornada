# Protocolo de revisão por checkpoint

## Objetivo

Registrar revisões técnicas periódicas sem transformar cada checkpoint em gate de release ou aprovação institucional.

## Cadência

Durante desenvolvimento ativo, criar **um checkpoint a cada 7 dias corridos**, contado a partir do último checkpoint material. Criar checkpoint adicional antes de:
- RC/release técnica;
- entrada em HML;
- alteração estrutural de identidade/Linkage que mude invariantes;
- decisão externa que altere política operacional.

Se não houve mudança material no período, registrar apenas `SEM_MUDANCA_MATERIAL` no checkpoint seguinte; não produzir documentação artificial.

## Nome

`YYYY-MM-DD_checkpoint.md`

## Conteúdo mínimo

1. SHA de `master` revisado.
2. PRs mergeadas desde o checkpoint anterior.
3. invariantes adicionados/alterados/removidos.
4. schemas/migrations afetados.
5. diagramas afetados conforme `Solution/docs/Diagramas_Rastreabilidade.json`.
6. testes/gates que provaram a mudança.
7. issues externas que continuam pendentes.
8. divergências conhecidas entre documentação e implementação.
9. decisão: `SEM_MUDANCA_MATERIAL`, `ATUALIZACAO_DOCUMENTAL`, `REQUER_CORRECAO_TECNICA` ou `BLOQUEIA_PROMOCAO`.

## O que um checkpoint não faz

- não homologa estatística de #31;
- não aprova HML/PRD;
- não substitui decisão PRODAM/#378;
- não concede base legal/finalidade;
- não cria gate novo por mera preferência;
- não reescreve histórico.

## Moratória de gates

Novo gate automático exige risco observável, propriedade verificável e custo de manutenção justificado. Primeiro documentar o risco e a prova manual; automatizar quando repetição/erro real justificar. O manifesto de diagramas é inicialmente informativo, não CI.
