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
