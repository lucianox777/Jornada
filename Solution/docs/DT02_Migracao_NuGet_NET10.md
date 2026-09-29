# DT-02 — atualização dos locks NuGet para .NET 10

Estado em 29/09/2026: **pendente de commit e validação**. Não promover a PR #607 com o grafo NuGet antigo ou com o gate de proveniência desativado.

## Evidência obtida

- SDK `10.0.100` instalado pelo `jornada-ci` e `dotnet restore Jornada.sln --use-lock-file --force-evaluate` executado no job `dependency-lock` da execução [36573433667](https://github.com/lucianox777/Jornada/actions/runs/36573433667).
- O job publicou o artefato `nuget-lockfiles` (ID `11036387086`), contendo `packages-locks.tar.gz` com 21 arquivos `packages.lock.json` sob `src/` e `tests/`. A atualização altera o alvo para `net10.0` e reavalia dependências transitivas. Não basta substituir a string `net8.0`.
- O job `ddl-upgrade` ainda falha com `NU1004` até que os locks regenerados estejam versionados. Os demais jobs dependentes foram ignorados; isso **não** comprova regressão verde.

## Aplicação reprodutível

1. Em checkout limpo da branch, dentro de `Solution/`, executar `dotnet --version` e confirmar `10.0.100`.
2. Recuperar o artefato `nuget-lockfiles` da execução acima, extrair o `packages-locks.tar.gz` em `Solution/` preservando caminhos `src/` e `tests/`, conferir exatamente os 21 locks e inspecionar o diff antes de versionar. Alternativamente regenerar localmente com `dotnet restore Jornada.sln --use-lock-file --force-evaluate`.
3. Atualizar `config/release/nuget-lock-provenance.json` com os SHA-256 **efetivamente calculados** para cada lock e a evidência real da execução SDK 10; adaptar `scripts/nuget-lock-provenance-gate.py` para reconhecer a nova geração sem apagar a evidência histórica v4.05. Não copiar hashes antigos nem declarar PASS antecipadamente.
4. Commitar locks, manifesto e gate juntos. Executar `./scripts/nuget-lock-bootstrap.sh` para provar ausência de diff após `--force-evaluate`, validar a proveniência e executar `dotnet restore Jornada.sln --locked-mode`.
5. Reexecutar CI completo, DDL, integração SQL, E2E, empacotamento/instalador e conferência independente DT-14, obrigatória pela mudança de runtime. Só retirar o rascunho e considerar merge após todos os gates e revisões exigidos.

Esta nota registra evidência de diagnóstico, não uma certificação da migração nem autorização para ignorar os gates.
