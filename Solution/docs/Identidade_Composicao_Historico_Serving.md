# Identidade — continuidade histórica publicada

A view `serving.v_identidade_composicao_historico_publicado` expõe, para auditoria e consumo derivado, apenas histórico de composição que foi efetivamente aplicado e posteriormente publicado.

Cada linha representa a participação de `member_uuid` em uma `reference_uuid` histórica, vinculada ao `decision_id`, à data do registro aplicado, à data de publicação e à versão de publicação.

## Regra de segurança

A projeção não calcula nem escolhe o sucessor atual de um UUID histórico. Em especial, após uma separação, o mesmo membro ou referência histórica pode ter relações válidas em momentos diferentes; consumidores devem preservar essa multiplicidade. A existência de uma única linha não autoriza inferir identidade civil, elegibilidade ou atribuição factual.

Somente decisões presentes em `identidade.composicao_publicacao` com `state='PUBLICADA'` aparecem na view. Planos apenas `PREPARADA` e aplicações ainda não publicadas permanecem invisíveis nessa superfície.

A projeção é read-only, não altera `pessoa_origem_progressiva`, âncoras CPF, `vinculo_fonte`, Gold, Serving factual ou Possibilidades e não ativa Linkage probabilístico. A issue #31 continua sendo o gate estatístico e institucional para qualquer ativação probabilística real.

## Paridade

Há implementação equivalente em SQL Server e PostgreSQL. O conteúdo de `members_json` é convertido para UUID de forma fail-closed: histórico persistido incompatível torna a leitura inválida em vez de produzir um sucessor parcial ou silenciosamente descartado.
