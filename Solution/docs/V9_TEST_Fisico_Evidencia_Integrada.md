# V9 — TEST físico independente na infraestrutura existente

Estado: **implementação parcial; evidências reais e aprovação pendentes**.

## Integração canônica (sem novo executor)

Usar a infraestrutura existente de validação independente, com
`IndependentResolutionEvaluator.EvaluateRecorded` e decisões finais do Runner.
Não usar `Evaluate` (replay de posterior legado V5) para V9.
O `V9PhysicalSqlAdaptiveCalibrationProbe.RunAsync` permanece estritamente
TRAIN/VALIDATION: **não remover a rejeição de TEST**. O TEST deve consumir
somente o modelo e o ruleset congelados, sem recalibração, seleção de opções
ou publicação. Não criar serviço, runner ou pipeline paralelo.

O verificador `scripts/v9-sealed-test-evidence-gate.py` recebe um
**artefato produzido externamente pela execução real**. Ele não produz dados,
não executa SQL, não assina o artefato e não certifica o modelo.
O campo `artifactSha256` é uma declaração de proveniência e precisa ser
conferido contra os bytes do objeto externo por um coletor/controle independente;
a simples presença do campo não prova autenticidade.

## Protocolo de medição

1. Congelar snapshot, scorer, TF, regras de blocking e corpus TEST por SHA-256
   antes de iniciar a execução. Registrar commit, run ID e ambiente isolado.
2. Com verdade completa e identidades-base separadas entre splits, enumerar
   `M_eligible` **antes** de executar o blocking. Executar a união SQL física
   completa e obter `M_sql` sem truncamento ou `TOP`.
3. Capturar os outcomes finais do Runner, sem redecidir por posterior; avaliar
   pelo `EvaluateRecorded` existente, preservando universo completo e guards.
4. Agregar TP/FP aceitos por identidade-base, incluindo bases sem aceites.
   Declarar e **enforçar** o cap por identidade antes de observar TEST. O limite
   Hoeffding unilateral 95% implementado estima razão de médias populacionais
   sob hipóteses iid; não equivale automaticamente ao FDP do frame nem
   estabelece certificação operacional.
5. Repetir **20 execuções físicas SQL** da mesma materialização, registrando
   cada tempo e run ID, com fingerprints idênticos de frame, ruleset e ambiente.
   Registrar P95/P99 apenas descritivos; 20 observações não sustentam uma
   alegação robusta de SLA P99.
6. Persistir o pacote em armazenamento durável com acesso governado, conferir
   SHA-256 dos bytes, guardar referências de artefatos e relatório de validação
   sem PII. Se qualquer vínculo de proveniência faltar, **falhar fechado**.
7. Submeter evidências à revisão estatística e à aprovação governada explícita.
   Sem promoção automática, sem certificação por CI verde.

## Limites de aceite

Nenhuma fixture unitária, bootstrap de 30 mil, medição SQLite/memória ou
CI sintética substitui TEST físico independente. A PR #1132 verifica parte
do contrato e os casos adversariais; **não implementa ainda a exportação
real dos outcomes, a coleta SQL repetida nem a persistência auditada**.
A issue #999 permanece aberta até a apresentação e verificação das evidências.


## Ponte de outcomes finais → conglomerados de identidade — 18/10/2026

A classe `V9RecordedClusterFdrValidation` é uma **ponte diagnóstica
exclusivamente para VALIDATION**, sem nova política de decisão, exportador
HML, runner ou acesso ao TEST. Recebe um censo pré-blocking de fingerprints
opacos de identidade-base, vínculo observação→identidade de origem,
`IndependentResolutionObservation` e `IndependentRecordedOutcome`
do MESMO run gravado. Chama primeiro o
`IndependentResolutionEvaluator.EvaluateRecorded` existente, incluindo
a exigência de candidatos completos, manifesto, versão do modelo, ruleset,
run ID e validação de resultados realmente resolvidos/conflituosos.

Só então agrega `TrueAccepted` e `FalseAccepted` das decisões
**`RESOLVIDO` finais** por identidade-base de **origem**. Não duplica
o mesmo falso vínculo nos dois lados do par e não conta uma sugestão FS
bruta como link final. Atribuição de origem exige vínculo explícito
para todas as observações; identidade desconhecida, vínculo duplicado ou
ausente falha fechado. Todas as identidades do censo anterior ao blocking
participam da análise, inclusive as sem observação de consulta ou
sem link aceito. TP/FP agregados precisam coincidir com
`Overall.TrueLinks`/`Overall.FalseLinks` do avaliador canônico.

