# Scripts da Solution

Este diretório reúne scripts de **operação local**, **validação/CI**, **testes de integração e resiliência**, **migração/release** e **ferramentas auxiliares** da Jornada.

O objetivo deste README é responder duas perguntas antes de executar qualquer arquivo daqui:

1. **Quando devo usar este script?**
2. **Como devo executá-lo?**

> Execute os comandos abaixo a partir de `Solution`, salvo indicação em contrário. Em PowerShell, isso significa estar em `...\Jornada\Solution` antes de chamar `./scripts/...`.

> **Rastreabilidade PowerShell:** scripts operacionais `.ps1` deste diretório devem imprimir `# <comando>` imediatamente antes de executar cada comando externo relevante. O comando executado aparece **somente no output**, sem ser duplicado como comentário de código. Segredos nunca entram nessa linha; use `<redacted>`.

## Sequência recomendada de validação local

Use esta sequência quando quiser validar uma alteração **passo a passo**, identificando exatamente em qual etapa aparece uma falha. A ideia é começar pelo `master` atualizado, provar compilação e testes rápidos primeiro e só depois avançar para banco, E2E, resiliência, linkage, escala e a suíte agregada.

### 0. Atualizar o `master`

Estes comandos são executados a partir da raiz do repositório (`...\Jornada`):

```powershell
git status
git switch master
git fetch origin
git pull --ff-only origin master
cd .\Solution
```

Se `git status` mostrar alterações locais inesperadas, pare aqui e entenda o que deve ser preservado antes de atualizar o `master`.

### 1. Restaurar dependências

```powershell
dotnet restore Jornada.sln
```

Falha aqui indica problema de restore, SDK, NuGet ou dependências. Ainda não é necessário subir banco/containers.

### 2. Compilar em Release

```powershell
dotnet build Jornada.sln --configuration Release --no-restore -warnaserror
```

Esse é o primeiro gate de código. A compilação deve terminar sem erros e sem warnings aceitos como sucesso.

### 3. Rodar os testes unitários rápidos

```powershell
dotnet test .\tests\Jornada.Tests\Jornada.Tests.csproj --configuration Release --no-build --filter "TestCategory=Unit"
```

Esse comando roda somente os testes explicitamente marcados como `Unit`. Ele é útil para feedback rápido, mas **não equivale** à suíte local principal: `local-test.ps1` também executa o conjunto não-integration mais amplo e os testes de integração do projeto.

### 4. Reutilizar o banco local e validar rapidamente a referência IBGE

No fluxo normal de desenvolvimento, preserve o banco local já materializado e valide apenas se a referência IBGE continua compatível com o snapshot versionado do repositório:

```powershell
.\scripts\local-check-ibge-reference.ps1
```

O script sobe apenas o container SQL Server/volume existente, sem `reset`, sem seed e sem executar o loader do IBGE. Ele verifica a versão `ATIVA`, o `referenceCode`, o total de linhas declarado no `projection-manifest.json`, o SHA-256 publicado e os triggers de imutabilidade. Se houver divergência, falha fechado e exige uma carga/upgrade explícito.

Use `.\scripts\local-db.ps1 -Action reset` somente quando o objetivo do teste for deliberadamente provar instalação limpa, reset determinístico ou reconstrução completa do banco; esse caminho apaga `ref.frequencia_nome` e portanto exige nova materialização da referência.

### 5. Rodar o core local

```powershell
.\scripts\local-test.ps1
```

Este script executa os gates OpenAPI/técnicos, SQL runtime smoke, restore/build Release, testes `TestCategory!=Integration` e os testes de integração SQL. Por desenho ele repete restore/build já executados acima: na sequência passo a passo isso é aceitável porque o objetivo é primeiro isolar uma eventual falha de compilação e depois validar o agregador oficial do core.

Considere esta etapa concluída somente quando aparecer:

```text
LOCAL CORE TEST: OK
```

### 6. Validar upgrade de DDL

```powershell
.\scripts\local-ddl-upgrade.ps1
```

Valida o caminho de upgrade a partir do baseline suportado e os invariantes de dados/schema. É obrigatório quando houver mudança de banco e continua sendo uma boa prova de regressão antes do fechamento local.

### 7. Rodar E2E destrutivo, quando necessário

```powershell
.\scripts\local-e2e.ps1
```

Exercita o caminho HTTP → Bronze → Silver → Gold → Serving → HTTP, mas atualmente faz `local-db reset`. Por isso não pertence ao fechamento padrão que preserva a referência IBGE; ele é exercitado pelo fluxo `local-test-from-zero.ps1`.

### 8. Rodar fault injection

```powershell
.\scripts\local-fault-injection.ps1
```

Valida comportamento de resiliência e o gate serial diante das falhas previstas pelo harness.

### 9. Recriar o cluster e validar linkage/calibração

Para uma rodada reproduzível completa — incluindo restore, build, testes unitários e validação a partir de cluster vazio — prefira o agregador único:

