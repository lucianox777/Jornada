FROM mcr.microsoft.com/dotnet/sdk:8.0.424 AS build
WORKDIR /src
COPY . .

RUN dotnet restore Jornada.sln --locked-mode

RUN dotnet publish src/Jornada.Api/Jornada.Api.csproj -c Release --no-restore -o /out/apps/Jornada.Api \
 && dotnet publish src/Jornada.Resultado.Api/Jornada.Resultado.Api.csproj -c Release --no-restore -o /out/apps/Jornada.Resultado.Api \
 && dotnet publish src/Jornada.Processor.Worker/Jornada.Processor.Worker.csproj -c Release --no-restore -o /out/apps/Jornada.Processor.Worker \
 && dotnet publish src/Jornada.Operations.Maintenance.Worker/Jornada.Operations.Maintenance.Worker.csproj -c Release --no-restore -o /out/apps/Jornada.Operations.Maintenance.Worker \
 && dotnet publish src/Jornada.Bronze.Maintenance.Worker/Jornada.Bronze.Maintenance.Worker.csproj -c Release --no-restore -o /out/apps/Jornada.Bronze.Maintenance.Worker \
 && dotnet publish src/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.csproj -c Release --no-restore -o /out/apps/Jornada.Linkage.Parameters.Worker \
 && dotnet publish src/Jornada.Linkage.Runner/Jornada.Linkage.Runner.csproj -c Release --no-restore -o /out/apps/Jornada.Linkage.Runner \
 && dotnet publish clients/Jornada.Integrador.CSharp/Jornada.Integrador.CSharp.csproj -c Release --no-restore -o /out/clients/Jornada.Integrador

FROM mcr.microsoft.com/dotnet/aspnet:8.0
RUN apt-get update \
 && apt-get install -y --no-install-recommends jq curl \
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
