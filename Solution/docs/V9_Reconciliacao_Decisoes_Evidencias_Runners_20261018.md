# Jornada V9 — reconciliação das decisões, código e runs físicos (18/10/2026)

**Natureza:** relatório de auditoria técnica, não certificação nem ato normativo. Distingue decisões, implementação na master, trabalho na PR #1152 e evidência histórica. A versão candidata não altera a última release selada. Para qualquer conclusão posterior, conferir GitHub HEAD, Actions, artefatos e status da PR.

## 1. Decisões vigentes e grau de comprovação

| Tema | Decisão | Evidência / limite |
| --- | --- | --- |
| Calibrador | **V9 C# único e definitivo** como motor-alvo; não manter calibrador V8 paralelo em DEV | Adaptador FS V9 real, 81 células m/u, prior, fingerprints TRAIN, coordenador. **Não** existe certificação/ativação V9 em HML/PROD. |
| Bootstrap | Gerar **parâmetros iniciais** por calibração explícita; snapshot só entra em **ref** após aprovação; o bootstrap da aplicação **carrega e confere**, não treina ao subir | PR #1151 integrada, merge `2111ad4cddeec571fa5bdd43e182ca3fe1eec533`: `v9-bootstrap-experimental-candidates.json` contém apenas planos HISTÓRICOS e `active=false`; não é modelo publicado. |
| Perfil de referência | **clean** é o único perfil sintético automático de bootstrap; **correlated/independent/field permanecem históricos para reprodução manual** | Mudança de defaults e workflows publicada na PR #1152, não na master enquanto draft. **clean não é estimativa representativa de erros de cadastros brasileiros.** |
| Partições | TRAIN estima; VALIDATION compara; TEST final deve ser **novo e lacrado** até modelo/grades fixados | Split por identidade-base e validação SQL física sem uso de TEST. TEST histórico 42/43/44 já exposto não pode certificar V9. |
| FS/TF | TF=0 (sem contribuição TF), TF=1 (integral), fator Bayes neutro LR=1 e parâmetro ausente/desconhecido são casos diferentes | Capacidade C# de TF positiva para prenome exato e snapshots TRAIN; grade física real executada **somente TF=0**. `U` pós-blocking e frequência populacional de termos têm condicionamentos diferentes; não certificar TF sem justificativa. |
| Blocking | Seleção automática de passes/campos/prefixos pelo calibrador sob orçamento; contar M elegível **antes** de filtrar SQL e deduplicar U | Probe físico com união real no SQL Server descartável; planos #1151 são apenas evidência histórica, não configuração ativada. |
| Rank→Resolve | Uma observação de origem deve resolver para **identidade** candidata; margem entre **identidades distintas**, não entre duas observações da mesma pessoa | Experimento #1152 usa o mesmo núcleo `ResolveRankedCore` em três braços RAW, BLIND e ORACLE, **sem provar formação Gold real nem paridade de todos os guards**. |
| FDR e seleção | Contar TP/FP **após** Resolve e CONFLITO separadamente; FDR agrupada por identidade com bound justificável, multiplicidade e gates | Probe físico mede pós-Resolve, mas **o seletor** `AdaptiveCalibrationModelSearch` continua consumindo métricas por par do adaptador e marcando `ConfidenceBoundForFdrMeasured=false`; não afirmar seleção final pronta. |
| Dados de origem | Pessoa exigida por contrato + mãe ou DOB conforme disponibilidade; não impor todos campos a todo cidadão | Catálogo e comparação binária de nascimento no FS; seleção dinâmica de atributos, comparadores parciais de DOB e guardas em paridade com Runner exigem conferência. |
| Operação | Diagnóstico DT-23 automático/read-only; calibração manual e promoção DT-15 somente com aprovação | Ensaios sintéticos de CI não autorizam V9, não escrevem Gold/PROD/HML, não substituem TEST HML. |

**30 mil pessoas sintéticas** são ponto de partida aceito para bootstrap e não asseguram suporte independente suficiente para todas as 81 células, a cauda de erros, FDR real ou pessoas sem CPF de múltiplas Secretarias. Meta de recall de um estudo é declarada na política (não piso universal de 95% no FS). Parâmetros da busca são limites; passes/limiares efetivamente escolhidos pertencem ao snapshot aprovado.

## 2. Cadeia de execuções físicas, versões e significado

