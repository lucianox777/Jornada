# Jornada - desenvolvimento e teste local — base normativa v3.62 / engenharia v3.96

## Objetivo

A infraestrutura local mínima usa **SQL Server 2022 Developer em Docker**. A edição Developer é destinada exclusivamente a desenvolvimento/teste/demonstração e **não deve ser usada em Produção**. O container aceita a EULA por `ACCEPT_EULA=Y` e declara `MSSQL_PID=Developer` explicitamente.

Docker reproduz somente a dependência SQL necessária aos testes/integracão. API e workers continuam preferencialmente no host durante desenvolvimento, facilitando breakpoints no Visual Studio/Rider/CLI. Power BI Desktop permanece uma ferramenta Windows separada e não é containerizado.

## Pré-requisitos

- Docker Desktop ou Docker Engine com Compose v2;
- .NET SDK 8 para build/teste das aplicações;
- Python 3 para o gate OpenAPI e geração determinística da fixture E2E;
- PowerShell ou shell POSIX para os scripts fornecidos;
- Microsoft Power BI Desktop homologado quando houver alteração de PBIP/PBIR/TMDL.

## Primeiro uso

No diretório `Solution`:

```powershell
Copy-Item .env.example .env
# edite a senha local, se desejado
.\scripts\local-db.ps1 -Action up
```

ou:

```bash
cp .env.example .env
./scripts/local-db.sh up
```

O padrão expõe SQL Server em `localhost:14333`, cria `JornadaLocal` e aplica, nessa ordem:

1. `database/Jornada_Fase1.sql`;
2. `database/Jornada_Seed_Dev.sql`.

A massa é sintética. `.env` é ignorado pelo Git.

## Limpeza completa e validação Release — v3.96

Quando quiser reproduzir uma execução a partir do zero:

A validação canônica preserva a imagem SQL durante a limpeza, mas antes dos Integration verifica o digest fixado no `docker-compose.yml`, sobe um probe descartável, consulta a versão do engine e executa `DBCC CHECKDB(master)`. Se o próprio engine não iniciar, há uma única tentativa automática de remover a imagem (somente se não estiver em uso por outro container), fazer novo `docker pull` pelo mesmo digest e repetir o probe. Falha funcional de teste não aciona reinstalação do SQL.

```powershell
.\scripts\local-clean.ps1
.\scripts\local-validate-release.ps1
```

`local-clean.ps1` remove recursos Docker locais da Jornada, o volume SQL, `.env`, `.local`, `TestResults`, `bin/obj` e o Bronze local, mas **preserva a imagem Docker do SQL Server**. O cache `.vs` é tentado em best-effort: se Visual Studio/Copilot mantiver um arquivo aberto, o script avisa e continua porque esse cache não participa do restore/build/test. O validador seguinte executa restores explícitos em `--locked-mode` para a Solution e os dois projetos de teste antes de compilar.

`local-validate-release.ps1` não reutiliza `JORNADA_TEST_SQL_CONNECTION` residual do shell: a suíte Integration sobe um SQL Server descartável via Testcontainers e cria `JornadaIntegration_Test_<guid>`. O token `Test` é intencional e mantém os guards fail-closed da suíte ativos.

Se o ZIP tiver sido baixado da Internet e o Windows propagar Mark-of-the-Web aos `.ps1`, execute uma vez:

```powershell
Get-ChildItem .\scripts\*.ps1 | Unblock-File
```

## Testes

```powershell
.\scripts\local-test.ps1
```

ou:

```bash
./scripts/local-test.sh
```

O script sobe/atualiza o banco, define `JORNADA_TEST_SQL_CONNECTION`, executa restore/build com warnings como erro e roda a suíte NUnit completa, inclusive `Integration`.

## Comandos do banco local

```text
local-db ... up      sobe, aguarda healthcheck, cria banco e aplica DDL/seed
local-db ... reset   derruba/recria JornadaLocal e reaplica DDL/seed
local-db ... down    para containers preservando o volume SQL
local-db ... clean   para e remove também o volume local
local-db ... status  mostra o estado do Compose
```

`reset` e `clean` são destrutivos apenas para o ambiente local Docker.

