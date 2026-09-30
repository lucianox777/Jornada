# Especificação — calibração bootstrap, evidência real e governança manual do modelo de linkage

**Data:** 2026-09-29  
**Estado:** especificação normativa candidata; requer implementação e gates antes de produzir efeito operacional  
**Escopo:** Jornada.Linkage.Parameters.Worker, Calibrador/Avaliador, relatório de calibração/monitoramento e governança de modelos  
**Precedência pretendida:** quando aprovada e implementada, esta decisão substitui a interpretação de que o default DEV de 100 bp é a fonte normativa permanente da tolerância de falso vínculo. Não altera por si só modelos ATIVOS nem autoriza HML/Produção.

## 1. Decisão

A Jornada adota calibração **offline, periódica, versionada e governada**, nunca aprendizado online.

O primeiro modelo é um **bootstrap descartável**. Na ausência de evidência real suficiente, seus parâmetros estatísticos são estimados a partir da referência nominal IBGE e do corpus sintético com degradações versionadas e verdade conhecida.

A operação normal acumula evidência independente por identidades resolvidas deterministicamente por CPF. Essa acumulação **não altera** o modelo ativo. Quando a evidência real atingir suficiência estatística, uma nova calibração poderá produzir um RASCUNHO exclusivamente a partir da evidência real aplicável. O modelo bootstrap permanece no histórico, mas deixa de contribuir estatisticamente para o novo modelo.

O relatório periódico deve avaliar o modelo ativo contra a evidência real acumulada e apresentar estado **VERDE / AMARELO / VERMELHO**. Esse semáforo é diagnóstico e recomendação operacional: nunca muda parâmetros, nunca gera publicação automática e nunca ativa modelo.

A substituição do modelo é sempre uma decisão humana explícita e auditável pelo fluxo padrão:

`GENERATE_DRAFT → revisão do operador → VALIDATE → ACTIVATE`.

**Monitoramento não calibra. Calibração não ativa. Evidência estatística recomenda; a mudança do modelo é manual.**

## 2. Objetivo estatístico

A decisão automática de identidade é assimétrica: um falso vínculo é prioritariamente mais indesejável que uma abstenção. `NAO_RESOLVIDO` e `CONFLITO` são resultados legítimos quando a evidência não sustenta vínculo automático.

Isso não significa minimizar `FP` isoladamente, pois a solução trivial de nunca vincular teria `FP=0`. O Calibrador deve expor conjuntamente falso vínculo, falso negativo/recall, cobertura e abstenção e selecionar uma região conservadora segundo método estatístico versionado.

Nenhuma taxa publicada por outro sistema constitui, sozinha, parâmetro normativo da Jornada. Literatura e sistemas comparáveis são referências de plausibilidade e comparação, não fonte automática de um epsilon universal.

## 3. Grandezas que não podem ser confundidas

Para estado de comparação `γ`:

`m(γ) = P(γ | M)`

é a distribuição dos estados observados entre pares que representam a mesma pessoa.

`u(γ) = P(γ | U)`

é a distribuição dos estados entre pessoas diferentes no universo definido pelo método.

O falso vínculo é propriedade da **decisão completa** — `m/u`, prior, TF, blocking, threshold, conflito e demais regras — quando comparada com uma verdade independente. Uma taxa de falso vínculo não é `m`, e `m` não deve ser manipulado artificialmente para tornar o sistema "pessimista".

O conservadorismo deve estar na política de decisão e na exigência de evidência, mantendo as estimativas estatísticas honestas.

## 4. Fase BOOTSTRAP

### 4.1 Fonte

Antes de existir evidência real suficiente, o modelo inicial usa:

1. snapshot nominal/demográfico IBGE versionado;
2. gerador sintético determinístico por seed;
3. degradações de nome, nome da mãe, nascimento e demais campos previstas pelo contrato do corpus;
4. verdade conhecida da pessoa sintética (`BasePersonId` ou equivalente);
5. partições TRAIN / VALIDATION / TEST;
6. snapshot TF e método de `u` coerentes com o scorer da versão.

### 4.2 Papel das degradações

As taxas de degradação do bootstrap são **hipóteses experimentais versionadas**, não alegações de que aquelas taxas são a distribuição verdadeira dos erros de São Paulo.

O gerador deve preservar:

- operação aplicada;
- campo afetado;
- seed;
- perfil de erro;
- `BasePersonId`;
- partição;
- `Corruptions` ou manifestação equivalente;
- fingerprint das entradas e do método.

As transformações devem ser traduzidas pelos mesmos comparadores usados pelo scorer em estados `γ`. O objeto estatístico final é a distribuição dos estados do comparador, não a contagem de erros textuais por si só.

### 4.3 Controle da degradação

