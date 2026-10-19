# V9 — congelamento científico local e TEST independente

`V9CalibrationFreezeService` complementa o codec portátil entregue em #1157 e reutiliza `V9FittedSnapshotArtifact`. Não publica referências SQL, não seleciona modelo ativo e não emite aprovação DT-15.

O novo congelamento exige `Payload.CandidateGrouping` igual à configuração do
envelope: posterior e tamanho máximo integram também o SHA do fit. Artefatos V2
antigos sem esse campo continuam legíveis para preview, mas são recusados como
congelamento integral. A política não pode relaxar o teto de FDR de 1% do seletor
por fonte. O replay de coorte usa `V9FrozenSourceResolveReplay`, sem refit.

O diretório é a raiz de uma linhagem científica. A mesma raiz deve acompanhar todas as recalibrações; copiá-la ou trocar o caminho não cria autorização para repetir TEST. O serviço protege escritores locais concorrentes com lease exclusivo e publica arquivos com criação exclusiva e rename. Não sobrescreve snapshots nem envelopes existentes. Em interrupção entre gravação do fit e envelope, somente o mesmo conteúdo pode reaproveitar o fit órfão.

## Fluxo

1. Estimar no TRAIN, medir Rank → Resolve no VALIDATION e obter o resultado Pareto 2D com política explícita.
2. Exportar o fit completo da configuração medida, inclusive a margem escolhida. `CandidateOption.ConfigurationFingerprintSha256` deve ser `V9FittedSnapshot.ContentSha256`, e não o fingerprint parcial do ajuste. `Payload.OptionId` permanece o identificador original do adapter.
3. Preencher `SelectedConfigurationId` usando `V9ParetoSourceSelection.ConfigurationId(optionId, grouping, margin)`. Informar os valores de posterior mínimo e tamanho máximo efetivamente consumidos pelo agrupamento complete-link; eles integram o envelope.
4. Chamar `FreezeAsync`: o serviço recalcula elegibilidade, fronteira e desempate a partir da tabela e política. Sem opção elegível retorna `null` sem escrever artefatos. Primeiro congelamento admite apenas `BOOTSTRAP_AUTOMATIC`; versões seguintes exigem `MANUAL_SELECTION`, ator e SHA do predecessor atual. A seleção manual deve pertencer à fronteira elegível.
5. Fixar o SHA do envelope externamente e carregar com `ReadPinnedAsync`. O envelope inclui catálogo completo/versionado, proveniência, política, métricas, método estatístico, tabela Pareto completa e SHA do fit. O fit contém contratos, passes ordenados, m/u conjunto e nascimento, prior, TF, threshold e margem.
6. Antes de abrir rótulos TEST, reservar uma execução com `ReserveIndependentTestAsync`. Reservar corpus novo, seed nova, protocolo, alpha, cap pré-declarado, snapshot/envelope e hash do quadro inteiro de identidades. `FingerprintTestIdentityGroups` calcula esse hash sem rótulos. Seeds históricas 42/43/44 são recusadas.
7. Concluir com recibo e `V9IndependentTestEvidence`: todo quadro pré-blocking, inclusive identidades sem aceites, métricas, hashes de corpus/identidades e digest calculado por `FingerprintTestEvidence`. O serviço recalcula o limite CP de erro por identidade/razão de médias para uma configuração TEST, com alpha e cap reservados, confere TP/FP e política congelada. Não escolhe outra configuração a partir de TEST.

## Evidência e limites

Uma reserva bem-sucedida consome a tentativa mesmo se o processo falhar antes do recibo. Estudo, execução, seed, quadro de identidades, corpus, envelope e conteúdo do fit não podem ser reutilizados na mesma linhagem. A rejeição de hashes iguais aos de TRAIN/VALIDATION considera todos os snapshots anteriores; hashes diferentes não demonstram ausência de sobreposição parcial. Recibos idênticos são idempotentes; recibos divergentes não substituem resultado anterior.

O código não demonstra IID por rótulos, contagens ou hashes. Ausência de referências de atestação independente de amostragem e medição física impede resultado `Passed=true`, mesmo com FDR pontual zero. Essas referências precisam vir de revisão externa; um SHA não autentica seu conteúdo e o serviço não é uma autoridade de certificação. O quadro e o cap precisam representar o desenho amostral real e a medição precisa ser física de ponta a ponta. Os testes unitários usam fixtures explícitas e não constituem evidência estatística operacional.

Congelado, TEST concluído, aprovado para publicação e ativado são estados distintos. Aprovação/publicação/ativação continuam no fluxo governado existente; nenhuma dessas operações é exposta por este serviço.
