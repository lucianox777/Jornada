# Dedicated DEV-only SQL bootstrap image. No user host mounts or local cluster.
FROM mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04@sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89

WORKDIR /workspace
# Copy only the SQL scripts required by the canonical DEV bootstrap.
# Never copy user data, backups, IBGE source files or the entire repository.
COPY database/Jornada_Fase1.sql database/Jornada_Fase1_v3.70.sql database/Jornada_Identidade_Progressiva.sql /workspace/database/
COPY database/Jornada_Seed_Dev.sql database/Jornada_Seed_Dev_UniquePayloads.sql /workspace/database/
COPY database/migrations/ /workspace/database/migrations/
COPY --chmod=0755 install/console-dev-e2e/sql-bootstrap/bootstrap.sh /usr/local/bin/jornada-e2e-sql-bootstrap

ENTRYPOINT ["/usr/local/bin/jornada-e2e-sql-bootstrap"]
