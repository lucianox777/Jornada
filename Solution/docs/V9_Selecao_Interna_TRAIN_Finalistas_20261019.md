# V9 — seleção interna no TRAIN e avaliação única de finalistas

**Fluxo manual corrente, 19/10/2026.** Substitui a busca automática de grades na VALIDATION. Os experimentos anteriores continuam como histórico; não se reinicia sua multiplicidade em três. A implementação adiciona uma etapa de seleção ao calibrador C# existente, sem outro estimador m/u ou scorer.

## Contrato e comandos

1. `--v9-calibrate <plano.json> <CSV-original> <diretório-novo>` lê atributos somente de **TRAIN**. Para VALIDATION/TEST, o leitor inspeciona apenas metadados de identidade/partição para detectar vazamento; não classifica nomes, não interpreta nascimento, não estima parâmetros ou calcula decisões desses holdouts. SHA do arquivo serve para integridade.
2. `V9InternalTrainingSplit` ordena identidades pelo SHA de versão, semente e ID, e separa exatamente `floor(0,70 × N)` para TRAIN_ADJUST. Todas as observações da mesma identidade ficam juntas. Manifesto registra semente, fração, atribuições e SHAs. Reordenar o CSV de entrada não altera a divisão.
3. Blocking inicial e m/u/prior são estimados usando somente TRAIN_ADJUST. O **M/U de ajuste vem da união SQL deduplicada desse frame**, rotulado pela identidade sintética conhecida. U pré-blocking usado para propor regras não substitui U físico. IBGE marginal não substitui esse universo na TF. TF positiva é recusada neste fluxo.
4. TRAIN_SELECTION recebe Rank→Resolve com agrupamento FS BLIND. O adaptador histórico chama seu slot de avaliação de `VALIDATION`; o envelope identifica explicitamente que, neste percurso, esse slot contém **TRAIN_SELECTION**, sem coorte externa. Os campos de proveniência do snapshot permanecem vinculados ao ajuste/seleção internos; não são reescritos depois da avaliação externa.
5. O primeiro lote usa opções grossas declaradas e controles de blocking; os seguintes exploram limiar/margem com passos menores e vizinhos que adicionam/removem um passe do catálogo disponível. Não existe teto arquitetural de quatro passes. Memória, quantidade de configurações e lotes têm orçamento explícito. A busca é heurística, não enumeração integral de todos os modelos.
6. P95 ponta a ponta, fanout falso P99 e suporte observado por célula são restrições anteriores aos resultados. Rejeições físicas e estatísticas ficam na tabela. Configurações já rejeitadas por suporte/fanout não gastam vinte repetições adicionais de latência. Configurações viáveis têm P95 medido com vinte replays SQL/scorer/Resolve congelados. Conexões e tabelas temporárias são independentes entre workers; opções com mesmos passes/margem compartilham materialização SQL.
7. A fronteira usa recall **global por fonte**, com elegibilidade anterior ao blocking, e FDR **depois do Resolve**. Faixas de risco são declaradas no plano. Equivalências de métricas são deduplicadas com desempate por SHA; rejeitados não desaparecem da evidência.

O plano inicial reproduzível contém a união de 19 passes e o baseline de três passes, três alphas (0,1/0,5/1), três thresholds (0,90/0,95/0,99) e margens 0,5/2: **36 configurações iniciais**. Esses controles não significam que todos os outros blocking foram avaliados. A geração de candidatos inicial pode usar a descoberta TRAIN existente; o catálogo declarado é outra opção explícita.

## Parada e finalistas

O bootstrap é **pareado por identidade**, reamostrando as mesmas identidades para duas configurações e recalculando as razões de TP/eligíveis e FP/aceitos. Não usa `sqrt(p(1-p)/N_identidades)` como se cada identidade produzisse uma decisão Bernoulli. As resoluções de recall e FDR são distintas, incluem piso prático pré-declarado e resolução de um evento para não produzir certeza artificial quando os erros observados são zero.

O progresso registra ganhos de recall nas mesmas faixas de risco, melhoria de FDR sem reduzir recall, resoluções, maior ganho em unidades da resolução pareada e lotes consecutivos sem ganho material. Dois lotes sem ganho encerram uma **heurística de exploração**. Fim de orçamento ou vizinhança é registrado separadamente; não comprova ótimo global, e a curva não é prova de independência ou de que toda exploração adicional seria inútil.

A regra de até três finalistas é congelada antes da avaliação externa:

1. Maior recall observado dentro de epsilon operacional.
2. Um representante distinto de faixa mais conservadora, quando existir.
3. Distância quadrática ao ideal após normalização min–max; completa com candidatos distintos dessa ordenação se necessário.

Assim, o ponto de maior recall sob epsilon não desaparece por escolher somente os vizinhos do ponto de distância ideal. Distância ao ideal não é apresentada como joelho geométrico. Uma fronteira curta produz um ou dois finalistas; os três são um máximo, não cópias artificiais.

O envelope `<SHA>.selection.json` contém plano, manifesto interno, todas as avaliações/rejeições, fronteira, curva, SHAs dos finalistas e regra. Os `.fitted.json` já estão congelados; não há refit após consultar o holdout. `selection-reference.json`, `training-split-manifest.json` e `gain-by-batch.json` permitem inspeção direta.

## VALIDATION externa

`--v9-validate-finalists <plano> <SHA.selection.json> <CSV-novo> <diretório-novo>` exige o envelope e os mesmos parâmetros, um a três snapshots fixados e um manifesto do gerador C# compatível. Verifica SHA do manifesto/CSV e cap populacional **4**, separadamente do guard de tamanho do cluster BLIND, que permanece 16.

