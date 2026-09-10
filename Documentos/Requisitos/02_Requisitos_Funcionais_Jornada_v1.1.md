# Requisitos Funcionais - Jornada do Cidadão - Fase 1 - Aditivo v1.1

**Versão do aditivo:** 1.1  
**Data:** 09/09/2026  
**Base:** `02_Requisitos_Funcionais_Jornada_v1.0.md`  
**Status:** VIGENTE - COMPLEMENTO NORMATIVO

Este documento complementa, sem apagar nem renumerar, os 50 RFs do baseline v1.0. Os requisitos abaixo passam a integrar a definição funcional corrente da Fase 1 e devem ser lidos cumulativamente com o baseline. A numeração canônica desta consolidação é **RF-051 a RF-056**; o antigo Adendo 06 foi reclassificado como histórico e não cria uma segunda numeração concorrente.

## RF-051 - Executar calibrador e avaliador com paralelismo quando vantajoso

**Requisito funcional.** O calibrador e o avaliador devem explorar paralelismo sempre que a operação puder ser executada independentemente e houver evidência de ganho de desempenho no ambiente-alvo, sem alterar determinismo, reprodutibilidade, isolamento do corpus ou resultados estatísticos.

**Critério de aceitação funcional.** A execução paralela deve produzir resultado semanticamente equivalente à execução serial para a mesma versão de regras, corpus e semente/configuração. O grau de paralelismo deve ser limitado e configurável. Paralelismo que degrade desempenho, aumente contenção ou comprometa reprodutibilidade não deve ser habilitado por padrão. A decisão serial/paralela deve ser sustentada por benchmark reproduzível no ambiente-alvo ou equivalente.

**Rastreabilidade de negócio.** RN-023, RN-024, RN-028, RN-035

**Rastreabilidade de qualidade/técnica.** RNF: RNF-015, RNF-020, RNF-021, RNF34-A, RNF34-B; RT: RT-057, RT-058

## RF-052 - Usar frequências agregadas oficiais do IBGE como evidência de blocking quando aplicável

**Requisito funcional.** O mecanismo de otimização de blocking deve poder usar dados oficiais agregados do IBGE, versionados e com proveniência verificável, para melhorar discriminação/priorização de combinações de nome, prenome, sobrenome e último nome sempre que a fonte estiver disponível, tecnicamente compatível e demonstrar ganho mensurável sem reduzir recall além dos limites aprovados.

**Critério de aceitação funcional.** A ausência ou indisponibilidade do IBGE não pode tornar o Linkage incorreto: o sistema deve operar com fallback versionado. O uso do IBGE deve registrar versão/fingerprint da fonte e nunca tratá-la como verdade individual nem usar CPF da fonte externa. Sempre que uma frequência oficial aplicável estiver disponível, o otimizador deve considerá-la como evidência candidata e registrar se ela foi usada ou por que foi descartada.

**Rastreabilidade de negócio.** RN-005, RN-014, RN-023, RN-024, RN-029

**Rastreabilidade de qualidade/técnica.** RNF: RNF-013, RNF-016, RNF-021, RNF-027, RNF34-A; RT: RT-027, RT-028, RT-029, RT-057

## RF-053 - Considerar componentes de nome e nascimento no blocking otimizado

**Requisito funcional.** O universo candidato deve permitir regras de blocking que considerem separadamente **nome completo, prenome, sobrenome, último nome** e os componentes **dia, mês e ano** da data de nascimento, inclusive combinações entre esses atributos. O otimizador de blocking deve avaliar as combinações candidatas e selecionar a melhor política segundo critérios versionados de recall, redução do espaço de pares, custo, tamanho de blocos, ganho incremental, dependência/redundância e risco de falso vínculo. Quando existirem frequências oficiais do IBGE aplicáveis aos componentes nominais, elas devem integrar a avaliação das combinações candidatas.