| Run Actions | Artefato / perfil | Resultado comprovado / ressalva |
| --- | --- | --- |
| [39138689675](https://github.com/lucianox777/Jornada/actions/runs/39138689675), HEAD `09e6dc4` | `12140703199` clean; `12140902027` correlated | Primeiro SQL físico e Rank→Resolve experimental. **Recall ~35% dividia decisões únicas por `2 × M_pares`**, denominador inadequado para decisão por fonte. Histórico superado, não valor atual do motor. |
| [39141120199](https://github.com/lucianox777/Jornada/actions/runs/39141120199), HEAD `39c8cf0` | `12141586972` clean; `12142275146` correlated | Denominador por **observação-fonte elegível pré-blocking**, comparação RAW vs ORACLE Gold. Oracle forma grupos **usando gabarito**. Histórico. |
| [39141852603](https://github.com/lucianox777/Jornada/actions/runs/39141852603), HEAD `50d1810` | `12142076606` clean; `12142100811` correlated | Inclusão BLIND por tripla exata sem gabarito; 3 thresholds × 3 margens × 3 modos = **27 resultados por perfil**; jobs físicos SUCCESS. Correlated passa a **histórico**, sem novos experimentos automáticos. |
| [39147073026](https://github.com/lucianox777/Jornada/actions/runs/39147073026), HEAD `ce2ad3186da36b02b93ae97e6a5d5cf3a895a7ef` | [12142909368 clean](https://github.com/lucianox777/Jornada/actions/runs/39147073026/artifacts/12142909368) | Somente **clean**, SQL Server efêmero `JornadaHarness`, seed42, 30.000 pessoas, treino TRAIN e VALIDATION; TEST excluído. **Job físico SUCCESS**, 20 repetições da materialização, 27 células TF=0 e auditoria por campo com SHA. HEAD ainda da PR #1152 draft ao ser auditada; **não pertence à master só por CI verde**. |

**Separação essencial:** SQL Server físico do `JornadaHarness` de GitHub Actions é execução real do SQL sobre **dados sintéticos**, não execução de Runner/HML municipal. `V9SqlRecordedRunEvidenceReader` já dispõe de leitor read-only com verificações de estado e ambiente, mas **os testes não demonstram que foi chamado na HML com verdade independente**. A tabela `identidade.linkage_resultado` não contém a união completa de candidatos; sem captura externa autenticada da coorte e truth, não há certificação.

### 2.1 Último clean: recall VALIDATION por fonte — threshold 0,99; TF 0

**Universo por fonte:** 7.841 observações elegíveis pré-blocking; 7.727 com verdadeiro parceiro SQL recuperado (**98,55% recall blocking por fonte**). **Universo por par:** 6.763 pares M elegíveis e 6.607 SQL (**97,69% recall blocking por par**). Não misturar esses denominadores.

| Margem (log-odds) | RAW TP / recall | BLIND tripla exata TP / recall | Gold ORACLE TP / recall | FP cada braço |
| ---: | ---: | ---: | ---: | ---: |
| **0** | **7.602 / 96,95%** | **7.602 / 96,95%** | **7.602 / 96,95%** | 0 / 0 / 0 |
| **0,5** | 4.666 / **59,51%** | 7.426 / **94,71%** | 7.602 / **96,95%** | 0 / 0 / 0 |
| **2** | 4.599 / **58,65%** | 7.337 / **93,57%** | 7.602 / **96,95%** | 0 / 0 / 0 |

Com margem 0,5: RAW registra **2.936 CONFLITOS**, BLIND **176**, ORACLE **0**. A igualdade em margem zero revela competição entre *observações da mesma pessoa*; **não é motivo para desligar a proteção de margem na operação**. Deve-se primeiro formar identidades candidatas sem truth e medir margem entre identidades distintas. BLIND une somente `(nome pessoa, nome mãe, DOB)` normalizados e completos, com risk de splits/matches de homônimos. ORACLE conhece `BasePersonId` e é teto sintético exploratório, não previsão do Gold real.

Na última corrida, o SQL físico materializou **o lote completo** em **P95 359,1637 ms; P99 359,8579 ms**, 20 repetições; não é consulta individual ou latência ponta a ponta, e P99 com 20 pontos não é SLA. O score FS de par não substitui o TP/FP pós-Resolve. Thresholds 0,999 e 0,9999 foram testados; **0,90 e 0,95 não foram executados** nesse run. A grade **não** inclui TF positiva.

### 2.2 Último clean: auditoria real dos erros sintéticos por campo (não TEST)

Do `field-quality-train-val.json` no artefato `12142909368`: **46.780 observações** (35.091 TRAIN + 11.689 VALIDATION); **32.782 pares M cross-gestor**, dos quais **27.127 com ambas pontas sem CPF**. CSV de observações SHA256 `28bbb9f4c63c97c353e7f29cdbc77091a4773b2f4c596e46103edadd93635d17`; manifesto SHA256 `1b82bb98f4824eee3764627047240e7f1f386f1f43ec8a43e1b88f540ab300b3`. TEST foi excluído dos contadores.

| Atributo | Operação efetiva / ausência por **observação** (TRAIN+VALIDATION) | Divergência em **pares M sem CPF** |
| --- | ---: | ---: |
| Nome completo | 2.820 / 46.780 = **6,03%** alterado | 3.170 / 27.127 = **11,69%** |
| Nome da mãe | 4.744 = **10,14%** alterado; 2.330 = **4,98%** ausente | 7.381 / 27.127 = **27,21%** divergente **ou ausente** |
| Nascimento | 1.913 = **4,09%** alterado; 470 = **1,00%** ausente | 2.752 / 27.127 = **10,14%** divergente **ou ausente**; com ambos presentes e divergentes **8,04%** |
| Três alterações efetivas na mesma observação | 14 / 46.780 = **0,030%** | 16 / 27.127 = **0,059%** com uma ponta triplamente alterada |

Os parâmetros nominais `clean` (nome 6%, mãe 10%, DOB 4%, mãe ausente 5%, DOB ausente 1%, corrupção comum 0%) são eventos **por registro**; as taxas de divergência são **por par verdadeiro**. Não interpretar 10,14% de discordância de DOB como taxa medida no município. A literatura brasileira examinada reporta frequentemente **completude** por fonte (nome materno ausente de 0% a 9,8%; DOB ausente 0% a 2,8%), não discordância pareada. NHS britânico mediu divergência de DOB em populações distintas (~0,11% e 0,492%). Usar clean **somente como bootstrap provisório**, sem fabricar números municipais.

## 3. Por que os resultados ainda não certificam nem devem alterar margem ou TEST

1. **Agrupamento FS sem gabarito:** classificar arestas por score não basta para fundir por transitividade; `A≈B` e `B≈C` não implicam `A≈C`. Auditar pontes, incoerências, limites de cluster, colisão CPF, homônimos, false-merges/splits, o conjunto real de candidatos Gold e os guards compartilhados antes de aplicar margem entre identidades.
2. **Seleção do V9:** adaptar `EvaluateValidation`/`AdaptiveCalibrationModelSearch` para usar resultados **de decisão final por fonte** e não o threshold par a par, com FDR e recall globais medidos; `ConfidenceBoundForFdrMeasured=false` é status real do código, não depreciação resolvida por existir outro avaliador.
3. **FDR:** com 0 FP e 7.426 aceitações BLIND, FDR pontual observada é **0**, mas Hoeffding por identidade reporta UCB unilateral em torno de **27,90%** no braço BLIND margem 0,5. Clopper–Pearson iid simples ou com `n_efetivo` informal **não comprova** FDR por identidade; bootstrap de clusters é diagnóstico complementar, não bound automaticamente válido. Suporte/cap, dependências e multiplicidade de escolhas precisam ser pré-fixados.
4. **TF:** a fonte marginal de termos IBGE sintético e a `U_TRAIN_SQL` condicionada ao blocking/ExactFirst não têm a mesma distribuição de referência. A fórmula de ajuste exige fundamentação estatística por unidade; TF=0 no run **não prova** desempenho de TF=0,5/1. LR Bayes neutro=1 e parâmetro desconhecido não são sinônimos de peso TF=0/1.
5. **TEST e HML:** seeds/TEST históricos com resultados revelados não são novo holdout. Antes de abrir TEST independente, fixar modelo, TF, passes, guards, seleção por identidade e SHA. A leitura de outcomes reais HML ainda carece de captura da coorte/união verdadeira e execução auditada. Nenhum runner sintético autoriza `DT-15`, HML/PROD ou promoção por CI.

## 4. Inventário da revisão documental e divergências encontradas

| Documento/família | Achado | Medida de vigência |
| --- | --- | --- |
| `Estado_Atual_Projeto`, `Manual_Sistema_Consolidado_20261009` | Fotografia de 09/10, sem chain dos runs 18/10 | Apontar para este relatório, preservar marcos DEV/Console anteriores. |
| `Indice_Decisoes_Vigentes`, `Indice_Acervo_Documental`, `docs/README`, `Solution/README` | Porta de entrada sem a decisão clean/histórico e os últimos runs | Adicionar orientação canônica e separação PR/master. |
| `Decisao_Calibrador_Unico_Adaptativo_Bootstrap`, `Decisoes_Canonicas_Identidade_Linkage` | Faltava complemento superveniente clean exclusivo e identidade candidata antes da margem | Emenda 18/10 mantendo C# único, ref aprovado e calibração manual. |
| `Bootstrap_Corpus_Lifecycle` | Texto legado diz “processa para calibrar” em HML/PROD e pode ser interpretado como treino do V9 ao subir | **Não se aplica à inicialização V9**. O bootstrap V9 verifica snapshot congelado, não recalibra. |
| `V9_Validation_Resolve_Physical_Table` (na PR #1152) | Parágrafos históricos anteriores à primeira execução real continuam dizendo que o probe não é chamado e que nenhuma tabela existe | Interpretar primeira seção como **proposta inicial de formatador**, substituída pelos runs listados; revisar ao integrar #1152. |
| `V9_Revisao_Testes_Blocking_Calibrador_Seletor`, `V9_Decisoes_Integral_Erros_Blocking_Tamanho_Amostra`, `V9_Sensibilidade_Clean_Correlated` | Planos de outubro exigiam repetir correlated/16 cenários como trabalho novo | Somente história, nunca nova prioridade/CI. |
| `V9_TEST_Fisico_Evidencia_Integrada` | Previa ausência de SQL repetido (era verdadeiro em 13/10) | SQL 20x físico **sintético** entregue; TEST novo e HML Runner independente **não** executados. |
| `V9_Origem_18pct_Erros_Sinteticos` | Registrava correlated como default da versão histórica | Preservar dados/manifest e marcar correlated histórico; clean padrão futuro sem recalcular runs antigos. |
| `Calibrador_Corpus_Sintetico_Avaliacao` | Comparação V1 de `m_exact` não é V9 realizado | Histórico, sem usar `expected_m_exact` como oráculo ou promover V9. |
| `DT15_Governanca_Decisao_Modelo`, `Dividas_Tecnicas` | Status parcial ainda precisava referência explícita às novas medições | Incorporar limites e rastreabilidade; não fechar DT-15/23. |
| `Solution/config/linkage/v9-bootstrap-experimental-candidates.json` (#1151) | Candidatos de clean/correlated no mesmo arquivo histórico | `HISTORICAL_DIAGNOSTIC_NOT_APPROVED_NOT_OPERATIONAL`, `active=false`; correlated NÃO é candidato atual por constar no inventário. |

**Precedência:** decisões supervenientes datadas prevalecem no desenho; HEAD/CI + bytes íntegros dos artefatos comprovam execução; textos históricos preservam estado da época. Nunca afirmar que branch PR aberta entrou em master, nem reescrever release selada.

## 5. Ordem de trabalho sem novos ensaios correlated

**P0:** fechar integração limpa do diagnóstico #1152 com CI na HEAD exata e documentação; obter resultados por fonte/atributo do clean com hashes; PR draft e gates pendentes impedem merge.

**P1:** construir grupos por FS sem gabarito com consistência e guarda contra fusões indevidas, aplicar margem **entre identidades** e reexecutar clean em SQL; investigar opções threshold 0,90/0,95/0,99 com FDR pós-Resolve, sem selecionar olhando TEST.

**P2:** conectar seletor adaptativo à decisão final por fonte e estabelecer bound de FDR por identidade/estrato defensável; calibrar TF somente com referências na **mesma unidade/condicionamento**, testar em VALIDATION.

**P3:** produzir snapshot completo inicial calibrado/aprovado em ref, novo TEST cego e homologação governada em HML com Runner real. Subida carrega snapshot, **não treina**; ativações são manuais e dependem de DT-15/institucional.

**Proibições:** sem V9 ativo automaticamente, sem novo teste correlated, sem dados IBGE originais mutados, V8/Gold em PROD alterados, sem NODE/NAS/JornadaLocal/HML/PROD não autorizados, sem TEST reciclado, sem publicar PII.

*Fontes primárias: artefatos GitHub Actions nomeados acima; `V9PhysicalSqlAdaptiveCalibrationProbe.cs`, `V9FellegiSunterAdaptiveCalibrationAdapter.cs`, `AdaptiveCalibrationModelSearch.cs`, `ProbabilisticLinkagePolicy.cs`, `V9IdentityClusterFdr.cs`, workflow `.github/workflows/v9-physical-seed42-probe.yml`. Fotografia datada, não substitui HEAD vigente.*