```powershell
.\scripts\local-linkage-monte-carlo-validation.ps1 -PairCount 1000000 -Seed 20260917
```

O script grava transcript em `.local\linkage-monte-carlo-validation\` e encerra imediatamente na primeira falha.

O agregador mais curto, sem restore/build/testes prévios, permanece disponível em:

```powershell
.\scripts\local-linkage-validation-from-zero.ps1 -PairCount 1000000 -Seed 20260917
```

Ele imprime cada comando antes de executá-lo e percorre, nesta ordem: `clean`, `up`, `calibrate`, relatório Monte Carlo IBGE read-only, `linkage`, `linkage-diagnose` e validação independente DEV.

A validação independente persiste fixtures auxiliares `SCALE-VAL-*` no mesmo banco. Eles **não fazem parte** da massa SCALE canônica de 11.000 origens (`5.000 SCALE-SEHAB + 5.000 SCALE-SMADS + 1.000 SCALE-PEND`). `local-db.ps1 -Action up` e o equivalente shell validam esses três segmentos separadamente e preservam fixtures adicionais; por isso o fechamento preservador pode ser executado depois da validação sem exigir `reset` apenas porque existem linhas `SCALE-VAL-*`.

A mesma sequência, expandida, é:

```powershell
.\scripts\local-cluster.ps1 -Action clean
.\scripts\local-cluster.ps1 -Action up
.\scripts\local-cluster.ps1 -Action calibrate
.\scripts\local-ibge-u-bootstrap.ps1 -PairCount 1000000 -Seed 20260917
.\scripts\local-cluster.ps1 -Action linkage
.\scripts\local-cluster.ps1 -Action linkage-diagnose
.\scripts\local-linkage-validation.ps1
.\scripts\local-linkage-evidence-readiness.ps1
.\scripts\local-linkage-triplet-collision-audit.ps1
```

A calibração nominal usa a referência IBGE internalizada por Monte Carlo: `NOME` combina prenomes nacionais `TODOS` com sobrenomes nacionais; `NOME_MAE` combina prenomes nacionais `FEMININO` com sobrenomes nacionais. A massa `MISSING` da mãe e as evidências de nascimento continuam estimadas no universo condicionado ao blocking.

**Limites que devem aparecer na leitura do resultado:** o recorte corrente de `NOME_MAE` usa `periodo_nascimento='TODOS'`, portanto ainda não condiciona a distribuição feminina por coorte materna; a composição de prenome e sobrenome é a hipótese versionada `INDEPENDENT_FIRST_NAME_SURNAME_MARGINALS_V1`. Não corrija nenhuma das duas hipóteses com fatores arbitrários: trate-as como análise de sensibilidade dentro da #31.

Após a MLE ordenada nominal, audite também `UNRESTRICTED_M_*`, `ORDER_RESTRICTED_BLOCK_*`, `ORDER_RESTRICTED_DELTA_LLR_*`, `ORDER_RESTRICTED_ADJUSTED_STATES_*` e `ORDER_RESTRICTED_MAX_ABS_DELTA_LLR_*`. O gate de monotonicidade não substitui essa leitura: pooling substancial é sinal diagnóstico a investigar.

Para avaliação estatística, uma única seed não basta para atribuir mudança de LLR ao modelo. Repita a calibração com seeds distintos e compare dispersão de `NOME`/`NOME_MAE`. Comparações A/B devem usar artefatos congelados identificados por `modelo_id`, algoritmo, fingerprints, referência e ruleset; não recalibre retrospectivamente um modelo antigo com código novo.

O diagnóstico read-only multi-seed automatiza a primeira parte sem recalibrar ou promover modelos:

```powershell
.\scripts\local-ibge-u-multiseed.ps1 -PairCount 100000 -Seeds 20260917,20260918,20260919,20260920,20260921
```

Ele preserva um JSON por seed e grava `.local\calibrador-ibge-u\multiseed\summary.json` com média, desvio-padrão, mínimo e máximo de cada estado nominal. Use primeiro uma contagem moderada para detectar instabilidade; aumente `PairCount` somente depois que a instrumentação e o ambiente estiverem validados.

`local-ibge-u-bootstrap.ps1` é **read-only** e reproduz separadamente as distribuições Monte Carlo de pessoa e mãe para comparação com o modelo ATIVO; não cria, valida ou ativa modelo. `local-linkage-validation.ps1` usa corpus independente DEV com positivos, impostores e probes de conflito. Nesta fase pré-homologação, não há comparação de regressão com modelo anterior; o harness reprova se produzir falso vínculo resolvido.

O fechamento do corpus independente é deliberadamente um **safety gate**, não um gate de capacidade. Zero falso vínculo é condição para passar; sensibilidade permanece métrica diagnóstica e pode ser zero sem que o safety gate seja reprovado. Portanto, `LINKAGE INDEPENDENT VALIDATION DEV SAFETY GATES: OK` significa somente que as invariantes conservadoras foram preservadas naquele corpus. Um futuro gate de capacidade precisa de controles positivos desenhados para medir resolução útil e não deve receber um piso de sensibilidade arbitrário sobre o corpus adversarial atual.

Quando nenhuma decisão é resolvida, PPV é matematicamente indefinido. O relatório grava `syntheticResolvedPpv=null` e o console mostra `N/A`; nunca use `0%` para representar `0/0`.

O diagnóstico dos negativos separa agora o **colisor planejado pelo fixture** do **melhor candidato realmente observado**. `generatorIntendedEvidenceProfiles` descreve o estado que o gerador pretendia criar; `plannedColliderAlignment` informa quantas vezes o UUID plantado foi de fato o melhor candidato; `observedBestScoreStates` mapeia os scores realmente produzidos de volta aos estados compatíveis da malha do modelo. Assim, um `NAME_COLLISION` planejado em `EXACT/LOW/EXACT` não é usado para explicar um score maior obtido contra outra Pessoa da Gold.

Runs completos só podem ser reutilizados quando `modelo_id` **e** o fingerprint do runtime/fixture corrente coincidirem com `.local\linkage-validation\run-provenance.json`. Run antigo sem essa proveniência falha fechado; gere evidência nova com `local-linkage-validation-from-zero.ps1`. Isso impede avaliar um resultado persistido produzido por código anterior apenas porque o modelo continuou com o mesmo UUID.

O relatório também explicita a proveniência do `PRIOR_MATCH_PROBABILITY`. Hoje o estimador usa `clamp(DISTINCT_BIRTH_DATE / POPULATION_SIZE, 0.000001, PRIOR_BLOCK_MAX)`; isto é uma **heurística de referência**, não a frequência empírica de match condicionada ao blocking. Se `clampedAtUpperBound=true`, o prior publicado está no teto da regra e essa saturação deve ser analisada antes de interpretar ou alterar `T_LINKAGE`.

Estados nominais com `matchedSupport=0` aparecem em `zeroMatchedSupportStates`. Seu `m` é sustentado por suavização e, quando aplicável, pela restrição de ordem, não por exemplos positivos observados. Assim, desempenho de abreviações nesses estados não deve ser generalizado para dados reais sem uma amostra `m` representativa.

A validação também executa a auditoria read-only `PTBR_POSITIONAL_INITIAL_COMPATIBLE_V1`. Ela testa, sobre o melhor candidato já produzido, uma regra estrita de abreviação: tokens de conteúdo precisam estar alinhados por posição; cada token deve ser idêntico ou uma inicial de uma palavra completa com a mesma letra; ao menos uma abreviação deve existir e qualquer token conflitante reprova a compatibilidade. O resultado fica em `.local\linkage-validation\abbreviation-compatibility-audit.json` e também entra em `validation-report.json`. Esse diagnóstico **não cria um quinto estado**, não estima `m/u`, não altera scorer, threshold ou prior; serve apenas para provar se `NAME_ABBREV` pode ser separado de `MOTHER_COLLISION` antes de qualquer mudança de modelo.

A mesma validação também calcula um **contrafactual read-only de política**: reaplica os rankings já produzidos como se apenas `SCORING_DUAL_THRESHOLD_CONFLICT_V1` fosse removido, mantendo `T_LINKAGE` e `CONFLICT_MARGIN_LOG_ODDS`. O bloco `counterfactualNoDualThresholdGuard` do `validation-report.json` mostra, para positivos e negativos, quantos casos seriam resolvidos, continuariam em conflito ou permaneceriam não resolvidos. Esse cálculo não recalibra o modelo, não altera parâmetros, não publica vínculos e não é autorização para mudar a política.

Além disso, `dualThresholdMarginFrontier` testa a alternativa mais restrita de **manter o guard como referência, mas perguntar se uma margem de log-odds maior permitiria liberar com segurança apenas parte dos casos em que os dois candidatos estão acima de `T_LINKAGE`**. O diagnóstico percorre os cortes de margem observados no próprio corpus e registra quantos positivos corretos e falsos vínculos seriam liberados em cada ponto. Sobreposição das margens verdadeiras com as margens dos impostores significa que a margem, sozinha, não é evidência discriminante suficiente. O cálculo também é read-only e não altera a política.

O bloco `currentEvidenceIdentifiability` fecha a pergunta seguinte: existem positivos e negativos que apresentam a mesma assinatura `EXACT/EXACT/EXACT` nos três campos atualmente usados pelo score (`NOME`, `NOME_MAE`, `DATA_NASCIMENTO`)? No cenário sintético `HARD_HOMONYM`, a validação comprova diretamente a igualdade desses três campos entre a observação negativa e seu melhor candidato. Se a mesma assinatura também aparece nos positivos `EXACT`, o relatório marca `observationalOverlapDetected=true`. Nessa situação, nenhuma regra determinística baseada somente nesses três campos consegue separar corretamente todos esses exemplos; a saída técnica é manter abstenção/conflito nesses casos ou acrescentar evidência independente. O diagnóstico não escolhe qual novo atributo deve existir e não altera a política.


Depois desse gate, `local-linkage-evidence-readiness.ps1` inventaria evidência se tentasse melhorar o score; por isso ele faz apenas o oposto: **mede a evidência já existente**. O relatório `.local\linkage-evidence-readiness\evidence-readiness.json` separa identificadores (`CPF`, `CNS`, `RG`, `UUID_JORNADA`) de atributos transversais. `TELEFONE_CONTATO`, `EMAIL_CONTATO` e `NOME_SOCIAL` já são elegíveis para resolução/blocking no catálogo e possuem projeção física, mas o scorer V6 ainda calcula LLR somente com `NOME`, `NOME_MAE` e `DATA_NASCIMENTO`. `CNS` e `RG` continuam condicionais e o diagnóstico não os promove a âncoras. `ENDERECO_RESIDENCIAL`, `REFERENCIA_TERRITORIAL` e `ENDERECO_CASA_ABRIGO_SIGILOSA` permanecem explicitamente inelegíveis para resolução de identidade.

A leitura principal é a cobertura em `POS_EXACT` e `NEG_HARD_HOMONYM`. Se uma evidência adicional não estiver presente/comparável nos dois grupos, o corpus DEV atual **não mede seu ganho discriminativo**; não se deve preencher essa lacuna com peso arbitrário, threshold novo ou hipótese sobre a população municipal. O próximo experimento somente deve calibrar `m/u` dessa evidência depois de existir cobertura representativa e regra de governança correspondente.

Para medir a pergunta de prevalência sem expor PII, use:

```powershell
.\scripts\local-linkage-triplet-collision-audit.ps1
```

A auditoria conta, na Gold ancorada por CPF, quantas Pessoas distintas compartilham a tripla `NOME_NORMALIZADO + NOME_MAE_NORMALIZADO + DATA_NASCIMENTO`. Os nomes normalizados vêm das chaves correntes `name_full` e `mother_name_full` da mesma projeção de blocking do modelo; o script exige cobertura de projeção coerente e grava apenas agregados, nunca nomes, CPFs, UUIDs ou datas individuais. O relatório fica em `.local\linkage-triplet-collision-audit\triplet-collision-audit.json`.

**Não interprete automaticamente essa taxa como municipal.** Em DEV/CI a Gold é sintética e o relatório marca o contexto do dataset. Uma estimativa de prevalência do município exige executar a mesma auditoria read-only sobre uma Gold ancorada representativa e governada. A medida serve para quantificar a classe de colisão; por si só não autoriza relaxar a guarda de dois candidatos acima de `T_LINKAGE`.

A massa SCALE canônica materializa 5.000 CPFs sintéticos estruturalmente válidos em `identidade.cpf_ancora`, obedecendo à mesma fonte de verdade normativa da produção. A auditoria exige essa cobertura para o corpus DEV e continua marcando o resultado como sintético; nenhuma taxa obtida da SCALE deve ser promovida a prevalência municipal.

### 10. Rodar o smoke de escala, quando necessário

```powershell
.\scripts\local-scale.ps1 -Profile smoke
```

O harness de escala também recria a base com massa própria. Portanto é um teste from-zero/destrutivo, não parte do fechamento padrão preservador. O perfil `smoke` valida com custo menor que `medium` ou `million`.

### 11. Escolher o fechamento depois de ensaio destrutivo

O harness de escala usa dados próprios e pode destruir a referência IBGE local. **Não execute `local-db.ps1 -Action reset` imediatamente antes do fechamento preservador**, porque esse reset também remove a referência que `local-test-all.ps1` espera reutilizar.

Se a referência IBGE ainda estiver materializada e o ambiente não tiver sido recriado pelo harness, siga para o fechamento preservador da etapa 12. Se você acabou de executar um fluxo destrutivo/scale e quer provar reconstrução completa, use diretamente `local-test-from-zero.ps1 -Suite full`, que rematerializa a referência de forma explícita.

### 12. Fechar com a suíte completa preservando a referência IBGE

```powershell
.\scripts\local-test-all.ps1 -Suite full
```

Este é o fechamento local padrão. Ele começa pelo quick check da referência IBGE já materializada e **não executa `reset`, `clean`, E2E ou scale harness destrutivos**. O core, fault injection, calibração/linkage e auditoria read-only rodam reutilizando a referência existente.

Quando o objetivo for deliberadamente provar instalação limpa/reconstrução completa, use o comando separado:

```powershell
.\scripts\local-test-from-zero.ps1 -Suite full
```

Esse segundo fluxo recria banco/volumes, executa E2E e scale e rematerializa a referência IBGE antes de concluir.

Considere a validação local concluída somente quando o fechamento terminar com:

```text
LOCAL TEST ALL: OK
```

### Regra prática

Durante desenvolvimento, pare no primeiro comando que falhar, corrija a causa e repita a etapa. Antes de considerar uma alteração pronta para PR/merge, percorra a sequência aplicável e finalize com o script dedicado da frente e os gates de CI. O padrão é `local-test-all.ps1 -Suite full`, que preserva a referência IBGE. Use `local-test-from-zero.ps1 -Suite full` somente quando a mudança precisar provar reconstrução completa.

Para frentes técnicas com testes direcionados, mantenha também um script dedicado em `scripts/` que concentre o comando reproduzível daquela mudança. Quando a referência IBGE já estiver materializada, o padrão é **reutilizá-la e executar um check read-only**, não apagá-la/recarregá-la.

## Atalhos: o que usar no dia a dia

| Necessidade | Script | Quando usar |
|---|---|---|
| Subir o ambiente local completo | `local-cluster.ps1 -Action up` | Desenvolvimento normal da aplicação, com os nós/serviços locais |
| Ver se o ambiente está rodando | `local-cluster.ps1 -Action status` | Antes de diagnosticar erro de conexão ou serviço |
| Ver logs do cluster | `local-cluster.ps1 -Action logs` | Diagnóstico de workers, API e serviços locais |
| Parar o cluster preservando dados | `local-cluster.ps1 -Action down` | Encerrar a sessão de desenvolvimento sem apagar volumes |
| Recriar o cluster | `local-cluster.ps1 -Action reset` | Quando é necessário reconstruir containers/serviços |
| Apagar completamente o cluster local | `local-cluster.ps1 -Action clean` | Ambiente inconsistente ou necessidade deliberada de começar do zero |
| Operar somente o banco local | `local-db.ps1` | Desenvolvimento/testes que precisam apenas do SQL Server local |
| Rodar suíte completa preservando IBGE | `local-test-all.ps1 -Suite full` | Fechamento padrão; reutiliza a referência existente e não executa reset/clean/E2E/scale destrutivos |
| Provar instalação limpa from-zero | `local-test-from-zero.ps1 -Suite full` | Cenário explicitamente destrutivo; recria banco/volumes e rematerializa a referência IBGE |
| Testar separação preserve/from-zero | `local-test-test-all-safety.ps1` | Prova que o modo padrão preserva a referência e que `-FromZero` sem autorização aborta antes de qualquer ação destrutiva |
| Rodar validação local específica | `local-test.ps1` | Iteração rápida durante desenvolvimento; preserva o banco existente |
| Conferir referência IBGE sem recarga | `local-check-ibge-reference.ps1` | Antes de testes que reutilizam `ref.frequencia_nome`; compara versão/linhas/hash publicado e imutabilidade sem carregar dados |
| Diagnosticar referência IBGE local | `local-diagnose-ibge-reference.ps1` | Read-only; lista versões, status, SHA e contagem de linhas por versão para investigar bases legadas/incompletas |
| Materializar referência IBGE sem reset | `local-load-ibge-reference.ps1 -AllowLoad` | Usa o bootstrap canônico; preserva o banco e só carrega quando o quick check não passa |
| Testar segurança da carga IBGE | `local-test-ibge-reference-load-safety.ps1` | Prova que o loader humano exige autorização e não chama `reset`, `clean` ou `local-db.ps1` |
| Testar o diagnóstico IBGE | `local-test-ibge-diagnostic.ps1` | Executa o diagnóstico read-only real e valida que aguarda SQL/banco ONLINE e não contém aspas SQL duplicadas |
| Reativar referência IBGE já materializada | `local-repair-ibge-reference.ps1` | Somente quando a versão canônica está íntegra, publicada e inativa; altera apenas status/ativado_em, sem recarregar `ref.frequencia_nome`; se a versão canônica não existir, chama o diagnóstico e falha sem alterar dados |
| Testar calibração DF/benchmark IBGE | `local-test-df-calibration.ps1` | Reutiliza e checa a referência IBGE existente por padrão; `-Quick` reduz o conjunto de testes e `-Offline` elimina a dependência do SQL local |
| Validar upgrade de DDL | `local-ddl-upgrade.ps1` | Toda alteração de schema/migração que precise provar upgrade sem perda de invariantes |
| Exercitar runtime SQL | `local-sql-runtime-smoke.ps1` | Mudanças em procedures, views, DDL e caminhos SQL que precisam de execução real |
| Rodar carga/escala | `local-scale.ps1` | Avaliação de comportamento com volumes maiores; não é o teste rápido do dia a dia |
| Executar drill de backup/restore | `local-backup-restore-drill.ps1` | Validar recuperação/DR, não durante desenvolvimento rotineiro |
| Validar artefato de release | `local-validate-release.ps1` | Antes de publicar/aceitar um pacote de release |

## 1. Ambiente local

### `local-cluster.ps1`

É a entrada principal para o ambiente local completo.

```powershell
./scripts/local-cluster.ps1 -Action up
./scripts/local-cluster.ps1 -Action status
./scripts/local-cluster.ps1 -Action logs
./scripts/local-cluster.ps1 -Action down
```

Ações disponíveis: `up`, `reset`, `down`, `clean`, `status`, `logs`, `calibrate`, `linkage` e `linkage-diagnose`.

Use `calibrate`, `linkage` e `linkage-diagnose` quando estiver trabalhando especificamente no pipeline de resolução de identidade. Prefira `down` quando quiser apenas parar; use `clean` somente quando aceitar perder o estado local correspondente.

### `local-db.ps1`

Controla o ambiente local centrado no banco de dados.

```powershell
./scripts/local-db.ps1 -Action up
./scripts/local-db.ps1 -Action status
./scripts/local-db.ps1 -Action down
```

Ações disponíveis: `up`, `reset`, `down`, `clean`, `status` e `backfill`.

Use este script quando não precisar do cluster completo. `backfill` é destinado aos cenários que exercitam explicitamente a recomposição/backfill local; não é necessário para simplesmente subir o banco. O `reset` recria schema/corpus e **não materializa sozinho** os 5.603.287 registros da referência IBGE. Quando precisar recuperar só a referência, use `local-load-ibge-reference.ps1 -AllowLoad` em vez de repetir o reset.

### `local-clean.ps1`

Limpa artefatos do ambiente local. É uma ferramenta de recuperação/manutenção, não um passo obrigatório antes de cada teste. Antes de usá-la, prefira os comandos `down` ou `reset` dos scripts de ambiente quando eles forem suficientes.

## 2. Testes e gates locais

### `local-diagnose-ibge-reference.ps1`

Diagnóstico **read-only** da referência IBGE local. Ao subir um volume existente, aguarda o SQL Server aceitar conexões e o `JornadaLocal` ficar `ONLINE` antes de consultar as tabelas `ref`. Não executa loader, seed, reset, alteração de status ou reparo.

```powershell
.\scripts\local-diagnose-ibge-reference.ps1
```

Para validar o próprio contrato do diagnóstico contra o banco já existente:

```powershell
.\scripts\local-test-ibge-diagnostic.ps1
```

### `local-test-all.ps1`

É o fechamento local amplo **padrão e preservador**:

```powershell
.\scripts\local-test-all.ps1 -Suite full
```

A execução pública busca `origin/master`, cria um worktree destacado e roda o SHA remoto sem alterar o working tree do desenvolvedor. O worktree **não recebe uma cópia do `.env`**: o wrapper aponta temporariamente `JORNADA_LOCAL_ENV_FILE` para o `.env` do checkout principal, e os scripts internos reutilizam essa configuração para acessar o mesmo SQL/volumes canônicos.

Nesse modo, a referência IBGE é checada em modo read-only e preservada; não há `reset`, `clean`, E2E ou scale destrutivo.

Para provar instalação limpa/reconstrução completa, use exclusivamente:

```powershell
.\scripts\local-test-from-zero.ps1 -Suite full
```

Esse wrapper aciona o modo `-FromZero -AllowDestructiveReset`, recria banco/volumes e rematerializa a referência IBGE antes de concluir.

O contrato preserve/from-zero e a propagação segura do `.env` podem ser validados isoladamente com:

```powershell
.\scripts\local-test-test-all-safety.ps1
```

### `local-test.ps1`

Entrada de teste mais focada/rápida. Use durante o ciclo editar → testar → corrigir. Ele sobe/reutiliza o banco local sem `reset`, portanto preserva uma `ref.frequencia_nome` já carregada. Não substitui o gate de instalação limpa quando a mudança exigir provar reconstrução completa do ambiente.

### `local-check-ibge-reference.ps1`

Gate read-only para a referência IBGE já materializada:

```powershell
.\scripts\local-check-ibge-reference.ps1
```

Ele **não executa o loader**. Compara o `referenceCode` ativo com os manifestos do repositório, confere o total esperado de linhas, exige SHA-256 publicado e valida os triggers que tornam a versão publicada imutável. Se não houver versão `ATIVA`, também diagnostica a versão canônica publicada e informa status/linhas/hash. Use `-NoStart` se quiser exigir que o container SQL já esteja em execução.

Quando a versão canônica estiver completa e publicada, mas apenas `OBSOLETA` ou `VALIDADA`, use o reparo explícito:

```powershell
.\scripts\local-repair-ibge-reference.ps1
```

Esse script falha se existir outra versão `ATIVA`, se a contagem divergir do `projection-manifest.json`, se o hash publicado estiver ausente ou se a proteção de imutabilidade não estiver habilitada. Quando passa, altera somente `status` e `ativado_em` em `ref.frequencia_nome_versao`; os milhões de registros de `ref.frequencia_nome` são preservados.

Se a versão canônica `CENSO2022_NOMES_BRASIL_V1` não existir, o reparo **não cria nem renomeia versões automaticamente**. Ele chama `local-diagnose-ibge-reference.ps1`, que mostra todas as versões existentes, status, SHA e quantidade de linhas por versão. Isso permite distinguir uma referência legada sob outro código de uma base realmente vazia antes de decidir por migração ou recarga.

### `local-load-ibge-reference.ps1`

Recuperação explícita da referência IBGE quando as tabelas existem, mas a versão canônica está ausente/incompleta. O script começa pelo `local-check-ibge-reference.ps1`; se a referência já estiver íntegra e `ATIVA`, sai sem carga. Quando a carga for necessária, exige autorização explícita:

```powershell
.\scripts\local-load-ibge-reference.ps1 -AllowLoad
```

Ele sobe somente o SQL existente, compila o serviço `jornada-reference-bootstrap` e executa `ENSURE_NAME_FREQUENCY_SNAPSHOT`. **Não chama `reset`, `clean`, `down -v` ou `local-db.ps1`**. Ao terminar, repete o quick check e só conclui se código, row count, SHA-256 e imutabilidade estiverem corretos. O total canônico esperado pelo `projection-manifest.json` é 5.603.287 linhas.

Se a imagem `jornada-node:test` já foi construída pelo mesmo SHA, `-NoBuild` evita recompilação:

```powershell
.\scripts\local-load-ibge-reference.ps1 -AllowLoad -NoBuild
```


### `local-test-df-calibration.ps1`

Teste dedicado da frente DF/benchmark. Por padrão começa pelo check read-only da referência já carregada e depois roda restore/build/testes direcionados:

```powershell
.\scripts\local-test-df-calibration.ps1 -Quick
```

Use `-Offline` somente quando quiser o teste puramente em memória, sem consultar o SQL local. Nenhum modo desse script carrega ou substitui a referência IBGE.

### `local-sql-runtime-smoke.ps1`

Executa smoke tests reais do caminho SQL. Use quando alterar DDL, stored procedures, views, índices, migrações ou comportamento que a análise estática não consegue provar.

### `local-ddl-upgrade.ps1`

Valida o caminho de atualização do banco a partir do baseline suportado até o DDL corrente e verifica invariantes. Use sempre que uma mudança de banco puder funcionar em instalação limpa, mas falhar em upgrade de uma instalação existente.

### Scripts `*-gate.py`

Arquivos como `architecture-dependency-gate.py`, `authorization-matrix-gate.py`, `compatibility-matrix-gate.py`, `contract-backward-compatibility-gate.py`, `bronze-*-evidence-gate.py` e demais `*-gate.py` são **gates especializados**.

Em geral, **não são a primeira escolha para execução manual**. Eles existem para verificar uma propriedade objetiva e normalmente são chamados por workflows ou pelos scripts agregadores. Execute um gate diretamente quando:

- estiver desenvolvendo/corrigindo exatamente a regra que ele valida;
- precisar reproduzir localmente uma falha do CI;
- quiser uma resposta rápida antes de rodar a suíte agregada.

Quando o gate ficar verde, ainda rode o agregador apropriado antes do merge.

## 3. Linkage e calibração

Use `-RunIntegration` quando precisar incluir a integração real, e não apenas o build/validações rápidas.

### `local-cluster.ps1 -Action calibrate`

Use para executar a calibração no ambiente local completo, quando a alteração envolve parâmetros/modelo de linkage.

### `local-cluster.ps1 -Action linkage`

Use para executar o linkage local deliberadamente. Não é necessário em toda mudança da aplicação.

### `local-cluster.ps1 -Action linkage-diagnose`

Use quando o objetivo for diagnóstico do linkage — por exemplo, investigar candidatos, conflitos, transitividade ou comportamento do modelo — sem tratar a execução normal como ferramenta de diagnóstico.

O diagnóstico corrente também separa, no corpus SCALE, a verdade em primeiro/segundo/empate/fora do top-2, a coorte sintética em que cada décimo nascimento é deliberadamente deslocado para fora do universo, a saturação dos posteriores e o limite superior aproximado de falso positivo pela regra do três quando nenhum FP é observado. Essas métricas são diagnósticas: não autorizam remover a trava de dois candidatos acima do limiar sem decisão explícita de política.

O contrato estrutural dessas métricas pode ser testado isoladamente com:

```powershell
python scripts/linkage-decision-quality-gate.py --self-test
```

## 4. Escala, resiliência e recuperação

### `local-scale.ps1`

Perfis disponíveis: `smoke`, `medium`, `million` e `custom`.

```powershell
./scripts/local-scale.ps1 -Profile smoke
./scripts/local-scale.ps1 -Profile medium
./scripts/local-scale.ps1 -Profile million
```

Use `smoke` para validar rapidamente o harness de escala. `medium` e principalmente `million` são ensaios deliberados: consomem mais tempo e recursos e devem ser usados quando a mudança ou o critério de aceite exigir evidência de escala.

### `local-backup-restore-drill.ps1`

Executa um exercício de backup/restauração. Use para validar recuperabilidade e mudanças que afetem persistência, backup ou procedimentos de DR. Não faz parte do loop normal de desenvolvimento.

## 5. Release, migração e compatibilidade

### `local-validate-release.ps1`

Valida localmente os artefatos/condições de release. Use no fechamento de uma versão ou ao reproduzir uma falha do gate de release.

### `generate-release-info.ps1`

Gera `RELEASE_INFO.txt` para uma versão informada.

```powershell
./scripts/generate-release-info.ps1 -Version <versao>
```

Use somente no processo de preparação de release; não atualize metadados de release como efeito colateral de desenvolvimento comum.

### `apply-migrations.sh`

Aplica migrações no ambiente para o qual foi configurado. É um script operacional: confirme explicitamente conexão/ambiente antes da execução. Não o use como substituto dos testes locais de upgrade.

### `fabric-sql-compatibility.ps1`

Valida compatibilidade SQL no contexto Fabric quando uma `ConnectionString` apropriada é fornecida.

```powershell
./scripts/fabric-sql-compatibility.ps1 -ConnectionString '<connection-string>'
```

Use apenas quando a mudança tocar o contrato/caminho cuja compatibilidade com Fabric precisa ser comprovada. O SQL Server operacional local continua sendo validado pelos gates próprios.

### `build-release-source-bundle.sh`

Monta o bundle de fontes de release. Use durante empacotamento/publicação, não para builds normais de desenvolvimento.

## 6. Fixtures, geração e manutenção de evidências

### `build-ingestion-fixture.py`

Gera fixture usada pelos testes de ingestão. Use ao criar/atualizar deliberadamente o cenário de teste correspondente; não regenere fixtures automaticamente apenas porque um teste falhou — primeiro determine se o contrato ou a fixture está incorreto.

### `consolidate-requirements.py`

Consolida requisitos a partir das fontes esperadas pelo projeto. Use quando a documentação/artefato consolidado precisar ser regenerado de forma rastreável, e não para edição manual de conteúdo derivado.

### `apply-testcontainers-integration.ps1`

Auxilia a aplicação/validação da integração baseada em Testcontainers.

```powershell
./scripts/apply-testcontainers-integration.ps1 -RepositoryRoot <caminho>
./scripts/apply-testcontainers-integration.ps1 -RepositoryRoot <caminho> -RunTests
```

É voltado à manutenção dessa integração; para simplesmente executar a suíte normal, prefira os scripts de teste locais.

## 7. Como escolher o script certo

Siga esta ordem:

1. **Estou desenvolvendo normalmente?** Use `local-cluster.ps1` para o ambiente e `local-test.ps1` para feedback rápido.
2. **Mudei banco/DDL?** Acrescente `local-sql-runtime-smoke.ps1` e `local-ddl-upgrade.ps1`.
3. **Mudei linkage/calibração?** Use as ações específicas de `local-cluster.ps1` e os gates do domínio afetado.
4. **Estou fechando a alteração para PR/merge?** Rode `local-test-all.ps1`.
5. **Estou investigando CI vermelho?** Reproduza primeiro o gate específico que falhou e, após corrigir, rode o agregador.
6. **Estou preparando release?** Só então use scripts de release/bundle/metadados.
7. **Preciso provar escala ou DR?** Use os harnesses correspondentes deliberadamente; eles não pertencem ao ciclo rápido.

## 8. Cuidados importantes

- **Diretório atual importa.** Os exemplos pressupõem `Solution` como diretório corrente.
- **Não use `clean` por reflexo.** Primeiro tente `status`, `logs`, `down` ou `reset`; limpeza destrutiva elimina evidência útil para diagnóstico.
- **Não confunda gate especializado com aceite completo.** Um `*-gate.py` verde prova uma propriedade; não prova o repositório inteiro.
- **Não confunda instalação limpa com upgrade.** Mudanças de DDL devem passar pelo caminho de upgrade.
- **Não use ensaios caros em toda iteração.** Escala, integração completa e DR devem ser executados quando o risco/alteração exigir e nos gates previstos.
- **Não rode scripts operacionais contra ambiente desconhecido.** Para migração, Fabric ou qualquer conexão externa, confira explicitamente o destino antes de executar.

## 9. Pré-requisitos usuais

Dependendo do script, podem ser necessários PowerShell, Docker/Docker Compose, .NET SDK, Python, `sqlcmd` e/ou Bash. O próprio ambiente/workflow do projeto é a referência de versão e configuração. Se um script falhar por dependência ausente, verifique primeiro o workflow que o chama e a documentação de instalação local antes de alterar o script.

---

Ao adicionar um novo script a este diretório, atualize este README indicando **finalidade**, **quando usar**, **como executar** e se ele é de **uso humano**, **agregador** ou **interno ao CI**.