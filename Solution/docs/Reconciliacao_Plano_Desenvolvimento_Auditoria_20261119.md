# Reconciliação do plano de desenvolvimento e da avaliação técnica — 19/11/2026

**Natureza:** avaliação técnica e proposta de ordenação; **não constitui aprovação de novas mudanças pelo PO**. Fonte de prioridade: [Plano de Desenvolvimento](Plano_Desenvolvimento.md). Fontes de estado: [Índice de decisões vigentes](Indice_Decisoes_Vigentes.md), [Estado atual](Estado_Atual_Projeto.md), [Dívidas técnicas](Dividas_Tecnicas.md), PRs/issues e evidências da master. Política GOV-DOC-01.

## Por que este registro existe
O plano existente já se declara **ponto único de priorização**, mas seu topo contém fotografia de outubro e o corpo mistura plano histórico, decisões supervenientes e entregas. A avaliação externa de 19/11/2026 (master `531bb6a`) identificou problemas de arquitetura, testes, OpenAPI e documentação; **não compilou nem executou testes .NET e não verificou Actions**. O inventário consultado em 19/11 retornou 23 PRs e 65 issues abertas, que não equivalem a 88 pendências independentes. Este documento reconcilia as críticas com o plano sem substituir as decisões vigentes.

## Fila única recomendada (sem criar bloqueios novos)
| Ordem proposta | Frente e referências | Próxima evidência necessária | Limite/precedência |
| --- | --- | --- | --- |
| P0-A | Integridade/proveniência do gerador LOCKED, #1734; divergência seed-43 relatada na auditoria | Diff entre fonte congelada e gerador atual; resultados de gates físicos; classificação dos artefatos afetados | Preservar LOCKs e manifestos; não declarar equivalência sem prova; campanhas históricas não se invalidam automaticamente |
| P0-B | Cadeia científica V9, #1089, #999, #1478, #1488, #1584, #1575 | Verificar na master o pacote imutável, SHA/fingerprint, coortes independentes, preflight, gates, fluxo de zero finalistas e TEST sem refit; mapear PRs substituídas | TEST científico não é ativação operacional; operador Master autoriza produção; não exigir certificação produtiva ampla como bloqueio automático do TEST |
| P1-A | Testes Python: 16 falhas/41 arquivos fora de workflows, números da avaliação anexa | Reproduzir falhas no SHA vigente e classificar obrigatório/manual/histórico/obsoleto | Não apagar gate do gerador LOCKED nem confundir teste textual frágil com erro de algoritmo |
| P1-B | DT-07 / modelo físico: contagens relatadas 80/99/110 | Inventário comparável por schema/tabela/migração e critérios de inclusão; verificar se DT-07 necessita complemento | Divergência é indício, não prova de 30 tabelas ausentes |
| P1-C | OpenAPI e Resultado.Api | Inventariar endpoints públicos/internos e testes bidirecionais; executar testes HTTP de comportamento | Rotas internas podem ter exclusão documentada; não inferir ausência de cobertura apenas por nome de arquivo |
| P1-D | PRs/issues e documentação vigente, #404 | Para cada item: decisão vigente, diff na master, CI, evidência, status e ação; corrigir especificação/estado atual sem apagar histórico | Não encerrar PR apenas por idade; #1429 preservada até prova de equivalência; #1576 sujeita à decisão posterior de TEST obrigatório |
| P2-A | Pesquisa misturada ao produto; tipos de parâmetros; Core/Runner | Inventariar dependências e propor extração incremental, testes de equivalência e schema tipado | Não refatorar durante gates científicos críticos; preservar retrocompatibilidade V8/legado |
| P2-B | Workflows, testes textuais e documentação extensa | Classificar gates permanentes vs campanhas sob demanda; simplificar documentos normativos sem perder rastreabilidade | Não remover evidência congelada ou obrigação GOV-DOC-01 |

## Divergências entre a auditoria e o plano
1. **“Ligar V9 ao runtime” não é prioridade imediata de ativação.** O plano e decisões supervenientes distinguem calibração, TEST independente, compatibilidade do executor e autorização do operador Master. O bloqueio fail-closed é intencional; falta provar o caminho científico e os requisitos de habilitação, não remover o bloqueio.
2. **“Apagar V5–V7” exige inventário de replay e compatibilidade.** O plano contém intenção histórica de retirada segura, mas a decisão posterior exige retrocompatibilidade permanente do executor. Remover somente caminhos comprovadamente dispensáveis.
3. **“Apagar testes órfãos” é prematuro.** Classificar e reproduzir; preservar o teste de proveniência até fechar #1734.
4. **“Unificar especificação” não significa apagar decisões.** Manter especificação normativa, estado atual curto, decisões e evidências históricas em camadas separadas.
5. **DT-22 (#861) permanece aberta e postergada**; não a transformar em impedimento da cadeia científica.
6. **HML, identidade corporativa e decisões institucionais** estão fora do escopo desta fila de engenharia, sem dispensa dos seus gates próprios.

## Controle de execução
A cada mudança: (a) selecionar **uma** frente desta fila; (b) confirmar estado da master, PR, issue e decisões posteriores; (c) explicitar aceite mensurável e regressão; (d) executar/registrar testes no SHA; (e) integrar com checks exigidos; (f) atualizar este documento, o Plano, a issue, as Dívidas e o Índice **somente se houver nova decisão aprovada**. Não declarar `CONCLUÍDO` por CI isolada, por texto de documentação ou por PR aberta. Não iniciar uma frente P2 enquanto P0 exigir atenção, salvo correção pequena e independente aprovada.

## Situação desta reconciliação
Registro documental proposto em PR; **nenhuma implementação, correção de testes, alteração de prioridades pelo PO ou ativação foi realizada**. Os números e achados do relatório de 19/11 exigem verificação independente no SHA atual antes de virar dívida confirmada.
