# Scripts da Solution

Este diretório reúne scripts de **operação local**, **validação/CI**, **testes de integração e resiliência**, **migração/release** e **ferramentas auxiliares** da Jornada.

O objetivo deste README é responder duas perguntas antes de executar qualquer arquivo daqui:

1. **Quando devo usar este script?**
2. **Como devo executá-lo?**

> Execute os comandos abaixo a partir de `Solution`, salvo indicação em contrário. Em PowerShell, isso significa estar em `...\Jornada\Solution` antes de chamar `./scripts/...`.

> **Rastreabilidade PowerShell:** scripts operacionais `.ps1` deste diretório devem deixar o comando legível no próprio arquivo como comentário e imprimir `# <comando>` imediatamente antes da execução. Segredos nunca entram nessa linha; use `<redacted>`.

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

### 4. Recriar o banco local conhecido

```powershell
.\scripts\local-db.ps1 -Action reset
```

A partir daqui os testes passam a depender do SQL Server local. O `reset` elimina estado residual e cria uma base conhecida para continuar a validação.

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

### 7. Rodar E2E

```powershell
.\scripts\local-e2e.ps1
```

Exercita o caminho HTTP → Bronze → Silver → Gold → Serving → HTTP.

### 8. Rodar fault injection

```powershell
.\scripts\local-fault-injection.ps1
```

Valida comportamento de resiliência e o gate serial diante das falhas previstas pelo harness.

### 9. Recriar o cluster e validar linkage/calibração

```powershell
.\scripts\local-cluster.ps1 -Action clean
.\scripts\local-cluster.ps1 -Action up
.\scripts\local-cluster.ps1 -Action calibrate
.\scripts\local-ibge-u-bootstrap.ps1
.\scripts\local-cluster.ps1 -Action linkage
.\scripts\local-cluster.ps1 -Action linkage-diagnose
.\scripts\local-linkage-validation.ps1
```

Use esta sequência para provar o pipeline de resolução de identidade sobre um cluster recriado. Se o objetivo for apenas desenvolvimento cotidiano, `clean` não deve ser usado por reflexo; aqui ele é deliberado porque estamos executando uma validação completa e controlada.

`local-ibge-u-bootstrap.ps1` é **read-only**: calcula a referência populacional sintética de `U_NOME_*` a partir das marginais IBGE já internalizadas e, quando há modelo calibrado ATIVO, mostra a diferença para o `u` operacional. Ele não cria nem ativa modelo e não altera thresholds. `local-linkage-validation.ps1` usa o corpus independente DEV com positivos, impostores e probes de conflito; seus números não constituem homologação HML/produção.

### 10. Rodar o smoke de escala

```powershell
.\scripts\local-scale.ps1 -Profile smoke
```

O perfil `smoke` valida o harness de escala com custo menor que `medium` ou `million`. Perfis maiores só devem ser executados quando a alteração ou o critério de aceite exigir evidência adicional de escala.

### 11. Restaurar o banco canônico

```powershell
.\scripts\local-db.ps1 -Action reset
```

O ensaio de escala usa dados próprios. O reset antes do fechamento deixa o ambiente novamente em estado canônico conhecido.

### 12. Fechar com a suíte completa

```powershell
.\scripts\local-test-all.ps1 -Suite full
```

A suíte completa é o aceite local final. Ela não substitui a utilidade das etapas anteriores: quando executadas uma a uma, elas mostram com precisão onde surgiu a primeira falha.

Considere a validação local concluída somente quando o fechamento terminar com:

```text
LOCAL TEST ALL: OK
```

### Regra prática

Durante desenvolvimento, pare no primeiro comando que falhar, corrija a causa e repita a etapa. Antes de considerar uma alteração pronta para PR/merge, percorra a sequência aplicável e finalize com `local-test-all.ps1 -Suite full`.

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
| Rodar a suíte local principal | `local-test-all.ps1` | Antes de abrir/atualizar PR ou quando se quer reproduzir os gates localmente |
| Rodar validação local específica | `local-test.ps1` | Iteração rápida durante desenvolvimento |
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

Use este script quando não precisar do cluster completo. `backfill` é destinado aos cenários que exercitam explicitamente a recomposição/backfill local; não é necessário para simplesmente subir o banco.

### `local-clean.ps1`

Limpa artefatos do ambiente local. É uma ferramenta de recuperação/manutenção, não um passo obrigatório antes de cada teste. Antes de usá-la, prefira os comandos `down` ou `reset` dos scripts de ambiente quando eles forem suficientes.

## 2. Testes e gates locais

### `local-test-all.ps1`

É o comando recomendado para a validação local ampla.

```powershell
./scripts/local-test-all.ps1
```

Use antes de considerar uma alteração pronta para merge, especialmente quando ela atravessa mais de uma camada. O script executa a validação em contexto isolado para evitar que alterações locais não relacionadas contaminem a evidência do SHA testado.

Quando estiver apenas iterando em uma correção pequena, rode primeiro o teste/gate específico e deixe `local-test-all.ps1` para o fechamento.

### `local-test.ps1`

Entrada de teste mais focada/rápida. Use durante o ciclo editar → testar → corrigir. Não substitui a suíte completa quando a mudança estiver pronta para integração.

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

### `postgresql-linkage-local-build.ps1`

Compila/valida localmente o caminho PostgreSQL do linkage.

```powershell
./scripts/postgresql-linkage-local-build.ps1
./scripts/postgresql-linkage-local-build.ps1 -RunIntegration
```

Use `-RunIntegration` quando precisar incluir a integração real, e não apenas o build/validações rápidas.

### `local-cluster.ps1 -Action calibrate`

Use para executar a calibração no ambiente local completo, quando a alteração envolve parâmetros/modelo de linkage.

### `local-cluster.ps1 -Action linkage`

Use para executar o linkage local deliberadamente. Não é necessário em toda mudança da aplicação.

### `local-cluster.ps1 -Action linkage-diagnose`

Use quando o objetivo for diagnóstico do linkage — por exemplo, investigar candidatos, conflitos, transitividade ou comportamento do modelo — sem tratar a execução normal como ferramenta de diagnóstico.

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