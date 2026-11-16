# V9 — Contrato operacional do corpus persistente (PR #1567)

**Estado:** implementação parcial; PR #1567 em **DRAFT, aberta e sem merge**, sincronizada à master após #1571. Contratos Python e transporte físico de **apenas 2 registros sintéticos** aprovados; persistência durável, gerador familiar e PREPARE→TRAIN real continuam pendentes. Registro de engenharia vinculado à decisão GOV-DOC-01 já documentada nesta branch. A aprovação de arquitetura pelo PO não autoriza executar a campanha científica nem mesclar.

## Fluxo implementado nesta branch

- `v9-corpus-input-receipt.py`: captura hashes do código-fonte do gerador, IBGE nomes e referência diária de nascimento; aceita referência familiar somente se explicitamente fornecida. Não supõe que o catálogo REF familiar contém valores estatísticos.
- `v9-corpus-snapshot.py`: identidade SHA-256 da configuração de geração, hash dos quatro arquivos de saída reais e manifesto; recusa alteração silenciosa; publicação com lock POSIX e rename.
- `v9-corpus-prepare.py`: lock do ciclo lookup/generate/publish; rejeita cache existente corrompido; handoff para diretório independente com nova validação.
- `.github/workflows/v9-corpus-prepare-opt-in.yml`: PREPARE manual na branch de trabalho, com cache GitHub Actions, artefato por `run_id` e job **`train-handoff-probe` em runner separado**, validando SHA, configuração e conteúdo. **Não é o TRAIN de 324 hipóteses nem prova de retenção durável.**
- `v9-corpus-handoff-verify.py`: admite somente corpus/manifesto/recibo de PREPARE íntegros em outro runner, rejeita corrupção, configuração trocada e paths simbólicos; não executa calibração.
- `.github/workflows/v9-corpus-persistence-contract.yml`: testes focados de corpus e guard de workflows na PR (não altera os testes históricos do gerador seed42/43/44).

## Limites e bloqueadores

1. O cache do GitHub Actions pode expirar ou ser removido; não constitui armazenamento durável garantido. A arquitetura aprovada exige política de retenção, ACL, auditoria, imutabilidade e recuperação cross-runner antes de ativar o fluxo principal.
2. O artefato do PREPARE é limitado à execução e retenção do Actions; ainda não há consumidor seguro no TRAIN de 324 hipóteses.
3. O PREPARE manual usa `expected_sha` para a branch de desenvolvimento. O fluxo científico existente mantém exigência de `master`; **não alterar essa admissão para permitir execução científica em branch**.
4. O modelo familiar exige valores quantitativos oficiais congelados e rastreáveis (não apenas catálogo de fontes). Não atribuir probabilidades inventadas a mães, irmãos ou gêmeos.
5. Perfil clean histórico: taxa de corrupção materna de **10%**, congelada com a fonte do gerador para seed42/43/44; **13,4% é hipótese ilustrativa e NÃO executável** no fluxo atual. O recibo pode registrar uma taxa experimental, mas o PREPARE manual recusa **qualquer override** antes de gerar. Uma variante de gerador com hash/versão próprios, testes científicos e autorização específica é requisito para habilitá-la.
6. O contrato de transferência por outro runner foi codificado e protegido, mas **a execução física do PREPARE manual e do download inter-runner ainda exige CI com recibo**. Persistência cross-run, performance, concorrência e armazenagem segura precisam de evidência específica. A mera existência de testes não prova execução.
7. A integração final exige evidências de preservação das 324 hipóteses, 81 células, TF, m/u, FDR/CP/P95, TRAIN/VALIDATION/TEST e regras de acesso.

## Critérios para declarar PR pronta (sem mesclar)

- [ ] Fonte REF familiar com números oficiais, período, município/UF, hashes, licenças e justificativa.
- [ ] Variante de gerador experimental isolada para taxa materna configurável, sem alterar os hashes congelados de seed42/43/44; no estágio atual override é explicitamente rejeitado antes da geração.
- [ ] Modelagem familiar estatisticamente rastreável e testes determinísticos.
- [ ] Persistência durável entre execuções e runners, com retenção e recuperação testadas.
- [ ] PREPARE integrado ao TRAIN principal com handoff fail-closed e controle de custo. **Existe apenas PROBE PREPARE → consumidor diferente com verificação SHA, nunca o TRAIN científico.**
- [ ] Testes unitários, CI e segurança aprovados com links de execução no HEAD atualizado. Hashes de código do gerador histórico mantidos; suíte científica independente é protegida.
- [ ] Revisão de diff, documentação GOV-DOC-01 e índice sincronizados.
- [ ] Revisão final do PO para autorizar merge **em momento posterior**.

**Instrução permanente do PO para esta atividade:** manter PR #1567 aberta, sem merge na master. A branch foi atualizada para conter todas as mudanças da master até #1571; isso **não** implica aprovação de integração da PR.

## Prova CI independente, sem executar 100k — 15/11/2026

