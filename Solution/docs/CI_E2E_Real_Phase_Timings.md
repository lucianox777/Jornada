# E2E privado — instrumentação de fases reais (issue #940)

**12/10/2026.** O job C3.2d `e2e-private-sql` executa SQL Server real, APIs .NET reais, três workers independentes, Crash/SIGKILL, rollback SQL real, supervisor ON/OFF, RunOnce, abort de conexão e auditoria, sempre em **GitHub-hosted runner descartável** no banco `JornadaE2E`. Essas provas **não** se equivalem à suíte DEV completa executada no computador do usuário com `JornadaLocal`, NODE1/NODE2 e NAS.

Na PR #939, run `38212129117`, os demais gates finalizaram, mas C3.2d permaneceu em progresso muito além dos cinco minutos típicos. A ferramenta GitHub de logs parciais retornou BlobNotFound, e o passo monolítico não mostrava qual fase consumia tempo. Não cancela jobs, não reduz testes e não interpreta ausência de logs como reprovação.

## Alteração

`Solution/scripts/e2e-private-sql-bootstrap-runtime.sh` passa a emitir `PRIVATE E2E phase START`, `PASS` e `FAIL` com duração de cada uma das 13 fases reais, sem apresentar dados, SQL, senhas, variáveis de ambiente, argumentos ou conteúdo de testes. O arquivo `Solution/.local/e2e/c3-2d-sql-bootstrap/phase-timeline.jsonl` é incluído no **artefato já existente** `e2e-private-sql-evidence`. Um `trap EXIT` registra a fase em falha quando houver término normal/não-zero, mas não inventa diagnóstico se o runner for interrompido antes da finalização. Não há uploads ou permissões adicionais.

Fases observadas: bootstrap SQL, build/saúde das duas APIs, supervisor OFF, SIGKILL de workers, ingestão sintética, rollback/recovery SQL, console ON, toggle global, três RunOnce, desconexão TCP, stop individual, auditoria e resumo de aceite. Os comandos originais e a ordem dos testes permanecem intactos.

## Aceite do experimento

A CI na HEAD exata do PR deve passar `unit-contracts` (incluindo a prova Bash `bash -n` e o contrato com os 13 marcadores), `unit-runtime`, `security-analysis`, `deterministic-build`, `integration-sql` e **ambos os E2E reais**, com o agregador `e2e` exigindo as duas evidências. Comprovar no novo run `e2e-private-sql-evidence` e depois medir qual fase domina os custos; **não supor que esta instrumentação acelera os testes**. Projetar divisão adicional somente após identificar dependências reais entre banco, APIs, workers e volumes efêmeros. Se o job travar, deixar sua própria política de timeout atuar, sem cancelamento manual da CI.

O roteiro de testes DEV em Windows (compilação, IBGE, JornadaLocal, cluster, NAS, E2E separado e ensaio sintético de três ondas) **não pode ser executado por GitHub-hosted runners sem acesso à máquina e infraestrutura locais**. Não confundir resultados do CI com homologação do cluster DEV; nunca usar `JornadaLocal` em script de aceitação descartável.
