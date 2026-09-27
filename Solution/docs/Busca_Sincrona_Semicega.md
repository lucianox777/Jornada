# Busca síncrona semicega — PR #532

## Contrato e fronteiras

`POST /api/v1/identidade/busca` recebe nome, nascimento opcional e nome da mãe opcional. Requer o escopo `jornada.identidade.busca.read`, com autenticação compartilhada DT-04 e política institucional. HML/Produção permanecem deny-by-default até a identidade corporativa.

A consulta utiliza o modelo ATIVO, o snapshot de ruleset, o loader de blocking e o ranking Fellegi–Sunter do Runner. Seleciona internamente até cinco candidatos, randomiza a ordem de apresentação com gerador criptográfico e retorna somente nome, nascimento, nome da mãe e identificador aleatório de opção. O identificador de opção NÃO é um token de confirmação e não pode ser usado para constituir ou publicar vínculos. A opção «Nenhum destes» é sempre válida, inclusive com lista vazia.

Sem nascimento, o blocking legado retorna lista vazia. Rulesets dinâmicos só executam quando existe passe elegível para a observação. O endpoint não inventa data, não degrada para varredura irrestrita e não altera o scorer.

## Pendências para retirar o rascunho

1. Executar build e testes unitários no Actions; não presumir aprovação.
2. Testar 401 e 403 na pipeline HTTP real; testar ausência de nascimento nos dois contratos de blocking.
3. Verificar integração SQL com 0/1/5 candidatos e modelo ATIVO, inclusive limite de fan-out.
4. Auditoria: o middleware existente registra rota, credencial e correlationId, mas atualmente captura e apenas loga falhas de persistência. Antes de liberar a busca, definir e testar fail-closed para auditoria obrigatória da consulta, inclusive em falha SQL, sem duplicar eventos.
5. Validar a especificação OpenAPI com os gates DT-03 e compatibilidade do contrato.
6. Definir protocolo governado de confirmação separado; o opcaoId desta fase é exclusivamente identificador de interface. Não persistir nem expor UUID na resposta.
7. Revisar a autorização por domínio institucional: a política atual valida escopo e código de recurso, mas a elegibilidade dos candidatos por instituição deve ser confirmada antes da exposição em produção.

Arquivos protegidos na solicitação não foram alterados.
