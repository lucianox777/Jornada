# Jornada V9 — decisões de bloqueio, experimentação e evidências (14/10/2026)

**Natureza:** registro técnico de decisões vigentes para desenvolvimento/ensaio. Não constitui norma publicada, ativação de modelo, homologação ou certificação de produção. Na divergência com documentação normativa selada, respeitar a hierarquia do índice do acervo e abrir revisão explícita.

## 1. Objetivo e política de identidade

O linkage é responsável por escolher e executar os campos/passes de bloqueio; não transferir a decisão ao usuário da Console DEV. CPF, quando válido, é vínculo determinístico sujeito às invariantes um UUID com no máximo um CPF corrente e um CPF corrente para um único UUID. Documentos como RG podem ser múltiplos por pessoa: igualdade é evidência, não imposição de unicidade de RG. Calibração é **manual**; o diagnóstico de suficiência e a sinalização de oportunidade de recalibração podem ser automáticos, sem ativação automática de novos parâmetros.

## 2. Projeção canônica e isolamento de amostras

- Normalização e fonética devem ser produzidas pelo projetor C# canônico (`BlockingProjectionKeyProjector.Project`, `IdentityComparison.NormalizeText`, `MetaphoneBr.Encode`), versão `BLOCKING_PROJECTION_KEY_PROJECTOR_V4`. Não manter implementação fonética paralela em Python.
- Colunas canônicas: `observacao_id`, `base_person_id`, `particao`, `nome_normalizado`, `nome_fonetico`, `nome_mae_normalizado`, `nome_mae_fonetico`, `data_nascimento`.
- Partições TRAIN, VALIDATION e TEST são disjuntas por `base_person_id`; observações têm IDs únicos. Qualquer identidade presente em mais de uma partição invalida o ensaio (falha fechada).
- Frequências marginais e conjuntas devem contar **identidades distintas em TRAIN**, não observações repetidas, e nunca usar VALIDATION/TEST para aprender frequências. Manifests e hashes preservam a proveniência do snapshot congelado.
- Frequência de célula igual a zero significa **não observada em TRAIN**, não impossibilidade de ocorrência. O limite de fanout observado em TRAIN não é garantia de fanout físico em SQL: não descartar passes sem fallback e avaliação.

## 3. Desenho experimental A/B/C/D

Comparar quatro estratégias A, B, C e D com fonética desligada/ligada (A0/A1, B0/B1, C0/C1, D0/D1), cada uma sem/com E0: **16 braços**. E0 é passe demográfico exato de `nome_normalizado + nome_mae_normalizado + data_nascimento`; índice físico E0 não equivale à consulta nem prova efeito do passe. Comparação pareada exige corpus original idêntico, mesma elegibilidade M/U, partições congeladas e semente, incluindo recall, falsos candidatos, fanout, custo SQL, planos, CPU/memória, FDR e intervalos adequados a dependência por identidade.

**Distinção obrigatória:** `v9-abc-paired-blocking.py` fornece proxies diagnósticos de passes fixos. Seus braços D **não são** a estratégia adaptativa real. `v9-adaptive-frequency-planner.py` produz planejamento por frequência TRAIN, mas não executa recuperação física nem Rank/Resolve. Não publicar vencedor ou alegar equivalência de D a partir desses relatórios.

## 4. Execução e rastreabilidade

O corpus original congelado de aproximadamente 30 mil identidades foi publicado pelo workflow de origem (run `38593005691`, artefato `v9-physical-sql-active-candidate-wrong-link-30k`). O workflow `.github/workflows/v9-original-corpus-abcd-diagnostic.yml` prevê download, exportação canônica C#, congelamento e relatório dos 16 braços; a integração proposta na PR #1066 acrescenta relatório `adaptive-plans.json`. **Registro histórico:** na elaboração inicial desta seção, a execução real ainda não estava confirmada. Os ensaios posteriores no corpus original da semente 42 já têm evidência agregada em `v9-memory-batch` das PRs #1072–#1078. O experimento em SQLite `:memory:` não valida por si só custo/planos ou paridade das novas células no SQL Server; não confundir esses resultados com os 14 passes físicos originais.

## 5. Evidência estatística e promoção

A regra proposta é minimizar falsos negativos sob teto de FDR `ε`, usando limite superior unilateral de incerteza, com TRAIN para estimar m/u/TF, VALIDATION para seleção e TEST independente para certificação. Valores de ε, α, estratos, n_min e gates dependem de política versionada; números ainda em discussão não devem ser tratados como homologados. Diagnóstico com variância ingênua de pares ou amostra insuficiente deve ser marcado **INCONCLUSIVO**. O problema de deterioração de ~18% segue distinto e não resolvido. Nenhum resultado sintético, proxy ou plano adaptativo isolado autoriza ativação em HML/PROD.

