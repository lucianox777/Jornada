# Registro de decisões vigentes — Calibrador V9 e Gold (20/10/2026)

**Natureza:** consolidação técnica de decisões, não release normativa, certificação ou relatório de ensaio. Em divergência, este registro de decisão superveniente prevalece sobre notas experimentais anteriores **somente para orientar implementação futura**. Verificar o HEAD e os gates antes de declarar conformidade do código.

## DC-V9-BOOT-01 — Descoberta dos parâmetros iniciais

- O Calibrador V9 C# é o motor único para o bootstrap. Deve **descobrir**, não receber previamente como constante, os parâmetros iniciais adequados ao corpus sintético clean derivado da referência IBGE congelada. O bootstrap é provisório, não validação populacional real.
- O limiar inferior de não-vínculo **não é fixado antecipadamente**. A busca inicial **não possui teto arbitrário de duplicatas** (inclusive 10% ou 2%) como filtro de admissibilidade ou escolha.
- Explorar candidatos no TRAIN, medir riscos e cobertura, produzir a **fronteira de Pareto** e apresentar **faixas de fronteira e candidatos recomendados com critérios transparentes**, sem mascarar trade-offs nem impor vencedor arbitrário. Se não houver evidência suficiente para recomendação, declarar insuficiência.
- Separar descoberta, recomendação, validação independente, certificação e ativação. O limite estatístico de FDR dos **vínculos aceitos** e os demais gates de segurança **não são removidos nem confundidos com orçamento exploratório de duplicatas**.
- Preservar a independência por identidade-base na inferência; não contar pares dependentes como observações independentes. TEST não é usado para escolher parâmetros. Recalibrações posteriores somente por comando manual; diagnóstico de suficiência pode ser automático e somente leitura.

## DC-V9-GOLD-02 — Medição incremental antes de decisão

- O resultado histórico do ensaio #1160 foi 9.240 observações, 5.003 identidades-base, 3.067 novas referências e **2.788 pendências**. A distribuição causal das pendências **ainda precisa ser medida**; não inferi-la de contagens por onda.
- A auditoria final Gold (#1161) e B-cubed (#1163) são componentes de medição; **merge e testes unitários não demonstram execução do ensaio**. A ligação do B-cubed ao harness é objeto da PR #1164.
- Comparar política atual e criação experimental no **Runner incremental real em banco descartável**, com **três ordens de ingestão pré-declaradas**, porque UUIDs criados numa onda passam a ser candidatos nas seguintes. A prévia estática da #1162 não estima efeitos cascata, positivos ou negativos.
- Publicar agregados de P/R por pares, B-cubed padrão e com cobertura (pendências penalizadas), fusões erradas, duplicatas, pendências por motivo, FDR, latência, incerteza e perícia de referências impuras; não divulgar rótulos pessoais ou UUIDs individuais.
- Não alterar Gold operacional, V8, JornadaLocal, IBGE original, HML/PROD, REF ativa ou TEST. Nenhum limiar inferior experimental autoriza criação operacional antes de validação e governança.

## Situação verificável em 20/10/2026

| Artefato | Situação observada | Lacuna |
| --- | --- | --- |
| #1161 | Merge anterior confirmado | Execução do harness e relatório final ainda não comprovados |
| #1162 | Merge confirmado | Diagnóstico estático, não mede criação incremental entre ondas |
| #1163 | Merge confirmado | Biblioteca B-cubed ainda sem números físicos do Gold |
| #1164 | PR aberta na última conferência | Integração B-cubed e CI/ensaio completos |
| #1165 | PR aberta na última conferência | Retira tetos arbitrários e escolha implícita; ainda requer CI/merge |
| Bootstrap completo | **Não demonstrado** | Fronteiras/faixas multicritério, ensaios e recomendação reproduzível |

## Navegação e precedência

- [Índice de decisões vigentes](Indice_Decisoes_Vigentes.md) — porta de entrada decisória.
- [Decisões canônicas de identidade e linkage](Decisoes_Canonicas_Identidade_Linkage_20260929.md) — regras estruturais anteriores, preservadas salvo decisão superveniente expressa.
- [Decisão do Calibrador único](Decisao_Calibrador_Unico_Adaptativo_Bootstrap_20261014.md) — arquitetura do bootstrap e recalibração.
- [Nota técnica do limiar inferior](V9_Limiar_Inferior_NaoVinculo_TRAIN_20261020.md) — **fotografia histórica da implementação V1**; os orçamentos nela descritos **não são decisão vigente**.
- [Índice do acervo](Indice_Acervo_Documental.md) — classificação de documentos, snapshots e evidências.
- [Documentos institucionais](../../Documentos/README.md) — release selada, especificação publicada e candidata.

**Regra editorial:** não editar retroativamente evidências, tags, especificações publicadas ou relatórios de ensaio. Corrigir documentos correntes por remissão datada; marcar divergências código × decisão explicitamente. Reorganização física de pastas só após inventário de referências e testes de links, para não quebrar rastreabilidade.


## Complemento de implementação — 24/10/2026 (seletor Gold V6)

Para realizar DC-V9-BOOT-01 no **limiar inferior Gold**, o calibrador único
`V9LowerNonMatchTrainCalibrator.Search` mantém como objetivos Pareto 2D
**falsas novas referências × pendências** em TRAIN (não confundir com o
Pareto 2D do bootstrap geral, **recall global por fonte × FDR pós-Resolve**).
O joelho é uma sugestão do ponto próximo ao ideal normalizado; não é um
certificado de FDR ou de duplicação. As faixas descritivas observadas de
falsa criação entre as referências propostas (0,5%, 1%, 2%, 5%, 100%)
**participam da escolha de alternativas para o experimento físico**:
são selecionados o joelho, a primeira faixa não vazia e a última faixa
não vazia, com deduplicação e complemento por faixas intermediárias até
três limiares distintos. Faixas vazias não são preenchidas artificialmente.
A largura das faixas **não constitui orçamento de risco para aceitar ou
rejeitar a criação operacional**.

A versão do estudo evolui de `V9_TRAIN_SELECTION_LOWER_NONMATCH_PARETO_V5`
(seleção do joelho + extremos absolutos, já usada no run físico
[#40124876599](https://github.com/lucianox777/Jornada/actions/runs/40124876599))
para `V9_TRAIN_SELECTION_LOWER_NONMATCH_PARETO_V6` após o merge da
correção. Não reclassificar retrospectivamente outputs do run V5 como V6;
resultados físicos precisam estar vinculados ao SHA e à versão exatos.
Nem este complemento nem qualquer merge ativam V9/DT-15, abrem TEST,
promovem `ref` ou alteram bancos operacionais.
