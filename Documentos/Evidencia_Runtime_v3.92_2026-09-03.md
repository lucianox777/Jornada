# Evidência externa de runtime — v3.92 — 2026-09-03

Esta evidência foi produzida no ambiente Windows do operador, não no ambiente de empacotamento.

## Ambiente observado

- Visual Studio 2022 Developer PowerShell 17.14.37
- .NET SDK 8.0.424
- Docker Engine acessível

## Resultado

- `scripts/local-clean.ps1`: concluiu; cache `.vs`/`CodeChunks.db` bloqueado foi reportado como aviso best-effort e não interrompeu a limpeza.
- restore `Jornada.sln --locked-mode`: PASS.
- restore locked explícito `Jornada.Tests`: PASS.
- restore locked explícito `Jornada.Integration.Tests`: PASS.
- build Release da Solution: PASS, 0 warnings / 0 errors.
- build explícito Unit: PASS, 0 warnings / 0 errors.
- Unit: 153 PASS / 0 FAIL / 0 SKIP.
- build explícito Integration: PASS, 0 warnings / 0 errors.
- Integration: 29 PASS / 29 FAIL / 0 SKIP, total 58.

## Sintoma dominante

Todas as 29 falhas registraram SQL Server 547 ao aplicar/reaplicar `Jornada_Fase1.sql`:

`The ALTER TABLE statement conflicted with the CHECK constraint "ck_vinculo_metodo" ... identidade.vinculo_fonte ... metodo_resolucao.`

O banco isolado usou o padrão `JornadaIntegration_Test_<guid>`.

## Diagnóstico incorporado à v3.93

O bloco de compatibilidade anterior ao bloco evolutivo v3.44 recriava `ck_vinculo_metodo`/`ck_vinculo_modelo` com domínio antigo. Em reentrada sobre banco já evoluído contendo `CONFLITO_GOVERNADO`, a constraint antiga falhava antes de o script alcançar o bloco posterior que restauraria o domínio final. A v3.93 corrige esse rebaixamento temporário no DDL corrente.

Esta evidência não declara Integration PASS para v3.92 nem para v3.93.
