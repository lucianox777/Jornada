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

## Checkpoint P0-A — verificação complementar de 19/11/2026

- **Plano sincronizado na master:** PR #1742 integrada, commit `68ed67cc2610ca8fc8be895ab2ea3f202b9ff48e`. No HEAD da PR, workflow obrigatório `jornada-pr-required-fast` #44588905500 concluiu `success`, com jobs `security-guards`, `unit-runtime` e `deterministic-build` aprovados. **Esse resultado é de PR, não comprovação de CI completa pós-merge da master.**
- **Evidência física existente:** workflow #44581595305, job `compare-historical-and-current` aprovado; decisão GOV-V9-P0-1734 documenta igualdade byte a byte de três arquivos científicos com seed 42/30k e divergência de metadados. Não generalizar para todas as seeds ou configurações.
- **Gate histórico inspecionado:** `Solution/scripts/v9-tf-frozen-transfer-lock-gate.py` exige SHA da árvore atual do gerador igual ao SHA LOCKED histórico e contrato de partição original. Esse gate deve continuar aplicável a replay da fonte congelada; seu uso como gate genérico de código beta evolutivo requer reconciliação com a decisão GOV-V9-BETA-CAMPANHAS-HISTORICAS-CI-20261119. **Não afrouxar a verificação de replay LOCKED.**
- **DT-10:** commit histórico `4ac707d4844e7d415c120769ef0ba6bd078208e2` documenta testes e job `dt10-evidence` seletivo, mas **não comprova a validação da correção recente #1706**. Conferir PR, SHA e run específicos antes de marcar como validada.
- **Limitação de observabilidade:** a consulta de runs por commit disponível retorna apenas eventos de PR; para o merge SHA da master retornou lista vazia. Isso **não significa ausência ou sucesso** dos workflows de push. É necessário verificar diretamente os runs pós-merge, jobs e branch protection/rulesets.

**Próxima ação única:** obter evidência da CI de push da master no SHA `68ed67c`, verificar gates obrigatórios/rulesets e a PR #1706; então atualizar #1734 com a conclusão estritamente suportada. Até lá, **P0-A permanece aberto**, sem TRAIN novo ou TEST one-use.

## Checkpoint de retomada P0-A → DT-10 — 20/11/2026 (em PR #1757, não integrado)

**Autoridade e ordem:** executar o Plano de Desenvolvimento vigente: P0-A (#1734), validação específica DT-10 (#1706), P0-B V9; P1/P2 posteriores. Esta anotação é evidência operacional, não nova autorização científica nem encerramento de P0-A.

**Estado revalidado:** master HEAD `1734ee976dfb8940689bd4ba9480b4e5f6c7d9cb`; PR #1721 integrada pelo commit `008bdb183adbe8c4935c730a99a80ef3bf02ef01`; PR #1706 integrada pelo commit `0d150c536ae518f5a1029e25e664e0d63cd23511`. A execução de push `jornada-ci` #44654406155 teve 21 jobs, 16 success, 5 skipped e nenhum failure; o job `dt10-evidence / dt10-evidence` consta success. A evidência do job geral **não demonstra isoladamente** que a correção específica #1706 foi validada contra seu critério de aceite. A issue #1734 permanece aberta e seus critérios de aceite não estão assinalados como concluídos.

**Lacunas antes de fechar P0-A:** (1) evidência independente da proteção de branch/rulesets e da obrigatoriedade dos gates de integridade; (2) confirmação de que a execução #44654406155 corresponde ao HEAD e evento de push da master, incluindo logs/artefatos dos gates pertinentes; (3) reconciliação do gerador atual com os manifestos LOCKED sem regenerá-los; (4) distinguir checks da PR de provas pós-merge. **Não considerar P0-A concluída** com base em CI global verde.

**Próxima ação executável:** inspecionar checks/rulesets e proveniência LOCKED da P0-A; registrar conclusão com evidências em #1734. Em seguida, conferir diff, testes, jobs e artefatos especificamente atribuíveis à PR #1706 e atualizar o estado DT-10. Só depois retomar P0-B. As PRs de observabilidade #1757–#1759 são auxiliares e não substituem essa ordem. Checkpoint deve ser reconciliado com o HEAD atual em toda nova sessão.