O bootstrap deve executar perfis versionados que cubram condições plausíveis e adversas, inclusive erros correlacionados quando suportados. Não se deve ajustar um perfil após observar TEST para fazê-lo passar.

Os relatórios devem mostrar, por perfil:

- suporte;
- estados de comparação;
- `m` estimado;
- `u`/TF aplicáveis;
- TP/FP/FN/abstenção;
- comportamento de colisões críticas, inclusive pessoas distintas com assinatura demográfica exata quando presentes.

A calibração inicial deve ser conservadora perante o conjunto de cenários definido **antes** da avaliação. A regra matemática exata de seleção deve ser versionada no contrato do Calibrador.

### 4.4 TEST congelado

TEST audita um candidato já congelado. Não escolhe degradação, `m`, `u`, TF, threshold, conflict floor, orçamento ou regra de decisão.

Falha em TEST produz evidência de falha. Uma hipótese alterada exige nova rodada identificável; não se otimiza retrospectivamente sobre TEST observado.

## 5. Evidência real determinada por CPF

### 5.1 Função do CPF

CPF é utilizado como evidência determinística independente para estabelecer a identidade verdadeira dos registros elegíveis.

A presença de CPF **não implica**, por hipótese, maior nem menor qualidade de nome, filiação, nascimento ou qualquer outro atributo.

É vedado ao Calibrador assumir:

`P(erro | CPF presente) < P(erro | CPF ausente)`

ou a desigualdade inversa sem evidência empírica específica.

O CPF determina a verdade; não concede qualidade estatística aos demais campos.

### 5.2 Replay sem CPF

Na avaliação/calibração demográfica, o CPF usado para estabelecer a verdade deve ser ocultado das features do linkage. O sistema mede como o scorer se comportaria sem esse identificador.

Para identidades verdadeiras estabelecidas por CPF:

`m_real(γ) = P(γ | M, verdade determinada independentemente por CPF)`.

A mesma verdade independente permite medir decisões incorretas do linkage quando o CPF é retirado da decisão.

Decisões produzidas exclusivamente pelo próprio linkage probabilístico **não podem ser recicladas como verdade** para recalibrar o mesmo mecanismo. Isso evita realimentação de erro.

## 6. Suficiência estatística da evidência real

Não existe nesta especificação um número mágico de casos como 5.000, 20.000 ou 1.000.000.

A matéria-prima é a quantidade acumulada de identidades/casos resolvidos automaticamente por CPF dentro da própria Jornada. A suficiência deve ser inferida estatisticamente a partir dessa evidência e do suporte necessário às estimativas do contrato vigente.

A avaliação de suficiência deve registrar no mínimo:

- quantidade de identidades e observações independentes elegíveis;
- quantidade de pares verdadeiros utilizáveis;
- suporte por estado de comparação necessário ao `m`;
- incerteza/intervalos das estimativas;
- estados sem suporte ou com suporte insuficiente;
- fingerprint e janela/snapshot da evidência utilizada.

A contagem total não substitui suporte. Uma amostra grande dominada por um único estado não prova precisão para estados raros.

Os critérios numéricos de suficiência devem ser parte versionada do método estatístico, nunca escolhidos após observar o resultado de TEST.

## 7. Transição BOOTSTRAP → REAL

Enquanto a evidência real for insuficiente, permanece válido o modelo BOOTSTRAP ativo.

Atingir suficiência **não altera o modelo ativo**. Apenas torna elegível a geração de um novo RASCUNHO de fonte REAL.

Na primeira calibração REAL elegível:

`m_new = m_real`

e o `m` sintético recebe peso estatístico zero no novo modelo.

Não haverá blending silencioso:

`m_new ≠ α m_real + (1-α) m_synthetic`.

Se o contrato REAL não tiver suporte suficiente para todos os parâmetros necessários, não se fabrica um modelo híbrido oculto; permanece o modelo anterior e o relatório explicita a insuficiência.

O modelo bootstrap e seus artefatos não são apagados. Permanecem imutáveis como proveniência histórica das decisões tomadas sob aquela versão.

Para `u`, TF, prior e outros parâmetros, a fonte e a regra de transição devem ser declaradas separadamente pelo contrato da versão; a substituição de `m` não autoriza inferir automaticamente que todos esses parâmetros possuem a mesma fonte ou o mesmo requisito amostral.

## 8. Calibração não é tempo real

Ingestão, resolução por CPF e crescimento da Gold não executam atualização incremental de parâmetros.

É proibido alterar silenciosamente durante a operação:

- `m`;
- `u`;
- TF;
- prior;
- threshold;
- conflict floor;
- comparadores;
- regra de decisão;
- versão do modelo.

Entre duas ativações, o modelo ATIVO é imutável.

A geração de novo modelo é evento explícito, manual ou orquestrado como run-once governado, nunca consequência automática da chegada de um registro.

## 9. Relatório periódico de calibração/monitoramento

