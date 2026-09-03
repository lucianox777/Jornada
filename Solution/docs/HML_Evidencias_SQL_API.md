# Evidências HML — SQL e consulta em lote

## SQL Server

`database/Jornada_HML_SQL_Performance_Evidence.sql` coleta apenas métricas agregadas: estado do Query Store, requisições bloqueadas, waits de lock e contador de deadlocks. Não retorna texto SQL, parâmetros nem identificadores de pessoa.

O wrapper `scripts/local-sql-performance-evidence.sh` coleta duas amostras separadas por uma janela (`JORNADA_SQL_PERFORMANCE_WINDOW_SECONDS`) e calcula deltas. Credenciais são lidas das variáveis `SQLCMD_SERVER`, `SQLCMD_USER` e `SQLCMDPASSWORD`; a senha não é persistida na evidência.

`sql-performance-evidence-gate.py` valida a estrutura e, quando `config/hml/sql-performance-policy.json` estiver `APROVADO`, aplica os limites homologados.

## Consulta de Pessoa em lote

`scripts/api-projection-load-harness.py` exercita `POST /api/v1/pessoas/consulta` com lotes de 1, 10, 100 e 1000 UUIDs autorizados. O arquivo de UUIDs é somente entrada. A saída persiste apenas:

- SHA-256 do arquivo de entrada;
- cardinalidade do lote;
- status HTTP;
- contagem de itens retornados;
- latências e P95.

UUIDs, payloads e corpos de resposta não são gravados na evidência. A chave de acesso vem exclusivamente de `JORNADA_HML_ACCESS_KEY`.

`api-projection-evidence-gate.py` exige sucesso e cardinalidade correta nas quatro cargas. Limites de P95 só são aplicados depois que `config/hml/api-projection-load-policy.json` for aprovado.
