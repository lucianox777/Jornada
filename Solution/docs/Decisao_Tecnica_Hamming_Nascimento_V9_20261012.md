# Decisão técnica V9 — distância de Hamming para data de nascimento

**Estado:** justificativa metodológica e diagnóstico de validação; **não** publica modelo V9 nem altera o contrato histórico do scorer.  
**Referência de implementação:** `BirthDateSemanticEvidence` (contrato semântico V5), `SyntheticBirthDistanceAuditBuilderV9` (auditoria sintética TRAIN) e `FellegiSunterScoring`.  
**Data da decisão:** 12/10/2026.

## Decisão e alcance

Manter **distância posicional de Hamming entre os oito dígitos canônicos `ddMMyyyy`** como **componente descritivo de comparação de duas datas civis válidas**, associado à precedência semântica existente, em vez de criar imediatamente um estado independente e uma regra de peso para cada erro de digitação. A função `DigitDistance(DateOnly a, DateOnly b)` devolve o número de posições diferentes, entre 0 e 8. Por construção, as duas sequências têm o mesmo comprimento, os campos estão normalizados com zeros à esquerda e o formato não depende de localidade.

Esta decisão **não** equivale a adotar a distância como score final, limiar fixo de match, regra universal de blocking, modelo probabilístico de erros nem substituto de `m/u`. O `FellegiSunterScoring` em modo semântico V5 usa **um único estado de nascimento mutuamente exclusivo**, com `LLR = ln(m_estado/u_estado)` estimado pelo Calibrador no universo de candidatos recuperados. O histórico V1–V8 não deve ser silenciosamente reparametrizado; testes podem ser ampliados/substituídos, mas execução de modelos históricos requer contrato versionado.

## Justificativas técnicas verificáveis

1. **Representação apropriada:** `DateOnly` evita comparações de texto livre e datas civis impossíveis; a representação `ddMMyyyy` fixa uma posição para cada dígito. Hamming é determinística, simétrica, de custo constante e não depende do idioma ou relógio. Aplica-se à **substituição posicional**, não a edição genérica de strings. Sua interpretação não é diferença em dias nem distância entre idades.
2. **Tolerância a diferenças cadastrais pequenas:** alterações de uma posição são reconhecidas com distância 1; troca de dois algarismos distintos gera distância 2; a comparação também preserva informações de concordância estrutural por dia, mês e ano. Isso permite **medir** sem fixar uma teoria única sobre qual erro é mais frequente.
3. **Precedente externo, sem equivalência indevida:** Barbosa et al., *CIDACS-RL* (BMC Medical Informatics and Decision Making 20, 289, 2020, DOI 10.1186/s12911-020-01285-w), relatam uso de **distância de Hamming em atributos de data** no linkage brasileiro. A publicação a descreve sobre sequências binárias; a Jornada usa explicitamente **dígitos decimais `ddMMyyyy`**. A referência justifica a **família de medida**, não comprova que a implementação, os pesos ou o desempenho sejam idênticos.
4. **Padrões empíricos reforçam a necessidade de tolerância sem definir pesos locais:** estudo *De-identified Bayesian personal identity matching for privacy-preserving record linkage despite errors: development and validation* (2023, PMC10163749) observou divergência de data em **0,492%** de 126.904 pessoas vinculadas por identificador independente; **93,3% das datas divergentes envolviam apenas um componente** (dia, mês ou ano), e **2,7% envolviam transposição de dígitos** em algum componente. Isto **não** implica que Hamming sozinho detecte 93,3% dos vínculos, nem que essas taxas se apliquem a Secretarias brasileiras. Componente diferente pode envolver vários dígitos, e o blocking pode sequer recuperar o par.
5. **Evitar proliferação prematura de parâmetros:** partir de apenas sete estados semânticos de nascimento reduz o risco de muitas células esparsas com `m/u` determinados por suavização. Uma separação adicional só é defensável se dados TRAIN com grupos independentes e validação separada mostrarem ganho real de precisão/recall e suporte estatístico.
6. **Evitar dupla contagem:** `NASC_DIA`, `NASC_MES` e `NASC_ANO` não devem ser somados ao LLR do estado conjunto/semântico como se fossem observações independentes da mesma data. A normalização da **representação** não elimina dependência estatística.
7. **Distribuições separadas:** a distribuição de datas **verdadeiras** no corpus demográfico primário deriva da projeção IBGE E2 (UF SP, 2026, decomposição sintética da cauda até 115 anos); a distribuição de **erros das observações** é outro mecanismo, parametrizado e ainda **não** comprovado pelas fontes municipais. O histograma Hamming TRAIN mede o resultado do gerador, não a frequência real de erros na população.

