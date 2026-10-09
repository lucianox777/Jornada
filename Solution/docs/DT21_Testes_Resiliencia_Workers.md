# DT-21 — aceite de resiliência e gates dos controles da Console DEV

**Decisão técnica aprovada em 08/10/2026.** Estado: **PENDENTE**, evidência
obrigatória antes de declarar conclusão.

## Princípios

A falha de um único executável não deve derrubar API/Resultado nem os
outros workers; quando a supervisão é ATIVADA o processo deve
reiniciar e seu trabalho deve poder ser recuperado. Quando
DESATIVADA o processo deve permanecer parado após `Parar processo`,
enquanto novos dados podem continuar chegando à API (se ela estiver
disponível). Recuperação não equivale à Trilha 4: nesta DT somente
recuperação normal de lotes **não concluídos**, leases expirados e
trabalhos próprios de cada worker. Reprocessamento automático de
RESOLVIDOS está proibido.

## Matriz mínima de aceite

1. **RunOnce:** retorno 0 apenas após ciclo concluído; código de
   incompletude/falha mantido; processo fecha e **não reinicia** por
   ter supervisão ativada.
2. **Start concorrente:** requisições simultâneas resultam em uma
   única instância contínua, inclusive durante reinício da Console.
3. **Supervisão desativada:** `Parar processo` mata apenas o PID
   selecionado; ele não reinicia sozinho; API e outros workers
   preservam disponibilidade; `Iniciar contínuo` volta a operar.
4. **Supervisão ativada:** `Parar processo` mata o PID;
   ele retorna com PID diferente sem restart dos demais;
   contagem de reinícios/status coerentes.
5. **Processor em lote ativo:** SIGKILL interrompe em ponto
   controlado do E2E; heartbeat/lease, fencing, reaquisição e
   idempotência são observados; registro confirmado não é duplicado,
   lote não fica bloqueado permanentemente.
6. **Outros workers:** Operations Maintenance e Bronze Maintenance
   respondem aos quatro controles e toggle; ensaiar interrupção e
   retorno; não assumir que têm `lotes` iguais aos do Processor.
7. **Falha da Console:** reiniciar a própria interface não cria
   outra instância nem perde observabilidade do estado do supervisor.
8. **Segurança:** DEV-only, worker allowlisted, recusa
   `JornadaLocal`/HML/PROD/volumes compartilhados, falhas sem efeitos
   colaterais, e confirmação antes de `Parar processo`.

## Ordem e merges

1. C3.1: entrypoint individual protegido, sem processos iniciados.
2. C3.2: supervisão isolada em Docker descartável, com toggle e
   proteção de estado efetivo; manter cluster padrão intacto.
3. C3.3: backend DEV de quatro ações mais toggle, fail-closed.
4. C3.4: interface Chromium e E2E real de resiliência dos 3 workers.

Cada PR deve ser pequena, com evidência de CI da HEAD exata e todos
gates **obrigatórios** verdes antes do squash merge. Itens pulados
não contam como testes executados. O monitoramento horário dos PRs
não autoriza bypass, timeout falso nem merge prematuro.

**Fora de escopo:** Trilha 4, qualquer reset de JornadaLocal,
uso de IBGE original ou promoção para HML/PROD.
