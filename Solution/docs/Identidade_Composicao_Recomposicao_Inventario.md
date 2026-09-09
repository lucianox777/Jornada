# Inventário de recomposição da composição de identidade

Estado: inventário técnico e contrato da próxima fatia da issue #36. Independente do Processor, do Gerador de Parâmetros e da homologação probabilística da issue #31. Esta alteração não publica Gold/Serving.

## Estado pós-aplicação estrutural

A composição `APLICADA` altera somente o estado progressivo estrutural governado e registra histórico/recibo. Ela não altera `identidade.vinculo_fonte`, `identity_map`, CPF âncora, fatos Gold ou `serving.registro_integrado`. Portanto, uma mudança estrutural não pode ser convertida implicitamente em reatribuição factual.

## Writers reais encontrados

### Ingestão/materialização operacional

`SqlProcessorRepository.Materialization.cs` materializa fatos correntes em `gold.beneficio_concedido`, `gold.servico_prestado` e `serving.registro_integrado`. Gold e Serving são atualizados na mesma transação do processamento do registro. As linhas carregam `registro_observacao_id`, `registro_origem_id`, `pessoa_origem_id`, `pessoa_uuid` anulável e `estado_atribuicao_identidade`.

O mesmo writer encerra a versão vigente anterior de Gold e Serving quando há alteração/retificação. Esse caminho é writer de ingestão; não deve ser reutilizado como executor de composição estrutural nem acionado artificialmente para simular nova entrega da origem.

### Correção factual governada SQL Server

A base principal contém `identidade.sp_sincronizar_atribuicao_fatos`, cuja fonte é o vínculo corrente e cuja finalidade é atualizar a atribuição derivada sem alterar o sujeito declarado histórico. O fluxo de correção governada também usa `identidade.sp_recompor_gold_pessoa` para reconstruir a projeção de Pessoa a partir das observações/vínculos correntes.

Essas procedures já possuem gates de atomicidade/execução SQL, mas não constituem, por si só, uma fronteira versionada de publicação de uma decisão de composição. A composição nova não deve chamá-las até que o escopo factual afetado, a regra de autorização e a atomicidade com o recibo de publicação estejam definidos.

### PostgreSQL

O provider PostgreSQL possui persistência de `gold.beneficio_concedido`, `gold.servico_prestado` e Serving pelo caminho operacional, mas a superfície histórica de correção governada da base principal não deve ser presumida equivalente à de SQL Server. A próxima implementação persistente precisa provar paridade real ou declarar explicitamente uma fronteira por provider; não é permitido anunciar paridade apenas por existir tabela homóloga.

## Leitores

`serving.registro_integrado` é a projeção física comum usada pelas views `serving.v_*`; as views de BI contam Pessoa apenas quando a atribuição está no estado adequado. `gold.pessoa` é uma projeção derivada da identidade/vínculos correntes e é consultada por superfícies operacionais e de saúde da API.

Uma troca de `pessoa_uuid` em fatos vigentes pode alterar contagens, joins e autorização por pessoa. Logo, a publicação não pode ocorrer linha a linha sem uma fronteira que impeça visibilidade parcial aos leitores.

## Chaves de fechamento do escopo

A ligação segura para inventariar impacto é:

`initial_uuid -> pessoa_origem_id -> registro_observacao_id -> linhas Gold/Serving`

O adapter futuro deve provar, sob a mesma unidade de consistência da publicação, que carregou exatamente uma `pessoa_origem_id` para cada `initial_uuid` alterado e todos os `registro_observacao_id` correspondentes. Ausência de fatos é um resultado válido somente quando a consulta está comprovadamente completa; ausência de prova é falha.

O novo `IdentityCompositionRecompositionPlanner` recebe esse escopo já fechado e produz somente o conjunto determinístico afetado: UUIDs iniciais, referências antes/depois, pessoas de origem e registros de observação. Ele não contém destino factual e não escreve banco. Assim, o contrato impede que a nova referência estrutural seja tratada como autorização tácita de reatribuição.

## Decisão para a próxima fatia persistente

Antes de qualquer writer de publicação, implementar um adapter de leitura do impacto que:

1. confirme que `decision_id` possui recibo `APLICADA` íntegro;
2. carregue todas as origens alteradas pelo plano aplicado;
3. feche os fatos por `pessoa_origem_id`/`registro_observacao_id` nos dois providers onde a persistência for equivalente;
4. compare o plano de impacto calculado com conteúdo persistido/versionado antes de escrever;
5. rejeite mudança concorrente, escopo incompleto ou atribuição factual não autorizada.

A publicação factual deverá ser uma etapa distinta, com recibo próprio, replay e versão de publicação. O primeiro writer não deve reaproveitar `decision_id` como se `APLICADA` já significasse `PUBLICADA`.

## Fora de escopo desta fatia

Nenhuma recomposição factual é executada. Nenhuma linha de Gold/Serving é atualizada. Não há endpoint, worker, cron, ativação probabilística, mudança de modelo/parâmetros ou alteração no Processor/Gerador. Consulta temporal `as-of` permanece separada e deverá usar histórico aplicado/versionado, não inferência sobre o estado corrente.
