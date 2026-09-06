# ADR candidato — Linkage Fellegi–Sunter multievidência e qualidade de identidade

Status: CANDIDATO — pós Base Normativa v3.64 / master v4.05+

## 1. Decisões

1. A resolução de identidade da Jornada não utilizará IA generativa nem modelo supervisionado como mecanismo canônico.
2. O fallback probabilístico permanece explicável e baseado em Fellegi–Sunter, com geração/calibração de parâmetros em componente separado.
3. Não será acrescentado um segundo modelo de IA não supervisionada. A estimação estatística de `m/u` do próprio calibrador é suficiente para o papel probabilístico.
4. `Soundex` não participa do score Fellegi–Sunter. Se futuramente demonstrar ganho operacional, poderá existir somente como uma chave adicional de blocking/candidate generation, versionada e desligável. A implementação inicial não depende de Soundex.
5. Nome e nome da mãe usam normalização versionada. A evolução V2 deve remover diacríticos, pontuação irrelevante, espaços redundantes e partículas nominais isoladas `DA`, `DAS`, `DE`, `DO`, `DOS` antes dos comparadores de similaridade. O valor original nunca é alterado.
6. Jaro–Winkler ou outro comparador de nome não é um modelo concorrente ao Fellegi–Sunter: ele apenas produz o estado de concordância que o Fellegi–Sunter pondera.
7. O calibrador deve poder estimar parâmetros para todas as evidências de identidade explicitamente habilitadas no catálogo de features, e não apenas para nome/nome da mãe/data de nascimento.
8. Nenhum campo novo entra automaticamente no score. Cada evidência precisa de semântica, normalizador, comparador, política de qualidade e parâmetros `m/u` versionados.
9. Para linkage probabilístico, a data de nascimento é preservada integralmente, mas sua evidência é decomposta em `NASC_DIA`, `NASC_MES` e `NASC_ANO`, calibrados e pontuados separadamente.

## 2. Núcleo de identidade pode estar incompleto ou inconsistente

Os campos `CPF`, `nome`, `nome da mãe` e `data de nascimento` podem estar ausentes ou inconsistentes na origem.

A ausência ou má qualidade de um campo:

- nunca elimina a observação recebida;
- nunca autoriza preenchimento sintético;
- não deve ser tratada automaticamente como discordância;
- pode impedir uma resolução determinística;
- não impede que outra Secretaria forneça evidência futura capaz de resolver o vínculo.

Toda evidência de origem permanece append-only em Bronze/Silver e pode ser reavaliada por versões posteriores do linkage.

## 3. Qualidade da evidência

A qualidade é metadado separado do valor original. Estados mínimos:

- `VALIDA`
- `SUSPEITA`
- `SENTINELA_PROVAVEL`
- `IMPOSSIVEL`
- `AUSENTE`
- `INCONSISTENTE` quando uma política versionada detectar contradição aplicável

Política de uso no linkage:

- `VALIDA`: participa normalmente do comparador e do score.
- `SUSPEITA`: não participa de chave determinística; só poderá participar do probabilístico se houver parâmetros específicos calibrados para essa classe. Na ausência deles, contribuição neutra.
- `SENTINELA_PROVAVEL`, `IMPOSSIVEL` e `AUSENTE`: contribuição neutra no score; continuam preservadas e reportadas.
- `INCONSISTENTE`: preservada; a política do atributo decide entre contribuição negativa calibrada, neutralidade ou conflito forte. Nunca é corrigida silenciosamente.

Exemplo: nascimento `1500-03-15` permanece armazenado como recebido e aparece no BI como `IMPOSSIVEL`, mas não pode criar nem reforçar a tripla cadastral determinística.

### 3.1. Nascimento: valor original e componentes de linkage

A decomposição para linkage não altera nem substitui `data_nascimento` recebida. Para uma data qualificada como apta, o comparador deriva:

- `NASC_DIA`
- `NASC_MES`
- `NASC_ANO`

Cada componente possui estados `EXACT` e `DIFF` com probabilidades `m/u` próprias. Assim, um erro apenas no dia não equivale a uma discordância completa de nascimento; mês e ano ainda podem fornecer evidência positiva. O mesmo vale para erros de mês ou ano.

Se a data estiver ausente, impossível ou não apta à política do modelo, os componentes derivados não podem transformar o valor ruim em evidência positiva. O original continua preservado para auditoria e reavaliação futura.

A data completa continua sendo usada na tripla determinística somente quando estiver íntegra, apta e a combinação for única.

## 4. Chaves determinísticas municipais

Ordem conceitual:

1. CPF válido, ativo e não conflitado → `CPF_DETERMINISTICO`.
2. Sem resolução por CPF: `nome + nome da mãe + data de nascimento`, todos presentes, qualificados como aptos e formando combinação única → `TRIPLA_CADASTRAL_DETERMINISTICA`.
3. Demais casos → candidate generation + Fellegi–Sunter.

A tripla é evidência de identidade municipal; não substitui a preservação dos campos originais.

## 5. Código interno da Pessoa na origem

`codigoPessoaOrigem` é chave de versionamento/idempotência dentro do namespace `Gestor + Sistema de Origem`. Ele não deve ser confundido com a identidade municipal transversal.

A política é definida no onboarding de cada Gestor/Sistema e pode refletir:

- `CODIGO_INTERNO_SISTEMA` — identificador estável já existente na origem;
- `CPF` — quando o próprio sistema utiliza CPF como chave local;
- `TRIPLA_CADASTRAL` — quando essa é de fato a chave operacional declarada pela origem.

Quando a origem possui um código interno estável, ele é preferível para rastreabilidade porque continua identificando o registro mesmo se atributos cadastrais forem corrigidos.

