# GOV-V9-100K-RETENCAO-OOM-20261110 — preservação de evidência OOM em container efêmero

**Data:** 10/11/2026. **Origem:** continuidade da investigação de memória SQL/Docker aprovada pelo PO. **Política:** GOV-DOC-01. **Estado:** correção instrumental em PR.

## Contexto e problema
A PR #1400 implementou medições físicas no workflow 100k manual. O SQL é iniciado com `docker run --rm`; quando o container desaparece ao morrer, uma consulta `docker inspect ... State.OOMKilled` somente ao fim pode retornar indisponível. Isso impede detectar um sinal de OOM que foi observado durante a execução, gerando lacuna de evidência. Não há prova de OOM na campanha #42804299288.

## Decisão e justificativa
Consultar `State.OOMKilled` em cada amostra durante o fitting e preservar qualquer valor `true` observado; registrar `false` apenas quando houver pelo menos uma leitura válida sem `true`; senão `null`. Registrar a quantidade agregada de amostras válidas e manter picos de memória do SQL, RSS dos workers, RAM disponível e eventos cgroup. Testar o caso de desaparecimento do container e de manutenção do indicador.

## Precedência, impactos e limites
Complementa GOV-V9-100K-MEMORIA-FISICA-20261110 e PR #1400. A telemetria é leitura somente e não altera default nativo SQL, limite Docker, AUTO, 24/16/8, TF/M/U/FDR, TEST/Gold, autorização da campanha ou preflight #1253. O custo adicional de `docker inspect` é operacional e nenhum conteúdo bruto, argv, segredo ou dado pessoal será publicado. Sem OOM observado, pressão de memória ainda pode existir.

## Aceite
Testes de canário e CI no SHA exato antes do merge. Campanha manual 100k permanece condicionada à verificação de segurança e à ausência de duplicidade; auditoria e FDR continuam pendentes.
