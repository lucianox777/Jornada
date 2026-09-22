# Calibrador — JORNADA_SYNTH_CORPUS_V2

## Objetivo

`JORNADA_SYNTH_CORPUS_V2` é o benchmark sintético controlado do Calibrador para testar estimação, robustez a corrupção administrativa e políticas de ground truth sem usar dados pessoais reais.

Ele complementa, mas não substitui, o benchmark nominal IBGE. O benchmark IBGE mede confundibilidade nominal e capacidade de blocking; a V2 mede comportamento do estimador sob uma verdade sintética conhecida.

## Regras normativas

1. O split TRAIN/VALIDATION/TEST é feito por `base_person_id` antes da geração de observações.
2. `base_person_id` é truth interna. É proibido em blocking, candidate generation e scoring.
3. `m_exact` usado como gabarito é calculado empiricamente depois da materialização das observações.
4. Pares com ausência em qualquer lado ficam fora do denominador do campo correspondente.
5. Taxas declaradas de corrupção são parâmetros do gerador, não gabarito observado.
6. Prevalência-base de CPF/CNS e retenção por observação são parâmetros diferentes.
7. CPF e CNS sintéticos devem ser fictícios e estruturalmente válidos no cenário limpo.
8. Cenários de CNS inválido, reutilizado e com conflito grave de nascimento são explicitamente rotulados.
9. CNS nunca cria, funde ou seleciona UUID, nem mesmo no benchmark.
10. Se CPF ou CNS forem usados como fonte de rótulo em uma avaliação, a própria variável e qualquer derivado ficam proibidos em blocking/candidate generation/scoring.

## Implementação

As regras históricas de `Solution/tools/calibrador/gen_corpus_v2.py` foram portadas para
`Solution/src/Jornada.Linkage.SyntheticCorpus`. O C# usa o PRNG versionado
`XOSHIRO256SS_SPLITMIX64_V1`; portanto a equivalência com o Python é de **regra,
distribuição e invariante**, não de sequência de draws para a mesma seed.

O Python permanece temporariamente como referência executável para o gate
`scripts/synthetic-corpus-equivalence-gate.py`. Esse gate gera dois corpora com a
mesma fonte e os mesmos parâmetros e compara prevalências, retenções, partições,
observações por pessoa, cenários CNS, pesos e `m_exact`. Quando a ponte e o
avaliador estiverem integralmente no caminho C#, o Python poderá ser aposentado
sem perder a especificação das regras.

A entrada nominal C# é a projeção `BRASIL_TOTAL` declarada em
`data/reference/ibge-nomes-2022/projection-manifest.json`; hashes físico e canônico
são verificados antes da geração.

Exemplo C#:

```bash
dotnet run --project Solution/src/Jornada.Linkage.SyntheticCorpus --configuration Release -- \
  generate \
  --reference-root Solution/data/reference/ibge-nomes-2022 \
  --out artifacts/corpus-v2-csharp \
  --people 50000 \
  --seed 42 \
  --error-profile correlated
```

A referência Python equivalente continua disponível durante a transição:

```bash
python Solution/tools/calibrador/gen_corpus_v2.py \
  --ibge-source Solution/data/reference/ibge-nomes-2022/projection/frequencia-brasil.ndjson.gz \
  --out artifacts/corpus-v2-python \
  --people 50000 \
  --seed 42 \
  --error-profile correlated
```

O C# materializa os mesmos três artefatos lógicos da V2 —
`pessoas_verdade.csv`, `observacoes.csv` e `gabarito.json` — e acrescenta
`generation-manifest.json` com versões do gerador/ruleset/PRNG, fingerprint das
entradas e hashes dos artefatos gerados.


## Ponte DEV para ingestão real

O comando `generate-ingestion` gera o corpus C# e, na mesma execução, materializa
pacotes de cadastro Pessoa no formato real de ingestão. A ponte reutiliza
`Jornada.Ingestion.DeterministicIngestionZipWriter`; não implementa ZIP paralelo.

Rotas default da massa DEV:

| Gestor sintético | Gestor Jornada | sistema de origem |
|---|---|---|
| `G0` | `SEHAB` | `SEHAB` |
| `G1` | `SMADS` | `ASSISTENCIA` |
| `G2` | `SMDET` | `TRABALHO` |
| `G3` | `SMS` | `SAUDE` |

