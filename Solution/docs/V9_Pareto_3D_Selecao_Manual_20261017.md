# V9 — decisão: fronteira de Pareto tridimensional e seleção manual

**Status:** contrato de diagnóstico e visualização; não é certificação nem autorização de produção.

## Objetivo e domínio
Cada configuração integral e imutável do V9 (blocking, parâmetros FS m/u, TF, scorer, thresholds, abstention, Resolve e fingerprints) gera uma observação em VALIDATION com três coordenadas:
- **X: recall global de vínculos aceitos**, maximizar: TP aceitos / **todos os pares verdadeiros elegíveis antes do blocking**, inclusive pares não recuperados. A elegibilidade deve ser contratual e auditada; não excluir observações sem nome apenas para elevar a taxa.
- **Y: FDR dos vínculos aceitos**, minimizar: FP aceitos / (TP aceitos + FP aceitos). Se não houver vínculos aceitos, FDR é **indefinido**, nunca zero.
- **Z: latência P95 em milissegundos**, minimizar: caminho físico completo SQL blocking → Rank/score/TF → Resolve, medido no mesmo ambiente e protocolo de concorrência, com repetições e distribuição identificada.

Sem medição física de P95 ou sem denominador global demonstrável, a configuração é **incompleta e não participa da fronteira certificável**. Não inventar valores ou estimar latência a partir de fanout. Não converter métrica de recall condicional do frame em recall global.

## Dominância e fronteira
A domina B se A.recall >= B.recall, A.FDR <= B.FDR, A.P95 <= B.P95, e ao menos uma desigualdade for estrita. Igualdade exata nas três métricas significa equivalência objetiva, não dominação: preservar todas as configurações e seus fingerprints, agrupando visualmente pontos coincidentes. Não arredondar antes da comparação; valores exibidos podem ser arredondados. Comparar apenas resultados da mesma rodada, corpus, contrato, ambiente, carga e protocolo. Ordenação estável por identificador/fingerprint.

A fronteira é um **conjunto discreto de pontos não dominados**, não interpolar uma superfície contínua nem alegar desempenho para parâmetros não testados. Calcular a fronteira 3D uma única vez no backend C#; as três projeções 2D são vistas auxiliares, não filtros independentes que escondam candidatos globalmente não dominados.

## Tela de decisão
- Um gráfico 3D rotacionável e com zoom: X recall %, Y FDR %, Z P95 ms, unidades e sentido de melhoria explícitos. Pontos não dominados em destaque; dominados discretos, ocultáveis; pontos coincidentes agrupados com contagem.
- Seleção por clique/toque ou teclado abre painel com **recall, FDR, P95 numéricos**, TP/FP/FN, aceitos, abstenções, intervalos de confiança e metodologia, hashes do corpus/fit/configuração, nome/versão da opção, parâmetros efetivos de blocking/TF/FS/Resolve, estado de evidência e motivo de inelegibilidade.
- Tabela acessível, pesquisável/ordenável, alternativa ao 3D; todas as opções e a fronteira disponíveis para auditoria. Não depender de perspectiva para comparar números exatos.
- Até três **sugestões**, correspondentes às projeções recall×FDR, recall×P95 e FDR×P95. Para cada par, considerar apenas pontos da **fronteira global 3D**; selecionar o menor afastamento euclidiano do ideal do par com min–max calculado sobre o mesmo conjunto comparável. Objetivos invertidos conforme sentido; se amplitude zero, contribuição dessa coordenada é zero. Empate: ordem determinística por fingerprint. Desduplicar sugestões; nunca fabricar três alternativas. Esta convenção simétrica não substitui preferências humanas nem implica aprovação.
- Seleção **manual** de qualquer configuração elegível da fronteira; registrar ator, data, justificativa, fingerprints, métricas e política de limites vigente. TEST independente só após congelamento; TEST não reordena nem recalibra. Nenhum auto-deploy/auto-promoção.

## Limites de governança
Política versionada, opcional e explícita: recall mínimo (se declarado), FDR máximo e P95 máximo; não há 95% de recall obrigatório nem FDR=1% universal. A ausência de orçamento é exibida como 'sem limite configurado', não 'aprovado'. Se nenhuma opção satisfizer os limites, apresentar 'nenhuma elegível', mantendo diagnóstico. Limites servem para filtrar elegibilidade, não alterar os valores medidos. Para FDR, reportar também limite superior de incerteza agrupada por identidade e volume de aceitos: estimativa pontual sozinha não é certificado.

## Fluxo de evidência
1. TRAIN: ajustar parâmetros exclusivamente aqui, mantendo identidade-base disjunta.
2. VALIDATION: avaliar *end-to-end* o mesmo Runner/Resolve, apurar denominador global, TP/FP/FN/abstenções, FDR e incerteza agrupada.
3. SQL físico: executar blocking e decisão de ponta a ponta, com 20–100 repetições comparáveis quando o protocolo exigir; coletar P50/P95/P99 e fingerprints do conjunto de candidatos, ambiente e concorrência. Sem execução física, visualização é diagnóstico incompleto.
4. Produzir fronteira 3D e três sugestões, com artefato agregado versionado e sem dados pessoais.
5. Operador seleciona e congela uma configuração; TEST independente apenas verifica o candidato congelado e pode reprová-lo.
6. Promoção requer gates e autorização existentes (DT-15 e demais); visualização não altera regras de PROD/HML, V8, Gold, IBGE original ou dados reais.

## Contrato de exportação sugerido
`{version, studyFingerprint, corpusFingerprint, environmentFingerprint, evaluationProtocolFingerprint, partition:"VALIDATION", objectives:["MAX_RECALL_GLOBAL","MIN_FDR_ACCEPTED","MIN_P95_MS"], options:[{optionId,configurationFingerprint,fittedParametersFingerprint,recallGlobal,fdrAccepted,p95Milliseconds,trueAccepted,falseAccepted,truePairsEligible,acceptedTotal,fdrUpper95Cluster,eligible,exclusionReasons,pareto3d}], suggestions:[{pair,optionId}], manuallySelectedOptionId:null, testUsed:false, operationalActivationAllowed:false}`.

Nunca incluir nomes, CPF, identificadores de pessoas ou pares individuais no relatório.

## Dívidas e critérios de aceite
- Implementar cálculo puro C# da fronteira com testes de dominação estrita, igualdade, extremos, empate, FDR indefinido, NaN/Infinity, P95 ausente, candidatos incomparáveis e determinismo.
- Conectar métricas globais reais ao executor V9; não reutilizar recall condicional.
- Implementar gráfico 3D acessível + tabela e painel, seleção manual auditada; não usar valores simulados como reais.
- Testar com corpus sintético rotulado e SQL físico; verificar paridade e isolamento de TEST.
- Revisar documentação histórica que imponha 95% como regra universal. Até conclusão, esta decisão não implica implementação física, CI verde ou merge.

## Decisão arquitetural
Preferir **uma fronteira 3D** com três projeções/sugestões e tabela, em vez de três fronteiras 2D independentes ou pesos arbitrários. Não gerar superfície interpolada; somente pontos efetivamente avaliados.