## 6. Estado atualizado em 14/10/2026 — evidência de blocking

**Integrações confirmadas na master:**

- #1066 integrou o planejamento adaptativo em snapshot congelado, com SHA-256 e bloqueios de vazamento.
- #1069 integrou comparação de BASE, UNIÃO-4 e UNIÃO-5 com política diagnóstica de `P AND (M OR D)`; #1070 acrescentou CI experimental rápida sem dispensar `jornada-ci`; #1071 corrigiu o exportador C# quando há vários sobrenomes.
- #1072 criou SQL SQLite indexado em `:memory:` sobre projeções C# congeladas, TRAIN/VALIDATION sem TEST; #1073 classificou os M faltantes; #1074 acrescentou cinco células direcionadas.
- #1075 integrou busca de menor U sujeita ao mínimo de **95% dos M verdadeiros totais sem CPF**, não apenas dos elegíveis; #1076 integrou bootstrap por **identidade-base independente**; #1078 integrou censo por chave real SQLite de concentração e colisões intergestores, sem divulgar PII.
- #1077 trata de sobrenomes significativos **estruturados por C#**, em vez da heurística Python, com modo de CSV V2 opt-in. **Pendente de confirmação do merge no registro**; não pressupor integração antes dos gates verdes no SHA exato.

### Evidências quantitativas que podem ser afirmadas

Apenas para **semente sintética 42**, com universo e partições congelados, o plano selecionado por TRAIN recuperou **19.436/20.458 M (95,0044%)** no universo total TRAIN sem CPF e **6.432/6.714 M (95,7998%)** em VALIDATION, com **515.918/59.376 U candidatos**, respectivamente. A incerteza por clusters de identidades-base deu **limite inferior unilateral 95% ≈94,673% em TRAIN**. Assim, o limite estatístico não certifica a meta ≥95% em TRAIN; o ponto acima do alvo, por uma margem pequena, não justifica promoção.

A UNIÃO ampliada 19 células, que não é o plano de custo mínimo, teve M elegíveis recuperados de **19.447/20.075 TRAIN** e **6.435/6.612 VALIDATION** e U **618.399/69.041**. **Os denominadores elegível e global são diferentes por contrato**, sendo o global o mais conservador para a meta. U de candidatos **não equivale** a falsos vínculos aceitos nem a FDR; `U/M` não é probabilidade de erro FS.

Os artefatos originais devem manter identificador do workflow, SHA do snapshot, método, elegibilidade, distribuição de grau incluindo zero, concentração por chave e proveniência das frequências por **identidade-base distinta em TRAIN**. Sobrenome IBGE `SOBRENOME` registra ocorrência em qualquer posição; sua frequência **não é** diretamente a frequência da última posição.

### Gates pendentes e ordem de execução

1. Concluir o merge versionado #1077 com provas de paridade de projeções nominais para pessoa e mãe, preservando CSV V1 congelado; não reinterpretar retrospectivamente chaves históricas do índice.
2. Executar comparação do **mesmo conjunto de pares candidatos** em SQL Server isolado e SQLite, com collation binária, índices reais, duração P95/P99, CPU, memória, fanout por gestor e plano de execução; caracterizar os vieses entre SQL de memória e banco físico.
3. Confrontar custo de colisões por chave e composição populacional real; não multiplicar simplesmente U por `11,45 milhões/30 mil` como se toda célula tivesse mesma distribuição condicional. Criar orçamento/governança de U e cauda de fanout P95/P99 antes de adotar passes largos.
4. Repetir evidências em sementes independentes (42, 43, 44) com mesma separação TRAIN/VALIDATION/TEST e estimar incerteza por identidade. Não alterar o gerador sintético apenas para atingir recall.
5. Separadamente, recalibrar somente **sob decisão manual**, com m/u/TF derivados de base independente TRAIN e universo de candidatos realmente induzido pelas novas regras, usando VALIDATION para seleção e TEST ainda fechado até a decisão formal. Medir FDR dos **vínculos aceitos** e gates de suficiência, jamais supor que U é FDR.
6. Não alterar V8, DT-15, scorer único Fellegi–Sunter, Rank/Resolve, Gold, IBGE original, HML/PROD, JornadaLocal ou NODE/NAS por resultados apenas diagnósticos.

DEV não exige retrocompatibilidade automática de executáveis antigos, mas as evidências congeladas, semântica de identidades e versões de método permanecem imutáveis.

**Critério de encerramento:** checksum de corpus, rastreabilidade do modelo escolhido, paridade física por pares no SQL Server, custos de escala e limite estatístico do recall ≥95% por split e por universo, FDR e suficiência aprovados sob política versionada. O ensaio SQL em memória não substitui a validação de promoção.
