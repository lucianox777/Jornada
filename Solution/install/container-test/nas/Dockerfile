FROM mcr.microsoft.com/dotnet/runtime-deps:8.0

RUN apt-get update \
 && apt-get install -y --no-install-recommends samba smbclient \
 && rm -rf /var/lib/apt/lists/* \
 && mkdir -p /srv/jornada/bronze \
 && chmod 0777 /srv/jornada/bronze

COPY install/container-test/nas/smb.conf /etc/samba/smb.conf

EXPOSE 445
HEALTHCHECK --interval=10s --timeout=5s --retries=20 --start-period=5s \
  CMD smbclient -L //127.0.0.1 -N -p 445 >/dev/null 2>&1 || exit 1

CMD ["smbd", "--foreground", "--no-process-group", "--debug-stdout"]