## Precedência do classificador atual (não confundir com Hamming simples)

O contrato `BirthDateSemanticEvidence.Classify` é mutuamente exclusivo e avalia nesta ordem: igualdade civil exata; inversão civilmente válida de **dia/mês**; diferença de **século** com mesmo dia/mês; distância Hamming **1**; pelo menos **dois componentes** (dia, mês, ano) exatamente iguais; distância Hamming **2**; um componente igual; demais divergências. Assim, o mesmo valor de Hamming pode levar a estados distintos, e **não** é correto dizer que toda troca de dígitos do ano entra em `TWO_DIGIT_ERROR`.

| Datas comparadas | Hamming `ddMMyyyy` | Estado V5 e interpretação |
| --- | ---: | --- |
| 15/03/1978 × 15/03/1978 | 0 | `EXACT` |
| 15/03/1978 × 15/03/1987 | 2 | `PARTIAL_COMPONENT_AGREEMENT`: dia/mês iguais; **inversão 78↔87** detectável no diagnóstico |
| 15/03/1978 × 15/03/1979 | 1 | `ONE_DIGIT_ERROR`: também ano adjacente |
| 15/03/1978 × 01/03/1978 | 2 | `PARTIAL_COMPONENT_AGREEMENT`: ano/mês iguais, dia diferente |
| 07/06/1978 × 06/07/1978 | 2 | `DAY_MONTH_SWAP`: inversão dos componentes, não de dígitos do ano |
| 15/03/1978 × 15/03/1878 | 1 | `CENTURY_SHIFT`: exceção estrutural com precedência sobre Hamming 1 |
| 29/02/2000 × 28/02/2000 | 1 | `ONE_DIGIT_ERROR`: data válida; **29/02/2001 não existe** |
| 31/12/1999 × 01/01/2000 | 7 | `OTHER_DISAGREEMENT`: datas com diferença civil de **um dia** e Hamming elevado |

O último exemplo demonstra uma **limitação decisiva**: Hamming **não é proximidade cronológica**. Da mesma forma, coincidência dia+mês com ano muito diferente não é necessariamente evidência favorável. Transposições e mudanças de formato exigem semântica separada quando justificadas por suporte observado; Hamming 2 também pode resultar de duas substituições independentes.

## Onde usar, onde não usar

- **Comparação:** usar a distância como diagnóstico observável; o scorer usa o estado semântico **calibrado** e uma contribuição única para nascimento. A decisão depende do LLR combinado com demais evidências, políticas e abstenção.
- **Blocking:** Hamming no scorer **não recupera** um candidato que não foi selecionado pela união de passes. Passes adicionais de inversão no ano ou outras expansões precisam mostrar **recall incremental** da união completa, cardinalidade, custo e taxas de falsos candidatos sob `VERSIONED_ALIAS`; não publicar busca irrestrita por `distância ≤ 2`.
- **Dados ausentes e inválidos:** `null` não recebe distância Hamming nem nascimento inventado; a ausência de um lado é evidência neutra no contrato vigente quando previsto. Data civil impossível preserva o valor bruto e o erro de validação; não virar `DateOnly` por correção silenciosa.
- **Extremos etários:** nenhum nascimento de uma pessoa-base de **200 anos** deve ser fabricado para reproduzir o estoque demográfico IBGE E2; casos extremos, datas antigas e pessoas falecidas pertencem a cenário adversarial/histórico separado. Idade implausível em cadastro não significa automaticamente outra identidade.
- **Proteção contra falsas conclusões:** `P(d_obs|d_real)` é diferente de `P(d_real)`, que é diferente de `P(comparação|par candidato,M/U)`. Não transportar frequências de erros estrangeiras para os pesos FS sem medição e validação por fonte.

