# V9 — revisão de qualidade: testes, blocking, calibrador e seletor (2026-10-14)

Status: recomendações técnicas e critérios de aceite; **não representam funcionalidades já implementadas**.

## Achados verificáveis no código
1. `v9-abc-paired-blocking.py` (PR #1053) usa passes fixos simplificados. Em particular D0/D1 são proxies, não a seleção adaptativa por frequência conjunta. B e C tampouco devem ser rotuladas equivalentes às estratégias produtivas até integrar TF/Rank/Resolve. Os resultados são apenas diagnósticos de recuperação.
2. O avaliador usa `particao`, `base_person_id` e `data_nascimento`, mas o planejador `v9-adaptive-frequency-planner.py` espera `split`, `truth_id` e `birth_date`. É necessária uma camada explícita e testada de tradução do esquema, sem recalcular fonética em Python.
3. O planejador recalcula frequências TRAIN a partir das linhas em cada execução. Deve consumir o snapshot imutável versionado, verificar hash e rejeitar frequência calculada com VALIDATION/TEST.
4. O planejador ordena passes por frequência crescente, aplica `max_passes=3` e sinaliza `over_cap_in_train`, mas ainda não usa ganho marginal real nem fanout SQL; selecionar passes raros pode sacrificar recall. Nenhum passe deve ser descartado sem fallback e medição.
5. O avaliador já conta M/U discordantes e E0 exclusivo, mas falta estimar variância por cluster, custo físico, fanout por consulta, abstenção e limites de FDR. A ausência de `M_eligible` deve gerar `NAO_AVALIAVEL`, não aprovação.
6. `v9-abc-paired-blocking.py` enumera pares dentro de células, que podem ser quadráticas; adicionar limites, instrumentação de memória e execução por lote/SQL antes de rodar corpus integral.

## Testes prioritários (falha fechada)
- Contratos de esquema: aliases explícitos, ausência/NULL/strings vazias, nomes acentuados, homônimos, múltiplas observações por identidade, nascimento inválido, CPF presente e gestores iguais.
- Testes de propriedade: nenhum par recuperado fora do universo elegível; `M_retrieved <= M_eligible`; `U_retrieved <= U_eligible`; E0 não reduz a união de candidatos; repetição com mesmo snapshot gera hashes/resultados idênticos.
- Paridade C#/SQL: quatro projeções canônicas, E0 e os passes físicos; evidenciar plano de índice, fanout e tempo. Nunca considerar equivalência comprovada apenas com execução Python.
- Testes de integração de 16 cenários (A0/A1/B0/B1/C0/C1/D0/D1 × E0 off/on) no mesmo corpus/split, mais teste de regressão de recall e custo; registrar artefatos na CI.
- Testes de falha: split vazado, hash alterado, frequência de TEST no TRAIN, número insuficiente de M, intervalo inconclusivo, zero candidatos, colapso por nomes comuns, indisponibilidade de SQL.
- Testes do calibrador: estimar m/u/TF apenas em TRAIN; selecionar limiares em VALIDATION; TEST apenas auditoria final. Usar UCB unilateral de FDR, abstenção explícita e gates por estrato/n_min. Nunca promover automaticamente.
- Testes do seletor: objetivo declarado (maximizar recall elegível sujeito a FDR/custo), desempates determinísticos, comparação pareada, penalidade de fanout e fallback seguro; não usar TEST para selecionar vencedor.

## Melhoria do desenho experimental
1. Congelar corpus, splits, hashes, projeções e TF individual/conjunta TRAIN por identidade distinta.
2. Separar **candidate recall** (blocking), **Rank/Resolve** (classificação) e **seleção/calibração** (política de erro); medir FDR sobre decisões, não confundir com fração U entre candidatos.
3. Avaliar ganho marginal de cada passe, especialmente E0 e fonética, com M/U exclusivos, deduplicação e custos SQL. Não tratar nome normalizado e fonético como evidências independentes.
4. Pré-registrar diferença mínima relevante, estratos e orçamento de custo. Estimar intervalos por bootstrap agrupado por identidade e sensibilidade a dependências entre pares; se insuficiente, resultado INCONCLUSIVO.
5. Exigir relatório de comparação com run IDs e denominadores antes de atribuir a alegada queda de 18%.

## Ordem de entrega
P0: corrigir contratos de esquema, testes e D adaptativo real; integrar snapshots e CI; executar comparação dos 16 cenários.
P1: instrumentar SQL, fanout, custos, intervalos agrupados; calibrador com gates fail-closed e seletor reproduzível.
P2: investigação de 18% com dois runs efetivamente comparáveis.

**Gates de promoção:** nenhuma escolha em TEST, nenhuma promoção baseada só em sintético, nenhum modelo sem suficiência por estrato, nenhum merge que substitua a estratégia real por proxy sem identificação explícita.