O relatório deve avaliar o modelo ATIVO contra a evidência independente acumulada sem modificar o modelo.

Deve mostrar no mínimo:

### 9.1 Identidade do modelo

- versão e fingerprint;
- fonte estatística: `BOOTSTRAP_IBGE_SYNTHETIC` ou `REAL_CPF_DETERMINISTIC_GOLD` (nomes finais a versionar);
- data da calibração;
- scorer/comparadores;
- snapshot de parâmetros;
- threshold ativo;
- conflict floor;
- referência/snapshot TF;
- demais parâmetros que influenciem a decisão.

### 9.2 Evidência disponível

- tamanho da amostra real CPF-resolvida elegível;
- quantidade efetivamente usada no replay;
- suficiência estatística global e por parâmetros/estados necessários;
- estados sem suporte;
- janela temporal/fingerprint;
- motivo de exclusões.

### 9.3 Comparação estatística

- `m` do modelo ativo;
- `m` observado na evidência real;
- diferenças e incertezas;
- TP/FP/FN/abstenções do replay sem CPF;
- cobertura/recall e métrica de falso vínculo corretamente nomeada e com denominador explícito;
- métricas por estados/estratos relevantes quando houver suporte;
- threshold ativo e comportamento observado em sua fronteira;
- diagnóstico de conflito.

O relatório não deve chamar razões diferentes de "FPR", "FDR", "false-match rate" ou "FP budget" indistintamente. Numerador e denominador são obrigatórios.

## 10. Semáforo

O relatório apresenta três estados.

### VERDE — modelo sustentado

Há evidência estatística suficiente para as verificações aplicáveis e não foi detectada condição que, segundo o contrato versionado, requeira nova calibração.

Ação: manter modelo ativo.

### AMARELO — evidência inconclusiva ou atenção

A evidência ainda é insuficiente para alguma conclusão necessária, ou há aproximação de condição de controle sem evidência suficiente para declarar deterioração.

AMARELO não significa reprovação.

Ação: manter modelo ativo, continuar acumulando evidência e registrar a causa.

### VERMELHO — recalibração recomendada

Há evidência suficiente de que o modelo ativo deixou de satisfazer uma condição estatística versionada de controle ou de que a evidência real já sustenta uma calibração que deve ser revista operacionalmente.

Ação: recomendar nova calibração.

**VERMELHO não altera o modelo e não concede autoridade de ativação.**

Os critérios matemáticos que produzem VERDE/AMARELO/VERMELHO devem ser versionados, reproduzíveis e definidos antes da avaliação. Não introduzir `FP > 1%` ou qualquer outro número apenas como convenção de interface.

## 11. Fluxo de governança

O fluxo normativo é:

1. modelo ATIVO permanece imutável;
2. operação acumula evidência determinística por CPF;
3. relatório periódico faz replay/avaliação sem CPF;
4. relatório produz evidência e semáforo;
5. VERDE: nenhuma ação de modelo;
6. AMARELO: acompanhamento, sem mudança de modelo;
7. VERMELHO: nova calibração é recomendada;
8. um operador/processo autorizado decide iniciar `GENERATE_DRAFT`;
9. o Calibrador produz RASCUNHO;
10. o operador revisa relatório/dossiê ATIVO × RASCUNHO;
11. `VALIDATE` executa os gates vigentes;
12. a decisão de promover continua humana;
13. `ACTIVATE` somente ocorre mediante ação explícita autorizada e auditável.

Nenhum passo 1–7 executa automaticamente os passos 8–13.

A existência de RASCUNHO também não implica obrigação de promovê-lo. O operador pode manter o modelo atual, solicitar nova evidência ou nova calibração, ou prosseguir para VALIDATE conforme a governança DT-15.

## 12. Relação com o default DEV de 100 bp

O repositório atual possui `DecisionCalibrationMaxFpValidationBasisPoints=100` e `DecisionCalibrationMaxFpTestBasisPoints=100` em caminhos DEV e documentação que os descreve como limite de engenharia.

Esta especificação determina que esses 100 bp **não sejam promovidos a constante científica, decisão institucional ou definição de m**.

A implementação futura deve inventariar todos os caminhos em que esse valor:

- seleciona candidato na fronteira;
- bloqueia VALIDATE/ACTIVATE;
- aparece no relatório;
- é persistido no modelo;
- é usado por TEST.

A substituição não será "trocar 100 por outro número". O método estatístico da versão deve declarar como a decisão inicial conservadora é derivada do bootstrap e como o comportamento real é posteriormente monitorado.

Até a implementação dessa mudança, a documentação e os gates atuais continuam descrevendo o comportamento executável; esta especificação não autoriza contornar gate existente.

## 13. Threshold e relatório

Todo modelo deve possuir threshold explicitamente versionado e exibido no relatório.

O relatório deve distinguir:

