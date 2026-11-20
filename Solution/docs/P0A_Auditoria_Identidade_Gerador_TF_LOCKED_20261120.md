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

## Evidência física automatizada no checkout da PR (20/11/2026)

O commit `2c8d020` adiciona o workflow independente `.github/workflows/p0-tf-locked-generator-audit.yml`, acionado por mudanças no auditor/teste/workflow/documento ou manualmente. Executa testes offline e calcula no checkout real os digests do gerador e dos dois LOCKED, publica o JSON como artefato retido por 30 dias e emite warning quando a comparação diverge. **A divergência não é mascarada no relatório**, mas não torna a nova checagem obrigatória nem bloqueia todas as PRs antes de decisão do PO. O sucesso do job não significa identidade LOCKED validada: a verdade consta de `matches` e `audit_exit_code`. Não gera corpus, não lê TEST, não modifica LOCKED. Rastreabilidade: P0-A #1734 e PR #1769. A execução e os valores físicos continuam pendentes até o workflow publicar o artefato.

## Falha real de CI e correção (20/11/2026)

O run `44763965458` do workflow diagnóstico falhou no teste offline antes da comparação física: `KeyError: 0` na linha que acessava `audit_module.LOCKS[0]`, após a evolução de `LOCKS` de tupla para mapeamento de caminho→`experiment_id`. Por consequência, o passo de auditoria foi `skipped` e o upload falhou por ausência do JSON. O commit `cf89606` corrige o teste para selecionar a primeira chave do mapeamento. **Não afirmar que o relatório físico foi produzido no run com falha.** A validação do novo commit e o artefato permanecem pendentes. Não houve mudança de contrato LOCKED, de fonte científica ou aprovação de correção arquitetural.

## Resultado físico do checkout GitHub Actions (20/11/2026)

O run `44764078274` da PR #1769 executou **com sucesso** os testes offline e o passo de comparação real; o artefato `p0-tf-locked-generator-identity-audit` foi publicado sob ID `14643085698` (retenção 30 dias). O log do job `136383918712` apresenta os seguintes valores **do checkout da PR**:

| Campo | Atual | Exigido pelos dois LOCKED |
| --- | --- | --- |
| Arquivos rastreados | 25 | 25 |
| `generation.generator_sha256` | `f23874cf21fff462a815a33d08ac1140cd1e9541210624bad0cf2fb3871f94cc` | `924ad6455e15b73093f7d4834174966717de13be9b89b14b9640d11ffbea8deb` |
| `partition_contract.generator_file_sha256` | `70cbd9280417238411ff7f7e6c1384c2ad1c2458c08dcf6d38962397bbc558a2` | `83015950790a5be15a5fb843c8330c8cccb10610f1a208ee46eeb7fc348c18e4` |
| `matches` (44–47 / 48–57) | `false` / `false` | `true` / `true` |

**Interpretação:** contagem correta, identidade agregada e identidade de partição incompatíveis com os dois contratos históricos. O job é deliberadamente não bloqueante e seu sucesso significa coleta de evidência, **não** validação LOCKED. O hash físico de partição (`70cbd9...`) é o valor autoritativo deste checkout; um cálculo anterior sobre texto retornado por conector (`83b045...`) não deve ser tratado como hash físico de arquivo e não substitui esta evidência. O checkout foi o merge ref de PR #1769, não uma execução isolada em `master`; não afirmar que se mediu diretamente o filesystem da master. Sem regeneração, alteração dos manifests ou autorização científica. Próximo passo sujeito à avaliação do PO: recuperar identidade histórica e decidir separação de geradores sem reescrever LOCKED. Issue #1734.

## Clareza do resultado não bloqueante (20/11/2026)

O commit `b939158` reforça o resumo do GitHub Actions com `MATCH` ou **`MISMATCH / ERROR — NOT VALIDATED`**, código de saída do auditor e aviso explícito de que o workflow verde não certifica integridade. Contexto: o run `44764078274` comprovou `matches=false` nos dois contratos apesar de ter finalizado o job com sucesso. Justificativa: evitar interpretação operacional incorreta do status não bloqueante enquanto a recuperação histórica aguarda decisão do PO. Impacto apenas na apresentação da evidência de CI; nenhuma alteração científica ou de regra obrigatória. A execução do novo commit permanece pendente de validação.

## Cobertura de gatilhos do diagnóstico (20/11/2026)

O commit `0cceb7e` amplia os filtros de `pull_request.paths` do workflow diagnóstico para incluir **qualquer arquivo do gerador** em `Solution/src/Jornada.Linkage.SyntheticCorpus/**` e os dois manifests LOCKED. Contexto: o filtro inicial observava apenas mudanças no próprio auditor, seus testes e documentação; uma alteração posterior do gerador poderia passar sem disparar nova medição. Justificativa: a evidência deve acompanhar mudanças nos próprios insumos que ela audita. Impacto: execução diagnóstica adicional nas PRs que tocarem esses caminhos, ainda não obrigatória e sem geração científica. A integridade real permanece divergente até correção aprovada. O workflow do HEAD anterior `44764326046` executou testes e comparação com sucesso no nível de passos, mas esse resultado não substitui a validação do novo commit.
