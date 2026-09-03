# Supply chain / reprodutibilidade — engenharia v3.86

Esta pasta registra os pins externos da referência técnica. O workflow usa GitHub Actions por commit SHA, .NET SDK `8.0.424` e SQL Server 2022 CU26 por digest OCI.

## NuGet

`Directory.Build.props` habilita `RestorePackagesWithLockFile`. O CI cria os `packages.lock.json` em um restore limpo, valida estrutura/`contentHash` com `scripts/nuget-lock-gate.py` e repete o restore em `--locked-mode` antes de build/test. O conjunto de lock files e seu resumo SHA-256 são publicados como evidência `nuget-lockfiles`.

**Fechamento de release:** quando a Solution estiver em um repositório executável com .NET/NuGet, os lock files produzidos pelo job `dependency-lock` devem ser incorporados ao Git. A partir desse commit, o job deve ser alterado para não regenerar (`dotnet restore --locked-mode` diretamente). A distribuição v3.68 não inventa hashes NuGet no ambiente de montagem, que não dispõe de SDK/rede NuGet.

## SBOM

O job `unit` produz `sbom.cdx.json` CycloneDX 1.5 a partir de `dotnet list package --include-transitive --format json` depois do restore bloqueado. A evidência é publicada no artifact `sbom-cyclonedx`.

A v3.65 preserva integralmente este mecanismo de supply chain da v3.60; o fechamento de atomicidade/conformidade não altera pins, lock bootstrap ou SBOM.


## v3.68

`generate-sbom.py` não contém versões normativas hardcoded: Base e Solution são lidas de `../RELEASE_INFO.txt` (ou de `--release-info` explícito). O CI gera o CycloneDX após restore bloqueado e valida JSON. `packages.lock.json` continuam sem ser fabricados no pacote montado sem NuGet; devem ser produzidos por restore real e persistidos no Git conforme o gate de supply chain.
## v3.69 — Git como fonte recuperável e tag como gate de release

A distribuição inclui `source/Jornada_Source_v3.68_v3.69.bundle` e `../../SOURCE_PROVENANCE.json`. O bundle é um repositório Git transportável e auto-contido com dois snapshots fonte encadeados. O escopo exclui somente artefatos de selagem gerados depois do commit (`MANIFESTO_ARQUIVOS.txt`, `SHA256SUMS.txt`, `RELATORIO_VALIDACAO_ENGENHARIA.txt`, `SOURCE_PROVENANCE.json` e o próprio bundle).

`release-source-gate.py` possui dois usos:

```bash
# pacote distribuído
python3 scripts/release-source-gate.py --bundle supply-chain/source/Jornada_Source_v3.68_v3.69.bundle --provenance ../SOURCE_PROVENANCE.json --compare-root ..

# checkout do repositório oficial em uma tag
python3 scripts/release-source-gate.py --repo .. --release-info ../RELEASE_INFO.txt --require-clean
```

Em uma tag `jornada-solution-v*`, o CI exige que a tag declarada em `RELEASE_INFO.txt` aponte exatamente para `HEAD` e que `source_git_predecessor_tag` seja ancestral. Assim, uma release não pode mais ser reconstruída a partir de memória/conversa e promovida silenciosamente como continuação linear.

Para NuGet, tags são mais estritas que branches: `dependency-lock` executa `nuget-lock-gate.py` sobre os arquivos já versionados e `dotnet restore --locked-mode`; não chama `--use-lock-file` nem `--force-evaluate`. Portanto a primeira tag posterior a esta release só será promovida depois que um restore confiável gerar e commitar os `packages.lock.json`.


## v3.70 — cadeia preservada e promoção com evidência

O bundle corrente é `source/Jornada_Source_v3.69_v3.70.bundle`. Ele preserva toda a ancestralidade já iniciada em v3.68 e ancora a nova tag v3.70 sobre v3.69. A promoção da tag agora exige também os jobs de restore SQL+Bronze e scale smoke, além dos gates existentes.


## v3.83 — pacote canônico, CI e projeto Integration dedicado

A v3.83 restaura a estrutura de distribuição `Solution/` + `.github/workflows/ci.yml`, incorpora `SOURCE_PROVENANCE.json` e um bundle Git `Jornada_Source_v3.80_v3.83.bundle`. O predecessor verificável disponível é a tag v3.80; o commit v3.83 materializa o snapshot técnico recebido na v3.82 e a reorganização do projeto de Integration. A lacuna de tags v3.81/v3.82 não é ocultada nem inventada.

## v3.84 — locks com proveniência explícita e fronteira Unit/Integration

A v3.84 não usa a mera presença de `packages.lock.json` como prova de restore. `scripts/nuget-lock-gate.py` valida estrutura, versões exatas, `resolved`/`contentHash` e, agora, igualdade **exata** do conjunto `Direct` com cada `.csproj`; a origem é registrada separadamente em `config/release/nuget-lock-provenance.json` e validada por `scripts/nuget-lock-provenance-gate.py`.

Neste ambiente de montagem, `dotnet` não está disponível. Doze locks permanecem byte-a-byte herdados da v3.83. Os locks de `Jornada.Tests` e `Jornada.Integration.Tests`, afetados pela remoção de Testcontainers do Unit e pela redução de ProjectReference diretos, foram derivados deterministicamente do grafo já resolvido da v3.83 por alcançabilidade, sem criar versões nem `contentHash`. Eles ficam explicitamente `PENDING_TRUSTED_DOTNET_RESTORE`; promoção de tag exige `dotnet restore Jornada.sln --locked-mode`.

A cadeia Git passa a ser direta `jornada-solution-v3.83 -> jornada-solution-v3.84`, preservada no bundle distribuído.

## v3.85 — locks herdados sem mutação

A v3.85 não altera `PackageReference` nem `packages.lock.json`. Todos os 14 locks são byte-a-byte herdados da v3.84. Os dois locks que já estavam `PENDING_TRUSTED_DOTNET_RESTORE` continuam com esse estado; a correção de friend assembly e dos scripts PowerShell não é usada para promover artificialmente a confiança dos locks.

A cadeia Git passa a ser `jornada-solution-v3.84 -> jornada-solution-v3.85`, preservada no bundle distribuído.


## v3.86 — locks herdados sem mutação

A v3.86 altera somente o acompanhamento de health do SQL Server local e metadados/gates da release. Não modifica `PackageReference` nem `packages.lock.json`. Os 14 locks permanecem byte-a-byte iguais à v3.85 e os dois estados `PENDING_TRUSTED_DOTNET_RESTORE` são preservados.

A cadeia Git passa a ser `jornada-solution-v3.85 -> jornada-solution-v3.86`, preservada no bundle distribuído.
