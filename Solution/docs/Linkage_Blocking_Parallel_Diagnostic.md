# Diagnóstico paralelo D/C/união — contrato executável do Avaliador sintético

**Estado:** implementação de engenharia read-only, não homologação estatística nem ativação do combinado.  
**Versão:** `BLOCKING_PARALLEL_SYNTHETIC_V1`; relatório `JORNADA_SYNTHETIC_EVALUATION_V2` e avaliador `JORNADA_SYNTHETIC_EVALUATOR_V2`.  
**Precedência:** [decisão arquitetural canônica](Decisao_Arquitetural_Blocking_Complementar_IBGE_20260927.md#20-evolução-paralela-observação-comparativa-e-eventual-desativação) e [Plano do Calibrador](Calibrador_Plano_Blocking_Analise.md#100-benchmark-paralelo-e-critérios-objetivos-de-manutenção).

## O que é medido, exatamente

Após `GENERATE_DRAFT`, o `Jornada.Linkage.Evaluation` em **Development**, modelo **RASCUNHO**, lê a massa sintética materializada e a verdade rotulada **dentro do avaliador**. Mantém as rotas de produção inalteradas:

- **D:** universo de pares produzido pelo **ruleset dinâmico persistido no modelo** (mesmo snapshot do Avaliador). Seu pipeline de avaliação existente calcula todos os pares sintéticos; **para D/C/união, restringir D ao universo comum** abaixo.
- **C:** os cinco passes correntes de `CombinedIdentityCandidatePlanner`, inclusive inversão civilmente válida dia/mês, ano ±1 válido e fonética individual de pessoa e mãe. Nenhum passe combinado é ativado no Runner em lote.
- **D∪C:** união de pares deduplicada; nenhum resultado de FS, ranking ou threshold altera essa contagem.

**Denominador idêntico e explícito:** todos os pares não ordenados de observações **materializadas**, **de Gestores distintos** e **com CPF ausente dos dois lados**, inclusive observações com mãe ausente (inelegíveis a C). A elegibilidade de verdade do combinado é um segundo denominador **auxiliar**, restrito às verdades com passes combinados possíveis de ambos os lados. Não excluir faltantes do recall geral nem misturar esta população com o relatório legado `Blocking`, que inclui pares CPF-rotulados e pode incluir mesmo Gestor. A verdade `base_person_id` só é usada em memória pelo avaliador e não aparece no JSON/ledger.

O diagnóstico reporta contagens de pares no universo comum, verdades conhecidas, verdades elegíveis ao combinado, D, C, D∪C, sobreposição, candidatos exclusivos, verdadeiros exclusivos e recall total e condicional de C. Também reporta redução de não-vínculos por mecanismo, com **um denominador**: todos os não-vínculos possíveis no universo comum. Campos JSON: `ParallelBlocking` em relatório V2; no ledger append-only, escopo `PARALLEL_BLOCKING` com métricas agregadas e sem dados pessoais.

Os indicadores devem respeitar invariantes aritméticas:

```text
|D ∪ C| = |D| + |C| - |D ∩ C|
Verdadeiros(D ∪ C) = Verdadeiros(D) + Verdadeiros(C) - Verdadeiros(D ∩ C)
Recall_X = Verdadeiros(X) / todos_os_pares_verdadeiros_sem_CPF_inter_Gestor
Recall_C_condicional = Verdadeiros(C) / verdadeiros_com_ambos_lados_elegíveis_ao_C
Redução_X = 1 - não_vínculos_candidatos_X / não_vínculos_possíveis_no_universo
```

Denominadores nulos são relatados como `0`, junto de contagem `0`; **0 não pode ser interpretado como taxa estimada com evidência suficiente**. Para desativar uma rota, o decisor precisa das contagens, cobertura e estratos, não somente de uma média agregada.

## Segurança de execução e limites

O Avaliador recusa em vez de amostrar ou truncar quando **a união D∪C** exceder `MaxCandidatePairs`. Permanecem os limites preexistentes de D e C, que podem ser ainda mais conservadores porque seu universo inicial é mais amplo. O relatório não inclui UUID/ID, nome, mãe, CPF, endereço, pares individuais, ranking ou score; um hash já existente do corpus e do modelo permite reproduzir o resultado. O ledger de avaliação continua append-only, exclusivamente de evidência sintética e **não promotável**. Este commit não modifica tabelas/procedures SQL nem flags de Runner, busca ou Calibrador.

O cálculo D/C na massa sintética usa a projeção corrente comum, não mede sozinho a manutenção de aliases históricos em Gold nem planos/latência real do SQL Server. **P50/P95/P99, CPU/I/O, falsos vínculos após FS e recuperação temporal em ondas continuam experimentos operacionais separados**. O protótipo C só se aplica quando nome, mãe e nascimento existem; datas ausentes podem ser excluídas do ZIP pelo contrato de ingestão atual, de modo que esse estrato deve ser relatado **como limitação do corpus materializado**, nunca presumido coberto.

## Execução e aceite restrito

1. Rodar o fluxo sintético de Development existente: gerar corpus, ingerir a ponte sintética, `GENERATE_DRAFT` com ruleset persistido, chamar `Jornada.Linkage.Evaluation` com `ModelId` e `MaxCandidatePairs` declarados. Não ativar/publicar o modelo.
2. Conferir `Blocking` e `CombinedBlocking` legados e o novo `ParallelBlocking`. **Somente o novo diagnóstico** constitui a comparação D/C/D∪C sobre o mesmo universo.
3. Verificar as contagens, os verdadeiros exclusivos e a ausência de PII; conferir persistência imutável das métricas `PARALLEL_BLOCKING`. Unitários exercitam sobreposição, faltantes, redundância, limites fail-closed e ausência de pares verdadeiros; integração SQL confirma report/ledger.
4. Ensaiar escala e corpora/estratos representativos, em especial nome/mãe comuns, homônimos, mãe ausente e erros simultâneos. A amostra sintética e os denominadores atuais **não demonstram** superioridade operacional ou equivalência estatística para pessoas reais da capital.
5. Qualquer futura promoção de C, substituição parcial de D ou desativação requer versão própria da política publicada, medição de recall por estrato, `u` condicionado ao novo universo, avaliação independente, DT-05/replay e autorização institucional. A instrumentação não muda a decisão corrente do Runner.

## Segunda etapa — comparação opcional sobre o SQL Server real (DEV/HML)

**Implementação:** `BlockingPassAuditCommand`, método `BLOCKING_PARALLEL_SQL_AUDIT_V1`, parametrização de `BlockingProjectionCandidateQueryBuilder.BuildTaggedCandidateUuidQuery`. A auditoria **somente leitura** usa o **modelo ativo e ruleset D já publicados**, mais os cinco passes C V1 **somente para avaliação**; **não ativa C no Runner operacional** nem promove nova política. Os dados de rótulo e a saída são arquivos privados do operador, fora do repositório:

```powershell
cd Solution
dotnet run --project src/Jornada.Linkage.Runner -c Release -- --blocking-pass-audit-labels "CAMINHO_PRIVADO\labels.csv" --blocking-pass-audit-output "CAMINHO_PRIVADO\blocking-paralelo.json" --blocking-pass-audit-compare-combined=true
```

O CSV existente mantém o cabeçalho `pessoa_observacao_id,pessoa_uuid_verdade`; a observação deve estar em Silver **sem CPF** e a verdade ser uma Pessoa `REFERENCIA` sob a autorização do operador. A flag é **opcional e desligada por padrão**, exige valor explícito `true` ou `false`; um valor inválido é recusado. O relatório mantém `summary` e `passes` legados e acrescenta `parallelComparison` quando a opção é ligada (senão, `null`). Não escrever o CSV com identificadores nem logs de observações em artefatos públicos.

**Mesmas linhas rotuladas para D, C e D∪C:** cada observação produz três conjuntos de UUID candidatos válidos em Gold, com filtro `estado_identidade=REFERENCIA` e fingerprint físico do ruleset ativo. A consulta tagged contém **todos os passes D e C**, reutiliza a lógica parametrizada OR/AND, gera tags de procedência e, **em uma única instrução SQL**, agrega por UUID com `MAX(in_dynamic)` e `MAX(in_combined)`. Assim, `|D∪C| = |D| + |C| - |D∩C|` é uma medida exata da mesma instrução, sem publicar IDs ou carregar o conjunto bruto no processo. As consultas individuais D e C são executadas separadamente para obter **latências observadas**; se suas contagens ou presença da verdade divergirem da instrução tagged (por exemplo, atualização concorrente da Gold/projeção), a execução **falha sem produzir relatório**.

O novo agregado inclui tamanho da amostra, observações elegíveis a C, candidatos por rota, candidatos compartilhados, verdadeiros recuperados, verdadeiros exclusivos de D/C, recall geral sobre **todas as observações rotuladas** e recall condicional de C entre observações com nome materno/nascimento válidos. Mãe ausente não sai do denominador geral. Latências em milissegundos são `P95` e máximo **por consulta individual D, consulta individual C quando elegível e consulta tagged D∪C com agregação**. A terceira possui custo adicional de proveniência: **não** interpretar latências como medição comparável de um futuro Runner com união publicada, nem extrapolar P95 de pequena amostra para SLA populacional. CPU/I/O, memória, qualidade da verdade, custo de índices e resultado do FS continuam experimentos separados.

O executor mantém a checagem de parâmetros, versão de normalização, vigência das features e fingerprint físico dos índices existentes. **Não** usa `TOP`, não trunca resultados e respeita o timeout SQL operacional; excesso de parâmetros, SQL inválido ou contagens incoerentes produzem erro explícito. Operar **somente em DEV/HML com massa autorizada**, evitando execução em horários de carga/serving. A telemetria de amostra rotulada com rótulos verdadeiros é diferente do experimento sintético por pares inter-Gestor: **não comparar diretamente seus recalls ou denominadores**, embora ambos tenham os três braços D/C/D∪C.

**Referências de código:** `SyntheticEvaluationEngine`, `BlockingParallelCandidateDiagnostic`, `SyntheticEvaluationEvidenceWriter`, `CombinedIdentityCandidatePlanner` e seus testes unitários/SQL. [Plano geral](Plano_Desenvolvimento.md), [documentação de avaliação sintética](Calibrador_Corpus_Sintetico_V2.md).