1. **threshold ativo** — parâmetro imutável do modelo em execução;
2. **comportamento observado no threshold ativo** — medição sobre a evidência independente atual;
3. **threshold candidato** — somente existe dentro de nova calibração/RASCUNHO;
4. **recomendação de recalibração** — não é um threshold novo.

O monitor nunca "move" o threshold para voltar ao verde.

Se o modelo ficar VERMELHO, um novo threshold somente pode nascer de nova calibração e somente pode entrar em operação após revisão, VALIDATE e ACTIVATE manual.

## 14. Conflict floor e parâmetros críticos

Parâmetros críticos da versão devem ser persistidos explicitamente. Ausência não deve ser mascarada por fallback silencioso para outro threshold.

Para versões em que `DUAL_THRESHOLD_CONFLICT_FLOOR` faça parte do contrato, sua ausência/invalidez deve falhar fechado conforme o contrato da versão.

Mudanças de scorer, comparadores, runtime ou faixa significativa de threshold continuam sujeitas às regras de conferência independente do Runbook/DT-14.

## 15. Proveniência e auditabilidade

Cada modelo e cada relatório devem permitir reconstruir:

- fonte BOOTSTRAP ou REAL;
- commit/versão do código;
- versão do algoritmo;
- scorer e comparadores;
- seed quando sintético;
- snapshot IBGE;
- perfil de degradação;
- snapshot da evidência real quando aplicável;
- TF e demais referências;
- `m/u` e suportes;
- threshold/conflict floor;
- TRAIN/VALIDATION/TEST;
- hashes/fingerprints;
- resultado do semáforo e justificativa;
- usuário/processo que solicitou calibração;
- decisão humana de manter, validar ou ativar;
- modelo ATIVO anterior e novo modelo, quando houver promoção.

Um relatório novo não reescreve relatório antigo. Uma calibração nova não reescreve modelo antigo.

## 16. Requisitos de segurança estatística

1. O próprio linkage probabilístico não cria sozinho sua Gold de treinamento.
2. CPF usado como verdade deve ser ocultado das features avaliadas quando o objetivo for medir o linkage sem CPF.
3. TEST não participa da otimização.
4. Monitoramento não altera parâmetros.
5. Suficiência não é inferida apenas por N total.
6. Ausência de suporte deve ser visível.
7. Métricas de falso vínculo devem declarar denominador.
8. Abstenção é resultado legítimo.
9. Modelo REAL não mistura silenciosamente `m` sintético.
10. Mudança do modelo ativo é manual.

## 17. Critérios de aceite para implementação

A implementação desta especificação somente poderá ser declarada concluída quando houver evidência automatizada de que:

1. o modelo ativo permanece imutável durante ingestões e monitoramento;
2. a Gold determinística por CPF pode ser selecionada sem usar decisões probabilísticas como verdade;
3. CPF é excluído das features do replay sem CPF;
4. o relatório registra modelo, threshold, fonte, amostra, suporte, incerteza e métricas;
5. o semáforo é calculado por contrato versionado;
6. AMARELO representa corretamente insuficiência/inconclusão;
7. VERMELHO não executa `GENERATE_DRAFT`, `VALIDATE` ou `ACTIVATE`;
8. a primeira calibração REAL elegível pode estimar `m` sem contribuição do bootstrap;
9. falta de suporte impede modelo REAL incompleto/híbrido silencioso;
10. TEST congelado não altera parâmetros;
11. novo RASCUNHO não altera o ATIVO;
12. VALIDATE não ativa;
13. ACTIVATE exige ação explícita autorizada;
14. histórico/fingerprints permitem reproduzir a decisão;
15. o default 100 bp não é tratado como definição de `m` nem como norma institucional.

## 18. Não decisões

Esta especificação não decide por si só:

- o valor numérico final dos critérios de suficiência;
- uma taxa universal aceitável de falso vínculo;
- a fórmula estatística final do semáforo;
- a política de nome social;
- política de merge sem CPF;
- promoção de qualquer modelo atual para HML/Produção;
- remoção imediata dos gates legados existentes.

Esses pontos exigem implementação/método versionado e, quando aplicável, decisão própria.

## 19. Invariantes resumidos

> **Bootstrap é temporário; evidência real suficiente o substitui, não o mistura.**

> **CPF estabelece verdade independente; não implica qualidade maior ou menor dos demais campos.**

> **O modelo ativo é imutável entre ativações.**

> **Monitoramento é observacional.**

> **VERMELHO recomenda recalibração; não recalibra nem ativa.**

> **GENERATE_DRAFT cria candidato; VALIDATE valida; ACTIVATE é decisão manual explícita.**

> **Falso vínculo é propriedade observada da decisão completa; não é m.**

> **A prioridade conservadora é evitar falso vínculo sem transformar abstenção geral em solução.**
