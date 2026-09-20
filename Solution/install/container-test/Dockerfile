FROM mcr.microsoft.com/dotnet/sdk:8.0.424 AS build
WORKDIR /src

# Mantém a resolução NuGet em uma camada independente do código-fonte. Assim,
# alterações em .cs/.sql/docs não invalidam o restore; a camada muda apenas
# quando metadados de projeto/locks mudam.
COPY Directory.Build.props ./
COPY src/Jornada.Api/Jornada.Api.csproj src/Jornada.Api/packages.lock.json src/Jornada.Api/
COPY src/Jornada.Resultado.Api/Jornada.Resultado.Api.csproj src/Jornada.Resultado.Api/packages.lock.json src/Jornada.Resultado.Api/
COPY src/Jornada.Processor.Worker/Jornada.Processor.Worker.csproj src/Jornada.Processor.Worker/packages.lock.json src/Jornada.Processor.Worker/
COPY src/Jornada.Operations.Maintenance.Worker/Jornada.Operations.Maintenance.Worker.csproj src/Jornada.Operations.Maintenance.Worker/packages.lock.json src/Jornada.Operations.Maintenance.Worker/
COPY src/Jornada.Bronze.Maintenance.Worker/Jornada.Bronze.Maintenance.Worker.csproj src/Jornada.Bronze.Maintenance.Worker/packages.lock.json src/Jornada.Bronze.Maintenance.Worker/
COPY src/Jornada.Linkage.Core/Jornada.Linkage.Core.csproj src/Jornada.Linkage.Core/packages.lock.json src/Jornada.Linkage.Core/
COPY src/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.csproj src/Jornada.Linkage.Parameters.Worker/packages.lock.json src/Jornada.Linkage.Parameters.Worker/
COPY src/Jornada.Linkage.Runner/Jornada.Linkage.Runner.csproj src/Jornada.Linkage.Runner/packages.lock.json src/Jornada.Linkage.Runner/
COPY src/Jornada.Bronze.Verify/Jornada.Bronze.Verify.csproj src/Jornada.Bronze.Verify/packages.lock.json src/Jornada.Bronze.Verify/
COPY src/Jornada.Linkage.Evaluation/Jornada.Linkage.Evaluation.csproj src/Jornada.Linkage.Evaluation/packages.lock.json src/Jornada.Linkage.Evaluation/
COPY src/Jornada.Linkage.Conference/Jornada.Linkage.Conference.csproj src/Jornada.Linkage.Conference/packages.lock.json src/Jornada.Linkage.Conference/
COPY clients/Jornada.Integrador.CSharp/Jornada.Integrador.CSharp.csproj clients/Jornada.Integrador.CSharp/packages.lock.json clients/Jornada.Integrador.CSharp/

# ProjectReferences transitivos dos artefatos publicados.
COPY src/Jornada.Contracts/Jornada.Contracts.csproj src/Jornada.Contracts/packages.lock.json src/Jornada.Contracts/
COPY src/Jornada.Operational.Sql/Jornada.Operational.Sql.csproj src/Jornada.Operational.Sql/packages.lock.json src/Jornada.Operational.Sql/
COPY src/Jornada.Pipeline.Coordination/Jornada.Pipeline.Coordination.csproj src/Jornada.Pipeline.Coordination/packages.lock.json src/Jornada.Pipeline.Coordination/
COPY src/Jornada.Bronze.Storage/Jornada.Bronze.Storage.csproj src/Jornada.Bronze.Storage/packages.lock.json src/Jornada.Bronze.Storage/
COPY src/Jornada.Ingestion/Jornada.Ingestion.csproj src/Jornada.Ingestion/packages.lock.json src/Jornada.Ingestion/

