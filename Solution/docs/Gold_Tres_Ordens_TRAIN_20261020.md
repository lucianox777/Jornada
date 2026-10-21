# Gold incremental — três ordens TRAIN com fit congelado

**Protocolo pré-declarado de sensibilidade à ordem de ingestão.**
O protocolo está conectado ao Runner C# físico e inclui modo experimental
restrito de limiar inferior; **não declara que qualquer corrida física da matriz
foi realizada**.

## 1. Três ordens imutáveis

| Identificador | Ordem das quatro ondas |
| --- | --- |
| `BASELINE_G0_G1_G2_G3` | G0 → G1 → G2 → G3 |
| `REVERSE_G3_G2_G1_G0` | G3 → G2 → G1 → G0 |
| `INTERLEAVED_G1_G3_G0_G2` | G1 → G3 → G0 → G2 |

As ordens foram estabelecidas antes de observar qualquer resultado
comparativo. Não inverter a preferência em função de acerto, custo ou
pendência. É obrigatório preservar **todos os gerentes e todas as fontes**,
executando cada tentativa em uma **instância LocalDB exclusiva, criada
vazia**, com o mesmo corpus TRAIN_SELECTION, snapshot FS fixo, regras
de blocking e TF, SQL e versão do Runner. As três ordens são selecionáveis no Runner real, mas precisam ser executadas
manualmente em LocalDB isolados.

## 2. Contrato implementado

`GoldTrainThreeOrderProtocol.ResolveOrder` aceita apenas estas três
ordens e exatamente G0/G1/G2/G3 sem repetição. O comparador
`GoldTrainThreeOrderProtocol.Compare` exige três provas de corrida
distintas e o contrato `TRAIN_CSV_OBSERVATION_ID_NOT_SQL_ID_V1`:
**as chaves para comparar partições são os IDs estáveis do CSV TRAIN**,
NUNCA o `pessoa_observacao_id` gerado por IDENTITY no SQL, pois muda
quando as ondas são inseridas em ordens diferentes. O harness deriva
`Source.Id` do campo original `observacao_id` do CSV TRAIN por
SHA-256 com prefixo de domínio e truncamento a 63 bits positivos,
recusando chaves repetidas/colisões. O identificador original do CSV
contém código sintético de identidade e **não é exportado**.
Não utilizar número de linha do CSV nem contador de linhas filtradas.
O harness precisa converter `pessoa_observacao_id` → `Source.Id`
estável antes de alimentar a comparação. O par
`gestor`/`OBS_<Source.Id>` permite auditar a associação
somente dentro do ensaio descartável.
O protocolo exige execuções distintas, mesmo SHA256 do TRAIN, mesmo SHA256 do snapshot, mesmos
rótulos independentes para todas as observações e sequência executada
exatamente igual à pré-declarada. Recusa flag TEST, ativação e onda
não publicada. Atestações do chamador sobre banco SQL isolado devem
ser verificadas externamente: meras flags e hashes de texto não são
prova criptográfica de execução real.

Em cada ordem usa `V9GoldEndToEndAudit.Measure` e
`GoldPartitionBcubedAudit.Measure` sobre os mesmos mapas de origem
e referências Gold finais. Reporta: fontes, pendentes, cobertura de
identidades verdadeiras, fragmentação, impureza, pares verdadeiros,
pares corretamente/erroneamente agrupados, precisão e recall por pares,
B-cubed convencional e a variante conservadora coverage-aware.

O comparador verifica a **invariância exata de partição por fontes**:
para cada UUID Gold representa seu conjunto de observações pelo menor
ID de origem; pendentes ficam em categorias singleton explícitas.
A igualdade dos UUIDs aleatórios entre execuções não é necessária,
e mera igualdade das contagens agregadas não prova invariância.
Um par de fontes agrupadas em ordens diferentes reprova a invariância.

`LargestPendingDifference` e `LargestFalseGoldPairDifference`
resumem sensibilidade quantitativa. `IncrementalLinkageFdrComputed`
permanece `false`, pois a FDR pós-Resolve exige gabarito **por
decisão individual aceita**; não é igual a `1 - PairPrecision`.
`IndependentlyCertified`, `TestUsed` e
`OperationallyActivated` permanecem `false`.

## 3. Seleção física das três ordens (contrato do harness)

O `V9GoldHarnessCommand` aceita ordem opcional e/ou `LOWER_NONMATCH=<decimal>`.
A opção de limiar é explícita e usa ponto decimal invariante, com
`0 < piso < limiar superior fixado no snapshot`. O contrato antigo de seis
argumentos mantém **exatamente** a política atual e a ordem padrão.
Os três finalistas não são deduzidos pelo CLI: devem vir da fronteira
Pareto de TRAIN validada, sem seleção automática ou teto de duplicatas.

```text
--v9-gold-harness <SHA.selection.json> <finalist-validation.json> <SHA.fitted.json Gold> <CSV TRAIN original> <saída nova> [BASELINE_G0_G1_G2_G3|REVERSE_G3_G2_G1_G0|INTERLEAVED_G1_G3_G0_G2] [LOWER_NONMATCH=<decimal>]
```

Antes de qualquer escrita SQL, valida que as quatro ondas
G0/G1/G2/G3 são exatamente as quatro do TRAIN original e aplica a
ordem pré-fixada por `GoldTrainThreeOrderProtocol.ResolveOrder`,
sem inspecionar os resultados. Preserva todos os filtros de snapshot,
CSV imutável, modelo RASCUNHO, universo/persistência do Runner e
isolamento LocalDB `JornadaV9Gold...` / `JornadaHarness`.
Cada execução permanece **em instância LocalDB exclusiva e vazia**.
A flag **não** cria bancos, não apaga tabelas e não executa
automaticamente três bancos sequenciais.

