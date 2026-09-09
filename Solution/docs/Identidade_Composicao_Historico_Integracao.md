# Integração do histórico aplicado — auditoria de fronteira

## Evidência no código

O planejador `IdentityCompositionPlanner.ResolveHistorical` já resolve um snapshot de membros contra uma leitura corrente, sem escolher sucessor arbitrário. O armazenamento da PR #52 grava `composicao_historico_aplicado` com chave `(decision_id,reference_uuid)`, JSON canônico e hash. O recibo APLICADA é separado do plano PREPARADA.

Entretanto, `IdentityCompositionAuthoritativeReader.LoadClosedReadSetAsync` ainda retorna History vazio e contém comentário de que o armazenamento efetivado não existe. Esse comentário ficou obsoleto após a PR #52. A próxima integração deve carregar exclusivamente histórico efetivamente aplicado, validar conteúdo e incluir os membros históricos necessários no fechamento. Não basta trocar o array vazio por uma consulta: o planejador exige que todos os UUIDs históricos estejam no readset e a expansão precisa provar a completude.

## Decisões de implementação

1. Não alterar a semântica do resolvedor puro para compensar leitura incompleta. Ausência de linha, histórico vazio e falha de leitura são situações diferentes.
2. O leitor histórico deve conferir `members_hash` contra a serialização canônica, rejeitar membros vazios/duplicados e validar a associação entre decisão, referência e recibo APLICADA. O hash detecta divergência de conteúdo, não autentica a origem.
3. A ordem histórica deve ser explícita e determinística. `registrado_em` sozinho não é chave de ordenação suficiente para decisões concorrentes. Definir versão/ordem de publicação antes de oferecer consulta temporal.
4. O contrato atual `ResolveHistorical` não implementa consulta as-of: recebe snapshot anterior e estado corrente. Não anunciar suporte temporal até existir reconstrução por eventos/versões e prova de fechamento.
5. Uma referência com descendentes múltiplos retorna AMBIGUA; membros sem destino impedem UNIVOCA. O resultado não deve ser usado como autorização automática para reatribuir fatos.
6. O replay de APLICADA deve validar novamente o conteúdo canônico do PREPARADA e as reservas, além dos hashes e contagens do recibo. Isso não reexecuta a decisão nem exige que o estado corrente continue igual ao anterior.
7. Nenhuma dessas correções depende de treinamento, score, CPF em claro, Processor ou Gerador de Parâmetros.

## Próxima prova

Adicionar testes de leitura de histórico aplicado nos dois providers: histórico legítimo, ausência, payload/hash divergente, membro ausente, duplicidade, replay e rollback. Testar separações sucessivas e referências históricas que reaparecem como destino, sem reciclagem. Só depois conectar o resolvedor a uma API de consulta. O plano de recomposição Gold/Serving permanece separado e exige inventário dos writers/leitores reais.
