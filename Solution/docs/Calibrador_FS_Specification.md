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

O IBGE permanece referência externa versionada de bootstrap/fallback para NOME e NOME_MAE.

A troca para u candidato-condicionado acontece por suficiência observável, nunca por data:

- mínimo de pares condicionados na união;
- mínimo de pares em cada passe;
- para nome da mãe, o requisito usa pares com o atributo presente;
- os limites são configuração explícita e persistida no modelo;
- a proveniência IBGE continua registrada mesmo quando o bootstrap deixa de ser aplicado.

Enquanto qualquer requisito de suficiência não for atendido, o campo correspondente continua usando o bootstrap IBGE. Quando atendido, o valor empírico estimado no universo do blocking substitui o bootstrap.

## 5. Term frequency

`SPLINK_TERM_FREQUENCY_V1` é preservado apenas como matemática reutilizável. Ele **não é um estágio DF** e não está habilitado automaticamente no score corrente.

O executável `Jornada.Linkage.Evaluation --export-calibration` expõe parâmetros, proveniência e vetores sintéticos da matemática TF em JSON, somente leitura. Essa superfície existe para conferência externa e **não participa do score**.

Se TF vier a entrar no FS, deve:

- ser calibrada dentro do mesmo universo candidato;
- ter peso/piso/versionamento explícitos;
- não contornar guards de conflito/não-unicidade;
- não transformar ausência da referência IBGE em unicidade;
- passar TRAIN/VALIDATION/TEST e validação adversarial.

## 6. Identificabilidade

Nome completo + data de nascimento exatos não constituem chave única. A política corrente impede auto-resolução quando esse núcleo demográfico exato é a base da decisão probabilística.

`EXACT/EXACT/EXACT` pode ocorrer tanto em match quanto em homônimo total; nenhum threshold, DF ou TF resolve essa indistinguibilidade sem evidência adicional independente.

## 7. ABBREV_COMPATIBLE e novas evidências

Abreviação é fenômeno do processo de registro. Um estado `ABBREV_COMPATIBLE` só pode entrar no LLR quando existirem m/u adequados ao canal de abreviação. Telefone, e-mail, CNS ou outros atributos podem ser avaliados como evidência adicional, mas presença no blocking não os promove automaticamente ao score.

## 8. Promoção

CI verde não equivale a homologação estatística. Antes de ativação probabilística real em HML/Produção continuam necessários corpus representativo, avaliação independente por estrato e aprovação institucional aplicável. A issue #31 concentra esse gate.