Exemplo:

```bash
export JORNADA_SYNTH_PSEUDONYMIZATION_KEY='<segredo DEV com pelo menos 16 bytes>'

dotnet run --project Solution/src/Jornada.Linkage.SyntheticCorpus --configuration Release -- \
  generate-ingestion \
  --reference-root Solution/data/reference/ibge-nomes-2022 \
  --out artifacts/synthetic-ingestion \
  --people 50000 \
  --seed 42 \
  --error-profile correlated \
  --pessoa-schema-versao 4 \
  --data-referencia 2026-09-21T00:00:00-03:00
```

A chave HMAC é lida somente de variável de ambiente. O valor não é gravado; somente
seu SHA-256 é registrado. `idPessoaEntrega` e `codigoPessoaOrigem` recebem um
pseudônimo HMAC independente da truth. `base_person_id` e o
`observacao_id` do corpus ficam apenas em `bridge-truth.jsonl`, que é sidecar de
avaliação e **não pode ser disponibilizado ao scorer/Calibrador**.

A ponte escreve:

- `corpus/`: artefatos do gerador e gabarito;
- `ingestion/ENTREGA_<GESTOR>_<SISTEMA>_v2_<sha>.zip`: pacotes reais de cadastro;
- `ingestion/bridge-manifest.json`: rotas, hashes, contagens e política de exclusão;
- `ingestion/bridge-truth.jsonl`: correspondência privada entre pseudônimo operacional e truth sintética.

### Gap representacional do contrato Pessoa

A ponte desta etapa materializa **somente Pessoa v4**, porque é a versão ativa
ingerível; Pessoa v5 permanece RASCUNHO e não pode ser escolhida apenas por opção
de CLI. Pessoa v4 (e o v5 atualmente em RASCUNHO) exige `dataNascimento`, enquanto
o corpus V2 pode produzir `DATE_MISSING`. A ponte **não inventa data** para fazer a
linha passar no contrato. Essas observações são registradas no sidecar com
`EXCLUIDA_CONTRATO_ATIVO_DATA_NASCIMENTO_AUSENTE` e não entram nos ZIPs.

Consequência normativa: toda avaliação posterior sobre dados materializados deve
usar o subconjunto `MATERIALIZADA` do sidecar e o
`materializedEmpiricalMExact` do `bridge-manifest.json`. O gap entre corpus
gerado e corpus representável é evidência do ensaio, não erro a ser escondido.


## Harness DEV do Calibrador real

O modo `SYNTHETIC_CALIBRATION_DEV` de `Jornada.Ensaio` executa a etapa seguinte
do ensaio sem acoplar o Parameters.Worker ao gerador. O projeto `Jornada.Ensaio`
**não referencia** `Jornada.Linkage.SyntheticCorpus`; ele inicia o gerador como
processo DEV separado e depois usa somente os pacotes operacionais e o
`bridge-manifest.json`.

Pré-condições fail-closed:

- o banco precisa possuir a extended property residente
  `Jornada.EnvironmentProfile=Development`;
- `Jornada.SolutionSchema` precisa ser `3.70`;
- o endpoint de ingestão precisa ser loopback;
- não pode existir origem/observação `SYNTH-*` de execução anterior;
- não pode existir corpus `SCALE-*` pré-carregado;
- não pode existir modelo `RASCUNHO` anterior.

`local-db.sh` e `local-db.ps1` agora provisionam explicitamente o marcador
`Development`. O DDL canônico continua neutro. Para o ensaio de recuperação, use o wrapper
dedicado; ele atualiza o schema sem destruir o banco e executa uma limpeza DEV
preservadora antes da rodada:

```bash
export JORNADA_SYNTH_PSEUDONYMIZATION_KEY='<segredo DEV com pelo menos 16 bytes>'
./scripts/local-synthetic-calibration.sh
```

No Windows:

```powershell
$env:JORNADA_SYNTH_PSEUDONYMIZATION_KEY='<segredo DEV com pelo menos 16 bytes>'
./scripts/local-synthetic-calibration.ps1
```

