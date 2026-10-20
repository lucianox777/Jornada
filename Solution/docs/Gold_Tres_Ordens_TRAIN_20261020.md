# Gold incremental — três ordens TRAIN com fit congelado

**Protocolo pré-declarado de sensibilidade à ordem de ingestão.**
Este trabalho implementa exclusivamente um planejador/comparador C# puro;
**não declara que as três execuções físicas já ocorreram**.

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
de blocking e TF, SQL e versão do Runner. O teste no Runner real só
será executado quando houver entrada de ordem explícita no harness.

## 2. Contrato implementado

`GoldTrainThreeOrderProtocol.ResolveOrder` aceita apenas estas três
ordens e exatamente G0/G1/G2/G3 sem repetição. O comparador
`GoldTrainThreeOrderProtocol.Compare` exige três provas de corrida
distintas e o contrato `TRAIN_CSV_OBSERVATION_ID_NOT_SQL_ID_V1`:
**as chaves para comparar partições são os IDs estáveis do CSV TRAIN**,
NUNCA o `pessoa_observacao_id` gerado por IDENTITY no SQL, pois muda
quando as ondas são inseridas em ordens diferentes. O harness precisa
converter `pessoa_observacao_id` → `Source.Id` do mesmo CSV antes de
alimentar o comparador. O par de nomes `gestor`/`OBS_<Source.Id>`
permite auditar essa conversão a partir das fontes do harness.
O protocolo exige
distintas, mesmo SHA256 do TRAIN, mesmo SHA256 do snapshot, mesmos
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

## 3. Execução física ainda necessária

Após merge protegido desta biblioteca:

1. Ligar a seleção de ordem ao `V9GoldHarnessCommand` com uma flag
   explícita e sem alterar o comportamento padrão. Usar o mesmo
   `--v9-gold-harness`, snapshots já pinados e corpus TRAIN.
2. Rodar as três ordens em LocalDB distintos, com saídas imutáveis
   e `run fingerprints` diferentes; cada execução deve chamar o
   **Runner real**, projeção Gold SQL e reavaliação final dos pendentes.
3. Reconciliar os três `gold-runner-study.json`, inclusive B-cubed,
   indicadores por pares, motivos agregados das pendências e FDR
   pós-Resolve efetiva com gabarito por associação.
4. Avaliar a proposta de limiar inferior APENAS em TRAIN, com
   verdade independente sobre identidade já representada e
   perdas de blocking; seguir fronteira de Pareto sem orçamento
   arbitrário de duplicação ou vencedor selecionado automaticamente.
5. Comparar na mesma escala física custo, grau falso, latência,
   casos de fusões e subgrupos de identidades novas, sem generalizar
   massa sintética para 11 milhões de cidadãos reais.

**Atenção:** o comparador puro não é um experimento físico. Não
o confundir com a sonda de calibração SQL de 30 mil registros, nem
com certificação municipal. Não habilitar NOVA_IDENTIDADE por score
abaixo do piso, nem ativar V9/DT-15, abrir TEST, usar VALIDATION
consumida ou alterar V8, Gold operacional, IBGE original, JornadaLocal,
NODE/NAS ou HML/PROD.
