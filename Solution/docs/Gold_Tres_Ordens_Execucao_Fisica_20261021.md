# Gold V9 — ligação física das três ordens TRAIN ao Runner incremental

**21/10/2026.** Esta alteração liga o protocolo puro
`GoldTrainThreeOrderProtocol` ao `V9GoldHarnessCommand` em
instâncias **LocalDB próprias, vazias e descartáveis**. Permite três
execuções físicas com o mesmo CSV TRAIN_SELECTION e snapshot Gold
congelado e uma comparação posterior, sem abrir TEST nem promover
novo modelo. **Código habilitado não equivale a três execuções realizadas.**

## Execução manual isolada

O comando histórico permanece válido, assumindo a ordem padrão
`BASELINE_G0_G1_G2_G3`:

```text
--v9-gold-harness <SHA.selection.json> <finalist-validation.json> <SHA.fitted.json Gold> <CSV TRAIN original> <saída nova>
```

Opcionalmente, acrescentar um **sexto argumento posicional depois da
flag** para ordem `REVERSE_G3_G2_G1_G0` ou
`INTERLEAVED_G1_G3_G0_G2`. Os únicos IDs aceitos são aqueles
pré-registrados, e as fontes devem abranger G0/G1/G2/G3 exatamente
uma vez. Não selecionar a ordem conforme os acertos de uma execução.

Cada execução precisa ter a **sua própria instância**
`(localdb)\\JornadaV9GoldYYYYMMDD_a1b2c3d4`, um banco
`JornadaHarness` inicialmente vazio, propriedades residentes
`Jornada.EnvironmentProfile=Development` e
`Jornada.V9.ExperimentalHarness=PINNED_FIT_PREVIEW_NO_ACTIVATION`.
Preparar pelo comando `--v9-gold-harness-create <diretório novo> <Solution/database>` conforme o contrato
existente; nunca apontar para JornadaLocal, HML, PROD ou instância
compartilhada. Usar **a mesma seleção, mesmo JSON de validação
condicional, mesmo fitted Gold e mesmo CSV**. Cada saída deve ser
um diretório novo. Não apagar bancos existentes: preparar novas
instâncias em vez de resetar estado compartilhado.

O harness executa o **Runner C# verdadeiro**, procedimentos SQL de
publicação/Gold e reavaliação final de pendências. O novo parâmetro
afeta apenas a ordem G0/G1/G2/G3, não o FS, a TF, limiares,
blocking, chaves temporais, a regra de NOVA_IDENTIDADE ou o código
operacional.

## Evidência privada e relatório agregado

Cada saída local contém:

- `gold-runner-study.json`: métricas por pares, B-cubed padrão
  e coverage-aware, pendências por motivo, FDR pós-Resolve e
  ordem executada, sem gabaritos pessoais nem UUIDs individuais.
- `gold-order-trial-private.json`: **material sensível de ensaio**
  com a associação ID estável no CSV TRAIN → rótulo-base sintético
  e UUID Gold gerado na execução. **Não anexar a PR, issues,
  logs ou artefatos públicos.** É usado apenas localmente para
  comparar a identidade dos agrupamentos entre execuções.
  Não inserir essas informações no scorer ou no blocking.

A tabela SQL de observações usa `IDENTITY` em ordem de chegada;
por isso a comparação por `pessoa_observacao_id` SQL é INCORRETA.
`GoldTrainOrderEvidence.Create` estabelece, após publicação,
o cruzamento SQL → `Source.Id` estável derivado da sequência do
mesmo CSV TRAIN. Recusa fonte duplicada, externa, UUID vazio,
ordem divergente ou número de fontes incoerente. Um fingerprint
efêmero distinto identifica cada execução, mas **fingerprints
auto-declarados não comprovam isolamento físico**; a prova depende
também dos guards residentes e da preparação da instância.

Comparar **somente depois** de obter as três evidências privadas:

```text
--v9-gold-compare-orders <.../baseline/gold-order-trial-private.json> <.../reverse/gold-order-trial-private.json> <.../interleaved/gold-order-trial-private.json> <comparacao-agregada-nova.json>
```

O comando de comparação roda **sem conexão SQL**, verifica
três ordens exatas, SHAs idênticos do corpus e fitted Gold,
gabarito das mesmas fontes, fingerprints distintos,
status TRAIN_SELECTION e proibição de TEST/ativação. O resultado
contém somente agregados e a equivalência **da partição por
fonte**, não compara bytes dos UUIDs Gold aleatórios.

## Limitações

O comparador não substitui a perícia dos **três falsos vínculos**
da primeira execução nem demonstra, por si, redução dos 2.788
pendentes. As três ordens físicas ainda precisam ser executadas
em Windows LocalDB com SQL isolado e reportadas com seus resultados
reais. O limiar inferior da PR #1162 continua hipótese diagnóstica:
não cria UUIDs por score baixo e não mede seus efeitos em cascata.
Em especial, `1 - PairPrecision` NÃO é FDR pós-Resolve de
associações aceitas; a FDR tem seu contador por decisão no
`gold-runner-study.json`.

Certificação real, seleção de parâmetros, promoção de ref,
TEST congelado e autorização DT-15 seguem apartados, condicionados
a validação independente. Não inferir desempenho municipal a
partir de uma massa sintética.