RUN dotnet restore src/Jornada.Api/Jornada.Api.csproj --locked-mode \
 && dotnet restore src/Jornada.Resultado.Api/Jornada.Resultado.Api.csproj --locked-mode \
 && dotnet restore src/Jornada.Processor.Worker/Jornada.Processor.Worker.csproj --locked-mode \
 && dotnet restore src/Jornada.Operations.Maintenance.Worker/Jornada.Operations.Maintenance.Worker.csproj --locked-mode \
 && dotnet restore src/Jornada.Bronze.Maintenance.Worker/Jornada.Bronze.Maintenance.Worker.csproj --locked-mode \
 && dotnet restore src/Jornada.Linkage.Core/Jornada.Linkage.Core.csproj --locked-mode \
 && dotnet restore src/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.csproj --locked-mode \
 && dotnet restore src/Jornada.Linkage.Runner/Jornada.Linkage.Runner.csproj --locked-mode \
 && dotnet restore src/Jornada.Bronze.Verify/Jornada.Bronze.Verify.csproj --locked-mode \
 && dotnet restore src/Jornada.Linkage.Evaluation/Jornada.Linkage.Evaluation.csproj --locked-mode \
 && dotnet restore src/Jornada.Linkage.Conference/Jornada.Linkage.Conference.csproj --locked-mode \
 && dotnet restore clients/Jornada.Integrador.CSharp/Jornada.Integrador.CSharp.csproj --locked-mode

COPY . .

RUN dotnet publish src/Jornada.Api/Jornada.Api.csproj -c Release --no-restore -o /out/apps/Jornada.Api \
 && dotnet publish src/Jornada.Resultado.Api/Jornada.Resultado.Api.csproj -c Release --no-restore -o /out/apps/Jornada.Resultado.Api \
 && dotnet publish src/Jornada.Processor.Worker/Jornada.Processor.Worker.csproj -c Release --no-restore -o /out/apps/Jornada.Processor.Worker \
 && dotnet publish src/Jornada.Operations.Maintenance.Worker/Jornada.Operations.Maintenance.Worker.csproj -c Release --no-restore -o /out/apps/Jornada.Operations.Maintenance.Worker \
 && dotnet publish src/Jornada.Bronze.Maintenance.Worker/Jornada.Bronze.Maintenance.Worker.csproj -c Release --no-restore -o /out/apps/Jornada.Bronze.Maintenance.Worker \
 && dotnet publish src/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.csproj -c Release --no-restore -o /out/apps/Jornada.Linkage.Parameters.Worker \
 && dotnet publish src/Jornada.Linkage.Runner/Jornada.Linkage.Runner.csproj -c Release --no-restore -o /out/apps/Jornada.Linkage.Runner \
 && dotnet publish src/Jornada.Bronze.Verify/Jornada.Bronze.Verify.csproj -c Release --no-restore -o /out/tools/Jornada.Bronze.Verify \
 && dotnet publish src/Jornada.Linkage.Evaluation/Jornada.Linkage.Evaluation.csproj -c Release --no-restore -o /out/tools/Jornada.Linkage.Evaluation \
 && dotnet publish src/Jornada.Linkage.Conference/Jornada.Linkage.Conference.csproj -c Release --no-restore -o /out/tools/Jornada.Linkage.Conference \
 && dotnet publish clients/Jornada.Integrador.CSharp/Jornada.Integrador.CSharp.csproj -c Release --no-restore -o /out/clients/Jornada.Integrador

FROM mcr.microsoft.com/dotnet/aspnet:8.0
RUN apt-get update \
 && apt-get install -y --no-install-recommends jq curl procps \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /opt/jornada
COPY --from=build /out/ /opt/jornada/
COPY config/ /opt/jornada/config/
COPY database/ /opt/jornada/database/
COPY install/container-test/entrypoint.sh /usr/local/bin/jornada-cluster-entrypoint

RUN chmod 0755 /usr/local/bin/jornada-cluster-entrypoint \
 && mkdir -p /data/bronze /data/staging /data/logs /data/node /etc/jornada

EXPOSE 5080 5081
ENTRYPOINT ["/usr/local/bin/jornada-cluster-entrypoint"]
