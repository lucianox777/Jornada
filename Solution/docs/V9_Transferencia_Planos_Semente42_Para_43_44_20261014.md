# V9 — transferência de regras congeladas de TRAIN semente 42 para sementes independentes 43 e 44

## Motivo e decisão
Após #1085, perfis `clean` e `correlated` tiveram sua melhor união de
blocking **retreinada separadamente em cada seed**, dentro de TRAIN.
Isso mede estabilidade da capacidade de ajuste do sistema e custos para
massas diferentes, mas **não mede a portabilidade de uma regra escolhida
uma vez**. O risco é particularmente importante porque na seed 44 o perfil
`correlated` não alcançou 95% nem com todas as 19 células.

Esta entrega congela as **listas de passes selecionadas somente em TRAIN
da seed 42**, separadas por perfil, e as reaplica **sem fit, sem
reordenação e sem mudar thresholds** a ambas as partições TRAIN/VALIDATION
de sementes distintas 43/44. O relatório mostra lado a lado (a)
performance da regra transferida, (b) apenas como comparação não decisória,
o plano selecionado localmente em TRAIN da seed alvo, quando existe, e
(c) limites inferiores bootstrap com clusters de pessoas-base.

## Validação independente e segurança
- A evidência fonte das duas configurações é gerada no **mesmo run CI**,
  com workflow + SHA e artifact `v9-clean-vs-correlated-seed42-mu-aggregates`.
  A matriz 43/44 só inicia quando a etapa de referência 42 tiver
  concluído, depois as sementes independentes rodam em paralelo.
- Cada run alvo contém a **mesma referência demográfica IBGE**, versão
  de projeção, arquivo canônico C# SHA-bound, perfil explicitamente
  `clean` ou `correlated`, geração separada e seed correspondente.
  A origem 42 não pode ter o SHA igual ao alvo. Falha se o plano
  fonte estiver ausente, vazio, contiver passe desconhecido ou repetido.
- A rotina `v9-frozen-cross-seed-plan-transfer.py` cria chaves reais
  indexadas SQLite para **somente as células congeladas**, executa
  junções com filtro estrito de partição e gestor, CPF fora da via
  probabilística, nome da pessoa obrigatório + (mãe ou nascimento),
  guarda `cap` por célula sem truncamento e uniões deduplicadas.
- Reconciliam-se **M e U de cada célula** com o relatório indexado
  original da seed alvo, bem como M global, M elegível e quantidade
  elegível. Todas as taxas usam M **global sem CPF**, incluindo perdas
  de elegibilidade, não apenas pares candidatos.
- Grau falso médio é `2U/N_elegível` e P95/P99 incluem
  observações de grau zero; U é par candidato não-vínculo,
  **não** vínculo falso aceito. A incerteza faz bootstrap
  por identidade-base, não por par.
- A regra de seed 44 permanecerá congelada da seed 42 mesmo quando
  o ajuste interno 44 registrar `INFEASIBLE`. **Nunca corrigir
  silenciosamente a regra fonte com dados da seed alvo.**
- VALIDATION não ajusta os passes; TEST está lacrado. Sem evidências
  dos pares de cidadãos, nomes, CPF, identidades ou
  frequências nominativas nos artefatos de saída.

## Como interpretar e o que continua pendente
Esse teste diferencia **seleção intra-seed** de **portabilidade
inter-seed**. Mesmo um recall >=95% nas três seeds **não certifica**
a distribuição de erros das Secretarias, o custo para 11,45 milhões,
a FDR dos vínculos aceitos, a latência SQL Server nem a adequação
de parâmetros do scorer/TF/m/u/prior. O gerador synthetic `clean`
é cenário, não realidade municipal; `correlated` permanece teste
de resistência adversarial, sem ajustar erros para melhorar scores.

#999/#946 continuam com prioridade para a posterior calibração
condicionada ao frame **SQL físico**, medição de vínculos FP após
Fellegi–Sunter e referência independente. Deve ocorrer em PR separada,
sem combinar automaticamente promoção V9/DT-15 com este diagnóstico.

Este incremento não modifica políticas ativas, V8, Gold, memória
persistente, IBGE original, HML/PROD, JornadaLocal, NODE/NAS,
contratos ou dados reais. Calibração e ativação permanecem manuais.

## Rastreabilidade
#1081: variantes nominais. #1082: Pareto e orçamento TRAIN.
#1083: proveniência e efeito dos 18% hipotéticos.
#1084: clean × correlated com mesmo IBGE. #1085: três seeds e
suficiência amostral. **Esta etapa: replay transferido e
inteiramente congelado da seed42**.
