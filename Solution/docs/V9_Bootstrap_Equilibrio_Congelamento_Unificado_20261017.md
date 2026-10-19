# V9 — decisão operacional: bootstrap, equilíbrio 2D e congelamento unificado

**Status:** decisão aprovada para implementação; não representa números medidos, execução física, TEST aprovado ou ativação. **Substitui** a proposta de Pareto 3D descrita em `V9_Pareto_3D_Selecao_Manual_20261017.md`. Não utilizar as sugestões tridimensionais na inicialização.

## Objetivo e métricas
Calibrar V9 com massa sintética inicial de **30.000 registros** e partições independentes por identidade-base TRAIN / VALIDATION / TEST. Em VALIDATION, cada configuração integral (blocking, m/u FS, TF, thresholds, abstention, Rank/Resolve e demais parâmetros efetivos) produz TP aceitos, FP aceitos e o total de pares/identidades verdadeiros elegíveis **antes** do blocking. Definir contratualmente a unidade de avaliação (resolução por consulta/identidade versus pares) e mantê-la idêntica no denominador e nas contagens; não usar recall condicional aos candidatos. `recall_global = TP_aceitos / verdadeiros_elegíveis`; `FDR = FP_aceitos/(TP_aceitos+FP_aceitos)`, indefinido quando não há aceitos. FN inclui perdas no blocking e abstenções, sem contagem dupla. Precisão = 1 - FDR. Não impor piso arbitrário de recall ou teto universal de FDR. Reportar volumes e incerteza com reamostragem/limites agrupados por identidade, não certificar com Wilson nominal por pares dependentes.

## Limite provisório de latência
Parâmetro inicial **`MaximumCalibrationP95Milliseconds = 60000`** (60 segundos) para o bootstrap sintético. É deliberadamente arbitrário e permissivo, **não** derivado de medição em 30 mil registros nem SLA de produção. P95 físico do caminho SQL blocking → TF/FS → Rank → Resolve deve ser registrado por configuração, sob protocolo reproduzível. Latência é somente filtro de elegibilidade; não é objetivo, nem eixo de Pareto. Falta de P95 físico implica evidência incompleta, não tempo zero. O limite inicial é versionado/congelado juntamente com o restante dos parâmetros; mudanças posteriores exigem nova versão. Dimensionamento dinâmico por volume/carga é dívida técnica.

## Pareto 2D e seleção automática inicial
Construir a fronteira **discreta** das configurações completas e comparáveis em VALIDATION: A domina B se `recall_A >= recall_B` e `FDR_A <= FDR_B`, com pelo menos uma desigualdade estrita. Manter todas as opções equivalentes e não interpolar pontos nem parâmetros. Excluir do conjunto elegível ausência de FDR, denominador global inválido, P95 físico ausente ou P95 acima de 60.000 ms. Não usar TEST para ranquear.

Para escolher **um equilíbrio inicial reproduzível**, usar a menor distância quadrática ao ideal (recall máximo, FDR mínimo) com normalização min–max por eixo **dentro da fronteira elegível**. Amplitude zero contribui zero; desempate determinístico por fingerprint da configuração e identificador. Trata-se de **convenção operacional simétrica**, não prova de ótimo universal ou garantia de segurança. Exigir evidência estatística suficiente por identidades independentes; quando insuficiente, **não congelar automaticamente** e retornar motivo explícito. Relatar limite superior de FDR agrupado por identidade, contagem de aceitos, FP, TP, abstenções, recall, P95 e incerteza. Diferenças pequenas de FDR não devem ser tratadas automaticamente como irrelevantes; mostrar variação absoluta de FP e intervalos.

## Fluxo único para todos os parâmetros
`ESTIMAR → AVALIAR → SELECIONAR → CONGELAR → TEST INDEPENDENTE → (gates/autorização de promoção separados)`.
1. **Primeira execução:** bootstrap gera estimativas em TRAIN; VALIDATION executa o caminho V9 real, calcula tabela/fronteira e escolhe o equilíbrio determinístico. Se todas as evidências mínimas e invariantes estiverem presentes, **congela automaticamente uma única configuração integral** (incluindo limite de latência e parâmetros não otimizados). Sem aprovação automática de TEST ou produção.
2. **Execuções posteriores:** produzir propostas **sem alterar** a versão congelada. A escolha da nova configuração será **manual**, após comparação; usar o **mesmo serviço/contrato** de congelamento integral.
3. **Congelamento imutável:** snapshot canônico completo e hash SHA-256; versão, origem bootstrap/manual, fingerprint de TRAIN/VALIDATION, protocolo e ambiente de medição, parâmetros efetivos, métricas/limites, algoritmo de seleção e versão, data/ator ou identidade de automação, predecessor, estado de evidência. Operação idempotente para o mesmo snapshot; divergência deve criar nova versão, jamais sobrescrever. Não incluir PII.
4. **TEST independente:** executado somente após congelamento, com rótulos nunca usados para ajuste/seleção. Pode reprovar; não retreinar, reranquear nem escolher outro candidato com TEST. Congelar não é ativar.

## Saída mínima agora
Artefato agregado, versionado e auditável: todas as opções avaliadas (incluindo inelegíveis e motivos), fronteira Pareto 2D, opção inicial recomendada, parâmetros completos, snapshot congelado e fingerprints, evidência física e status TEST não utilizado. Tabela com `optionId, recallGlobal, FDR, limiteSuperiorFDRPorIdentidade, TP, FP, aceitos, P95ms, elegibilidade, pareto2d, selecionado`. Valores só podem ser chamados de reais após execução física. Caso não haja candidato válido, produzir diagnóstico e **não** inventar configuração congelada.

## Dívida técnica (não bloquear bootstrap)
**Monitor operacional / interface de seleção posterior:** tabela comparativa, faixa de configurações não dominadas, diferenças pequenas e intervalos, parâmetros completos, escolha manual auditada, acompanhamento de deriva e cálculo de orçamento de latência conforme tamanho da base, carga, concorrência e infraestrutura. Implementar em rodada futura; nesta rodada priorizar executor, evidências e congelamento inicial.

## Critérios de aceite imediato
- Implementar função pura Pareto 2D e sugestão determinística, com testes de dominância, empates, zero aceitos, valores inválidos, limite P95 e ordenação.
- Implementar serviço único de snapshot imutável e idempotente, separado de ativação, com testes de primeira execução, repetição e proposta posterior manual.
- Integrar com runner físico de 30 mil sintéticos e avaliar realmente TRAIN/VALIDATION; **não** substituir por fixture fictícia ou números impressos sem assert.
- Registrar resultados numéricos efetivamente observados; sem ambiente físico disponível, declarar explicitamente `PENDENTE_EXECUCAO_FISICA`.
- Não tocar PROD/HML, Gold, IBGE original, JornadaLocal ou dados pessoais; nenhum merge sem gates.
