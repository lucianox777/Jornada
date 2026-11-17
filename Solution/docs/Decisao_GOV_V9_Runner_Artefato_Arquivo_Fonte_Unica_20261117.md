# GOV-V9-RUNNER-ARTEFATO-ARQUIVO-FONTE-UNICA-20261117

**Autoridade:** Product Owner, 17/11/2026. **Estado:** arquitetura aprovada; implementação pendente. **Política:** GOV-DOC-01.

## Contexto
O Runner atual seleciona a versão na configuração, mas carrega parâmetros do modelo SQL tradicional. O V9 já dispõe de artefato operacional validado `<sha>.operational.json` e leitor com verificação de integridade; a execução produtiva V9 ainda não está demonstrada. O PO requer troca/rollback manual com Runner parado, sem converter a escolha em atualização de parâmetros científicos no SQL.

## Decisão aprovada
Para o seletor operacional V9, o arquivo de configuração aponta **explicitamente** o artefato operacional imutável. O Runner, ao iniciar, lê exclusivamente esse artefato, verifica identidade, SHA e compatibilidade, congela os parâmetros em memória durante toda a execução e falha fechado em caso de ausência, corrupção ou incompatibilidade. Não há busca automática, fallback ao modelo SQL, hot reload, nem substituição silenciosa. O Operador Master pode publicar/alterar a referência ao arquivo com Runner parado; a mesma referência pode ser trocada manualmente, com rastreabilidade e rollback. O arquivo científico não é sobrescrito.

**Separação de responsabilidades:** SQL continua necessário para dados operacionais, candidatos, identidades, decisões e eventual auditoria administrativa. **Não** é etapa intermediária para gravar, reconstruir ou recarregar parâmetros científicos do artefato V9. Registrar no SQL metadados de execução (identificador/SHA/caminho/horário), se necessário, não altera a fonte de verdade do modelo.

## Precedência e limites
Esta decisão **substitui, exclusivamente para o carregamento do modelo V9**, a origem SQL definida em GOV-RUNNER-STARTUP-MODELO-OPERADOR-MASTER-20261117; preserva suas garantias de seleção antes da partida, imutabilidade por execução e falha fechada. Preservar REPLAY histórico conforme DT-05, sem redirecionamento implícito. Não presumir equivalência produtiva: validar contrato completo, blocking, coorte, regras e paridade E2E antes de ativar. Nenhum TRAIN/TEST científico ou publicação autorizados por esta decisão. Definir posteriormente layout de configuração, distribuição atômica, permissões e rollback auditável.

## Rastreabilidade
PO aprovou em conversa de 17/11/2026 arquitetura de artefatos imutáveis, referência manual na configuração e congelamento até encerrar; esclarecimento: não inserir parâmetros do artefato no SQL. Implementação depende de PR própria, CI e validação operacional.