Os wrappers executam `local-db up --no-synthetic-corpus` e depois
`database/Jornada_Dev_SyntheticCalibration_Cleanup.sql`. A limpeza é autorizada somente
quando o marcador residente é `Development`: remove estado operacional dos schemas
`ingestao`, `bronze`, `silver`, `gold`, `identidade` e `auditoria`, mas
preserva as duas tabelas append-only da avaliação sintética, todo o schema `ref`,
as extended properties e o schema/migration ledger. Ela também falha fechado se
uma tabela preservada mantiver FK habilitada para estado que seria apagado.

Depois disso os wrappers leem porta/banco/senha do mesmo `.env` usado pelo Docker
local, montam `ConnectionStrings__Jornada` em memória e chamam
`SYNTHETIC_CALIBRATION_DEV`. A chave HMAC permanece apenas na variável de ambiente
e não é passada como argumento de processo. `ExpectedSeeds` e `RunGroupId`
podem ser fixados entre invocações para formar um grupo multi-seed sem perder as
rodadas já persistidas.

O default do ensaio é **200.000 pessoas-base**. O Calibrador aplica o mínimo
`MinimumIndependentMatchedPairs=5000` depois do split determinístico TRAIN
(default 60%) e escolhe no máximo um par de fontes por pessoa. Com prevalência-base
de CPF ~22%, retenção por observação ~55%, perfil `correlated` e exclusão das linhas
`DATE_MISSING` pelo contrato Pessoa v4, 100 mil pessoas produziriam apenas cerca de
3,25 mil pares TRAIN em expectativa. Com 200 mil, a expectativa sobe para cerca de
6,5 mil, preservando margem sem reduzir o gate estatístico.

A massa de 200 mil gera aproximadamente 96 mil linhas por ZIP/Gestor no caso mais
denso (`clean`), ainda abaixo de `MaxPessoasPorEntrega=100000`. O Processor iniciado
pelo ensaio recebe esse limite de 100 mil somente nesse processo DEV; o harness
também rejeita qualquer pacote que o ultrapasse. Esse limite técnico é registrado na
evidência e não altera o estimador.

O harness:

1. restaura a Solution em `--locked-mode` e compila Release;
2. executa `generate-ingestion`;
3. valida hashes dos quatro ZIPs sem ler a truth;
4. carrega as credenciais GESTOR sintéticas de
   `config/security/test-access-keys.json`, aceitas somente em Development;
5. sobe `Jornada.Api` e `Jornada.Processor.Worker` reais;
6. envia cada pacote por `POST /api/v1/ingestao/entregas` e aguarda
   `PROCESSADA`;
7. confere que o total de origens/observações `SYNTH-*` na Silver coincide com
   o total materializado do bridge;
8. reutiliza a referência nominal IBGE `ATIVA` já preservada; somente quando ela
   não existe executa o Parameters.Worker em `LOAD_NAME_FREQUENCY_SNAPSHOT`;
9. executa `GENERATE_DRAFT` pelo Parameters.Worker real e exige exatamente um novo
   modelo ainda em `RASCUNHO`;
10. executa o Runner real em `MODEL_VALIDATION` com `publish=false` e exige
    `CONCLUIDO_SEM_PUBLICACAO`;
11. executa `SYNTHETIC_EVALUATE`, grava JSON/SHA-256 e persiste apenas os agregados
    append-only.

O relatório `synthetic-calibration-dev.json` contém apenas evidência agregada:
baseline do banco, fingerprints, hashes de pacotes, Entregas processadas, contagens
materializadas e metadados do RASCUNHO. O runner não abre
`bridge-truth.jsonl`, não conhece `base_person_id`, não chama `VALIDATE` nem
`ACTIVATE`, e registra explicitamente `syntheticTruthConsumed=false` e
`modelPromotionAttempted=false`.

A limpeza preservadora ocorre antes das pré-condições do harness e remove o seed
operacional DEV, massas `SCALE-*`, rodadas anteriores e modelos descartáveis.
Assim o universo candidato da rodada contém somente a massa que o ensaio acabou de
materializar. `baseline.totalObservations` continua registrado e deve ser zero
nesse caminho. A referência IBGE não participa dessa limpeza e pode ser reutilizada
entre rodadas.

## SYNTHETIC_EVALUATE pós-RASCUNHO

Depois que o Parameters.Worker publica exatamente um novo modelo em
`RASCUNHO`, o modo DEV chama `Jornada.Linkage.Evaluation` com
`--synthetic-evaluate-root`. Só nesse ponto a truth é lida.

