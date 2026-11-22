# GOV-V9-ORACULO-VALIDACAO-NAO-PRODUCAO-20261122 — Esclarecimento de fronteira

**Data:** 22/11/2026. **Autoridade:** Product Owner. **Estado:** esclarecimento aprovado da arquitetura de validação; não é autorização de execução científica ou ativação. **Política:** GOV-DOC-01.

## Contexto e decisão

Ao discutir a introdução do termo “oráculo” no E2E integral, o PO questionou se ele seria um novo seletor ou calibrador e reafirmou: **o oráculo não faz parte da produção; pertence ao processo de validação**.

Fica registrada a fronteira funcional:
- **Calibrador:** prepara/avalia hipóteses e parâmetros científicos conforme seu próprio processo e governança.
- **Seletor/Operador Master:** seleciona o modelo/pacote governado para o Runner, segundo os gates existentes.
- **Runner V9:** executa o linkage operacional segundo o modelo selecionado; não consulta o gabarito de validação.
- **Oráculo independente:** referência de verificação em ensaios, testes e E2E DEV; compara decisões e resultados com verdade externa. **Não é seletor, não calibra, não decide identidades em produção e não é dependência do fluxo produtivo.**

O gabarito pode ser preparado antes da execução, mas permanece externo ao ZIP de ingestão e indisponível ao Runner; é consumido exclusivamente na avaliação posterior dos resultados. A implementação de verificador independente deve reaproveitar capacidades existentes de validação científica quando adequadas, evitando inventar um serviço produtivo denominado “oráculo”.

## Justificativa e impactos

**Motivo:** impedir confusão entre validação independente, calibração, seleção e execução operacional; preservar a não circularidade do teste sem ampliar a superfície de produção.

**Impacto arquitetural:** a comparação com a verdade ocorre fora do caminho produtivo HTTP → Bronze → Processor → Silver/Gold → Runner → APIs. Nenhuma alteração de contrato produtivo ou nova dependência de runtime é aprovada por este esclarecimento.

**Impacto de aceite:** evidências de oráculo podem demonstrar qualidade em DEV e apoiar os gates científicos existentes, mas não certificam automaticamente o Runner V9 nem autorizam publicação, TRAIN/TEST, 100k, HML ou PROD.

## Precedência e rastreabilidade

Complementa e esclarece `GOV-DEV-E2E-INTEGRAL-V9-PLANO-20261122` (etapas 2 e 8) e `GOV-DEV-E2E-INTEGRAL-V9-PARALELISMO-20261122`, sem revogar seus critérios de aceite. Referências de implementação: issues #1584 (Runner e verificador independente), #1851 (E2E integral), #1488 (seleção governada), PRs #1873 e #1875 integradas; #1876 em validação.

**Estado:** fronteira aprovada/documentada; implementação e execução do E2E integral continuam pendentes.