## Conexão das aplicações

Não se versiona senha em `appsettings*.json`. Para rodar um componente contra o Docker local, use variável de ambiente, por exemplo no PowerShell:

```powershell
$env:ConnectionStrings__Jornada='Server=localhost,14333;Database=JornadaLocal;User Id=sa;Password=<senha-do-.env>;TrustServerCertificate=true;Encrypt=false'
dotnet run --project src/Jornada.Api
```

Cada processo pode ser executado separadamente. O ambiente local não introduz scheduler: run-once continua manual e o scheduler corporativo permanece responsabilidade de HML/Produção.

## Power BI Desktop

Os parâmetros versionados do PBIP usam `localhost,14333` / `JornadaLocal` como destino de desenvolvimento. Credenciais SQL ficam no estado local do Power BI Desktop (`localSettings`/credential store), que é ignorado pelo Git. Antes de qualquer release com alteração no BI, abrir, validar e salvar o PBIP no Power BI Desktop homologado.

## Limites

O ambiente local não pretende reproduzir IdP, ingress/TLS, secret store, scheduler, Gateway do Power BI, storage Bronze corporativo, backup/DR corporativo ou capacidade de Produção. Esses itens continuam em HML/Infraestrutura. A v3.55 inclui apenas um drill local controlado do procedimento SQL+Bronze.


## Harness técnicos

Após `local-db up`, consulte `Runbook_Testes_Tecnicos.md` para:

```text
local-scale              massa sintética + Parameters + Runner
local-fault-injection    perda deliberada da sessão do gate serial
local-backup-restore-drill backup SQL + Bronze e verificação pós-restore
local-ddl-upgrade         upgrade v3.57 -> DDL atual + fingerprint/idempotência
local-e2e                 HTTP -> Bronze -> Processor -> Gold/Serving -> HTTP + retransmissão
openapi-contract-gate.py  igualdade exata entre rotas do código e OpenAPI
Jornada.Linkage.Evaluation blocking V1/V2 + transportabilidade de m em amostra rotulada
linkage-evaluation-smoke.sh smoke CI com rótulos SCALE + invariância operacional
```

Os artefatos de evidência são gravados em `.local/` e permanecem fora do Git.


## Fechamento técnico v3.65

Execute `python3 scripts/technical-closure-gate.py` para verificar invariantes estáticos de atomicidade e conformidade telefônica. `local-test` já executa esse gate automaticamente antes da suíte .NET. O baseline imediato para `local-ddl-upgrade` é `database/baselines/Jornada_Fase1_v3.65.sql`, com seed congelado `database/baselines/Jornada_Seed_Dev_v3.65.sql`.


## Smoke semântico SQL v3.67

Execute `scripts/local-sql-runtime-smoke.sh` (ou `.ps1`) com o SQL Server local ativo. O script reaplica DDL/seed e roda `database/Jornada_Runtime_Smoke.sql`; `local-test` já o chama automaticamente.


## Fechamento local v3.68

Antes de build/test, execute `python3 scripts/technical-closure-gate.py` e `python3 scripts/openapi-contract-gate.py`. O gate técnico cobre telefone/e-mail V2, migração fail-closed, 51110–51119, ordem do 51114, supressão de falso conflito MULTI, readiness Base 3.62/Solution 3.68, SBOM por RELEASE_INFO e smoke SQL.

Para verificar um predecessor materializado, use `python3 scripts/predecessor-integrity-gate.py --artifact origem_engenharia_anterior_N=/caminho/pacote.zip`. Em promoção formal, acrescente `--require-all-predecessors`; a ausência de qualquer predecessor declarado bloqueia a promoção.


## Evidência externa v3.95 incorporada na v3.96

A execução real do fluxo acima concluiu com restore locked Solution/Unit/Integration PASS, build 0 warnings / 0 errors, Unit 153/153, digest da imagem SQL OK, ProductVersion 16.0.4265.3, DBCC CHECKDB(master) OK e Integration 58/58 PASS. O registro está em `../Documentos/Evidencia_Runtime_v3.95_2026-09-03.md` no pacote completo.