No caso atual da SEHAB, o master já admite o fallback de `codigoPessoaOrigem` para o CPF quando o código local não é enviado. Isso é coerente com os arquivos analisados.

O valor de `codigoPessoaOrigem` serve para localizar/reversionar a observação da fonte; CPF/tripla/evidências continuam sendo avaliados separadamente para resolver `PESSOA_UUID`.

## 6. Fellegi–Sunter multievidência

O linkage pode e deve considerar informação além do núcleo, quando disponível e habilitada.

Features candidatas iniciais:

- `NOME`
- `NOME_MAE`
- `NASC_DIA`
- `NASC_MES`
- `NASC_ANO`
- `RG/RNE/documento equivalente`, com escopo e normalização próprios
- `TELEFONE_CONTATO`
- `EMAIL_CONTATO`
- `ENDERECO/REFERENCIA_TERRITORIAL`, apenas na granularidade e semântica aprovadas
- outros identificadores estáveis declarados pelo sistema de origem
- evidências familiares, quando governança e calibração demonstrarem utilidade e ausência de efeito indevido

Cada feature possui estados de comparação próprios. Ausência em qualquer lado é `MISSING/NEUTRAL`, não `DISAGREE`.

O calibrador separado estima `m` e `u` por feature/estado sobre amostras controladas do corpus, mantendo versão, snapshot, origem, cobertura e tamanho de amostra. A ativação de uma nova feature exige evidência de calibração e validação antes de publicação do modelo.

A decomposição de nascimento não pressupõe independência perfeita entre dia, mês e ano; a validação/calibração do modelo deve medir o efeito combinado e ajustar `T_LINKAGE` antes da ativação.

## 7. Blocking / candidate generation em escala municipal

Nunca comparar cada observação com toda a Gold.

A V2 de nascimento usa múltiplos passes, mantendo a busca apoiada no índice existente de `data_nascimento`:

- data completa exata;
- mesmo ano+mês, permitindo variação do dia, estreitado por inicial de nome/nome da mãe quando disponível;
- mesmo ano+dia em meses válidos, permitindo variação do mês, também estreitado por inicial;
- transposição dia/mês quando a data resultante for válida;
- pequena tolerância configurável de ano para erros comuns de registro.

Outros passes futuros podem usar identificador documental, telefone/e-mail normalizado e combinações de tokens de nome/nome da mãe. Chaves fonéticas somente entram se um experimento posterior provar ganho e mantiver recall aceitável.

Blocking apenas reduz candidatos. Ele não decide que duas Pessoas são iguais.

Nenhum bloco pode ser truncado silenciosamente: excesso exige novo passe/estratégia ou falha operacional explícita.

Modelos V1 continuam usando apenas data de nascimento completa exata. Os passes V2 só são habilitados quando o modelo publicado contém os parâmetros de nascimento por componente, evitando mudança silenciosa de comportamento de um modelo já ativo.

## 8. Relatórios de qualidade do BI

Os estados de qualidade de identidade fazem parte do produto de BI e devem ser segmentáveis por Gestor, Sistema, Tipo de origem e data de referência.

Indicadores mínimos:

- cobertura de CPF, nome, nome da mãe e nascimento;
- `VALIDA`, `SUSPEITA`, `SENTINELA_PROVAVEL`, `IMPOSSIVEL`, `AUSENTE` e `INCONSISTENTE` por campo;
- motivos de qualidade por campo;
- datas de nascimento futuras, idades acima dos limites operacionais e valores sentinela frequentes;
- qualidade de `NASC_DIA`, `NASC_MES` e `NASC_ANO` derivada da qualidade da data de origem, sem perder o valor original;
- cobertura de evidências adicionais (telefone, e-mail, documento, endereço etc.);
- resolução por `CPF_DETERMINISTICO`, `TRIPLA_CADASTRAL_DETERMINISTICA` e `LINKAGE_PROBABILISTICO`;
- pendências, conflitos, ausência de candidato e distribuição de score/modelo;
- percentual de observações que foram reavaliadas após chegada de nova fonte;
- divergências entre fontes sem apagar nenhuma versão recebida.

O BI não deve publicar CPF ou outros identificadores pessoais em claro apenas para produzir indicadores de qualidade.

## 9. Código de Tipo de Benefício/Serviço

A regra corrente permanece: código de Tipo tem exatamente quatro caracteres alfanuméricos maiúsculos, `^[A-Z0-9]{4}$`.

Não há, nesta decisão, restrição adicional para `duas letras + dois dígitos`. Assim `AA01` e `AE01` são válidos, mas códigos como `CRA1` também continuam possíveis. Restringir a `^[A-Z]{2}[0-9]{2}$` seria mudança contratual separada e não é necessária para o linkage.

## 10. Implementação incremental

1. preservar/qualificar todo núcleo e evidências de origem;
2. expor a qualidade no BI;
3. decompor nascimento em dia/mês/ano no calibrador e scorer, preservando a data original;
4. evoluir candidate generation para os passes V2 de nascimento sem alterar modelos V1 já publicados;
5. evoluir normalização de nomes para V2 com remoção de partículas;
6. permitir núcleo incompleto nos contratos de Pessoa sem perder o registro;
7. manter `codigoPessoaOrigem` como chave de origem configurada por Gestor/Sistema;
8. generalizar calibrador e scorer Fellegi–Sunter por catálogo de features;
9. implementar multi-pass blocking sem Soundex obrigatório;
10. replay/reavaliação de pendências quando nova fonte/evidência chegar;
11. validar em HML antes de ativar nova versão do modelo.

Esta ADR registra a direção arquitetural e a primeira implementação V2 de nascimento. Alterações de runtime/DDL devem permanecer compatíveis com o `master` corrente e passar pelos gates existentes antes de merge.