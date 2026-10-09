> **Atualização 09/10/2026:** a matriz atual de gates `jornada-ci`
> tem dez identidades, sendo `dt10-evidence` **condicional**
> a alterações relevantes e executado em workflow reutilizável
> após #858. Os testes de supervisão, três RunOnce,
> SIGKILL/rollback de lote e painel DEV pertencem ao
> **runner SQL descartável `JornadaE2E`**, não são uma nova
> fase de Ensaio, nem homologação HML/PROD. Consulte
> [Runbook de testes](Runbook_Testes_Tecnicos.md),
> [Manual do sistema](Manual_Sistema_Consolidado_20261009.md)
> e [Console DEV](Console_DEV_Supervisao_Atual.md).
>
# Testes e operação — roteiro único

**Sequência:** DEV → Ensaio único → HML → Produção. Os testes locais em DEV não constituem um segundo Ensaio. Scripts e logs são a evidência de execução; este índice não afirma testes aprovados.

| Fase | Fonte | Evidência esperada |
|---|---|---|
| DEV | [Runbook local](Runbook_Desenvolvimento_Local.md), [testes técnicos](Runbook_Testes_Tecnicos.md) | Restore, build, unit, SQL/API, OpenAPI, E2E; preservar JornadaLocal, JornadaE2E e IBGE |
| Ensaio único | [Contrato completo](Ensaio_Unico_Paridade_HML.md), [ondas](Ensaio_Progressivo.md), [conferência](Linkage_Implementation_Conference.md) | Todos os contratos e funcionalidades HML prontos; ondas, CPF tardio, dependências do lado candidato, Gold, ledger, carga, falhas, segurança, recuperação e busca semicega até cinco candidatos |
| HML | [Readiness](Governanca_Tecnica_Readiness.md), [operação](Runbook_Operacao.md), [SQL/API](HML_Evidencias_SQL_API.md) | Mesmo produto e contratos do Ensaio; massa de testes preparada pelas Secretarias, preservando as características relevantes das bases reais; fidelidade às características reais, proteção de dados e adequação dos testes verificadas |
| Produção | [Git/release](Runbook_Git_Release.md), [evidências](Release_Evidence.md) | Gates e aprovações de promoção, commit, artefatos e rastreabilidade |

Medir recall@5 apenas com verdade de referência independente apropriada. Medir separadamente tempo, erros e omissões na conferência humana. Documentar diferenças de infraestrutura e massa, sem mudar contratos funcionais. Não tratar testes planejados como executados.
