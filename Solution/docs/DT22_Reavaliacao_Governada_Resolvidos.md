# DT-22 — Reavaliação governada dos RESOLVIDOS por nova execução de linkage

**Rastreamento:** [issue #861 — DT-22](https://github.com/lucianox777/Jornada/issues/861), aberta e postergada.\n\n**Decisão de arquitetura:** 09/10/2026 · **Tipo:** dívida técnica funcional
posterior à conclusão da proposta de sistema · **Estado:** **ABERTA,
IMPLEMENTAÇÃO POSTERGADA E SEM ACEITE**. A DT-22 é o destino formal do
objetivo antes denominado **Trilha 4 — reavaliação contínua dos RESOLVIDOS**.
A **Trilha 4 como frente autônoma/serviço periódico fica ENCERRADA COMO
PROPOSTA**; isso **não** significa que a capacidade de reavaliação já
exista ou que a deficiência corrente seja irrelevante.

**Precedência:** as [decisões canônicas sobre CPF/UUID](Decisoes_Canonicas_Identidade_Linkage_20260929.md)
e as regras de preservação de fatos e de publicação prevalecem.
O presente documento registra uma **decisão prospectiva de escopo**,
não modifica a especificação normativa publicada nem comprova
execução de replay em ambiente real. Ver
[Plano de desenvolvimento](Plano_Desenvolvimento.md),
[backlog DT](Dividas_Tecnicas.md) e [manual do sistema](Manual_Sistema_Consolidado_20261009.md).

## 1. Decisão aprovada

Não criar serviço permanente, cron, timer ou fila de **reavaliação
temporal indiscriminada** de todas as identidades RESOLVIDAS.
O sistema pode manter a melhor associação disponível quando não há
novas evidências ou reavaliação explicitamente solicitada: identidade
progressiva **não** tem prazo universal de perfeição.

A futura **DT-22** disponibilizará uma operação **extraordinária,
explícita, governada, auditável e recuperável** para submeter
observações sem CPF **já RESOLVIDAS** a uma **nova decisão com
evidências/candidatos e modelo selecionados**. Poderá ser solicitada
após ativação governada de nova versão de modelo ou por decisão
operacional justificada para um universo delimitado.

A ativação `ACTIVATE` **não** passa a executar reavaliação
automaticamente pela simples publicação desta decisão. O replay
de RESOLVIDOS é **passo operacional separado**, a ser autorizado e
versionado, **não bloqueio implícito nem side effect oculto** da
promoção de modelos. Registrar no dossiê a necessidade, o escopo, a
realização/pendência e a versão do modelo efetivamente aplicada.

## 2. Limitação que será aceita provisoriamente

**Conferência do código em 09/10/2026:**

- `Jornada.Processor.Worker/IdentityResolutionCoordinator.cs` e
  `SqlProcessorRepository.Persistence.cs` usam CPF válido no
  caminho determinístico (`cpf_ancora`); observações sem CPF
  entram em `PENDENTE_PROBABILISTICO` ou são associadas por
  identidade admitida segundo regras separadas. **Uma âncora CPF
  por UUID e um UUID por CPF** é restrição obrigatória; CPF
  tardio **não** pode “carimbar” um grupo fundido.
- `Jornada.Linkage.Runner/ProbabilisticLinkageBatchRunner.cs`,
  `EligibleFromWhereSql()`: o modo `INCREMENTAL` inclui
  observações sem vínculo, `NAO_RESOLVIDO`, `CONFLITO` e
  `PENDENTE_PROBABILISTICO`, **não inclui genericamente
  `RESOLVIDO` por mudança de candidatos/Gold/origem**. O
  teste `ProbabilisticLinkageIncrementalEligibilityTests.cs`
  caracteriza essa seleção; não prova invalidação abrangente.
- Existe processamento de **nova versão de origem** pelo
  Processor, mas isso **não prova** que todas as observações
  antigas já RESOLVIDAS, afetadas indiretamente pela alteração
  de **outra pessoa**, sejam reenfileiradas e reavaliadas.
- O modo `REPLAY` implementado é de **reprodutibilidade histórica**:
  exige `ReplaySourceRunId`, fixa a versão do modelo de origem
  e proíbe troca desse modelo. Portanto **não serve**, sem
  mudança de semântica/contrato, para recalcular com modelo novo.
  O modo `FULL` já admite avaliação mais ampla de observações
  sem CPF, inclusive RESOLVIDAS, mas sua existência **não
  comprova** uma operação governada de DT-22 com transição
  de modelo, checkpoint, impacto e cobertura auditados.

**Risco conhecido e assumido na proposta:** após entrada de novas
evidências ou candidatos, uma associação probabilística anteriormente
RESOLVIDA pode permanecer desatualizada se não houver execução
específica que a inclua. A hipótese de que todos os registros
“com evento novo se consertam sozinhos” não é comprovada para
**terceiros indiretamente afetados**. Este risco deve constar
nos critérios de aceite do produto/Ensaio e não pode ser
encoberto com promessa de reprocessamento automático.

## 3. Contrato funcional mínimo da DT-22

1. **Solicitação explícita e autorizada:** indicar ator/finalidade,
   justificativa, modelo ativo/versão desejada e origem da
   solicitação (mudança de modelo ou revisão operacional).
   A autorização da ativação do modelo **não** equivale à
   autorização irrestrita de reatribuir identidades.
2. **Escopo determinístico:** definir previamente e registrar o
   conjunto elegível de observações RESOLVIDAS **sem CPF**,
   selecionável por recorte governado ou abrangente, com
   identificadores, high-watermark, fingerprints e versões
   das fontes/projeções/regras de blocking/candidatos. Regras
   devem incluir dependências e chaves de candidatos **antigas e
   novas** quando o recorte alegar cobertura dos afetados;
   se a abrangência não puder ser provada, declarar lacuna.
3. **Modelo e comparação:** avaliar os candidatos e o modelo
   atual autorizado contra o estado publicado anterior, sem
   trocar a semântica do `REPLAY` histórico. Nome da operação
   a definir (por exemplo `REASSESS_RESOLVED`), separado de
   `FULL`, `INCREMENTAL`, `REPLAY` e `MODEL_VALIDATION`
   até contrato e testes próprios; não reutilizar `REPLAY`
   histórico como atalho.
4. **Autoridade CPF/identidade:** CPF-first, nunca fundir CPFs
   distintos, nunca transferir âncora existente por score.
   Para divisão sem CPF, seguir decisão canônica de novos
   UUIDs/ambiguidade sem herança arbitrária; registrar razões,
   sucessores e divergências. **Não** perder observações,
   atributos nem fatos finalísticos por abstenção/confusão.
5. **Publicação governada:** comparar decisão anterior e nova;
   manter rastros imutáveis (modelo, scores, candidatos,
   políticas, UUID/estado, timestamps, execução, autor), aplicar
   transições semânticas somente quando mudarem e
   recompor Gold/Serving de todos os afetados de maneira
   transacional/consistente. Respeitar ledger DT-05 e regras de
   publicação histórico/auditoria.
6. **Lotes confiáveis:** checkpoint durável, exclusão entre
   jobs incompatíveis, retry/fencing, idempotência, retomada de
   run interrompido, métricas de elegíveis/avaliados/resolvidos/
   alterados/abstidos/conflitos e prova de que nenhuma página
   de RESOLVIDOS foi perdida por truncamento/timeout.
7. **Governança e promoção:** reportar impacto/risco e custo
   antes de publicar, com aprovação conforme perfis. Após
   troca de modelo, preservar a possibilidade de coexistência
   **explicitamente identificada e auditada** de decisões
   antigas/novas até o replay extraordinário. Não declarar
   toda a Gold recalibrada apenas por ter ativado um modelo.
8. **Nenhum novo scheduler** de varredura temporal automática.
   A frequência e o universo são decisões operacionais
   expressas; execução não periódica é regra padrão.

## 4. Provas de aceite futuras (não executadas nesta decisão)

- **CPF tardio em agrupamento probabilístico errado:** titular vai
  para sua âncora; membros restantes são reavaliados no escopo
  solicitado, sem herdar UUID/fatos indevidos.
- **RESOLVIDO sem nova entrega própria:** chega candidato novo
  ou muda projeção de terceiro; operação explícita com universo
  abrangente detecta e revisa a decisão anterior quando
  necessário, conservando históricos e fatos.
- **Modelo A → B:** depois da ativação governada de B, a
  execução extraordinária avalia RESOLVIDOS com B, sem usar
  `REPLAY` histórico de A para simular reavaliação.
- **Replay histórico A:** reproduz resultados com A e
  universo/candidatos imutáveis, não altera o modelo do run
  original nem equivale à DT-22.
- **Sem mudança semântica:** resultado idempotente, nenhuma
  duplicação de ledger ou materialização, raw auditável.
- **Falha e retomada:** interrupção entre lotes, expiração,
  retry/reexecução sem duplicação; checkpoints/contagens
  fecham e provam não truncamento.
- **Concorrência/escopo/segurança:** job concorrente/sem
  autorização, fingerprint divergente ou âncora CPF conflitante
  é recusado (fail-closed) com evidência e sem publicação parcial.
- **Desempenho:** massa sintética grande e estimativa
  auditável de custo e janelas; não inferir escala HML/PROD de
  um teste unitário nem usar IBGE original para ensaio CI.

## 5. Prioridade e impactos no encerramento da proposta

**Prioridade:** **POSTERGADA**, para depois do fechamento do escopo
arquitetural principal, salvo se o Ensaio/avaliação de risco exigir
antecipação. **Não é bloqueador artificial da documentação ou
da conclusão da proposta de arquitetura**.

**Não equivale a encerrar débitos distintos:** DT-05 (replay
histórico/fidelidade NAS), DT-15 (governança/decisão de modelo),
validação estatística V8, segurança/IdP/HML/PROD, cancelamento
confirmado de RunOnce, telemetria durável e melhorias de CI
mantêm critérios próprios.

**Entrega desta DT-22 será comprovada somente por** PR de código/
schema/runbooks com CI e E2E efetivos da **HEAD exata**, decisão
institucional quando aplicável e evidências do escopo da reavaliação.
A criação deste documento, a existência de `FULL` ou a CI verde
de documentação **não** constitui aceite técnico DT-22.