## Evidência exigida antes de reconsiderar os sete estados

A auditoria `SyntheticBirthDistanceAuditBuilderV9` deve registrar somente em `gabarito.json.birth_date_distance_train_audit` (truth-only) a distribuição de **distâncias × estados semânticos** no TRAIN, preservando zeros, denominadores, ausências e perfis específicos (ano ±1, inversão dos dois últimos dígitos e heaping). Isto fornece evidência **diagnóstica agregada**, não M/U reais. TRAIN é para análise/ajuste, VALIDATION para escolher intervenções candidatas, TEST fica lacrado para verificação final independente.

Não aprovar nova taxonomia, novo peso de data ou promoção V9 sem: (a) pares M/U rotulados e elegíveis **após blocking**, com suportes por célula/grupo e pesos de desenho; (b) desempenho/estabilidade por fonte e coorte, inclusive comparando Hamming isolado, regra semântica V5 e variantes; (c) ganhos de recall/FP/FDR e não dependência indevida dos mesmos dados; (d) paridade SQL real com o universo amostral e gates DT-15. **Um sucesso de CI materializando 30 mil pessoas sintéticas não certifica esses critérios.**

## Fontes verificáveis

- Barbosa GCG et al. (2020), **CIDACS-RL**, *BMC Medical Informatics and Decision Making*, DOI: https://doi.org/10.1186/s12911-020-01285-w (método de distância de Hamming no linkage brasileiro; finalidade técnica, não estimativa de taxa local).
- *De-identified Bayesian personal identity matching for privacy-preserving record linkage despite errors: development and validation* (2023), https://pmc.ncbi.nlm.nih.gov/articles/PMC10163749/ (estudo observacional de discrepâncias de DOB; percentuais dependem do universo e do método).
- Código local: `Solution/src/Jornada.Contracts/BirthDateSemanticEvidence.cs`, `Solution/src/Jornada.Linkage.Core/FellegiSunterScoring.cs`, `Solution/src/Jornada.Linkage.SyntheticCorpus/SyntheticBirthDistanceAuditV9.cs`.
- Decisão demográfica E2: [Referência diária IBGE — emenda E2](DC-SYN-01-E2_Referencia_Diaria_Nascimento_Projecao_IBGE.md); [Arquitetura de blocking](Decisao_Arquitetural_Blocking_Complementar_IBGE_20260927.md); [Corpus sintético V2](Calibrador_Corpus_Sintetico_V2.md).

### Continuidade de aferição: 63 células com contagem de pessoas-base distintas

A auditoria foi expandida para a **versão 3**: cada célula **estado semântico V5 × distância Hamming (7×9)** contém `observation_count` e `distinct_training_people`. A primeira medida conta observações TRAIN; a segunda conta **identificadores de pessoa-base sintética distintos, usados somente internamente**, cujas observações caíram naquela célula. Nenhum ID é exportado. Duas observações da mesma pessoa com `PARTIAL_COMPONENT_AGREEMENT` e Hamming 2 representam **dois registros, mas uma única pessoa-base distinta** nessa célula.

O valor de `distinct_training_people` nunca excede o de observações e é zero nas células não observadas. **Não somar o número de pessoas distintas entre células** como se fossem pessoas exclusivas: a mesma pessoa pode contribuir em estados diferentes. Ele **não** é ESS, garantia de independência amostral, tamanho da amostra de não-matches U ou inferência sobre erros reais de Secretarias. A medida reduz uma interpretação otimista de suporte nos dados simulados sem antecipar a adequação estatística do modelo ou alterar o scorer.