São recusadas seeds históricas 42/43/44, a seed da massa de ajuste e o mesmo arquivo de origem. Identidades de coortes sintéticas diferentes recebem namespace do manifesto, pois o gerador reutiliza IDs locais `P0000000` entre seeds. Esse namespace evita colisão administrativa; não comprova independência estatística.

Antes de ler os atributos da nova VALIDATION, o processo reserva atomicamente uma exposição por seed para o envelope e os finalistas exatos. Registro padrão: `%LOCALAPPDATA%/Jornada/V9/holdout-exposures` no Windows, ou diretório correspondente de dados locais do usuário. Fixtures usam `JORNADA_V9_HOLDOUT_REGISTRY` isolado. Tentativa interrompida conserva a reserva; não se apaga a tentativa nem se fornece outro trio com novo alpha no mesmo holdout. Registro local não é prova universal de ausência de consultas externas: a governança da coorte continua necessária.

A multiplicidade externa é o número real de finalistas congelados, **até três**. O bound existente divide alpha entre finalistas **e** componentes do numerador/denominador: CP estima a probabilidade de qualquer FP por identidade, e Hoeffding limita inferiormente a média de aceites; cap 4 converte para uma razão superior condicional. Não confundir probabilidade de algum erro com FDR dos vínculos.

O melhor resultado condicional é o de maior recall que respeita FDR superior, recall mínimo declarado, P95 e fanout. Se nenhum passar, o relatório registra a reprovação. A competição entre identidades ainda impede atestar IID automaticamente: `selectedApprovedSnapshotSha256` permanece nulo e `certified=false`. Mesmo um vencedor condicional não autoriza publicação `ref`, DT-15 ou ativação. TEST permanece reservado para uma avaliação posterior do vencedor governado.

## Evidência executada

Instância exclusiva `JornadaV9Integral20261019`, banco descartável `JornadaHarness`. Nenhuma alteração em JornadaLocal, referência IBGE, nós DEV, HML ou PROD. GPU e benchmarks não foram retomados.

**Prova de encadeamento, não certificação:** corpus C# de 1.000 identidades; 548 identidades sem CPF em TRAIN, divididas em 383/165. A fixture declara piso zero por célula exclusivamente para testar o encadeamento, identificada como `PLUMBING_FIXTURE_NOT_CERTIFICATION`; esse piso é recusado no propósito normal de bootstrap. Foram 22 configurações, três lotes, parada após dois lotes sem ganho material e um finalista distinto. Avaliação com seed diferente executou vinte replays do mesmo snapshot, sem refit e com multiplicidade 1. O finalista não passou no bound de FDR. Reutilização da coorte e seed histórica foram recusadas. Nenhum modelo foi aprovado.

**Rodada do bootstrap de 30 mil:** mesmo clean conservador aceito, CSV SHA `28bbb9f4c63c97c353e7f29cdbc77091a4773b2f4c596e46103edadd93635d17`. TRAIN sem CPF contém **16.676 identidades**: **11.673 TRAIN_ADJUST** e **5.003 TRAIN_SELECTION**. Todas as **36 configurações** foram avaliadas e mantidas na tabela. A política conservadora desta rodada exige ao menos um M e um U observados em cada uma das 81 células; não confunde suavização com suporte:

| Blocking | M de ajuste | U de ajuste | Células com M | Com U | Sem M nem U |
|---|---:|---:|---:|---:|---:|
| União de 19 passes | 14.314 | 505.169 | 38 | 48 | 25 |
| Baseline de 3 passes | 13.997 | 13.472 | 30 | 20 | 44 |

As 36 falharam nesse piso; 18 também excederam fanout. Resultado `NO_FEASIBLE_CONFIGURATION_NO_HOLDOUT_USED`, **zero finalistas**, **nenhuma avaliação externa real de VALIDATION/TEST**. A política de suporte usada é explícita, não demonstra que todas as células tenham probabilidade positiva. Antes de ampliar N ou rever uma exigência por célula, justificar alcance/zeros estruturais pelo contrato e calcular o desenho amostral; não relaxar o piso para obter aprovação, nem preencher a falta de evidência com probabilidades suavizadas.

Artefatos compactos: `data/calibration/v9/nested-train-selection-exploratory/`. Resultados completos e snapshots ficam nos artefatos da CI. A suíte local executou **1.791 testes C# e 117 fixtures Python**, além da prova SQL com um holdout exclusivo da fixture. Build Release com SDK 10.0.113; PKCS 9.0.21 preservado.

## Reprodução

```powershell
python Solution/scripts/v9-nested-calibration-study.py <observacoes.csv> <plano-novo.json>
dotnet <Jornada.Linkage.Parameters.Worker.dll> --v9-calibrate <plano-novo.json> <observacoes.csv> <diretorio-novo>
python Solution/scripts/v9-nested-calibration-verify.py search <diretorio-novo>
```

Conexão `ConnectionStrings__Jornada` somente para banco descartável `JornadaHarness`. Para exportar a curva com Matplotlib 3.10.8: `v9-nested-gain-plot.py <gain-by-batch.json> <novo.svg>`. A CI reproduz a busca interna, a prova pequena de finalistas e as rejeições de holdout; **não executa a antiga grade na VALIDATION histórica**. As CLIs antigas permanecem como diagnósticos históricos explícitos, sem promoção.

Pendências de homologação: resolver suporte/suficiência das células com contrato estatístico defensável, demonstrar incerteza residual aplicável ao pipeline, medir Runner incremental/Gold real, obter finalistas viáveis, executar nova VALIDATION real e TEST congelado e cumprir promoção `ref`/DT-15. Merge do fluxo não encerra essas pendências nem ativa V9.
