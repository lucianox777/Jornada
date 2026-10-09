# C3.2f2 — Contrato de evidência de recuperação de lote após SIGKILL

**Estado:** especificação preparatória, **não executada**. Este documento não
constitui aceite operacional DT-21 ou prova de processamento recuperado.

## Pré-condições de isolamento

A prova precisa ocorrer exclusivamente no GitHub-hosted runner efêmero, depois
do aceite de SQL create-only (C3.2d), APIs (C3.2e) e restart de processo
(C3.2f1), no projeto `jornada-workers-e2e-ci<run><attempt>` já conferido por
labels. Somente banco sintético `JornadaE2E`, rede Compose privada, credencial
distinta e volumes criados pelo próprio projeto. Nunca `JornadaLocal`, IBGE
original, volumes/containers existentes, Compose NODE canônico ou HML/PROD.
Não usar comandos globais de `prune`, `down -v`, `DROP DATABASE` ou reset.

O teste deve reutilizar a imagem construída no **mesmo E2E existente**. Não
criar um segundo build dotnet nem job completo para a recuperação.

## Sequência verificável

1. Produzir **uma entrega realmente processável** a partir de ZIP sintético e
   manifest/Pessoa/Registro válidos, enviado à **API privada**. Conferir, por
   consulta SQL, `entrega_id`, `lote_id`, estado inicial, hashes e chave de
   idempotência. Nunca fabricar entrega `PROCESSADA` diretamente.
2. Garantir que o Processor residente está em execução no mesmo projeto,
   e observar que ele **reservou o lote real** com
   `status IN ('VALIDANDO','PROCESSANDO')` e `lease_id`, `lease_owner`,
   `tentativa_count` e linha pareada de
   `ingestao.lote_heartbeat`. O token precisa ser guardado como
   **evidência anterior ao SIGKILL**.
3. Para testar rollback de transação de persistência, o SIGKILL precisa
   acontecer **durante uma transação real aberta**, comprovado por barreira
   determinística no executor isolado; apenas matar PID em polling ocioso
   **não demonstra rollback nem recuperação de lote**. O mecanismo dessa
   barreira deverá ser limitado ao runner CI e desabilitado fora dele.
4. Encerrar exclusivamente o PID 1 **do contêiner Processor identificado
   por labels exatas**; observar `RestartCount`, novo PID e novo
   `instance_id` de heartbeat (evidência de C3.2f1). Sem afetar SQL,
   APIs, ResultadoApi ou os dois outros workers.
5. O lease antigo expira sem falsificar a recuperação: o Processor vivo
   invoca `RecoverExpiredLeasesAsync` em inicialização e periodicamente.
   No SQL, o vencimento efetivo é
   `COALESCE(h.lease_expira_em, l.lease_expira_em)` somente quando
   `h.lease_id=l.lease_id`. A recuperação retira a linha heartbeat do
   token antigo, limpa o lease, incrementa `recuperacao_count` e volta a
   `PENDENTE`, ou marca `POISON` ao atingir limite de tentativas.
6. Reprocessar a entrega pelo Processor e comprovar transição terminal
   `PROCESSADO`/`PROCESSADA` com `tentativa_count` incrementado,
   `recuperacao_count >= 1`, lease final nulo, completude real e
   **cardinalidade sem duplicação** em Silver, `ingestao.item_processado`,
   Gold e Serving, usando **chaves de negócio próprias do ZIP**.
7. Submeter novamente o **mesmo ZIP/chave de idempotência** e provar que
   não cria segunda Entrega, segundo Lote ou segunda materialização.
   Identidade de PID nova isoladamente é insuficiente.
8. Produzir evidências de antes, falha, lease expirado, fencing, retomada,
   contagens por chave e hashes do payload em JSON/TSV com critérios
   binários de aprovação. Falha em qualquer critério deve resultar em
   exit status não-zero; logs sensíveis/secret nunca devem ser publicados.

## Asserções contra falso positivo

