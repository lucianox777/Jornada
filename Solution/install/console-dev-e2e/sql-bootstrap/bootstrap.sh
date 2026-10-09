#!/usr/bin/env bash
# Create-only, opt-in bootstrap of the SQL instance inside a disposable
# jornada-workers-e2e-* Compose project. Never target the host SQL Server.
set -euo pipefail

deny() { echo "E2E SQL bootstrap recusado: $*" >&2; exit 2; }
[[ "${JORNADA_WORKERS_E2E_SQL_BOOTSTRAP:-}" == "true" ]] ||
  deny "flag de bootstrap ausente"
[[ "${JORNADA_RUNTIME_MODE:-}" == "DEV" &&
   "${DOTNET_ENVIRONMENT:-}" == "Development" ]] ||
  deny "somente DEV"
[[ "${JORNADA_E2E_SQL_DATABASE:-}" == "JornadaE2E" &&
   "${JORNADA_SQL_DATABASE_OVERRIDE:-}" == "JornadaE2E" ]] ||
  deny "somente JornadaE2E"
[[ "${JORNADA_WORKERS_E2E_ID:-}" =~ ^[a-z0-9]{8,32}$ ]] ||
  deny "ID exclusivo do projeto E2E ausente/inválido"
[[ "${JORNADA_WORKERS_E2E_SQL_HOST:-}" == "sqlserver" ]] ||
  deny "servidor SQL deve ser o serviço privado 'sqlserver'"
[[ -n "${SQLCMDPASSWORD:-}" ]] || deny "segredo SQL do E2E ausente"
[[ "$#" == 0 || ( "$#" == 1 && "$1" == "--check" ) ]] ||
  deny "argumentos inválidos"
# Inert CI contract mode: validate requirements without connecting or writing.
if [[ "${1:-}" == "--check" ]]; then
  printf 'bootstrap=JornadaE2E;host=sqlserver;mode=create-only\n'
  exit 0
fi

cd /workspace
sql() {
  /opt/mssql-tools18/bin/sqlcmd -S sqlserver,1433 -U sa -C -b -I -l 10 "$@"
}
# Never alter/reset an existing database: only a pristine, private instance.
existing="$(sql -d master -W -h -1 -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name=N'JornadaE2E';" | tr -d '[:space:]\r')"
[[ "$existing" == "0" ]] ||
  deny "banco JornadaE2E já existe; não reaplicar seed nem resetar"

sql -d master -Q "CREATE DATABASE [JornadaE2E];"
sql -d JornadaE2E -i database/Jornada_Fase1_v3.70.sql
sql -d JornadaE2E -i database/Jornada_Seed_Dev.sql
sql -d JornadaE2E -i database/Jornada_Seed_Dev_UniquePayloads.sql
# Preserve the canonical local DEV bootstrap order and migration guards.
sql -d JornadaE2E -i database/migrations/20260907_Cpf_Ancora.sql
sql -d JornadaE2E -i database/migrations/20260910_Schema_Consolidation_370.sql
sql -d JornadaE2E -i database/migrations/20260922_Processor_Lease_Heartbeat_Isolation.sql
sql -d JornadaE2E -Q "EXEC sys.sp_addextendedproperty @name=N'Jornada.EnvironmentProfile',@value=N'Development';"

# A SQL write succeeded only if the profile marker and core tables exist.
sql -d JornadaE2E -Q "IF OBJECT_ID(N'ingestao.entrega',N'U') IS NULL OR OBJECT_ID(N'silver.pessoa_observacao',N'U') IS NULL THROW 51591,N'Bootstrap E2E incompleto.',1;"
echo 'JORNADA WORKERS E2E SQL BOOTSTRAP: SUCCESS (private instance, create-only)'