O resultado inclui ponto FDR, sensibilidade leave-one-identity-out e
limite Hoeffding de razão de médias de aceites por identidade, com
**cap de aceites por origem predeclarado e enforçado**. Sem denominador
inferior positivo, o limite fica **nulo**, nunca zero risco. Essa
aproximação exige amostragem iid entre identidades-base e rotulagem
completa; os fingerprints/coortes e testes **não atestam por si só** essas
hipóteses. `OperationallyCertified=false` em todas as saídas.
Ainda falta o exportador real do Runner integrado ao censo, assinatura
de proveniência, execução física e TEST lacrado.


### Integridade da origem de grupos no FDR final — 18/10/2026

**Problema diagnosticado:** o SHA do relatório do avaliador final
`IndependentResolutionEvaluator` identifica o run e suas decisões
gravadas, **não** a vinculação entre observações e identidades-base
nem a coorte integral pré-blocking. Alterar apenas o agrupamento
poderia deixar o SHA do run idêntico, mas modificar a inferência
por conglomerados, sem alterar TP/FP globais.

A ponte `V9RecordedClusterFdrValidation` V2 acrescenta
`BoundCohortEvidenceFingerprintSha256`. É um digest SHA-256
determinístico que abrange **o fingerprint das decisões finais**,
a coorte **integral** de identidades (incluindo grau zero), o mapa
`observation fingerprint -> base identity fingerprint`, o limite
máximo pré-declarado de aceites por origem e o valor `alpha`.
O digest é independente da ordem de entrada. Testes demonstram
que alterar somente o agrupamento, uma identidade de grau zero,
o cap ou o alpha modifica esse digest, mesmo que TP/FP globais e
fingerprint do Runner permaneçam iguais.

Esse vínculo serve à **rastreabilidade e detecção de substituições**;
não autentica sozinho um censo, a amostra, uma assinatura externa,
a veracidade do mapeamento ou a independência entre grupos.
`OperationallyCertified` permanece sempre `false`. A execução
física integral, o exportador de proveniência assinado, a validade
das hipóteses do limite e o TEST lacrado ainda precisam ser comprovados.


## Ponte dos resultados gravados do Runner → avaliador independente (18/10/2026)

O novo `V9RecordedRunnerOutcomeBridge.Bind` recebe resultados
`ProbabilisticLinkageDecision` **já calculados após os guards do
Runner C#**, com identificação do run/modelo, fingerprint da observação
e da identidade-base e um mapeamento completo
`CandidatePersonUuid → CandidateFingerprintSha256 + posterior`.
Recebe **separadamente** a moldura de avaliação
`IndependentResolutionObservation` com rótulos externos de verdade.
Não calcula novos scores, não refaz `Resolve` a partir de posteriores,
não usa o gabarito para escolher candidato e **não modifica** decisões.

Antes de exportar `IndependentRecordedOutcome` e
`V9RecordedBaseObservation`, exige cobertura observação-a-observação,
run/modelo não vazios, declaração explícita de universo SQL completo,
ausência de duplicatas, correspondência **integral** dos candidatos e
seus posteriores com a moldura independente, SHA válido para fonte e
identidade-base, modelo exato, coerência entre UUID e fingerprints de
melhor/segundo/resolvido, score registrado e status final
`RESOLVIDO`, `CONFLITO`, `NAO_RESOLVIDO`.
O relatório resultante **não exporta UUIDs/nomes/verdade de vínculo**;
só fingerprints, status, motivos, IDs de run/modelo e mapeamento
observação→base. Ordenação determinística garante reprodução.

Essa ponte pode alimentar diretamente
`IndependentResolutionEvaluator.EvaluateRecorded` e
`V9RecordedClusterFdrValidation.Evaluate`, sem reconstruir o
resultado dos limiares. **Limitação importante:** a função pura
não prova que o chamador realmente capturou esses dados de uma
execução HML autenticada, que o SQL não usou `TOP N`, ou que os
rótulos independentes, amostragem das bases e cap estatístico são
válidos. Não altera o Runner operacional nem publica/ativa V9.
`OperationallyCertified=false` é incondicional; falta integrar
o exportador a uma execução HML genuína, com atestação do mesmo
run/modelo/ruleset e TEST lacrado.


