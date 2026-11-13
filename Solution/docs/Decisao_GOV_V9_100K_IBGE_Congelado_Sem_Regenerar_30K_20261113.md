# GOV-V9-100K-IBGE-CONGELADO-SEM-REGERAR-30K-20261113

**Autoridade:** Product Owner (13/11/2026, America/Sao_Paulo). **Estado:** alteração operacional aprovada em conversa e implementada em PR, a validar antes de integrar. **Política:** GOV-DOC-01.

## Contexto e evidências do código

O PO identificou trabalho repetido na campanha TF4/324: a rotina `scripts/v9-tf-prepare-pin.sh` gerava novamente um corpus **sintético de 30.000 pessoas, seed42** para reconstruir um comprovante do IBGE, tanto no job TRAIN quanto no job independente VALIDATION. O script antigo não recalibra 30k, mas regenera arquivos `pessoas_verdade.csv`, `observacoes.csv`, `gabarito.json`, calcula `bootstrap.json` e exporta `pinned-ibge.json`. Essa etapa era compartilhada com outros estudos históricos, que não devem ser modificados.

A referência IBGE canônica **já é imutável e local** em `data/reference/ibge-nomes-2022/manifest.json`: `CENSO2022_NOMES_BRASIL_V1`, arquivos comprimidos NDJSON com SHA-256 declarados, `operationalSource=LOCAL_IMMUTABLE_SNAPSHOT`, `remoteApiRequiredAtRuntime=false`, `replaceInPlace=false`. A proveniência **sintética histórica 30k** é distinta de dados IBGE e tem hashes e identidade seed42 congelados em `data/reference/v9-synthetic-seed42-30000/manifest.json`. O plano TF4/324 já contém `PinnedIbgeReferenceVersionId`, `PinnedIbgePublishedSha256`, `PinnedIbgeMaterializedFrequencySha256` e `PinnedIbgeBootstrap`; a aplicação usa esses valores ao avaliar TF positiva. Não é necessário regerar 30k para comprovar esses metadados.

## Decisão aprovada, escopo e alternativa descartada

**Deixar de regenerar o corpus sintético histórico de 30 mil pessoas em ambas as fases da campanha manual V9 TF4/324**, substituindo a preparação de 30k por admissão fail-closed dos **valores e SHA-256 congelados**, sem alterar o modelo, pesos TF, quatro unidades, matriz 81×2×2, seed42 do TRAIN 100k ou seed independente de VALIDATION.

**Continuar carregando os arquivos locais IBGE no SQL descartável de cada job**, porque cada runner GitHub Actions inicia com banco novo; uma referência fixada no Git não é uma tabela pronta dentro do SQL efêmero. Essa carga é reidratação, **não regeneração IBGE**, não download remoto e não autorização para atualizar a publicação. Não persistir SQL ou segredos entre jobs. Não compartilhar banco de TRAIN com VALIDATION.

Não adotar como atalho imagem SQL com dados pré-carregados, bypass da carga, substituição dos hashes pelo SHA de gzip nem referência mutable `ATIVA` sem pin: seriam mudanças arquiteturais/de segurança diferentes e perderiam parte da verificação física.

## Implementação e controles

- Novo helper exclusivo do 100k `Solution/scripts/v9-100k-load-frozen-ibge.sh`: exige execução manual na master com SHA exato, usuário proprietário, ambiente DEV, banco `JornadaHarness` descartável e diretório novo; cria esquema do SQL e carrega a publicação do manifesto local usando o loader C# de produção.
- `Solution/scripts/v9-100k-frozen-ibge-admission.py`: confere integralmente que o pin do plano (ID, digest publicado, digest TF materializado e bootstrap) coincide com os dois registros congelados; exporta **somente bootstrap de metadados** para o verificador C#, sem gerar CSV. Em seguida usa o C# existente `--v9-tf-export-ibge-pin` para calcular o digest **físico SQL** e rejeita divergência perante o plano. Não recalcula novas frequências estatísticas.
- Continua aplicando a migração SQL `20261103_Ref_Corpus_Sintetico_Evidencia_V9.sql`, que registra os hashes 30k **já congelados**, não pessoas, linhas, nomes ou novas frequências IBGE.
- Workflow `.github/workflows/v9-v3-four-name-100k.yml`: em TRAIN utiliza `$root/bound/plan.json`; em VALIDATION usa `$root/handoff/plan.json`, proveniente do artefato verificado com SHA. Ambos chamam o helper novo; o workflow não usa mais `v9-tf-prepare-pin.sh`. Removem-se diretórios temporários de verificação em ambos os runners.
- **Não mudar** `v9-tf-prepare-pin.sh` nem outros estudos 30k/preflight, preservando seu contrato original e as provas históricas.

## Impacto, riscos e precedência

Ganho esperado: remover geração desnecessária, varredura/validação de três arquivos brutos sintéticos e armazenamento temporário 30k em cada job, mantendo os mesmos dados de referência e vínculo científico. Não se presume ganho de tempo nem dispensa do carregamento da referência IBGE no banco descartável. Uma divergência entre hashes do plano, registro fixo ou SQL deve falhar fechado antes de iniciar calibração/VALIDATION.

**Precedência:** complemento operacional à decisão [GOV-V9-V3-TF4-324-20261111](Decisao_GOV_V9_V3_TF4_324_100k_20261111.md), à [separação dos jobs](Decisao_GOV_V9_TF4_324_Validation_Job_Separado_20261113.md) e à publicação IBGE congelada [Linkage_V9_Frozen_IBGE_TF_Snapshot_20261013](Linkage_V9_Frozen_IBGE_TF_Snapshot_20261013.md). Não altera 324 hipóteses, governança de TEST, FDR/CP, P95, contratos de segurança, anti-reexposição nem decisão de ativação. O run [#43453565012](https://github.com/lucianox777/Jornada/actions/runs/43453565012) utiliza versão anterior e não muda retroativamente.

**Gates de integração:** teste de contrato em ambos os jobs, rejeição de pin alterado, CI obrigatória no commit final, squash merge e verificação na master. O teste físico da carga e equivalência dos digests ainda é necessário antes de declarar o caminho 100k comprovado. [Índice de decisões](Indice_Decisoes_Vigentes.md).