O arquivo `gold-runner-study.json` inclui `goldThreeOrderTrial` com:
versão do protocolo, ordem solicitada e executada, fingerprints
do CSV e snapshot fixados, fingerprint não autenticado da execução
isolada e `stablePartitionSha256` (SHA-256 da partição **final por
IDs estáveis do CSV**, com marca de publicado/pendente). O digest
canonicaliza cada UUID Gold pelo menor Source.Id derivado do
`observacao_id` CSV de seu grupo; portanto,
UUIDs aleatórios diferentes com **a mesma partição** geram o mesmo
digest. Nenhum UUID, rótulo verdadeiro ou identificador por observação
é exportado nesse relatório. O digest é um auxiliar de comparação e
**não** substitui a prova de execução independente de cada SQL.

A auditoria B-cubed, os pares, fusões e razões agregadas de pendência
continuam em `gold-runner-study.json` e são calculados apenas
**após a publicação**. Um único relatório **não** equivale ao
`GoldTrainThreeOrderProtocol.Compare`, que exige três ensaios
efetivamente realizados com mapas completos em memória confiável.

## 4. Execução física ainda necessária

Após merge protegido desta biblioteca:

1. **Executar fisicamente** as três ordens (a seleção explícita
   está implementada) em LocalDB distintos, com saídas imutáveis e
   fingerprints diferentes. Cada instância deve chamar o Runner
   real, as projeções Gold SQL e a reavaliação final dos pendentes.
2. Reconciliar os três `gold-runner-study.json`, inclusive B-cubed,
   indicadores por pares, motivos agregados das pendências e FDR
   pós-Resolve efetiva com gabarito por associação; conferir
   igualdade de SHA do CSV/fit e as três ordens esperadas.
3. Avaliar a proposta de limiar inferior APENAS em TRAIN, com
   verdade independente sobre identidade já representada e
   perdas de blocking; seguir a fronteira de Pareto descritiva
   sem impor teto arbitrário ou publicar vencedor automaticamente.
4. Comparar na mesma escala física custo, grau falso, latência,
   casos de fusões e subgrupos de identidades novas, sem generalizar
   massa sintética para 11 milhões de cidadãos reais.

**Atenção:** o comparador puro não é um experimento físico. Não
o confundir com a sonda de calibração SQL de 30 mil registros, nem
com certificação municipal. Não habilitar NOVA_IDENTIDADE **operacional** por score
abaixo do piso, nem ativar V9/DT-15, abrir TEST, usar VALIDATION
consumida ou alterar V8, Gold operacional, IBGE original, JornadaLocal,
NODE/NAS ou HML/PROD.

## 5. Política experimental C# (21/10/2026; execução pendente)

O `V9GoldLowerNonMatchExperiment` reutiliza
`V9LowerNonMatchPreview.Evaluate` e **o mesmo Rank/Resolve FS/TF congelado**;
não executa um segundo scorer. Só a condição
`CandidateRejectedNewReferenceHypothesis` converte o motivo bruto em
`LIMIAR_INFERIOR_GOLD_TRAIN_EXPERIMENTAL` sem alterar posterior, candidato,
status ou decisão de vínculo. O nome do motivo é deliberadamente distinto
de `SEM_CANDIDATO_%`: foram recuperados candidatos reais.

Para publicar essa hipótese **somente na instância descartável**, o SQL do
Runner exige `DB_NAME()='JornadaHarness'`, propriedade Development e
`Jornada.V9.ExperimentalHarness=PINNED_FIT_PREVIEW_NO_ACTIVATION`; qualquer
uso do motivo fora desse domínio aborta a publicação com erro 51822.
Continuam valendo precedências, conflitos, origem persistente, UUID inicial,
recomposição e destino externo. Busca com passes inelegíveis não cria
referência. O próprio harness compara as chaves Gold esperadas contra a
projeção SQL materializada após cada onda; o candidate SQL é UNION completo,
sem TOP/truncamento. Uma busca tecnicamente completa **não é prova** de
recuperação da identidade verdadeira: medir falsas novas identidades contra
o gabarito TRAIN e a sensibilidade às ordens continua obrigatório.

Cada `gold-runner-study.json` contém `lowerNonMatchExperiment`, criação
efetiva por onda, `elapsedMilliseconds`, B-cubed, métricas por pares,
pendências por motivo, FDR e SHA estável da partição. O comparador de três
ordens recusa mistura de políticas ou de limiares.

Para as **12 execuções** (controle + três valores TRAIN × três ordens),
executar o mesmo `--v9-gold-harness` uma vez por banco LocalDB exclusivo
e fornecer os 12 relatórios, em ordem de política
`CURRENT`, `KNEE`, `CONSERVATIVE`, `LOWEST_PENDING`, cada política com
ordens baseline, reversa e intercalada, a:

```text
--v9-gold-compare-matrix <12 caminhos gold-runner-study.json> <saída-nova.json>
```

O comparador `GoldTrainLowerNonMatchMatrixCommand` é offline,
não abre SQL, recusa relatório duplicado, fit/corpus divergente e limiares
iguais entre finalistas. Publica uma tabela de 12 linhas com pares,
B-cubed, fusões/fragmentação, pendências categorizadas, falsos vínculos,
FDR, latência e SHA por partição. Não divulga UUIDs de pessoas, gabarito
por fonte ou identificadores individuais. Os nomes das três alternativas
são rótulos da entrada; não certificam que a escolha Pareto foi validada.
CI/unidades apenas verificam o software: o **ensaio físico e a decisão de
governança permanecem pendentes**.
