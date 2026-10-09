> **Vigência 29/09/2026:** [decisões canônicas](Decisoes_Canonicas_Identidade_Linkage_20260929.md) prevalecem sobre regras legadas de guard exato, V6/V7 e nome social. A V8 atual não implementa todas essas decisões.

# Calibrador Fellegi–Sunter — especificação corrente

**Estado:** arquitetura técnica corrente. O corpus sintético/DEV prova engenharia, não homologação estatística municipal.

## 1. Fluxo canônico

O linkage probabilístico da Jornada possui um único resolvedor estatístico operacional: **Fellegi–Sunter (FS)**. O fluxo é:

1. gerar candidatos com o ruleset versionado de blocking;
2. deduplicar a união dos passes;
3. calcular estados de evidência e LLR no FS;
4. aplicar política de decisão versionada;
5. publicar apenas decisões que atravessem todos os guards de segurança.

Não existe estágio DF anterior ao FS. Similaridade nominal e frequência não constituem um resolvedor paralelo.

## 2. Separação TRAIN / VALIDATION / TEST

O split é determinístico por pessoa-base e acontece antes da geração das amostras.

- **TRAIN** estima m/u e seleciona o ruleset;
- **VALIDATION** constrói a fronteira de decisão e calibra threshold/margem;
- **TEST** reaplica o candidato congelado e pode somente reprovar.

CPF pode definir ground truth, mas é ocultado do blocking e do score.

## 3. m

m vem de pares positivos independentes, preferencialmente observações de Gestores distintos ligadas por identificador determinístico aceito como ground truth. Volume não substitui diversidade/representatividade dos erros cadastrais. Estados sem suporte observado permanecem dívida estatística e não devem ganhar interpretação forte apenas por suavização.

## 4. u e o universo candidato

O alvo operacional de u é:

> P(estado de evidência | não-match, par sobrevive ao blocking efetivo)

Não é o u incondicional da população.

O Runner atual une/deduplica os passes e depois pontua sem transportar o passe de origem para o scorer. Por isso, o u usado pelo FS corrente é estimado sobre a **união deduplicada do ruleset**. O Calibrador mede também suporte nominal por passe para revelar heterogeneidade e impedir que um passe com pouco suporte fique invisível.

### Bootstrap e convergência

O IBGE é referência externa versionada de **bootstrap** para NOME e NOME_MAE. Seu snapshot é carregado explicitamente uma vez na preparação do ambiente, validado integralmente na carga e preservado para os modelos que o utilizaram. Não há recarga periódica automática nem revalidação integral em cada calibração.

Na **primeira** preparação do ambiente, a carga única do snapshot IBGE exige integridade, compatibilidade e fingerprint auditáveis. Após esse bootstrap, `GENERATE_DRAFT` utiliza os parâmetros persistidos do modelo e os dados ingeridos da Jornada para calibrações posteriores; **não exige referência IBGE ativa**, não recarrega nem recalcula o snapshot. Se o primeiro bootstrap necessário ainda não ocorreu, falhar explicitamente; não transformar essa guarda inicial em dependência permanente. Mudanças futuras de referência demandariam nova decisão explícita, nunca atualização implícita.

Cada modelo registra o identificador, a versão e o fingerprint do snapshot efetivamente utilizado. Snapshots anteriores são imutáveis e retidos para replay e auditoria, inclusive após a convergência para `u` empírico; uma nova referência ativa não modifica retroativamente modelos antigos. A referência IBGE não é verdade de identidade individual.

A troca para u candidato-condicionado acontece por suficiência observável, nunca por data:

- mínimo de pares condicionados na união;
- mínimo de pares em cada passe;
- para nome da mãe, o requisito usa pares com o atributo presente;
- os limites são configuração explícita e persistida no modelo;
- a proveniência IBGE continua registrada mesmo quando o bootstrap deixa de ser aplicado.