- A linha `ingestao.lote_heartbeat` só vale quando pertence ao **mesmo
  `lease_id`**; vencimento do snapshot `ingestao.lote.lease_expira_em`
  sozinho não comprova lease expirado.
- Nenhum lote criado ou colocado artificialmente em `PROCESSADO` pode
  satisfazer a prova. A janela SIGKILL/rollback deve ser observável, não
  inferida de `recuperacao_count`.
- `COUNT(*)` global nas tabelas não comprova ausência de duplicação:
  contar por `entrega_id`, `lote_id`, `codigo_pessoa_origem`,
  `codigo_registro_origem` e chave sintética de idempotência.
- Não alterar `Processor:LeaseDurationSeconds` ou transações fora do
  Compose isolado; caso reduza prazos no E2E privado, registrá-los na
  evidência e manter a política de fencing.
- `controle.runtime_componente` indica apenas vida de processo;
  `ingestao.lote`, `ingestao.lote_heartbeat`, `ingestao.item_processado`
  e tabelas de negócio são a fonte para comprovar **recuperação de trabalho**.

## Referências de implementação a integrar

- `src/Jornada.Processor.Worker/SqlProcessorRepository.Reservation.cs`:
  `ReserveNextAsync`, `HeartbeatAsync` e `RecoverExpiredLeasesAsync`,
  inclusive remoção de heartbeat e fencing do lease.
- `src/Jornada.Processor.Worker/ProcessorWorker.cs`: recuperação inicial
  e varredura periódica após reinício do worker.
- `docs/Processor_Lease_Heartbeat_Isolation.md`: separação do heartbeat
  de `ingestao.lote` durante a transação Serializable.
- `tests/Jornada.Integration.Tests/Integration/ProcessorRepositoryTests.cs`:
  testes de recuperação/retries/commit atômico existentes, **não substituem**
  a prova E2E de kill com processamento real.
- `scripts/e2e-private-worker-restart-runtime.sh` (C3.2f1):
  identidade Compose, restart, PID e heartbeat já verificáveis.

**Bloqueio até implementação:** falta uma barreira determinística de
interrupção de **transação real** e um ZIP sintético com contagens finais
assertivas que rodem no Compose privado. Não reduzir a exigência a
simulação SQL ou ao reinício do processo.

## Contrato de verificação offline (preparatório)

O validador **somente de leitura**
`scripts/e2e-lot-recovery-evidence-gate.py` rejeita qualquer pacote de
evidência sem a sequência coerente de estados, lease/token novo, fencing,
rollback confirmado, `recuperacao_count` e `tentativa_count` crescentes,
contagens de Silver/itens conforme ZIP, consistência de Gold/Serving,
ausência de chaves duplicadas e replay idempotente. O conjunto de
testes `scripts/test-e2e-lot-recovery-evidence-gate.py` gera uma
**amostra sintética em memória**, cobrindo casos positivos e negativos
sem usar SQL/Docker. Esses testes são apenas validadores de estrutura.

**Atenção:** um JSON autodeclarado `origin=live_sql_in_ephemeral_ci`
não comprova sua própria procedência. Apenas coletor operacional
independente, em job E2E da HEAD exata, com consultas reais
`JornadaE2E`, barreira determinística e logs do GitHub Actions,
poderá transformar esse contrato em prova de recuperação real. O
validador não está integrado ao job pesado, nem houve tentativa de
matar processos ou recuperar lotes nesta branch.

**Limitação atual de injeção:** C3.2f1 (#847) demonstrou que
`kill -KILL 1` executado dentro do namespace PID do próprio
contêiner não provoca necessariamente a morte do processo PID 1.
O primeiro E2E dessa PR falhou em `no automatic restart after SIGKILL:
processor`. A variante de injeção com sinal privilegiado no PID host
não passou pelo controle de segurança da integração; não contornar
esse bloqueio. Até existir forma aprovada de interrupção efetiva,
o requisito SIGKILL→restart e esta prova C3.2f2 permanecem pendentes.
