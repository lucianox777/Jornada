# Testes e operação — roteiro único

**Sequência:** DEV → Ensaio único → HML → Produção. Os testes locais em DEV não constituem um segundo Ensaio. Scripts e logs são a evidência de execução; este índice não afirma testes aprovados.

| Fase | Fonte | Evidência esperada |
|---|---|---|
| DEV | [Runbook local](Runbook_Desenvolvimento_Local.md), [testes técnicos](Runbook_Testes_Tecnicos.md) | Restore, build, unit, SQL/API, OpenAPI, E2E; preservar JornadaLocal, JornadaE2E e IBGE |
| Ensaio único | [Contrato completo](Ensaio_Unico_Paridade_HML.md), [ondas](Ensaio_Progressivo.md), [conferência](Linkage_Implementation_Conference.md) | Todos os contratos e funcionalidades HML prontos; ondas, CPF tardio, dependências do lado candidato, Gold, ledger, carga, falhas, segurança, recuperação e busca semicega até cinco candidatos |
| HML | [Readiness](Governanca_Tecnica_Readiness.md), [operação](Runbook_Operacao.md), [SQL/API](HML_Evidencias_SQL_API.md) | Mesmo produto e contratos do Ensaio; massa anonimizada enviada pelas Secretarias; anonimização e adequação dos testes verificadas |
| Produção | [Git/release](Runbook_Git_Release.md), [evidências](Release_Evidence.md) | Gates e aprovações de promoção, commit, artefatos e rastreabilidade |

Medir recall@5 apenas com verdade de referência independente apropriada. Medir separadamente tempo, erros e omissões na conferência humana. Documentar diferenças de infraestrutura e massa, sem mudar contratos funcionais. Não tratar testes planejados como executados.
