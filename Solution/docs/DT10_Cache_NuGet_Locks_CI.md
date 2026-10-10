# DT-10 — Cache de pacotes NuGet com chave dos locks na CI

**Estado:** incremento de otimização submetido à CI, **não** substitui
a compilação determinística e não permite dispensar qualquer gate.
**Escopo:** workflow chamador `.github/workflows/ci.yml` e reusable
`.github/workflows/dt10-evidence.yml`, sem alteração de fontes de
produto nem de bancos.

## Estratégia

- Ativar `cache: true` na ação **SHA-pinned** `actions/setup-dotnet`,
  com `cache-dependency-path: 'Solution/**/packages.lock.json'`,
  em **todos os dez jobs SDK da CI principal** e no reusable SQL
  `dt10-evidence`. Há 22 arquivos `packages.lock.json`
  versionados em projetos `src` e `tests`; a chave de cache
  acompanha o conjunto completo de hashes dos locks.
- Continuar baixando a prova `nuget-lockfiles` da **mesma execução**,
  revalidando seu manifesto quando aplicável e invocando
  `dotnet restore Jornada.sln --locked-mode` **sempre**. O cache
  contém pacotes e nunca é considerado prova de dependências,
  build, teste ou publicação.
- Manter `deterministic-build` com dois builds e comparações,
  `security-analysis`, `integration-sql`, `harness-smoke`,
  `e2e` real e `dt10-evidence` com mínimo 4 testes SQL e zero
  ignorados. Job `impact` falha fechado em caso de mudança de
  estrutura/ausência de cache ou gate obrigatório.
- Cache compartilhado de pacotes não significa **reutilizar
  binários compilados** entre jobs; builds ainda ocorrem em
  runners independentes. Impacto esperado: potencial redução
  de download/restore, sujeito a cache hit, dimensão, política
  de retenção e hits em PRs GitHub. **Não** prometer redução
  medida de tempo até coletar métricas antes/depois nos runs.

## Aceite

A PR deve passar em todos os gates obrigatórios da HEAD exata,
inclusive a execução real do reusable DT-10 porque alterou seu
YAML e dos jobs SQL/E2E. O novo contrato
`scripts/test-ci-locked-nuget-cache-contract.py` garante presença
do cache em **todos os setups** e existência dos restore locked,
da prova de dependências e dos dez status checks.

A dependência NUnit/lock da PR #809 foi integrada à master antes
deste incremento e os hashes dos locks são da base corrente,
não hardcoded. Evitar cache com chaves que não incluam
todos os locks, artefatos `obj`/binários reaproveitados entre
SHAs, cache bypass de testes ou uso de instalações locais.

**Isolamento:** só workflows GitHub-hosted e SQL efêmero. Nunca
`JornadaLocal`, IBGE original, host/volumes/contêineres existentes,
NODE canônico, HML/PROD.
