## P0 — censo físico SQL da união do ruleset ATIVO e dos passes C1 e taxa de vínculos errados (13/10/2026)

Esta rodada responde à pergunta quantitativa sobre os **candidatos errados efetivamente produzidos pelo SQL**. No banco **isolado `JornadaHarness`**, o comando `Jornada.Linkage.Evaluation --v9-physical-sql-blocking` materializa em `#v9obs` 30 mil pessoas sintéticas e todas as observações geradas. Usa a **projeção canônica C#** `BlockingProjectionKeyProjector` e executa, por `JOIN` físico SQL Server com deduplicação `SELECT DISTINCT`, **a união dos passes efetivamente persistidos no ruleset ATIVO** com os cinco passes C1 do catálogo `CombinedIdentityCandidatePlanner`: nome+mãe+nascimento exato, transposição dia/mês, ano adjacente, fonética do nome e fonética do nome da mãe (com os outros campos exatos). Preserva a semântica de AND entre atributos e OR entre passes. O frame de avaliação é **por split, entre gestores, observações sem CPF, data presente**. Se o censo superar 1 milhão de candidatos, **falha explicitamente**; nunca substitui candidatos U por pares adjacentes amostrados.

A classificação verdadeira de cada par vem exclusivamente de `base_person_id` do gabarito sintético. O scorer FS C# V9 é aplicado **a cada par retornado pelo SQL**, com o **mesmo modelo m/u+prior+nascimento+limiar da seed 42**, congelado inclusive para seeds 43 e 44. TF continua **explicitamente desligado** neste ensaio: não afirmar execução de TF calibrada.

### Denominadores distintos: não confundir proporção de candidato falso com erro de vínculo

- `M_eligible`: todos os pares verdadeiros intergestores, sem CPF e com data, **antes do blocking**.
- `M_sql`: pares verdadeiros que apareceram na união deduplicada do SQL; `M_eligible-M_sql` são verdadeiros perdidos pelo blocking.
- `U_sql`: pares SQL de **pessoas-base diferentes**, a contagem pedida de *candidatos falsos*.
- **Candidatos falsos por verdadeiro recuperado** = `U_sql / M_sql` (relatar também `U_sql / M_eligible` e `U_sql/(M_sql+U_sql)`). É razão de candidatos, **não probabilidade de vínculo errado**.
- `TP`: `M_sql` aceitos pelo limiar FS V9; `FP`: `U_sql` aceitos erroneamente. `FN=M_eligible-TP`, incluindo perda no blocking. `TN_sql=U_sql-FP` são apenas **falsos candidatos rejeitados**.
- **Taxa observada de vínculos errados** = `FP/(TP+FP)` **dentre os pares aceitos**. Também relatar `FP/TP` (erros por acerto), `TP/M_eligible` (recall do pipeline no frame), e `FP/U_sql` (FPR condicional aos candidatos falsos SQL). **Nenhuma destas é FDR municipal estimada para produção**, nem garante identidade única em cluster.

A saída `v9-sql-physical-seed-{42,43,44}.json` contém por split somente contagens e taxas agregadas. O resumo combina apenas os TESTs das três seeds, soma numeradores/denominadores **antes de calcular** as taxas e nunca tira uma média ingênua das taxas. Há dupla conferência independente em Python de identidades de contagem, aritmética de cada divisão, modelo congelado e cobertura dos três splits.

### Suficiência e significado de 30 mil pessoas

Os **30.000 indivíduos sintéticos por seed** são um tamanho fixo e reproduzível para exercer todas as etapas desse *frame* e comparar seeds sem recalibrar o modelo. **Não constituem prova de suficiência estatística para homologação municipal**, principalmente para erros raros, caudas de homônimos, subgrupos com mãe/nascimento ausentes ou razões de erro de ordem muito menor que 1%. O denominador relevante para a taxa de vínculos errados é o número efetivo de **pares aceitos**, não 30.000 pessoas. Mesmo zero `FP` observado exige intervalo de incerteza (aproximadamente limite superior unilateral 95% de `3/N` com `N` pares aceitos independentes), e dependência entre pares reduz a informação efetiva. Para decisão DT-15 serão necessários orçamentos por risco, estratos, incerteza/intervalos e testes independentes com maior cobertura e massas sintéticas adversariais; nunca declarar a V9 validada só por 3 seeds de 30 mil.

O núcleo de identidade permanece progressivo: **CPF, nome completo, nome da mãe e data de nascimento não são campos universalmente obrigatórios de uma pessoa nova**. Ausência de atributo não autoriza fabricar valor nem converter busca incompleta em `NOVA_IDENTIDADE`; a obrigatoriedade é decidida pelo **contrato versionado de cada origem/cliente** e a suficiência de candidatos pela seletividade dos passes. Este experimento intencionalmente restringe sua mensuração a observações **sem CPF e com nascimento presente**: seus resultados não demonstram desempenho quando mãe, nome ou nascimento estão ausentes, nem alteram as regras da borda/contrato. Ver também a issue #612 e `Gold_Pessoa_Universo_CPF.md`.

