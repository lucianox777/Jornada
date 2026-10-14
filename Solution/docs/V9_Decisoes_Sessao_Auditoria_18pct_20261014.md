# V9 — decisões e auditoria da alegação de 18% (2026-10-14)

## Decisões da sessão
- Normalização é obrigatória nos nomes da pessoa e da mãe. Projeções fonéticas PT-BR são obrigatoriamente **geradas, persistidas e indexáveis**, mas sua utilização nos passes é uma dimensão experimental **ligada/desligada** para medir ganho marginal; desligar o uso fonético em um braço não dispensa gerar as projeções.
- Quatro campos derivados: nome_normalizado, nome_fonetico, nome_mae_normalizado, nome_mae_fonetico. Preservar os originais apenas para proveniência, sem usá-los diretamente nos novos passes.
- Projetar exclusivamente com BlockingProjectionKeyProjector.Project em C#, usando IdentityComparison.NormalizeText e MetaphoneBr.Encode. Exigir paridade SQL.
- Criar índices compatíveis com os passes físicos, medindo planos, fanout e custo.
- **Não existe estratégia F separada.** Comparar oito braços: A sem fonética, A com fonética, B sem fonética, B com fonética, C sem fonética, C com fonética, D sem fonética, D com fonética. Mesmos corpus, seeds, split e denominador.
- Duas representações do mesmo nome não são evidências independentes.
- Congelar corpus, partições, identidades, versões, hashes, projeções, frequências individuais e conjuntas TRAIN, parâmetros e resultados. Snapshots são imutáveis; novos algoritmos geram novas versões.
- Isolar a dimensão fonética: manter iguais os demais parâmetros de cada par de braços; declarar quando um baseline histórico não permitir desligar a fonética sem alterar sua semântica. Não chamar essas variantes de comparáveis antes de comprovar paridade do restante.
- Selecionar em TRAIN/VALIDATION; TEST lacrado. Medir M/U elegíveis, recall, tempo, memória, FDR e abstenção. Não promover V9 só com sintético.

## Origem da alegação de deterioração de 18%
**Não comprovada.** A busca textual no repositório encontrou 18% como dúvida, não como cálculo:
- Solution/docs/V9_Memory_First_Experiment_And_CI_Plan.md pergunta qual é o denominador e o conjunto de pares.
- Solution/docs/DT_V9_Experimento_Memoria_Lote_CI_20261014.md solicita investigar o denominador.

Evidência diferente: Solution/docs/V9_Suporte_81_Celulas_MU_SQL_TRAIN_20261013.md informa 7.786/18.036 M elegíveis em TEST, aproximadamente 43,17% de recall absoluto, não queda de 18%. Outros relatórios têm cerca de 79–80% em TRAIN/VALIDATION e universo/política diferentes; **não** subtrair números não comparáveis.

## Investigação necessária
1. Localizar os dois relatórios originais supostamente comparados: run IDs, commits, seeds, hashes de corpus/modelo, splits, filtros e política.
2. Registrar numerador e denominador de M em ambos; distinguir 18 pontos percentuais de redução relativa de 18%.
3. Reexecutar baseline e variante no mesmo universo; decompor M perdidos por ausência de dados, regra, índice, fanout e partição.
4. Se faltar evidência, classificar NAO_COMPROVADO. Não publicar 18% como deterioração medida.

PR #1045: planejador; PR #1047: snapshots; PR #1044: testes integrados. Comparação completa dos oito braços A/B/C/D com/sem uso fonético ainda pendente.

## Passe E0 — igualdade demográfica completa (decisão adicional)

Incluir **passe de recuperação**, não nona estratégia, com igualdade de `nome_normalizado`, `nome_mae_normalizado` e `data_nascimento` válida/presente nos dois lados. O passe **não aceita CPF como atalho** e não transforma homônimo em identidade confirmada. Avaliar E0 com e sem o passe em cada um dos oito braços A0/A1/B0/B1/C0/C1/D0/D1, preservando o mesmo universo M/U, seeds e splits. Não interromper passes posteriores após um match exato; deduplicar candidatos na união.

Materializar índice composto físico compatível e congelar a frequência **conjunta tripla** em TRAIN por `base_person_id` distinto; não aproximar por produto de marginais. Medir `ExclusiveNewM`, `ExclusiveNewU`, recall marginal, fanout, tempo, CPU/I/O e memória. E0 é igualdade de blocking; distâncias graduais são apenas recursos de recuperação e não substituem Rank/Resolve nem o LLR/FDR. Falta de mãe/nascimento impede E0, mas não outros passes. Nenhuma promoção automática.

