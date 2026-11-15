# V9 — Contrato operacional do corpus persistente (PR #1567)

**Estado:** implementação parcial; **não apta a merge**. Registro de engenharia vinculado à decisão GOV-DOC-01 já documentada nesta branch. A aprovação de arquitetura pelo PO não autoriza executar a campanha científica nem mesclar.

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
