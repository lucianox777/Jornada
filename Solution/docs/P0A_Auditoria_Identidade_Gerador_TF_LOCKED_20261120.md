# P0-A — Auditoria somente leitura de identidade do gerador TF LOCKED (20/11/2026)

**Estado:** implementação diagnóstica proposta, sem decisão de correção aprovada pelo PO. Issue #1734. Precedência: GOV-DOC-01 e contratos históricos dos manifests LOCKED de sementes 44–47 e 48–57.

## Contexto e justificativa

Os dois manifests congelam o mesmo `generation.generator_sha256` (digest agregado dos arquivos rastreados) e `generation.partition_contract.generator_file_sha256` (SHA-256 individual de `SyntheticCorpusGenerator.cs`), além da expectativa de 25 arquivos. O gerador atual da master apresenta divergência de identidade em relação à versão histórica; equivalência semântica dos defaults não substitui integridade byte a byte.

## Implementação diagnóstica

`Solution/scripts/audit-v9-tf-locked-generator.py` calcula os SHA-256 dos bytes do checkout, reproduz o algoritmo `tree_digest` do gate histórico com `git ls-files`, exige 25 arquivos e compara os dois LOCKED. Imprime JSON para análise e retorna código 1 em caso de qualquer divergência. **Não altera fontes, manifests, referências, SQL, corpora ou resultados.** Não é integrado automaticamente à CI enquanto a divergência da master permanecer sem correção; isso evita impor novo gate que falharia em todas as PRs sem uma decisão de recuperação aprovada.

O teste offline `Solution/tests/scripts/test_audit_v9_tf_locked_generator.py` exercita repositório Git temporário, divergência de contagem, cenário de 25 arquivos correspondentes e mutação posterior de fonte. A execução dos testes e do auditor no checkout real ainda depende da CI ou de ambiente com checkout.

## Impactos, limites e rastreabilidade

Impacto restrito a diagnóstico auditável, sem mudança científica. A existência do script não resolve a incompatibilidade LOCKED e não autoriza TRAIN, TEST, merge ou certificação. A correção arquitetural (preservar fonte histórica e eventual gerador parametrizado) depende de escolha explícita do PO e documentação subsequente. Não atualizar manifests para passar gates.

Rastreabilidade: issue #1734; PR desta branch `fix/p0-locked-generator-diagnostic-20261120`; commits `079e5b9` e `1be549e`.

## Integração de regressões à CI (20/11/2026)

A PR incorpora agora o teste offline isolado ao job obrigatório `unit-runtime` em `.github/workflows/jornada-pr-required-fast.yml` (commit `ac9bd1e`). **Somente os testes em Git temporário são executados na CI**, não a auditoria do gerador da master, cuja divergência histórica é conhecida. O teste adicional (commit `ecbf9c4`) cobre alteração de apenas um dos dois LOCKED e ausência física de arquivo rastreado; preserva fail-closed sem mudar os manifests reais. Justificativa: garantir regressão do próprio auditor antes de sua adoção operacional, sem confundir testes unitários sintéticos com prova do digest real. Impactos: tempo pequeno de testes Python e nenhuma execução científica; gate de integridade real continua pendente de decisão de recuperação P0-A #1734. Os commits precisam de CI verde; nenhuma aprovação do PO foi presumida.

## Endurecimento da identidade dos contratos (20/11/2026)

Os commits `a9bba38`, `eddef17` e `3a9c3d1` adicionam casos negativos independentes para digest individual de partição, status não LOCKED e `experiment_id` incorreto, e vinculam explicitamente cada caminho de manifesto ao identificador de campanha registrado no gate original. Justificativa: um arquivo com hashes corretos mas identidade de campanha substituída não pode ser considerado contrato científico válido. Impacto limitado à auditoria diagnóstica e seus testes; sem alteração dos dois LOCKED ou de execução científica. O novo HEAD requer CI própria, e o resultado anterior não é transferível.
