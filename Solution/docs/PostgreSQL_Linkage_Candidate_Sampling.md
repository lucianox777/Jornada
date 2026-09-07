# Linkage PostgreSQL — desenho amostral de dois estágios

Estado: implementação técnica da segunda fatia da issue #31. Não constitui homologação estatística, autorização de calibração real, ativação ou publicação. SQL Server permanece canônico; Fabric mantém o papel analítico.

## População-alvo e desenho

A unidade é o par dirigido `(observação fonte, UUID candidato)`, pertencente à união dos cinco passes V2. O quadro completo de observações-fonte é fornecido por um processo externo governado, com identificadores estáveis, atributos congelados e referência opaca de proveniência. O chamador precisa atestar que o quadro é completo para a população-alvo declarada. O código valida limites e duplicações, mas não pode provar sozinho que o universo institucional de fontes foi integralmente fornecido. O desenho não autoriza inferência para uma população mais ampla do que o quadro atestado.

`PostgreSqlCandidateSampler` executa amostragem aleatória simples sem reposição (SRS) de `n_S` entre `N_S` fontes. Para cada fonte selecionada, enumera integralmente a união dos passes, no mesmo snapshot PostgreSQL `REPEATABLE READ READ ONLY`. Atribui cada par a exatamente um estrato pelo primeiro pass da ordem publicada, preservando a máscara completa de pertencimento. Dentro de cada estrato `(fonte, pass primário)`, seleciona `n_sh=min(q_h,N_sh)` pares por SRS. Cotas positivas são obrigatórias para todos os passes habilitados; um estrato vazio não gera par nem peso. No V1 somente a data exata é habilitada.

A seleção usa ranks HMAC-SHA256 com chave criptográfica de 256 bits gerada independentemente dos atributos e rótulos. Domínios separados são usados para fontes, pares e evidências. Os menores ranks são selecionados, com desempate canônico por UUID. A semente deve ser gerada por `RandomNumberGenerator.GetBytes(32)`, guardada em cofre autorizado e nunca escrita em logs, artefatos ou tabelas de modelo. Uma semente fixa é permitida apenas nos testes sintéticos. A reprodução exige a mesma semente, desenho, quadro de fontes e corpus congelado; o snapshot MVCC não é exportado nem recuperável após o término da transação. Reutilizar a mesma semente em sorteios independentes não é o procedimento aprovado.

Sob o desenho aleatório e quadro/corpus fixos, a probabilidade de inclusão do par é:

```text
pi_s = n_S / N_S
pi_(s,c) = (n_S / N_S) * (n_sh / N_sh)
w_(s,c) = (N_S / n_S) * (N_sh / n_sh)
```

A probabilidade e o peso são calculados a partir dos denominadores reais, não das cotas solicitadas. A união é deduplicada antes da seleção; probabilidades de passes sobrepostos nunca são somadas. A primeira etapa também cobre fontes sem candidatos, que permanecem no denominador de fontes. A estimativa Horvitz–Thompson do total de pares para o quadro completo é a soma dos pesos dos pares selecionados. Para o total de pares por fonte, a segunda etapa se cancela e equivale a `N_sh / pi_s`. A implementação registra os totais de desenho; não os apresenta como contagens exatas quando fontes foram subamostradas. As contagens `Members` e `Primary` descrevem somente os pares efetivamente enumerados, enquanto `EstimatedMembership` e `EstimatedPopulation` são somas ponderadas. Mesmo quando todas as fontes são selecionadas, o total ponderado de um componente pode diferir do total observado se os pares daquele componente forem subamostrados em estratos de outros passes. Precisão, variância e intervalos de confiança exigem tratamento do desenho em conglomerados e dos rótulos, não uma hipótese falsa de independência entre pares da mesma fonte.

## Limites e evidência

São obrigatórios limites de tamanho do quadro, fontes selecionadas, candidatos por fonte, pares enumerados, pares selecionados e timeout. Exceder limites interrompe a operação; não há redução automática de cotas nem truncamento silencioso. A etapa de candidatos lê todos os elegíveis de cada fonte selecionada antes de considerar o resultado válido. A SQL utiliza os predicados parametrizados do mesmo `BirthBlockingPlan` do scorer e compara a máscara retornada com o plano .NET. A normalização Unicode/collation ainda depende de validação representativa.

A saída restrita contém os identificadores dos pares selecionados, máscara, pass primário, denominadores, probabilidades e pesos. Esses identificadores são pseudônimos e não devem aparecer em logs de CI ou artefatos agregados. A evidência agregada contém versão, referência e fingerprint do quadro, compromisso da semente, configuração, fingerprints do universo observado e da seleção, snapshot, contagens de fontes e pares, sobreposições, cobertura por pass, cotas efetivas e totais estimados. Os fingerprints são HMAC e dependem da chave; não constituem anonimização nem substituem controle de acesso, finalidade, retenção e preservação governada do corpus. Não há persistência automática dos pares ou da semente. O componente não é registrado no worker operacional.

## Fronteira da calibração

O resultado é uma amostra de **candidatos**, não uma amostra u rotulada. UUIDs diferentes, CPF ausente, coincidência com UUID conhecido e scores anteriores não são rótulos negativos. O processo seguinte deverá obter rótulos independentes de match, não-match e inconclusivo, separar treinamento de avaliação e registrar não resposta, perdas e probabilidades de seleção/rotulagem. O estimador existente permanece inalterado e não aceita automaticamente esses pesos. Uma versão explicitamente ponderada e validada deverá estimar as distribuições condicionadas ao blocking, inclusive dependências dos componentes de nascimento, antes de qualquer substituição de m/u ou prior.

O método piloto `M_INTERGESTOR_U_GOLD_MVCC_V2` e seu bloqueio de validação real/ativação continuam intactos. Esta fatia não altera DDL, modelo, parâmetros, thresholds, margem, precedência determinística do CPF, runner, correções governadas ou publicação Gold/Serving. Todos os campos preservados permanecem evidências candidatas conforme a ADR universal; nenhum campo novo é automaticamente ativado no score.

## Validação técnica

A suíte cobre probabilidades de dois estágios, pesos, cotas, seleção determinística reproduzível, ordem de entrada, cinco passes, sobreposição, deduplicação, V1, fontes vazias, censo, subamostragem e falha fechada. A integração utiliza exclusivamente `JornadaPgSamplingTest`, com opt-in e corpus sintético, rejeita corpus preexistente e remove somente UUIDs da fixture. O workflow dedicado executa restore locked, build Release com warnings como erros, instalação dupla dos seis DDLs, unidades, política e integração real, preservando logs, binlogs e TRX. A aprovação técnica não substitui avaliação de representatividade, recall, precisão, calibração probabilística, falsos vínculos, subgrupos, escala ou aprovação institucional.