A separação é deliberada:

- o gerador produz truth e pacotes;
- API/Processor materializam os pacotes sem truth;
- Parameters.Worker gera o RASCUNHO sem saber que a massa é sintética;
- somente o avaliador pós-RASCUNHO abre `bridge-truth.jsonl` e
  `corpus/observacoes.csv`;
- o relatório agregado não contém `base_person_id`, `observation_id`, CPF,
  CNS, nome ou qualquer linha de truth.

A avaliação V1 usa o próprio ruleset persistido no RASCUNHO e o
`BlockingProjectionKeyProjector` compartilhado com o runtime. Para cada passe,
reconstrói as assinaturas canônicas das observações materializadas, faz AND entre
os campos do passe e OR entre passes e deduplica a união de pares.

O universo V1 é explicitamente:
**observações sintéticas materializadas**. Ele não é apresentado como população
real nem como substituto da validação #31.

A união candidata é exata. O parâmetro
`Ensaio:SyntheticCalibration:MaxCandidatePairs` (default 10.000.000) é um teto
de segurança: se a união excedê-lo, a avaliação falha. Não existe amostragem
silenciosa de truth.

O relatório `synthetic-evaluation.json` inclui:

- seed do gerador e SHA-256 de `generation-manifest.json`, `observacoes.csv`,
  `bridge-truth.jsonl` e `bridge-manifest.json`;
- perfil residente `Jornada.EnvironmentProfile`;
- identidade/versão/status do RASCUNHO, fingerprint canônico do snapshot decisório
  do modelo e fingerprint do ruleset;
- estratos observacionais mutuamente exclusivos
  `CPF_PRESENT_CNS_PRESENT`, `CPF_PRESENT_CNS_ABSENT`,
  `CPF_ABSENT_CNS_PRESENT` e `CPF_ABSENT_CNS_ABSENT`;
- recall verdadeiro do blocking na união e por passe;
- retenção de não-match e redução do universo candidato;
- `m_truth_cpf_labeled`: pares verdadeiros interfonte com o mesmo CPF não nulo;
- `m_truth_no_cpf_target`: pares verdadeiros interfonte com ambos os CPFs
  ausentes, usados para medir o gap de transportabilidade;
- `u_truth_candidate_union`: não-vínculos na união candidata deduplicada;
- distribuições de nome, nome da mãe (incluindo `MISSING`) e nascimento
  semântico;
- versões bruta e reponderada da truth;
- distância de variação total entre modelo e truth e entre o estrato CPF e o alvo
  sem CPF.

A semântica publicada de `u` permanece
`CONDITIONED_ON_DEDUPLICATED_BLOCKING_CANDIDATE_UNION`. O avaliador não
reinterpreta `u` como par aleatório populacional.

## Persistência append-only da avaliação

Depois que `synthetic-evaluation.json` e seu SHA-256 são materializados, o mesmo
processo de avaliação registra a evidência agregada no SQL. O registro só é aceito
quando:

- o modelo ainda está em `RASCUNHO`;
- o marcador residente é exatamente
  `Jornada.EnvironmentProfile=Development`;
- o fingerprint canônico do modelo, recalculado pelo SQL, é idêntico ao fingerprint
  observado pelo avaliador antes de abrir a truth;
- o relatório declara natureza
  `SYNTHETIC_PARAMETER_RECOVERY` e finalidade
  `ENGINEERING_EVIDENCE_ONLY_NOT_PROMOTABLE`.

O cabeçalho fica em `auditoria.linkage_avaliacao_sintetica` e as métricas
numéricas tipadas em `auditoria.linkage_avaliacao_sintetica_metrica`.
Cabeçalho e métricas são gravados na mesma transação e protegidos por triggers
append-only. O SHA-256 do relatório torna a repetição idempotente para o mesmo
modelo e a mesma proveniência.

A persistência guarda somente agregados. Não há linha de pessoa, identificador de
truth, atributo pessoal ou score par-a-par. O contrato força
`validacao_estatistica=NOT_ASSESSED_ISSUE_31` e
`promocao_autorizada=0`.

Cada execução recebe também um `run_group_id` e a lista explícita de seeds
esperadas. Cada seed persiste uma avaliação independente. O grupo só publica
dispersão quando todas as seeds esperadas aparecem exatamente uma vez; grupo parcial
fica `INCOMPLETO` e grupo de proveniência divergente fica `INCONSISTENTE`.

