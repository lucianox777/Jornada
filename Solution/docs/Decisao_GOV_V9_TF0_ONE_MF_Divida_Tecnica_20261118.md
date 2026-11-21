# GOV-V9-TF0-ONE-MF-DT-20261118 — Comparação diagnóstica como dívida técnica

**Estado:** decisão do Product Owner aprovada em 18/11/2026; registro documental recuperado da PR #1710 em 21/11/2026; integração e quitação da dívida ainda pendentes. **Natureza:** priorização operacional, sem mudança científica ou de código.

## Contexto e justificativa

A comparação controlada TF0 × ONE_MF (também referida na conversa como “AMF”) foi concebida para investigar diferenças de V2/V3, M/U, ranking e resolução em mesmo frame SQL, não para certificar isoladamente o vencedor. A campanha principal do calibrador V9 foi refeita e executa TRAIN sintético 100k, V3 TF4 e 324 hipóteses; sua conclusão e auditoria permanecem independentes do diagnóstico anterior.

## Decisão aprovada

Registrar como **dívida técnica aberta, de prioridade secundária**, a consolidação e eventual reavaliação da comparação TF0 × ONE_MF. Ela deixa de ser tratada como bloqueio adicional **de priorização operacional** para a conclusão da campanha TF4/324. Preservar os resultados existentes, sem apagar evidências nem cancelar automaticamente execuções já iniciadas.

Esta decisão **não declara** resolvidas as diferenças V2/V3, nem comprova equivalência entre TF0, ONE_MF e TF4; não altera o plano científico congelado, critérios de FDR, multiplicidade, TRAIN/VALIDATION/TEST, regras de seleção, uso único de TEST ou autorização DT-15. Se um gate científico vigente exigir especificamente tal comparação, prevalece o gate até revisão formal e expressa pelo PO.

## Critérios para quitação

1. Inventariar resultados, SHA, frame SQL, seeds, parâmetros e recibos da execução diagnóstica #44365218012 e estudos anteriores.
2. Confrontar o diagnóstico com os resultados auditados do TRAIN TF4/324, distinguindo hipóteses comparáveis de perguntas não cobertas.
3. Documentar se há lacunas materiais de atribuição M/U, TF, ranking ou Resolve e decidir, com justificativa, se novo ensaio é necessário.
4. Registrar evidência e aprovação explícita para quitação; não encerrar por mero sucesso técnico da CI.

## Impactos e precedência

- **Impacto imediato:** acompanhamento diagnóstico despriorizado; campanha científica principal permanece prioritária.
- **Sem impacto:** código, algoritmos, parâmetros, banco, execução produtiva, TEST, DT-15 e guardas fail-closed.
- **Precedência:** políticas científicas e gates de segurança vigentes prevalecem; esta decisão apenas organiza backlog técnico e não revoga aprovações anteriores.

## Rastreabilidade

- Discussão e aprovação do PO: conversa do projeto em 18/11/2026.
- Execução diagnóstica: https://github.com/lucianox777/Jornada/actions/runs/44365218012
- Campanha TF4/324: https://github.com/lucianox777/Jornada/actions/runs/44329203788
- Estudos de origem: PRs #1296, #1297, #1299 e #1300; proposta confirmatória #1367 permanece distinta.
- Índices: [Dívidas Técnicas](Dividas_Tecnicas.md) e [Decisões Vigentes](Indice_Decisoes_Vigentes.md).

**GOV-DOC-01:** preservar este registro e sua rastreabilidade; nenhuma conclusão estatística é inferida desta reclassificação.