## Pendência preexistente P1 e nova pendência P2 — bloqueantes de conclusão experimental

### P1 — Origem e magnitude da alegada deterioração de 18% (PREEXISTENTE; permanece ABERTA)
A seção anterior é hipótese de auditoria, **não** resultado. Exigir rastreabilidade até dois runs comparáveis, hashes, filtros, mesma verdade de referência, mesma definição de elegibilidade e denominador; separar diferença absoluta em pontos percentuais de diferença relativa percentual. Caso não se encontrem evidências, registrar explicitamente `NAO_COMPROVADO`. Não encerrar P1 com a simples comparação de 43,17% TEST e ~80% TRAIN/VALIDATION.

**Aceite P1:** relatório reproduzível com IDs de runs, código e artefatos congelados, tabelas de TP/FN e denominadores por split e estrato, diferença com incerteza, e atribuição dos pares ganhos/perdidos; ou conclusão documentada de ausência de proveniência.

### P2 — Tamanho e suficiência da amostra para medir melhora/piora de blocking (NOVA; ABERTA)
**População sintética de 30.000 identidades e ~58.000 observações não equivale a 30.000 amostras independentes nem a 30.000 pares verdadeiros.** A unidade primária de recall de blocking é o **par M elegível** (mesma identidade, partições e gestores conforme política, sem CPF se exigido); a unidade de precisão/custo exige contabilizar também os pares U elegíveis, sem usar U amostrado como se fosse o censo. Pares que compartilham a mesma pessoa/observação são dependentes: intervalos binomiais ingênuos por par podem ser estreitos demais.

Para cada braço A0/A1/B0/B1/C0/C1/D0/D1, e para E0 desligado/ligado, congelar e divulgar: total de identidades e observações por split/estrato; `M_eligible` e `U_eligible` reais; `M_retrieved`, `M_missed`, `U_retrieved`; fanout por observação e percentis; proporção de ausências de nome/mãe/nascimento; contagens de identidades e clusters independentes; custo físico SQL. Comparar **os mesmos pares** entre braços e informar tabela pareada: ambos recuperam, apenas baseline recupera, apenas variante recupera, nenhum recupera. O ganho de recall é (apenas variante − apenas baseline)/M_eligible, com denominador constante.

**Planejamento de tamanho:** declarar antes de olhar TEST a diferença mínima relevante `delta` (pontos percentuais de recall), confiança, poder estatístico e estratos obrigatórios. Estimar variabilidade de diferenças por **bootstrap agrupado por identidade** ou método pareado que respeite clusters e a política de pares; dimensionar por simulação/piloto apenas em TRAIN/VALIDATION, usando a fração de pares discordantes e efeito de desenho. Nunca dimensionar por número bruto de observações ou por contagem de U. Se o intervalo de confiança da diferença incluir zero ou a região de equivalência pré-declarada, concluir `INCONCLUSIVO`, não melhora/piora. Reportar intervalos para recall e delta, e para U/fanout/custo quando pertinentes.

**Exemplo exclusivamente ilustrativo, sem correção por clusters:** para estimar recall ~50% com erro ±2 pontos percentuais e IC normal de 95%, seriam necessários ~2.401 pares M *independentes* (`1,96² × 0,5 × 0,5 / 0,02²`). Isso **não** é tamanho suficiente certificado para o Jornada, nem substitui poder para detectar diferenças pareadas pequenas; dependência entre pares, estratos e discordância aumentam ou alteram o tamanho necessário. Não fixar um N final antes do piloto e da definição de delta.

**Aceite P2:** protocolo de dimensionamento pré-registrado, tabela de elegibilidade e pares discordantes, método de IC agrupado, análise de poder/sensibilidade por estrato e conclusão explícita `SUFICIENTE`/`INSUFICIENTE`/`INCONCLUSIVO` para a menor diferença relevante. Sem isso, não declarar superioridade entre estratégias.

### Ordem de execução e controles
1. Concluir exportador canônico C#, snapshot imutável, índice/consulta E0 e teste de paridade SQL.
2. Congelar um universo único de TEST e executar oito braços × E0 ligado/desligado, sem selecionar parâmetros no TEST.
3. Publicar P2 (nova: suficiência e incerteza) e atualizar P1 (já existente: origem dos 18%) **como pendências separadas**; nenhuma depende de inventar explicação para a outra.
4. Diagnóstico de suficiência pode ser automático; **recalibração e promoção do modelo continuam decisões manuais**, sem ativação automática baseada no sintético.
