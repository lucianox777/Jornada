> **RETIFICAÇÃO DE DECISÃO (20/10/2026):** as menções abaixo a tetos/orçamentos de falsas criações, `MeetsPointBudgets`, escolha automática de vencedor e status `NO_TRAIN_POINT_CANDIDATE_MEETS_DUPLICATE_BUDGET` descrevem o código V1 da PR #1162, **não uma política vigente**. A decisão superveniente é explorar e recomendar candidatos pelas **faixas da fronteira de Pareto sem orçamento prévio de duplicatas nem limiar inferior pré-fixado**. A PR #1165 corrige parcialmente o código, mas sua integração e os ensaios físicos devem ser conferidos. Ver [registro canônico](V9_Registro_Decisoes_Bootstrap_Gold_20261020.md). Este arquivo fica preservado como histórico técnico, não como especificação atual de seleção.

# V9 — limiar inferior de não-vínculo, calibração TRAIN e risco de duplicação

**Data:** 20/10/2026. **Fase:** implementação diagnóstica **sem
publicação, sem ativação, sem TEST**. Complementa a auditoria Gold #1161
sem depender de alterar Gold, SQL de publicação ou versões V8.

## Problema no ensaio Gold existente

No Gold incremental sintético de #1160, 9.240 observações de 5.003
pessoas geraram 3.067 novas referências e 2.788 pendências finais.
O Runner já cria `NOVA_IDENTIDADE` após `SEM_CANDIDATO_%`
com busca materializada e guardas de publicação. Se a consulta SQL traz
algum candidato mas o melhor posterior é insuficiente para associá-lo,
o FS C# retorna `NAO_RESOLVIDO` com `ABAIXO_T_LINKAGE`, sem criar
referência.

**Distinção não negociável:** rejeitar um ou todos os pares recuperados
não demonstra que a identidade verdadeira não existe no Gold: o
blocking pode não tê-la encontrado. A escalabilidade da política de
criação requer medir **falsa criação entre referências já existentes**,
não apenas reduzir pendências. As contagens por onda
`NaoResolvidos - SemCandidatoNoBloco` sugerem acúmulo, mas NÃO são
distribuição exata dos 2.788 pendentes finais: é preciso ler
`motivo` bruto e `motivo_publicacao` do último resultado publicado,
instrumentação da PR #1161.

## Implementação (duas camadas, mesmo calibrador C#)

### 1. Hipótese de não-vínculo após o FS já congelado

`Jornada.Linkage.Runner/V9LowerNonMatchPreview.cs` recebe a decisão
**do mesmo Rank/Resolve existente** (não pontua novamente) e prova de
recuperação sobre a projeção compatível com o snapshot:

- `0 < lowerThreshold < fixedUpperThreshold < 1`, fit SHA e frame
  SHA obrigatórios; rejeita proveniência divergente.
- `RESOLVIDO` mantém vínculo original; `CONFLITO` continua pendente.
  A decisão normal `ABAIXO_T_LINKAGE` vira **hipótese experimental
  de nova referência** somente se `melhorScore < lowerThreshold`,
  existe pelo menos um candidato e busca/índice/pass são completos.
- Faixa intermediária é `PossibleLink`. Busca parcial, incompleta,
  timeout ou passes insuficientes (declarados como tais pelo chamador)
  é `TechnicalAbstention`; nunca supor zero candidatos.
- Busca completa realmente vazia permanece sob a política
  `SEM_CANDIDATO_%` existente; não duplica o mecanismo de criação.
- **Em todos os resultados**
  `AllowedToPublishNewReference=false` e
  `OperationalActivation=false`. Não são escritos SQL,
  Gold ou referência `ref`.

A prova `VerifiedRetrieval` possui flags atestados pelo chamador;
elas **não são prova criptográfica independente** dos índices. Antes
de habilitar qualquer execução, o adaptador físico terá de autenticar
o manifesto SQL, o snapshot e a completude sem truncamento.

### 2. Pareto no calibrador único, exclusivamente TRAIN

O método
`V9AdaptiveCalibrationCoordinator.SearchLowerNonMatchOnTrain`
chama `V9LowerNonMatchTrainCalibrator.Search`. Recebe exemplos
rotulados independentes de `TRAIN_SELECTION`, congelados pelo
snapshot e quadro físicos conhecidos; não aceita VALIDATION/TEST,
execução automática nem linhas duplicadas.

Varia **somente** o limiar inferior de uma grade explícita; mantém
o FS, blocking, peso TF, limiar superior, conflitos e FDR de aceites
**inalterados**. Para cada limiar, calcula:

- `ProposedNewReferences` e
  `CorrectNewReferences` quando a pessoa verdadeira ainda
  **não estava** no Gold de referência pré-execução.
- `FalseNewReferences` quando a pessoa verdadeira **já estava**
  no Gold mas a hipótese criaria UUID duplicado.
- `FalseNewAmongCreates = FalseNew / ProposedNew`, com NULL
  se não houver criação; e
  `FalseNewAmongAlreadyRepresented = FalseNew / N_present_before`.
  Taxa de duplicação entre referências finais por identidade
  permanece métrica distinta, a ser medida em #1161.
- `BaselinePending` e `PendingAfter` para a mesma coorte
  (sem atribuir efeitos sobre fusões futuras), além do número de
  grupos de identidade independentes com proveniência de treino.
- Fronteira de Pareto **falsas criações × pendências**, com teto
  explícito para os dois riscos. Sem vencedor que respeite orçamento,
  retorna `NO_TRAIN_POINT_CANDIDATE_MEETS_DUPLICATE_BUDGET`.
  Uma opção possível é apenas
  `TRAIN_POINT_SUGGESTION_ONLY_UNCERTIFIED...`,
  jamais certificação ou publicação.

O fingerprint do estudo registra fit, corpus de treino, grade,
limite superior e orçamentos. Não há reset da multiplicidade por lote
ou seleção por VALIDATION. Todas as conclusões retornam
`DuplicateConfidenceCertified=false`,
`LinkageFdrRecalibrated=false`,
`AutomaticCalibrationOrActivationAllowed=false`.

Os dados de exemplo dos testes são artificiais, não evidência dos
2.788 pendentes nem da população municipal.

## Critérios para habilitar a política no futuro

1. #1161 totalmente aprovada/mergeada e executada em Gold LocalDB
   descartável com razões reais das pendências e B-cubed separado.
2. Persistir/exportar exemplos treináveis a partir do Runner Gold
   TRAIN_SELECTION, sem labels entrando no FS ou nos índices.
3. Calibrar risco de falso `NOVA_IDENTIDADE` em TRAIN com correção
   por dependência (mesma pessoa em múltiplas ondas, classes Gold
   concorrentes) e limites superiores de erro por identidade.
4. Repetir Gold em pelo menos três ordens pré-declaradas, sem refit,
   comparando pares P/R, B-cubed, duplicatas, fusões, pendências,
   FDR e custo. A ordem precisa ser tratada como fator do estudo.
5. Desenho de amostra suficiente e nova avaliação TEST **uma vez**
   com fit, limiares, snapshot e política previamente congelados.
6. Somente depois publicar snapshot integral em `ref`,
   vincular loader/Runner/Gold e obter aprovação institucional DT-15.

**Não faz parte desta PR:** mudar `ProgressivePublicationSql`,
estender status operacional, criar UUID real, relaxar threshold,
acionar auto-bootstrap, executar TEST, mexer em Gold real, V8,
HML/PROD, JornadaLocal, NODE/NAS ou IBGE original.
