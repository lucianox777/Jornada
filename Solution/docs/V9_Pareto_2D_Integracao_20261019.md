# Pareto 2D integrado às decisões por fonte — 2026-10-19

`V9ParetoSourceSelection.EvaluateValidation` reutiliza o reconciliador
`V9PostResolveAdaptiveSelector` do merge #1157 e aplica `V9BootstrapPareto2D`
às contagens finais de Rank → Resolve. Não estima parâmetros, não executa um
segundo scorer e não congela, publica ou ativa modelos.

Cada linha representa configuração, agrupamento e margem. Recall usa TP por
fonte dividido pelas fontes elegíveis antes de blocking; FDR usa FP/(TP+FP)
após Resolve. Perdas de blocking, conflitos e abstenções permanecem no
denominador global. TEST e agrupamentos oracle não podem escolher modelos.

A elegibilidade exige política explícita pelo menos tão rigorosa quanto o
orçamento do reconciliador, suporte independente, multiplicidade cobrindo
todas as configurações comparadas, limite numérico CP/Hoeffding dentro do
orçamento e atestado independente das hipóteses de clusters. O componente CP
também deve existir, ser válido e não exceder o limite da razão. Um booleano,
FDR pontual zero ou uma amostra pequena não substituem essa evidência.
O produtor continua responsável pelo cálculo com contribuições por identidade,
cap pré-declarado, alpha e hipóteses corretas. A ponte não transforma uma
sensibilidade condicional em certificação estatística.

P95 físico end-to-end é exclusivamente guardrail. A evidência inclui hashes
de configuração completa, TRAIN, coorte VALIDATION, relatório por fonte,
protocolo e ambiente de medição. Deve apontar para o digest exato do relatório
reconciliado. Configurações com margens, thresholds, pesos TF ou agrupamentos
distintos não podem reutilizar o mesmo hash de configuração. É responsabilidade
do produtor incluir todos os demais parâmetros efetivos nesse hash.

A fronteira maximiza recall e minimiza FDR pontual entre opções elegíveis. A
sugestão minimiza a distância quadrática ao ideal normalizado pelos extremos
da fronteira. Eixos constantes contribuem zero; empates usam SHA-256 e depois
identificador ordinal. Latência não participa desse desempate.

A tabela preserva cada opção, todos os motivos de rejeição, todos os dominadores,
equivalentes e distância ao ideal das opções da fronteira. Não há truncamento
de busca nem amostragem da fronteira. O relatório completo de relações pode
exigir espaço e tempo quadráticos no número de opções; esse custo é inerente
à enumeração de todas as relações e deve entrar no orçamento de execução.

O overload legado sem política explícita retorna ausência de opção elegível.
A interface com política retorna somente sugestão; congelamento e aprovação
governada pertencem ao fluxo de snapshots existente. A evidência física real,
as hipóteses estatísticas e o TEST independente continuam gates separados.

Os testes cobrem fronteira/equivalência/dominância, determinismo, guardrail P95,
todos os motivos de rejeição, orçamento, multiplicidade, cópia imutável,
reconciliação por fonte, segregação TRAIN/VALIDATION/TEST e vínculo dos hashes.
O cenário positivo usa limite calculado por `V9IdentityClusterFdr`, sem
inventar uma aprovação estatística para o ensaio físico.
