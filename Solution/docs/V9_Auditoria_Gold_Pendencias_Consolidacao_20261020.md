# V9 — medir o Gold FINAL sem esconder identidades pendentes

**20/10/2026 — diagnóstico, não mudança de política.** Continuação da PR #1160
(`gold-runner-study.json`, TRAIN_SELECTION, 9.240 fontes de 5.003
identidades-base, 3.382 vínculos corretos, três falsos e 2.788
`INDEFINIDA`). A CI do último commit de #1160, inclusive o job
`v9-physical-seed42-probe`, terminou com `success`.

## Interpretação obrigatória dos denominadores

- `3382/3401=99,44%` é **recall condicional à presença da identidade
  verdadeira no Gold ANTES da última tentativa**. Mantido como métrica
  de diagnóstico incremental; não mede toda a consolidação.
- `3382/(9240-5003)=79,82%` é razão descritiva de vínculos aceitos
  corretos sobre **mínimo de arestas para conectar identidades**.
  Não é recall de pares; também não substitui a precisão ou a
  recuperação de identidades. No caso de uma pessoa com três fontes,
  existem três pares positivos mas só duas arestas mínimas.
- `3067/5003` compara referências Gold com identidades verdadeiras,
  não prova cobertura: a mesma pessoa pode estar em duas referências,
  enquanto uma referência pode conter pessoas distintas.
- `2788/9240=30,17%` é pendência **final publicada**, não o total
  `NAO_RESOLVIDO` bruto (que também inclui criação de referência).

## Nova instrumentação em código C#

`V9GoldEndToEndAudit.Measure` recebe SOMENTE o gabarito da massa
sintética e os UUIDs Gold **já publicados** no banco descartável; não
entra no blocking, FS/TF, Resolve nem na criação de UUIDs. Calcula
a partição final sobre toda a massa, incluindo fontes pendentes:

- `AllTrueObservationPairs = Σ_p C(n_p,2)`: universo positivo completo,
  sem exclusão de fontes cuja pessoa nunca entrou no Gold.
- `CorrectGoldObservationPairs = Σ_(uuid,p) C(n_uuid,p,2)` e
  `FalseGoldObservationPairs = Σ_uuid C(n_uuid,2) - Correct`.
- `FinalGoldPairRecall = Correct/AllTrue`;
  `FinalGoldPairPrecision = Correct/(Correct+False)`.
  Denominador zero => `null`, nunca resultado 100% artificial.
- Cobertura de **pessoas verdadeiras distintas** observadas em Gold,
  sem dividir referências por pessoas; pessoas verdadeiras ausentes.
- Fragmentação: identidade verdadeira com fontes publicadas em dois ou
  mais UUIDs; fusão: UUID que contém fontes de duas ou mais pessoas.
- Fontes pendentes, proporção pendente, fontes em referência e
  número de referências distintas; tudo após a última reavaliação.

O Runner lê o estado final de `pessoa_origem_progressiva` + `gold.pessoa`
numa consulta isolada apenas para diagnóstico. Faz *fail closed* quando
fonte/UUID é desconhecido ou quando número de UUIDs impuros e pendências
não reconcilia `outcomes`, `finalOwnership` e publicação.

`PendingReasonsAsync` consulta o último `linkage_resultado_id`
(publicado) por observação, distingue `status` bruto,
`motivo` bruto e `motivo_publicacao`. Agrega SOMENTE as fontes
`INDEFINIDA` por categorias restritas e registra contagens. O motivo
bruto não é exportado, por poder carregar texto livre; se um código não
pertencer ao vocabulário reconhecido, surge
`OUTRO_REQUER_AUDITORIA`, não é automaticamente classificado como
sucesso ou falta de candidato.

**Invariante:** `Sum(PendingReasons.Sources) == PendingSources ==
Count(PublicationResult=INDEFINIDA)` e todos os IDs de fontes têm
exatamente uma linha de resultado mais recente. Nunca publicar
estatística de um estado parcial após erro.

## Decisão de criação de identidade sem CPF já vigente

`ProbabilisticLinkageBatchRunner.ProgressivePublicationSql` já
promove `initial_uuid` para `NOVA_IDENTIDADE` quando
`raw_status=NAO_RESOLVIDO`, `raw_motivo LIKE SEM_CANDIDATO_%`,
a busca foi completa, existe origem persistente/UUID inicial e
nenhum destino externo impede. A migration DT-10 recusa criação por
busca parcial ou sem recibo de universo. Não se deve usar score
abaixo de threshold como prova de não identidade, nem exigir
simultaneamente mãe+nascimento em toda fonte: passes elegíveis
dependem do contrato.

## Próxima rodada após o merge deste diagnóstico

1. Ler as **contagens reais** do `pendingReasonBreakdown` gerado
   pelo Runner numa nova instância LocalDB exclusiva. Não inferir
   antecipadamente que as 2.788 pendências são erro de blocking ou de
   política: podem ser competição, score baixo, dados insuficientes.
2. Instrumentar a perícia local restrita dos três erros, inclusive
   referência impura; cruzar grupo-alvo antes/depois de cada onda,
   score, segundo score, margem e motivo, sem PII em artifacts públicos.
   Não ajustar threshold apenas para zerar FP na mesma massa.
3. Repetir **no mesmo TRAIN** com ordem invertida e permutações
   determinísticas pré-declaradas. Comparar UUIDs por gabarito/partição
   final, não seus valores aleatórios; nunca usar VALIDATION nova
   consumida como seletor.
4. Aprovar protocolo de independência/cluster e de indicadores
   Gold antes de abrir TEST. Não declarar Clopper–Pearson válido para
   decisões dependentes de outras referências sem revisão independente.
5. Persistir snapshot aprovado em `ref`, ligar Runner/Gold à versão
   imutável e seguir DT-15 separadamente. Nenhum desses passos
   é autorizado por CI verde do diagnóstico.

## Escopo protegido

Apenas código C# e testes de instrumentação de ensaio sintético
em `JornadaHarness` LocalDB descartável. Não altera decisão FS,
`ProbabilisticLinkageBatchRunner`, stored procedures, política de
`NOVA_IDENTIDADE`, V8/Gold ativo, IBGE original, DT-15,
HML/PROD, JornadaLocal, NODE/NAS, TF, TRAIN/VALIDATION/TEST.
Testes unitários usam dados inventados de seis fontes para validar
matematicamente pares, fragmentação, fusão e casos nulos; não são
certificação estatística nem autorização operacional.
