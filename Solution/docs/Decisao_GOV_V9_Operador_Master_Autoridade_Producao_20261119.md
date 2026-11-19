# GOV-V9-OPERADOR-MASTER-AUTORIDADE-PRODUCAO-20261119

**Estado:** DECIDIDO pelo Product Owner em 2026-11-19; documentação proposta para integração. **Não implementa ativação.**

## Contexto e decisão
Após a campanha V9 10k (GitHub Actions run #44444572285), o calibrador completou TRAIN, AUDIT, VALIDATION e TEST, selecionou um vencedor científico condicional, mas registrou `certified=false` e `activated=false`. O PO esclareceu expressamente: **a autorização de utilização em produção não é responsabilidade nem competência do calibrador; é atribuição do operador master**, que verificará os dados, **inclusive os históricos**, antes de autorizar produção.

## Competências e limites
- **Calibrador:** produzir evidências, métricas, finalista/vencedor científico, snapshots congelados e TEST independente; não autorizar, publicar ou ativar o modelo em produção.
- **Operador master:** analisar os dados e evidências, inclusive históricos, e exercer a competência decisória de autorização em produção conforme os controles de governança aplicáveis.
- O status `TEST_MEASURED_NOT_CERTIFIED_OR_ACTIVATED` não significa reprovação do vencedor: descreve o limite de competência do workflow científico.
- As pendências de paridade operacional e garantias estatísticas devem ser comunicadas como evidências/limitações para exame do operador master, não como autorização ou veto automático do calibrador.
- Esta decisão **não dispensa** requisitos de segurança, trilha de auditoria ou controles existentes e **não define ainda** interface, procedimento, limiares ou implementação de autorização pelo operador master.

## Impactos, precedência e rastreabilidade
Interpretação de governança com precedência sobre qualquer leitura de que o calibrador decide ativação. Preserva a separação já implementada de `certified=false`, `activated=false` e os controles fail-closed. Não altera código nem resultado científico. Referências: execução #44444572285; vencedor SHA256 `0408328cbe15a7f7a4886e43cbfb01aa4f3ab373be97e6ea555823c7a230ef60`; `Solution/scripts/v9-tf4-e2e-closeout.py`; GOV-DOC-01. Próximas alterações operacionais, se necessárias, exigem especificação e validação próprias.
