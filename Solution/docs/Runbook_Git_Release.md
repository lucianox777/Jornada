# Jornada - Git e releases - engenharia v3.72

## Fonte oficial

O repositório Git é a fonte oficial dos artefatos editáveis da Jornada: código C#, DDL/seed, JSON Schemas, PBIP/PBIR/TMDL, diagramas, documentação, scripts **e `RELEASE_INFO.txt`**. O ZIP é artefato de distribuição e não substitui o histórico Git.

A v3.69 corrige a regra anterior em que `RELEASE_INFO.txt` era criado depois da tag e ignorado pelo Git. Isso impedia a própria tag de provar qual linhagem/versionamento ela pretendia representar.

`MANIFESTO_ARQUIVOS.txt`, `SHA256SUMS.txt`, o relatório de validação e o ZIP continuam sendo artefatos de selagem gerados a partir de uma árvore tagueada. `RELEASE_INFO.txt`, ao contrário, precisa estar no commit aprovado **antes** da tag.

## Fluxo mínimo fail-closed

1. criar branch de trabalho;
2. atualizar código/documentação e `RELEASE_INFO.txt` no mesmo PR;
3. `RELEASE_INFO.txt` deve declarar `source_git_tag` e `source_git_predecessor_tag`;
4. executar/revisar CI de branch: gates estáticos (incluindo Power BI), build, unitários, integração SQL, fault injection sem skips, harness, DDL upgrade, E2E, vulnerabilidades e evidências aplicáveis;
5. antes da primeira tag promovível, gerar `packages.lock.json` por restore confiável e **commitá-los**; branch/PR pode usar o bootstrap, tag não pode regenerá-los;
6. merge do PR aprovado; árvore rastreada limpa;
7. validar PBIP no Power BI Desktop quando houver alteração de BI;
8. garantir que a tag predecessora declarada existe e é ancestral do commit corrente;
9. criar tag imutável `jornada-solution-vX.YY` exatamente igual a `source_git_tag` do `RELEASE_INFO.txt`;
10. enviar a tag. O job `release-promotion` só passa após locks, unit, integração SQL/fault injection, harness, DDL upgrade, E2E, restore SQL+Bronze com faults negativos e scale smoke; então executa `release-source-gate.py` em `fetch-depth: 0`;
11. gerar bundle/proveniência da fonte com `build-release-source-bundle.sh` e selar manifesto, hashes e ZIP;
12. reextrair o ZIP e validar hashes, bundle, proveniência e gates novamente.

Não fazer release de working tree com alterações rastreadas não commitadas. Não reconstruir uma versão numericamente seguinte a partir de conversa/memória quando a tag/commit predecessor não puder ser obtida.

## Verificação da tag

Em Bash:

```bash
python3 Solution/scripts/release-source-gate.py \
  --repo . \
  --release-info RELEASE_INFO.txt \
  --expected-tag jornada-solution-v3.72 \
  --require-clean
```

Em PowerShell o comando é equivalente. Os antigos wrappers `generate-release-info.sh/.ps1` foram mantidos apenas para compatibilidade operacional: desde a v3.69 eles **não geram** `RELEASE_INFO.txt`; apenas chamam o gate acima e falham se tag, HEAD, predecessor ou árvore limpa divergirem.

## Bundle de fonte transportável

Uma release pode produzir um Git bundle auto-contido:

```bash
Solution/scripts/build-release-source-bundle.sh \
  . \
  Solution/.local/release-source/Jornada_Source.bundle \
  Solution/.local/release-source/SOURCE_PROVENANCE.json \
  RELEASE_INFO.txt
```

O bundle inclui a tag corrente e a predecessora. `SOURCE_PROVENANCE.json` registra commits, trees, quantidade de arquivos e SHA-256 do bundle. A distribuição pode validar que o snapshot corrente corresponde ao seu escopo fonte:

```bash
python3 Solution/scripts/release-source-gate.py \
  --bundle Solution/supply-chain/source/Jornada_Source_v3.71_v3.72.bundle \
  --provenance SOURCE_PROVENANCE.json \
  --compare-root .
```

## Proteção de segredos

Nunca versionar `.env`, senhas SQL reais, tokens/chaves privadas/certificados, connection strings de HML/Produção com credenciais, `localSettings.json` do Power BI Desktop ou PBIX/PBIT gerados. `.env.example`, chaves sintéticas explicitamente identificadas como Development e contratos de configuração sem segredo podem ser versionados.

## Normalização

`.gitattributes` fixa EOL para fontes textuais e trata DOCX/PDF/imagens como binários. Antes do commit, executar `git diff --check`. O pacote de release deve ser comparado ao snapshot Git no escopo fonte para evitar divergência de conteúdo após a geração do bundle.
