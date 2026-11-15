# V9 — Contrato operacional do corpus persistente (PR #1567)

**Estado:** implementação parcial; **não apta a merge**. Registro de engenharia vinculado à decisão GOV-DOC-01 já documentada nesta branch. A aprovação de arquitetura pelo PO não autoriza executar a campanha científica nem mesclar.

## Fluxo implementado nesta branch

- `v9-corpus-input-receipt.py`: captura hashes do código-fonte do gerador, IBGE nomes e referência diária de nascimento; aceita referência familiar somente se explicitamente fornecida. Não supõe que o catálogo REF familiar contém valores estatísticos.
- `v9-corpus-snapshot.py`: identidade SHA-256 da configuração de geração, hash dos quatro arquivos de saída reais e manifesto; recusa alteração silenciosa; publicação com lock POSIX e rename.
- `v9-corpus-prepare.py`: lock do ciclo lookup/generate/publish; rejeita cache existente corrompido; handoff para diretório independente com nova validação.
- `.github/workflows/v9-corpus-prepare-opt-in.yml`: PREPARE manual na branch de trabalho, com cache de GitHub Actions e artefato de handoff. **Não é o workflow principal TF4**.
- `.github/workflows/v9-corpus-persistence-contract.yml`: testes de contrato em PR.

## Limites e bloqueadores

1. O cache do GitHub Actions pode expirar ou ser removido; não constitui armazenamento durável garantido. A arquitetura aprovada exige política de retenção, ACL, auditoria, imutabilidade e recuperação cross-runner antes de ativar o fluxo principal.
2. O artefato do PREPARE é limitado à execução e retenção do Actions; ainda não há consumidor seguro no TRAIN de 324 hipóteses.
3. O PREPARE manual usa `expected_sha` para a branch de desenvolvimento. O fluxo científico existente mantém exigência de `master`; **não alterar essa admissão para permitir execução científica em branch**.
4. O modelo familiar exige valores quantitativos oficiais congelados e rastreáveis (não apenas catálogo de fontes). Não atribuir probabilidades inventadas a mães, irmãos ou gêmeos.
5. Perfil clean histórico: taxa de corrupção materna de 10%; 13,4% é ilustração, não novo padrão aprovado.
6. CI, testes de corrida, cache cross-run e verificação end-to-end precisam de evidência de aprovação. A mera existência de testes não prova execução.
7. A integração final exige evidências de preservação das 324 hipóteses, 81 células, TF, m/u, FDR/CP/P95, TRAIN/VALIDATION/TEST e regras de acesso.

## Critérios para declarar PR pronta (sem mesclar)

- [ ] Fonte REF familiar com números oficiais, período, município/UF, hashes, licenças e justificativa.
- [ ] Parâmetros de corrupção materna configuráveis e testes de compatibilidade do padrão histórico.
- [ ] Modelagem familiar estatisticamente rastreável e testes determinísticos.
- [ ] Persistência durável entre execuções e runners, com retenção e recuperação testadas.
- [ ] PREPARE integrado ao TRAIN principal com handoff fail-closed e controle de custo.
- [ ] Testes unitários, CI e segurança aprovados com links de execução.
- [ ] Revisão de diff, documentação GOV-DOC-01 e índice sincronizados.
- [ ] Revisão final do PO para autorizar merge **em momento posterior**.

**Instrução permanente do PO para esta atividade:** manter PR #1567 aberta, sem merge na master.
