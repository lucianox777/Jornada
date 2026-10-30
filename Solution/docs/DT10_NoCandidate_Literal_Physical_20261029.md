# DT-10 — prova SQL física do motivo SEM_CANDIDATO (29/10/2026)

## Escopo e estado

A PR #1210 instala uma migration aditiva que redefine as procedures escalar e lote. O padrão SEM[_]CANDIDATO[_]% interpreta os underscores literalmente. O erro SQL 51810 rejeita motivos incompatíveis; 51835 rejeita a ausência alegada com melhor/segundo candidato persistido. A guarda do Runner precede a atualização da decisão.

Implementado não significa fisicamente comprovado. A evidência obrigatória é o resultado real da suíte LinkageNoCandidateLiteralGuardsSqlServerTests, sem skips, no SHA exato. Não registrar SUCCESS até haver run verde e TRX.

## Matriz executável

- SEMXCANDIDATO_ e SEM_CANDIDATOX: erro 51810, escalar e lote.
- Motivo legítimo SEM_CANDIDATO_NO_RULESET_BLOCKING com melhor ou segundo candidato persistido: erro 51835, escalar e lote.
- Lote misto: uma origem legítima e outra contraditória: erro 51835, zero eventos para ambas, estados e versões inalterados.
- Ausência legítima: promoção exclusiva do initial_uuid, um evento e replay idempotente, escalar e lote.

A fixture de integração usa Testcontainers descartável ou banco Test/Dev explicitamente isolado, instala schema canônico e seed sintético. A massa do teste negativo é persistida antes da tentativa de publicação; depois de XACT_ABORT, a transação é revertida e uma nova conexão reconsulta o estado. O positivo é revertido ao final para não contaminar outras fixtures.

A constraint ck_linkage_resultado_resolvido impede pessoa_uuid_resolvido não nulo quando status=NAO_RESOLVIDO. Não desabilitar a constraint para fabricar um cenário impossível; a guarda adicional dessa coluna permanece coberta estruturalmente.

## Compatibilidade #1199

A migration governada 20261026_DT10_V9_Lower_Governed_Off.sql da #1199 deve ser instalada DEPOIS de 20261029_Linkage_NoCandidate_Literal_Guards.sql da #1210, preservando autorização DT-15, digest, padrão literal e erro 51835 na procedure de lote. Essa é ordem lógica, não ordem lexical pela data. Não editar migrations históricas, ativar V9 ou alterar HML/PROD.

Antes do merge: instalação limpa e upgrade, manifesto, DDL flattened e CANDIDATE_INFO (hash de manifesto e fingerprint estrutural medidos). Esta alteração de testes e documentação não modifica DDL. CI, prova SQL e probe 30k devem ter SUCCESS no HEAD exato; SKIPPED, CANCELLED e NOT_REQUIRED_FOR_THIS_DIFF não são prova física.

## Manutenção

Qualquer asserção falha mantém a PR aberta. Registrar erro SQL, SHA, run/job e TRX. A prova #1210 não substitui os testes DT-15 positivos, loader REF, TEST independente e aprovação institucional da #1199.