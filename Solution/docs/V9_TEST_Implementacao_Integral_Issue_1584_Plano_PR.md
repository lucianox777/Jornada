# Implementação do TEST científico V9 — issue #1584

**Estado:** implementação técnica na [PR #1822](https://github.com/lucianox777/Jornada/pull/1822). A PR #1820 reconciliou os contratos na master, no squash [ccd7bf06f250](https://github.com/lucianox777/Jornada/commit/ccd7bf06f25041c5de10c4b7876e6908307fae23).

| Etapa | Controle | Evidência |
| --- | --- | --- |
| VALIDATION | Exporta o arquivo operacional imutável e declara o SHA do único vencedor no próprio run. | Contrato V3, TF4/81 e SHA do arquivo verificados no handoff. |
| Preflight TEST | SQL local descartável com TLS obrigatório, certificado efêmero fixado, credencial própria, marcador de inicialização e linha imutável TEST_ONLY. | Preflight C# rejeita formato, conteúdo, projeção, ambiente e marcador incompatíveis. O loader V9 confere os mesmos bytes do arquivo. |
| Reserva e coorte | O workflow reserva UUIDv4/seed globalmente antes de gerar a coorte. O recibo vincula tag, run, tentativa, código e vencedor. | Registros de SHA do manifesto e observações. TRAIN e VALIDATION permanecem isolados da coorte do TEST. |
| Avaliação | Executa uma configuração, com a projeção e os parâmetros congelados. Usa o kernel V9 compartilhado em SQL próprio. | Recibo agregado com métricas, SHA do grafo e das decisões por observação, sem registros individuais. |
| CLOSEOUT | Vencedor obriga TEST. Ausência de vencedor encerra sem preparo nem reserva. | Recibo com o SHA do vencedor e verificação da integridade do relatório. Ausência ou falha de TEST fica explícita. |
| Rotas legadas | Guardas do TEST histórico preservados. O antigo diagnóstico de banco apontado pelo usuário é recusado antes do TRAIN. | Regressões de admissão. O workflow padrão mantém a referência estatística externa do PREPARE bloqueada até a #1626. |

A correção inicial da rota legada está registrada em [1807915](https://github.com/lucianox777/Jornada/commit/1807915). O bloqueio `V9_LEGACY_EXTERNAL_TEST_ROUTE_DISABLED_PREPARE_ONLY` foi movido para a admissão, antes de provisionar SQL ou iniciar TRAIN.

## Verificações e rastreabilidade

- [CI da PR #1822](https://github.com/lucianox777/Jornada/pull/1822/checks). A suíte rápida verifica o fluxo e os casos negativos, incluindo evidência adulterada e ausência de vencedor.
- O workflow `v9-operational-model-sql-smoke.yml` executa a CLI científica em modo diagnóstico com fixture de quatro observações e compara o artefato carregado com o loader V9 em banco efêmero. O smoke de `RunAsync` continua como evidência separada de regressão do Runner.
- [Decisão de isolamento](Decisao_GOV_V9_TEST_Isolamento_Retrocompat_Liberacao_20261117.md), [relatório independente](Decisao_GOV_V9_TEST_Independente_Relatorio_20261119.md) e [índice de decisões vigentes](Indice_Decisoes_Vigentes.md) registram os critérios e a precedência (GOV-DOC-01).

Os checks técnicos cobrem implementação, isolamento e compatibilidade do contrato. Validação científica da campanha e certificação integral do pipeline produtivo exigem evidência própria. A campanha 100k e a abertura da coorte científica de uso único exigem autorização específica.

O [check de segurança inicial](https://github.com/lucianox777/Jornada/actions/runs/44880060797/job/136773347610) identificou a conexão do ensaio sem validação do certificado. A correção usa TLS obrigatório com o certificado gerado para o contêiner descartável e valida a conexão antes de materializar o vencedor.

O [primeiro ensaio físico da CLI](https://github.com/lucianox777/Jornada/actions/runs/44880060779/job/136773168407) confirmou o preflight e revelou que o caminho diagnóstico de duas repetições passava pelo validador do P95 científico. O caminho diagnóstico agora mede o fixture sem calcular P95. O protocolo do workflow padrão continua com 20 repetições.
