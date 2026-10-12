# DT10 — Transporte mínimo de binários para integração SQL

**12/10/2026 — Decisão experimental, não comprovada até a CI passar.**

O reuso de binários introduzido em #932 preservou a origem dos artefatos, porém passou a enviar aproximadamente **378.564.265 bytes** de pacote compactado por execução (GitHub Actions run `38202947991`). A rotina anterior empacotava todos os `src/*/bin/Release` e `tests/*/bin/Release`, contendo múltiplas cópias de dependências de projetos que não são usados pelo job `integration-sql`.

A comparação observacional entre um run anterior e um run com esse reuso não mostrou ganho garantido: em `38202415748`, o tempo observado foi de ~7,03 minutos de parede e ~31,67 runner-min; em `38203110197`, ~9,33 minutos e ~33,17 runner-min. Esses são **runs diferentes**, com variação de filas, caches e testes, não um benchmark controlado. Portanto o custo de upload/compressão precisa ser reduzido antes de ampliar essa estratégia.

## Escopo desta alteração

O produtor continua rodando **após** a prova obrigatória de dois builds limpos determinísticos e mantém as evidências completas `deterministic-build-evidence`. Ele passa a exportar *somente* `tests/Jornada.Integration.Tests/bin/Release/net10.0/**`, que contém a DLL de testes, as dependências transitivas e os arquivos copiados ao diretório de saída pelo MSBuild (config, SQL e fixtures necessários).

O consumidor `integration-sql` continua sujeito a:
- SHA exato de checkout, árvore Git, `GITHUB_RUN_ID`, SDK 10.0.112, arquitetura, SO e hash do grafo de `packages.lock.json`;
- SHA-256 por arquivo, SHA-256 do arquivo TAR, paths confiáveis e rejeição de symlinks;
- presença explícita da DLL `Jornada.Integration.Tests.dll`, do `.deps.json` e do `.runtimeconfig.json`;
- `dotnet restore Jornada.sln --locked-mode` e revalidação de locks no mesmo job;
- ambos os testes de integração e de fault injection `dotnet test --no-build`, com provas TRX sem skips.

O pacote **não** incorpora saídas duplicadas de APIs e workers que não são consumidas diretamente pelo job SQL. O schema do manifesto sobe de 1 para 2 e o consumidor falha fechado diante de conteúdo incompatível; nunca aceita artefatos de outro SHA/run.

## Critérios de merge

1. A suíte Python `test_ci_built_binary_gate.py` deve comprovar que diretórios bin de terceiros foram excluídos e que a falta dos três arquivos de teste impede a extração.
2. `integration-sql` e *todos* os gates obrigatórios devem concluir com sucesso na HEAD exata do PR, inclusive `e2e` agora composto das duas suítes reais paralelas e `security-analysis`.
3. Comparar o tamanho real do artefato e o custo de produzir, baixar e verificar com as execuções anteriores. **Não alegar aceleração antes de medir.**
4. Se os testes exigirem algum binário fora do diretório de saída do projeto, atualizar a dependência e a cópia por contrato explícito, sem falsificar aprovação ou dispensar teste.
5. Após merge, exigir nova CI de `master` e observar tempo de parede e runner-minutos; considerar reversão se a estratégia ainda degradar o desempenho.

Nenhuma ação em `JornadaLocal`, NODE, NAS, dados IBGE originais, SQL real, HML/PROD ou volumes persistentes. Somente GitHub-hosted runners e evidência sintética descartável.