**Critério de aceitação funcional.** A política selecionada deve identificar explicitamente os atributos/passes utilizados, a versão do otimizador, o corpus/evidência, as métricas de comparação e o motivo da seleção. Nenhuma combinação pode ser escolhida apenas por correlação ou apenas por reduzir candidatos; recall de vínculos verdadeiros e risco de falso vínculo são gates obrigatórios.

**Rastreabilidade de negócio.** RN-005, RN-006, RN-014, RN-023, RN-024

**Rastreabilidade de qualidade/técnica.** RNF: RNF-016, RNF-021, RNF-025, RNF-027, RNF34-A; RT: RT-027, RT-028, RT-029, RT-057

## RF-054 - Preservar a semântica oficial dos nomes do IBGE

**Requisito funcional.** Os dados de nomes e sobrenomes provenientes do IBGE devem preservar a grafia e a frequência oficiais publicadas. A preparação técnica pode aplicar somente transformações necessárias à comparação computacional, como tratamento versionado de caixa, espaços e acentuação, sem colapsar grafias distintas por regras fonéticas, duplicação de letras ou equivalências inventadas não presentes na fonte oficial.

**Critério de aceitação funcional.** O valor original e sua frequência oficial devem permanecer recuperáveis para auditoria. Toda representação técnica derivada deve registrar versão da transformação e permitir rastreamento até o valor original. Mudanças de normalização exigem nova versão e regressões antes de uso real.

**Rastreabilidade de negócio.** RN-005, RN-014, RN-024, RN-029, RN-030

**Rastreabilidade de qualidade/técnica.** RNF: RNF-013, RNF-016, RNF-021, RNF-030, RNF34-A; RT: RT-029, RT-049, RT-057

## RF-055 - Reutilizar no avaliador a regra dinâmica versionada produzida pelo calibrador

**Requisito funcional.** Toda política dinâmica de blocking utilizada pelo calibrador deve ser materializada como artefato versionado e imutável de regras/configuração e o avaliador deve consumir exatamente essa mesma versão, sem reconstrução semântica independente.

**Critério de aceitação funcional.** O relatório de avaliação deve registrar o identificador/fingerprint da política recebida do calibrador e falhar fechado quando a política estiver ausente, incompatível ou divergente. Calibrador e avaliador executados sobre o mesmo corpus e política devem concordar sobre a elegibilidade de cada par candidato.

**Rastreabilidade de negócio.** RN-003, RN-014, RN-024, RN-029

**Rastreabilidade de qualidade/técnica.** RNF: RNF-013, RNF-016, RNF-021, RNF34-A; RT: RT-027, RT-029, RT-057

## RF-056 - Evitar snapshots redundantes da base oficial do IBGE

**Requisito funcional.** Antes de baixar e publicar um novo snapshot de dados oficiais do IBGE, o processo de aquisição deve verificar se a origem mudou. `Content-Length` deve ser usado como pré-verificação barata quando disponível, combinado preferencialmente com validadores HTTP como `ETag` e `Last-Modified`. Igualdade apenas do número de bytes não é evidência suficiente para declarar o conteúdo idêntico.

**Critério de aceitação funcional.** Quando validadores fortes ou combinação confiável de metadados provarem que a origem não mudou, o download deve ser evitado. Quando os metadados forem ausentes, inconclusivos ou divergentes, o conteúdo deve ser baixado para área temporária e comparado por SHA-256 com o snapshot vigente. Um novo snapshot imutável só pode ser publicado/versionado quando o fingerprint do conteúdo efetivamente mudar. A proveniência deve registrar URI, tamanho, validadores disponíveis, SHA-256 e instante de aquisição/verificação.

**Rastreabilidade de negócio.** RN-014, RN-023, RN-024, RN-029

**Rastreabilidade de qualidade/técnica.** RNF: RNF-013, RNF-020, RNF-021, RNF34-A, RNF34-B; RT: RT-057

## Governança comum

Os RF-051 a RF-056 não autorizam ativação probabilística em Produção. Otimização, dados IBGE e paralelismo somente podem integrar uma política real após regressões unitárias/integradas, avaliação estatística independente e aprovação institucional conforme a issue #31.
