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

## 3. Seleção física das três ordens (contrato do harness)

O `V9GoldHarnessCommand` agora aceita um **sétimo argumento opcional**
somente com um dos identificadores pré-declarados (o sexto argumento
continua sendo o diretório de saída). O contrato de seis argumentos
mantém **exatamente** a ordem padrão `BASELINE_G0_G1_G2_G3`.

```text
--v9-gold-harness <SHA.selection.json> <finalist-validation.json> <SHA.fitted.json Gold> <CSV TRAIN original> <saída nova> [BASELINE_G0_G1_G2_G3|REVERSE_G3_G2_G1_G0|INTERLEAVED_G1_G3_G0_G2]
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
com certificação municipal. Não habilitar NOVA_IDENTIDADE por score
abaixo do piso, nem ativar V9/DT-15, abrir TEST, usar VALIDATION
consumida ou alterar V8, Gold operacional, IBGE original, JornadaLocal,
NODE/NAS ou HML/PROD.