O bootstrap IBGE é calculado **uma única vez** e seus parâmetros iniciais e proveniência ficam persistidos. As gerações seguintes não consultam nem exigem referência IBGE ativa: evoluem exclusivamente com evidências ingeridas da Jornada, observando suficiência e suporte do universo condicionado ao blocking. Se a evidência empírica ainda for insuficiente, o modelo conserva os parâmetros iniciais persistidos, sem recarregar o IBGE nem declarar convergência empírica inexistente. A exigência atual de suficiência da união e de todos os passes permanece até avaliação específica no Ensaio. Esta é a **decisão de arquitetura de 29/09/2026**, ainda dependente de adequação da implementação RF-052 (#602); não interpretar este texto como prova de que o Worker já a implementa.

## 5. Term frequency

`SPLINK_TERM_FREQUENCY_V1` é preservado apenas como matemática reutilizável. Ele **não é um estágio DF** e não está habilitado automaticamente no score corrente.

O executável `Jornada.Linkage.Evaluation --export-calibration` expõe parâmetros, proveniência e vetores sintéticos da matemática TF em JSON, somente leitura. Essa superfície existe para conferência externa e **não participa do score**.

Se TF vier a entrar no FS, deve:

- ser calibrada dentro do mesmo universo candidato;
- ter peso/piso/versionamento explícitos;
- não contornar os guards de conflito e segurança **vigentes na V8 após retirada do veto demográfico fixo**;
- não transformar ausência da referência IBGE em unicidade;
- passar TRAIN/VALIDATION/TEST e validação adversarial.

## 6. Identificabilidade

Nome completo + data de nascimento exatos não constituem chave única. **Decisão de 29/09/2026:** retirar da V8 o veto demográfico exato fixo; o FS deve decidir mediante distribuições e thresholds calibrados, com demais conflitos e gates preservados. A retirada **ainda não foi implementada** e exige teste adversarial de homônimos e reconferência (DT-14).

`EXACT/EXACT/EXACT` pode ocorrer tanto em match quanto em homônimo total; nenhum threshold, DF ou TF resolve essa indistinguibilidade sem evidência adicional independente.

## 7. ABBREV_COMPATIBLE e novas evidências

Abreviação é fenômeno do processo de registro. Um estado `ABBREV_COMPATIBLE` só pode entrar no LLR quando existirem m/u adequados ao canal de abreviação. Telefone, e-mail, CNS ou outros atributos podem ser avaliados como evidência adicional, mas presença no blocking não os promove automaticamente ao score.

## 8. Experimentos nominais, sem pesos presumidos

[DC-LK-03](Decisoes_Canonicas_Identidade_Linkage_20260929.md#dc-lk-03--núcleo-nominal-e-nome-social) fixa a hipótese de que a ausência unilateral de nome social **pode** sinalizar não-match; a ausência bilateral, concordância, divergência e comparação cruzada nome civil×social também precisam ser avaliadas. O Calibrador mede suporte por estado e estrato (Secretaria/sistema/período de coleta), estima m/u e dependência civil/social, avalia ganho em VALIDATION e audita em TEST. Não existe uma quarta evidência nominal independente pré-aprovada, peso nominal arbitrário nem presunção de neutralidade/penalização para nome social. Sem amostra que sustente efeito validado, o atributo continua como observação e feature de blocking. Alterar scorer aciona DT-14 e atualização de fingerprint/modelo.

## 9. Promoção

CI verde não equivale a homologação estatística. Antes de ativação probabilística real em HML/Produção continuam necessários corpus representativo, avaliação independente por estrato e aprovação institucional aplicável. A issue #31 concentra esse gate.


> **Norma vigente (09/10/2026) — FS, Splink, TF, IBGE e Calibrador:** consultar [DC-LK-TF](Decisoes_Canonicas_Identidade_Linkage_20260929.md#dc-lk-tf--norma-vigente-de-frequência-nominal-fs-e-calibrador-09102026). O peso TF zero é neutro (peso 1 aplica ajuste integral); os pesos e m/u devem ser estimados pelo Calibrador a partir do bootstrap sintético IBGE e, progressivamente, de evidência histórica real. Primeiro nome e último sobrenome significativo de pessoa e mãe devem participar do FS sem dupla contagem. V8 é referência histórica, não segunda implementação operacional. Em caso de divergência, prevalece a decisão canônica; este documento não certifica implementação concluída.


## 10. Nome completo e componentes nominais (decisão vigente)

A comparação FS principal preserva o **nome completo** da pessoa e da mãe. Primeiro nome e último sobrenome significativo são componentes auxiliares, não substitutos do nome completo. Exemplo: MARIA APARECIDA DE OLIVEIRA SANTOS continua integral; MARIA e SANTOS qualificam sua evidência.

O Calibrador deve estimar níveis nominais compostos ou distribuições conjuntas, evitando somar como independentes as evidências do nome completo e de seus componentes. O mesmo vale para o nome materno. A TF por componente e seu peso, incluindo zero, devem ser estimados e avaliados com partições TRAIN/VALIDATION/TEST; o scorer somente aplica o snapshot aprovado.

O IBGE fornece marginais iniciais: frequência de sobrenome em qualquer posição é **proxy**, não frequência observada do último sobrenome. A transição para dados municipais depende de suficiência e proveniência. Não inferir famílias a partir das marginais sintéticas.

**Estado atual:** o código compara nomes completos e ajusta TF de primeiros nomes nos estados EXACT; TF de último sobrenome, níveis compostos e seleção automática do peso ainda exigem implementação e testes. Consultar a norma DC-LK-TF no documento de decisões canônicas.


### Diagnóstico de implementação para a próxima frente (09/10/2026)

A geração de rascunho em `LinkageParametersWorker.GenerateDraftFromGoldAsync` ainda define explicitamente `TERM_FREQUENCY_WEIGHT = 1m` após preparar `NominalTermFrequencyReferenceStore`. Portanto, a presença do parâmetro e a correção para aceitar zero **não significam estimação automática**. A próxima implementação deve remover a atribuição fixa e selecionar o peso com dados de calibração, sem consultar TEST para ajuste. É obrigatório conservar a referência do snapshot e evidências de seleção.

A preparação atual (`NominalTermFrequencyReferenceStore`) publica apenas prenomes da pessoa e mãe; o suporte de sobrenome em `NominalTermFrequencySnapshot` está em evolução separada, e não deve ser habilitado no score antes de haver frequências governadas, calibração conjunta e validação contra dupla contagem. Na falta de evidência suficiente, não substituir um valor fixo por outro peso arbitrário. O corpus sintético de 30 mil pessoas é bootstrap metodológico, não prova de FDR real.