### Limite de equivalência com o bloqueio operacional

**É blocking físico SQL Server com regras do ruleset ATIVO + C1, mas não reproduz todo o histórico de aliases `VERSIONED_ALIAS` da tabela `identidade.blocking_chave`.** Os passes do ruleset ATIVO são lidos diretamente das tabelas SQL `identidade.linkage_ruleset_passe` e `identidade.linkage_ruleset_passe_campo` após a ativação controlada no banco CI, e submetidos a consultas físicas indexadas para cada cláusula; as regras C1 são adicionadas à união, como previsto no catálogo. O relatório guarda fingerprint e quantidade de passes ativos. Contudo, não há a materialização de **todas as versões históricas de chaves** da tabela `identidade.blocking_chave`: a projeção está limitada às observações atuais do corpus sintético; não equivale à recuperação completa de aliases históricos. Este teste usa o blocking de um modelo de CI para **gerar candidatos** e aplica neles apenas o FS V9 congelado: não compara escores V8 e V9. Nesta massa sintética não existem históricos completos de alias por cidadão. Além disso, o subframe sem CPF exclui vínculos pela rota determinística. Portanto, o relatório sinaliza `NOT_OPERATIONAL_ACTIVE_VERSIONED_ALIAS_OR_CALIBRATED_TF` mesmo se a taxa observada for baixa. A comparação V8×V9 continua **fora** da aceitação da V9.

# Prioridade P0 — o FS nominal V9 funciona em 30 mil sintéticos?

**Escopo 13/10/2026.** O termo "venom" foi interpretado como **V9** pelo contexto do projeto. Esta avaliação remove a comparação V8×V9 como dependência; a V8 é apenas replay histórico. DT-15 continua tratando de ATIVO×PROPOSTO quando houver substituição operacional, independentemente da versão.

## Experimento que a CI executa

O comando `generate --people 30000 --seed 42 --population-profile demographic-primary --v9-functional-probe true` roda o **scorer Fellegi–Sunter C# V9 verdadeiro**, não uma implementação paralela, sobre o corpus sintético versionado. Gera o artefato agregado `v9-functional-probe.json` com os campos `Validation`, `Test`, `SyntheticProxyVerdict` e `OperationalConclusion`. O job de CI verifica que foram usadas efetivamente 30.000 pessoas e o fingerprint/seed do mesmo manifesto.

- TRAIN estima as 81 probabilidades nominais conjuntas M/U e os estados de nascimento; os grupos são separados por pessoa-base. Células sem observação M/U são informadas, e suavização técnica α=0,5 permite um **experimento** sem declarar que ganhou suporte observacional.
- Blocking **proxy** por primeira inicial. M: primeiro par entre gestores de mesma pessoa, com perda por blocking explicitamente contabilizada como FN; U: pares de bases distintas adjacentes no bucket. **Não** são os passes SQL reais `VERSIONED_ALIAS`.
- O prior usa a proporção M/U **da amostra artificial TRAIN**, não a prevalência operacional. O ajuste TF foi desabilitado **explicitamente neste experimento**; não se alega calibração completa de peso TF.
- VALIDATION congela um threshold com orçamento exploratório de FPR em pares U proxy de 1%; TEST não influencia a escolha. TEST relata TP, FP, FN, TN, recall, FPR, precisão e FDR da **amostra pareada**, não da população.
- Critério exploratório declarado antecipadamente: `PASS_ON_SAMPLED_SYNTHETIC_PROXY` só com TP>0, recall TEST≥80% e FPR TEST≤1%; caso contrário `FAIL_ON_SAMPLED_SYNTHETIC_PROXY`. Este critério **não substitui orçamento aprovado de DT-15**. Mesmo em PASS, `OperationalConclusion` permanece `NOT_PROVEN_ON_REAL_SQL_BLOCKING_OR_MUNICIPAL_POPULATION`.

**Conclusão a extrair:** o artefato da execução CI indica objetivamente **se a implementação V9 passou ou não passou no teste sintético proxy**, com contagens. Isso **não é suficiente** para afirmar que o algoritmo completo, incluindo blocking SQL real e TF calibrada, funciona na Jornada. O gate CI valida execução, integridade e relatório, **não exige PASS** e nunca ajusta TEST até passar.

**Atualização após a rodada física desta página:** a união SQL de passes ativos + C1 **já é medida** no ensaio descrito no topo, com todos os candidatos desse recorte, mas **não** com todo o histórico `VERSIONED_ALIAS`. Para responder sobre o **algoritmo operacional completo**, ainda faltam aliases históricos, TF estatisticamente calibrada na mesma unidade, orçamento de risco por estrato, abstenção para células sem suporte e avaliação congelada sobre universo operacional representativo. Não extrapolar precisão/FDR de pares proxy ou do SQL sintético para população municipal, nem converter 59/81 células sem suporte bilateral de TRAIN em evidência por suavização.