## Encadeamento vinculante de decisões finais → FDR por coorte (18/10/2026)

`V9RecordedRunnerOutcomeEvaluation.EvaluateValidation` agora executa,
na **mesma chamada** e sobre os mesmos dados:
`V9RecordedRunnerOutcomeBridge.Bind` →
`V9RecordedClusterFdrValidation.Evaluate` →
`IndependentResolutionEvaluator.EvaluateRecorded`. Recebe
`ProbabilisticLinkageDecision` já calculado pelo Runner C#, todo o
mapa `UUID ↔ fingerprint ↔ posterior` da união candidata,
o gabarito independente de `VALIDATION`, a coorte completa de bases
**anterior ao blocking** (inclusive grau zero), run/model/ruleset
fixados, limiar, margem, limite por identidade e alpha.

Diferentemente de testes que apenas imprimiam o score, a integração
exige o **status final pós-guards** e confere TP/FP com o avaliador
independente. Um SHA final vincula `runId`, `modelId`,
versão do modelo, fingerprint do ruleset, fingerprint do registro de
decisões e fingerprint da coorte com proveniência. Os testes
exercitam TP=1/FP=1, bases sem consulta, recusa de universo truncado,
identidade-base inexistente, mudança silenciosa de origem, inversão
de decisão, parâmetros falsos e estabilidade por permutação de linhas.

**Não confundir com uma execução física aprovada**: a nova API
integra corretamente objetos **fornecidos pelo chamador**, mas ainda
não extrai automaticamente essas evidências do run real da HML,
não autentica assinaturas/estado SQL de um run, não prova isolamento
TRAIN/VALIDATION/TEST, cobertura sem `TOP N`, independência da amostra
de grupos, suficiência de 30 mil ou validade do cap por origem.
Permanece `OperationallyCertified=false` e **não ativa V9/DT-15**.
O contrato exige uma exportação posterior genuína com captura fixada
do mesmo run/modelo/ruleset e do TEST lacrado, sem substituir a
verdade independente por rótulos do próprio Runner.


## Leitura REAL de decisões terminais persistidas no SQL da HML — 18/10/2026

O novo `V9SqlRecordedRunEvidenceReader.ReadCompletedHmlAsync` avança
a cadeia de proveniência da V9 além das fixtures artificiais.
Usa **somente SELECT parametrizado** para ler
`identidade.linkage_run` e **todas** as linhas correspondentes de
`identidade.linkage_resultado` (sem `TOP N`, paginação ou filtro de
candidatos). Antes de ler identidade exige que o **marcador residente**
`Jornada.EnvironmentProfile` do banco seja exatamente `HML`.
Rejeita Development, Production, perfis ausentes e conexões fechadas.

Exige `tipo_run=MODEL_VALIDATION`,
`status=CONCLUIDO_SEM_PUBLICACAO`, `publicado_em IS NULL`,
`registros_elegiveis=avaliados>0`, decomposição exata
resolvidos/conflitos/não resolvidos, uma linha por observação e
modelo/versão estáveis em todas as linhas. Reconstitui
`ProbabilisticLinkageDecision` **dos dados efetivamente persistidos
pelo Runner**, sem recalcular score, guard ou resultado e produz um
SHA-256 determinístico de run/modelo, contagens e todas as decisões
com motivo e posteriores.

A informação de UUID/ID de observação fica **apenas no objeto C#
diagnóstico em memória**, não deve ser publicada ou registrada em
logs; o hash, desacompanhado do objeto original, é exportável para
rastreabilidade. Não escreve no banco, não muda V8/Gold e não
publica nenhum vínculo.

**Limitação estrutural verificada no DDL existente:** a tabela
`identidade.linkage_resultado` grava no máximo dois melhores
candidatos, seus posteriores e o resultado. Portanto, mesmo a leitura
de **todas as linhas do run** não prova a **união integral** dos
candidatos por observação, a identidade-base de origem nem a verdade
independente. Os campos de saída
`HasCompleteCandidateUnion=false` e
`OperationallyCertified=false` são invariáveis. A eventual
alimentação de `V9RecordedRunnerOutcomeBridge` requer captura
separada e autenticada da lista completa de candidatos e da coorte
pré-blocking com mapeamento de origem. Testes puros verificam a
integridade de linha/contador e rejeição de status, run, modelo e
publicação falsificados. O método **não foi executado em HML real**
por esses testes e não rompe o lacre de TEST.
