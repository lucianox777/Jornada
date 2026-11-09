FROM mcr.microsoft.com/dotnet/runtime-deps:10.0

# GH #1359: archive.ubuntu.com:80 indisponível durante o bootstrap NAS.
# Exigir pacotes samba/smbclient normais, via TLS e retries finitos; sem relaxar checks.
RUN find /etc/apt -type f \( -name '*.list' -o -name '*.sources' \) -exec sed -i \
      -e 's|http://archive.ubuntu.com/ubuntu|https://archive.ubuntu.com/ubuntu|g' \
      -e 's|http://security.ubuntu.com/ubuntu|https://security.ubuntu.com/ubuntu|g' {} + \
 && apt-get -o Acquire::Retries=3 -o Acquire::https::Timeout=20 update \
 && apt-get -o Acquire::Retries=3 -o Acquire::https::Timeout=20 install -y --no-install-recommends samba smbclient \
 && rm -rf /var/lib/apt/lists/* \
 && mkdir -p /srv/jornada/bronze \
 && chmod 0777 /srv/jornada/bronze

COPY install/container-test/nas/smb.conf /etc/samba/smb.conf

EXPOSE 445
HEALTHCHECK --interval=10s --timeout=5s --retries=20 --start-period=5s \
  CMD smbclient -L //127.0.0.1 -N -p 445 >/dev/null 2>&1 || exit 1

CMD ["smbd", "--foreground", "--no-process-group", "--debug-stdout"]