| Gate | Execução | Resultado | Escopo real |
|---|---|---|---|
| [PR #1567](https://github.com/lucianox777/Jornada/pull/1567) | DRAFT, não mesclar | aberta | Só protótipo de corpus |
| [PR-only #43759160834](https://github.com/lucianox777/Jornada/actions/runs/43759160834) | `probe-prepare` + `probe-consumer` | **2/2 SUCCESS** | Dois runners separados; **duas linhas públicas sintéticas**; arquivo/recibo completos; **NÃO TRAIN** |
| [Contrato #43759160878](https://github.com/lucianox777/Jornada/actions/runs/43759160878) | Python corpus 14/14 + segurança workflow 3/3 | **SUCCESS** | Corrupção e metadados rejeitados; sem ciência |
| Fonte congelada seed42/43/44 | Código C# do gerador | preservado | Alteração anterior revertida em vez de mudar os locks |

O job consumidor em runner separado registrou exatamente `V9_CORPUS_CROSS_RUNNER_HANDOFF_PASS` com `status=TRANSFER_VERIFIED_NONSCIENTIFIC` e `scientific_test_used=false`. O contrato usa `v9-corpus-crossrunner-pr-smoke.yml`, fixture de duas linhas (`tests/scripts/v9_corpus_crossrunner_fixture.py`) e `v9-corpus-handoff-verify.py`, sem certificados TEST, sem banco real, sem pontuação de pares e sem 324 hipóteses. Não extrapolar o smoke de duas linhas para escala/durabilidade 100k.

**Bloqueios ainda abertos:** armazenamento durável independente do cache evictável, publicação imutável de corpus entre execuções, verdadeiros parâmetros familiares congelados em REF, variante experimental do gerador para degradação materna `13,4%` sem mexer no gerador histórico, integração no workflow físico TRAIN com handoff completo, controle de acesso/retentividade, medição P95 e aceite posterior do PO. O fluxo `workflow_dispatch` criado somente na branch draft não equivale a workflow manual registrado na master. O TEST científico 100k permanece lacrado e NÃO utilizado.

## Correção de concorrência e regressão CI — 15/11/2026

**Estado: IMPLEMENTADO na branch, validação do HEAD mais recente PENDENTE.** O handoff agora mantém `flock(LOCK_SH)` em `.prepare.lock` durante a verificação e cópia dos quatro arquivos e manifesto. Falha fechada quando o lock está ausente ou é symlink. O PREPARE utiliza `LOCK_EX` no mesmo lock para lookup/generate/publish. Isso é uma proteção local em volume POSIX compartilhado, **não** prova de armazenamento durável entre runs ou imutabilidade contra escritores externos não cooperativos.

Rastreabilidade: commits `04234e839d0f8645f13fadd7deadb5d52d95cfc4` (handoff), `bb70758a62a19bf859061d881dcde7b1338d6332` (teste de lock ausente) e `fb3a9ba1948f6b082ff672181739e2c33e61210c` (fixture de publicação direta). A [CI de contrato #43778938540](https://github.com/lucianox777/Jornada/actions/runs/43778938540) **FALHOU** no SHA `bb70758` porque seis casos do fixture chamavam `snapshot.publish` diretamente, sem criar o lock que o PREPARE normalmente cria. A correção do fixture adiciona explicitamente esse lock; **não afirmar PASS até o novo SHA passar pela CI**. O [smoke cross-runner #43778938541](https://github.com/lucianox777/Jornada/actions/runs/43778938541) no SHA anterior passou, mas não substitui o contrato reprovado.

Precedência: complementa o contrato operacional da PR #1567, sem modificar a decisão canônica GOV-V9-CORPUS-PERSISTENTE-REF-FAMILIAR-20261115, nem autorizar merge, TRAIN científico ou TEST 100k. Próximo gate: CI no HEAD corrigido; depois durabilidade real com ACL, retenção, recuperação e provas entre execuções.

## Verificação de regressão e negativos adicionais — 15/11/2026

No HEAD `6f97aa928758557abc665938c692cc0b452dd99c`, os quatro workflows de PR terminaram **SUCCESS**: [contrato #43779051691](https://github.com/lucianox777/Jornada/actions/runs/43779051691), [cross-runner #43779051798](https://github.com/lucianox777/Jornada/actions/runs/43779051798), [fast experimental #43779051701](https://github.com/lucianox777/Jornada/actions/runs/43779051701) e [required-fast #43779051694](https://github.com/lucianox777/Jornada/actions/runs/43779051694). Essa evidência resolve a regressão do fixture no SHA citado, **não** valida commits posteriores.

O commit `e79df6bcbc6a221e06dd86c5d1a3dd0edd1e21e9` acrescenta dois cenários negativos: lock de preparação simbólico rejeitado no handoff e corpus reutilizado corrompido rejeitado sem executar gerador novamente. **Testes adicionados, CI deste novo HEAD ainda não confirmada.** Sem mudança de decisão do PO, sem ativação de armazenamento durável, TRAIN ou TEST científico.
