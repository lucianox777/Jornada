# DT-11 — runner único para scripts Bash e PowerShell

O entrypoint comum é `python3 Solution/scripts/jornada-runner.py`. Ele delega aos scripts especializados existentes, que permanecem disponíveis, mas fixa previamente o banco de cada perfil. Em Windows usa PowerShell; em Linux/macOS, Bash. `--shell` permite selecionar explicitamente uma das duas implementações.

| Perfil | Banco obrigatório | Comandos |
| --- | --- | --- |
| `local` | `JornadaLocal` | `db up`, `db status`, `db backfill`, `test`, `cluster-status`, `db reset`, `ddl-upgrade` |
| `e2e` | `JornadaE2E` | `db up`, `db status`, `db reset`, `e2e` |
| `synthetic` | `JornadaSyntheticDev` | `db up`, `db status`, `db reset`, `diagnose`, `synthetic` |

Exemplos, executados da raiz do repositório:

```bash
python3 Solution/scripts/jornada-runner.py --profile local db status
python3 Solution/scripts/jornada-runner.py --profile local test
python3 Solution/scripts/jornada-runner.py --profile e2e --dry-run e2e
python3 Solution/scripts/jornada-runner.py --profile e2e --allow-reset e2e
python3 Solution/scripts/jornada-runner.py --profile synthetic diagnose
python3 Solution/scripts/jornada-runner.py --profile synthetic --allow-reset synthetic
python3 Solution/scripts/jornada-runner.py --profile local --allow-reset ddl-upgrade
```

**Sem reset implícito entre perfis:** `e2e`, `synthetic`, `db reset` e `ddl-upgrade` exigem `--allow-reset` porque o especialista reseta ou limpa dados no **próprio** banco de teste. O runner jamais autoriza E2E no banco Local, nem sintético em E2E/Local. `down` e `clean` não estão disponíveis pelo runner porque afetam containers ou volumes compartilhados entre perfis; para manutenção deliberada continuam existindo nos especialistas. Os scripts Bash de E2E e calibração agora também fixam, por padrão, `JornadaE2E` e `JornadaSyntheticDev`, mesmo quando `.env` declara `JornadaLocal`.

**Configuração:** prepare previamente `Solution/.env` com credenciais locais seguras; o runner não imprime nem copia senhas. Em Bash, `JORNADA_SQL_DATABASE_OVERRIDE` fixa o destino após a leitura do arquivo `.env`, com suporte a `JORNADA_LOCAL_ENV_FILE` explícito. Em PowerShell, `local-db.ps1` recebe `-DatabaseName` explícito, `local-e2e.ps1` usa o isolamento já existente e o perfil sintético exige `Solution/.env.synthetic.local` (ou `JORNADA_LOCAL_ENV_FILE`) contendo `JORNADA_SQL_DATABASE=JornadaSyntheticDev`; esse arquivo deve permanecer fora do Git. O teste PowerShell do perfil Local recusa configuração apontando para outro banco.

**Escopo e concorrência:** os três bancos compartilham o mesmo SQL Server/container, mas não são apagados um pelo outro. Não execute simultaneamente dois harnesses destrutivos **sobre o mesmo perfil**; o runner não implanta locks distribuídos. `ddl-upgrade` usa seu banco descartável `JornadaDdlUpgrade`. A suíte preservadora mais ampla `local-test-all.ps1` e o `from-zero` destrutivo continuam especialistas explícitos, fora das ações automáticas deste runner.

**Teste sem Docker/SQL:** `python3 -m unittest discover -s Solution/scripts/tests -p test_jornada_runner.py -v`. Para executar E2E ou calibração reais, usar os respectivos bancos isolados e aprovação explícita de reset.