A integridade com o modelo é verificada no instante da gravação: o SQL exige o
modelo em `RASCUNHO`, recalcula seu fingerprint canônico e compara com
`modelo_snapshot_sha256`. Depois desse commit, `modelo_id` é identificador
histórico, não FK viva. A migração
`20260922_Linkage_Synthetic_Evaluation_Retention.sql` remove essa dependência para
que a evidência continue consultável depois da limpeza do RASCUNHO; versão,
fingerprint do modelo, ruleset e fingerprints permanecem autocontidos no ledger.

O oracle de decisão usa a política compartilhada do Calibrador, seleciona em
`VALIDATION`, congela o candidato e usa `TEST` somente para avaliação/gate.
O Monitor sintético DEV lê diretamente o ledger retido, mostra a faixa
`SINTÉTICO — NÃO PROMOVÍVEL`, a última avaliação, o estado do grupo e a dispersão
multi-seed. A rota e o bloco HTML só são mapeados/renderizados quando o host está em
Development e o banco comprova o marcador residente `Development`; o Monitor não
faz JOIN com o RASCUNHO já descartável.

As procedures de promoção e a governança da conferência independente não consultam
essas tabelas. Portanto a existência da evidência sintética não satisfaz #31, não
substitui conferência e não autoriza `VALIDATE`/`ACTIVATE`.

## Gabarito

`gabarito.json` inclui:

- versão do gerador e seed;
- fingerprint SHA-256 da fonte nominal;
- perfil de erro e taxas declaradas;
- prevalência-base e retenção observacional de CPF/CNS;
- taxas dos cenários de anomalia CNS;
- `empirical_m_exact` com `eligible_pairs`, `exact_pairs`, valor bruto e valor reponderado;
- invariantes anti-leakage.

O valor reponderado usa o peso materializado em cada pessoa/observação para corrigir o oversampling da cauda nominal.

## Identificadores sintéticos

CPF é gerado com dois dígitos verificadores coerentes e sem sequência uniforme.

CNS sintético limpo usa a família provisória iniciada por 7, 8 ou 9, com 15 dígitos e soma ponderada pelos fatores 15..1 divisível por 11. Essa regra é compatível com implementações públicas do algoritmo de validação do CNS e serve apenas à geração de benchmark sintético; a regra operacional homologada da Jornada continua sendo a autoridade para aceitar identificadores reais.

## Cenários CNS

A V2 pode produzir, de modo parametrizado:

- `INVALID_CHECK_DIGIT`: quebra controlada do dígito final;
- `REUSED`: mesmo CNS atribuído a pessoas-base diferentes;
- `DOB_CONFLICT_REUSE`: reutilização entre pessoas com diferença de nascimento de pelo menos dez anos;
- `CLEAN` e `ABSENT`.

Esses cenários existem para testar o filtro de elegibilidade de ground truth. Não transformam CNS em âncora de identidade.

## Testes

`Solution/tools/calibrador/test_gen_corpus_v2.py` continua cobrindo a referência
Python. `SyntheticCorpusV2PortTests` congela as mesmas regras no C# e
`synthetic-corpus-equivalence-gate.py` prova equivalência estatística entre as duas
implementações.

Cobertura mínima:

- validade dos dígitos verificadores de CPF;
- validade estrutural do CNS provisório;
- quebra efetiva do CNS inválido;
- corrupção de data sempre efetiva, inclusive com dia maior que 12;
- exclusão de missing do denominador de `m`;
- preservação da truth `base_person_id` sob cenários CNS;
- determinismo do C# para mesma seed/entradas;
- materialização byte a byte estável;
- equivalência estatística Python ↔ C#.

A correção de data é intencional: a implementação Python anterior podia escolher
`DATE_TRANSPOSE` para dia > 12 e devolver a data original. Python e C# agora
preservam a intenção do parâmetro de corrupção: uma tentativa aceita de corrupção
de data produz mudança efetiva.

## Limites

O corpus é sintético e não prova representatividade da população sem CPF. Ele valida corretude e invariantes do mecanismo. Representatividade e suficiência para Produção continuam dependentes de evidência observada e diagnóstico versionado do Calibrador.
