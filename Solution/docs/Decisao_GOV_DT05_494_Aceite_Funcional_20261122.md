# GOV-DT05-494 — Aceite funcional das transições semânticas e do replay (22/11/2026)

**Autoridade:** decisão expressamente aprovada pelo Product Owner na revisão da [issue #494](https://github.com/lucianox777/Jornada/issues/494). **Política:** GOV-DOC-01.

## Contexto e evidências

A #494 foi aberta para separar a avaliação append-only de cada `linkage_run` das **transições operacionais semânticas**, sem quebrar publicação, auditoria, Gold e replay. O ledger `identidade.linkage_transicao_semantica`, a procedure `identidade.sp_registrar_transicoes_linkage_run` e a assinatura V1 foram implementados. O aceite estreito de três ondas com CPF tardio, retry sem transição adicional e três resultados brutos preservados foi comprovado no [PR #540](https://github.com/lucianox777/Jornada/pull/540) e [CI #36301124197](https://github.com/lucianox777/Jornada/actions/runs/36301124197). O replay histórico determinístico sobre manifesto e estado congelado é documentado como comprovado no [PR #719](https://github.com/lucianox777/Jornada/pull/719). Ver `DT05_Assinatura_Semantica_V1.md` e `DT05_Snapshots_Parquet_NAS.md`.

## Decisão aprovada

1. **Aceitar como concluído o escopo funcional/de desenvolvimento da #494**, com encerramento da issue após integração desta decisão documental. O mecanismo de replay e as transições semânticas **não devem ser reimplementados** por confundirem-se com pendências operacionais.
2. **Diferir medições complementares de custo, latência e capacidade** para avaliação operacional futura, coerente com uso excepcional do replay. A medição pontual sintética existente não certifica percentis, SLA, NAS de produção ou custo institucional. A medição de crescimento por run e participação de reavaliados do critério 5 da #494 permanece identificada como acompanhamento de desempenho, não como prova já realizada.
3. **Manter a política institucional de retenção fora do escopo de desenvolvimento**, sob a [issue #379](https://github.com/lucianox777/Jornada/issues/379); não permitir expurgo de evidências sem política aprovada.
4. O **aceite funcional da #494 não equivale a encerramento irrestrito da DT-05 global**, certificação de desempenho, implantação em HML/PROD ou autorização de GC/retention. A documentação anterior que qualificava a DT-05 global como PARCIAL continua aplicável às dimensões operacionais externas. A DT-22 de reavaliação de RESOLVIDOS permanece separada.

## Impactos, precedência e rastreabilidade

A decisão do PO **altera o gate de encerramento da #494**, antes vinculado a medições operacionais completas, sem falsificar testes ou dispensar invariantes de integridade e segurança. Para o escopo funcional, prevalece sobre a condição de fechamento global descrita em `DT05_Assinatura_Semantica_V1.md`; os requisitos de medição de `DT05_Snapshots_Parquet_NAS.md` continuam como trabalho de capacidade/operacional, não bloqueadores do aceite funcional #494. Não revoga #379, nem autoriza HML/PROD.

**Fontes:** #494; PRs #540, #719; `Solution/docs/DT05_Assinatura_Semantica_V1.md`; `Solution/docs/DT05_Snapshots_Parquet_NAS.md`; `Solution/docs/DT05_Medicao_Replay_Historico_Sintetico.md`; índice de decisões vigentes.