Nenhuma execução deste ensaio publica/ativa V9, altera V8, DT-15, banco, referências IBGE ou JornadaLocal.

## P0 — rodada de transferência de modelo inteiramente congelado (13/10/2026)

A evidência anterior nas seeds 42, 43 e 44 calibrava **três modelos diferentes**, cada um com TRAIN e limiar VALIDATION próprios; por isso 3/3 PASS era **repetibilidade de treinamento**, não prova de transporte dos parâmetros. Esta rodada executa um experimento adicional, mais rigoroso: **treina e seleciona o único limiar na seed 42**, congela juntos os **81 parâmetros nominais conjuntos m/u, evidência de nascimento m/u, prior do frame e limiar de VALIDATION**, exporta `v9-frozen-model.json` e **reusa literalmente o mesmo objeto em 43 e 44**, sem ajustar pesos, prior ou threshold conforme os resultados externos. O SHA-256 do snapshot e o fingerprint do corpus-fonte devem coincidir nos três relatórios. Os TEST das sementes 43 e 44 permanecem independentes de treino/seleção.

A CI preserva todos os PASS e FAIL da avaliação original e agora publica `v9-synthetic-multiseed-summary.json` com `crossSeedProxyVerdict=PASS_ALL_FROZEN_MODEL_TRANSFER_REPLICATES` ou `AT_LEAST_ONE_FROZEN_MODEL_TRANSFER_FAILED`, nunca substituindo uma falha por suavização ou troca de limiar. **Mesmo que a VALIDATION de uma seed-alvo exceda FPR 1%, o modelo não é retreinado e o experimento deve registrar esse fato; isso não é uma falha de integridade do programa, mas pode ser falha de desempenho.** A verificação independente rejeita modelo trocado, digest incorreto, reuso da base de treino, alteração do limiar e métricas inconsistentes.

A hipótese desta rodada é estritamente **transferibilidade do modelo FS V9 no proxy sintético de primeira inicial**. **Não é blocking SQL físico `VERSIONED_ALIAS`, não calibra TF, não garante FDR populacional, nem resolve os 59/81 estados do TRAIN sem suporte M/U bilateral.** Não depende de comparar V8. O modelo preservado é somente experimental, sem publicação, ativação ou impacto em JornadaLocal/IBGE/HML/PROD.

## Rodada anterior — réplicas com recalibração por seed (PR #996)

A avaliação isolada da seed 42 já deu **3.414 TP, 39 FP, 67 FN e 5.833 TN**
em TEST no *proxy por inicial*, recall 98,08% e FPR 0,664%.
A reprodutibilidade num único conjunto não basta para atestar robustez da
calibração. Por isso, a CI executa **mais duas massas independentes de 30.000
pessoas cada**, seeds fixadas **43** e **44**, com o **mesmo código de geração,
fonte IBGE, nascimento e contrato FS V9**, mantendo a execução original 42.

O validador `scripts/v9-synthetic-multiseed-gate.py` confere, de forma
independente do gerador, (a) a integridade criptográfica dos três manifestos
e arquivos de dados; (b) unicidade de seed, fingerprint e verdade sintética;
(c) impossibilidade de vazamento de TRAIN/VALIDATION/TEST e aritmética de
TP/FP/FN/TN/recall/FPR/precisão/FDR; (d) critérios de PASS/FAIL originais
por seed, sem converter FAIL em PASS; e (e) indicadores de faixa
de recall/FPR em três réplicas. O arquivo agregado
`v9-synthetic-multiseed-summary.json` informa `passedProxySeeds`,
`failedProxySeeds`, `crossSeedProxyVerdict`, limites e resultados por seed.

**Importante:** a seed 42 é original e 43/44 são **novas execuções**, mas
cada seed recalibra seu próprio TRAIN e seleciona o limiar em sua própria
VALIDATION. **Não** é validação de transportabilidade de um modelo
congelado entre amostras, nem corrige o *proxy U* não aleatório. Um PASS
em todas as réplicas comprova apenas repetibilidade funcional desse recorte;
não comprova FDR operacional, suficiência das 81 células ou equivalência
com passes SQL `VERSIONED_ALIAS`. Nenhuma execução publica/ativa V9.

**Política de falha:** se qualquer réplica não alcançar os limiares
exploratórios, o resumo marcará `AT_LEAST_ONE_PROXY_REPLICATE_FAILED`
mesmo com CI verde — a CI valida a **honestidade do relatório**, não
mascara fracassos de desempenho. O julgamento final dependerá
de análise humana dos próprios números, nunca de um PASS artificial.

**P0 subsequente à rodada física deste documento:** estender a prova SQL aos
aliases históricos `VERSIONED_ALIAS` e depois avaliar o FS V9 com TF calibrada
na mesma unidade e política explícita de abstenção por célula sem suporte
bilateral. A rodada física atual **não** executa TF nem valida a união histórica
completa. Nenhum comparativo V8×V9 é requisito dessa validação.
