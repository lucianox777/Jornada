# Jornada — Parecer consolidado FS, Splink, Calibrador e bootstrap IBGE

**Data:** 2026-10-09  
**Referência de inspeção:** `master` em `ce6cb5305ad909c94c9702b80ebd747367852299`  
**Natureza:** consolidação para avaliação independente; **não altera decisões canônicas, scorer, gates ou modelos ativos**.  
**Normas precedentes:** [Decisões canônicas](Decisoes_Canonicas_Identidade_Linkage_20260929.md), [Decisões de calibração IBGE](Decisoes_Linkage_Calibracao_IBGE_20260926.md), [Calibração progressiva — candidata](Especificacao_Calibracao_Progressiva_Evidencia_Real_20260929.md), [Corpus sintético V2](Calibrador_Corpus_Sintetico_V2.md).

## 1. Conclusão arquitetural já decidida

1. **Um único decisor estatístico operacional:** Fellegi–Sunter (FS) em C#, evoluindo o contrato V8. Splink é referência metodológica e instrumento de conferência independente, não segundo motor de produção.
2. **Calibração baseada em evidências:** todos os atributos de identidade elegíveis devem poder ser avaliados quanto a comparabilidade, concordância/divergência, frequência, contribuição individual e efeitos conjuntos. Isso **não** significa que todos já participem do score ou que todos devam ter peso não nulo.
3. **Sem independência nem correlação presumidas:** o Calibrador deve testar hipóteses e avaliar dependência **condicional a Match e NonMatch**, com suporte, incerteza e validação; não escolher de antemão primeiro nome/sobrenome independentes nem sobrenomes maternos obrigatoriamente conjuntos.
4. **Bootstrap descartável:** gerar população sintética e versões degradadas com verdade conhecida, ancoradas em marginais IBGE versionadas. Preservar distinção entre estatística observada, transformação sintética e hipótese do gerador. Não confundir associação introduzida pelo gerador com dependência populacional comprovada.
5. **Evolução progressiva:** ingestões reais fornecem novas evidências; somente após suficiência e validação produzir novo RASCUNHO. Monitoramento não modifica modelo ativo; promoção é explícita.
6. **CPF:** vínculo determinístico e possível fonte de pares positivos; não presumir que disponibilidade de CPF represente qualidade dos demais atributos. rótulos retrospectivos por CPF tardio, com âncoras determinísticas independentes e sem revisão humana será necessária para avaliar viés e segurança populacional.
7. **Não reabrir arquitetura:** antes de propor V9, modelos paralelos, combinações fixas ou regra rígida de dois atributos, identificar divergências demonstráveis entre normas, código e resultados.

## 2. Referências estatísticas e limites do sintético

A DC-LK-02A/DC-SYN-01 prevê: pessoa `NOME/TODOS` e `SOBRENOME/TODOS` de São Paulo município **3550308**; mãe `NOME/FEMININO` e `SOBRENOME/TODOS` do **Brasil**. Nascimento segue distribuição diária derivada do Censo 2022/SIDRA 9514, com aproximações e proveniência descritas em [DC-SYN-01-E1](DC-SYN-01-E1_Referencia_Diaria_Nascimento.md). Manter cobertura, supressão e recortes explícitos, sem fallback geográfico oculto.

As marginais IBGE **não determinam** a distribuição conjunta de nomes, sobrenomes, filiação e nascimento. A calibração sobre o corpus sintético estima as relações **representadas naquele corpus**, inclusive erros correlacionados versionados, mas não comprova sua prevalência no município real. Separar população sintética primária representativa, challenge adversarial e partições TRAIN/VALIDATION/TEST. Não usar TEST para selecionar parâmetros ou perfis.

## 3. Formulação estatística

Para estado comparativo `γ_j`, estimar `m_j=P(γ_j|M)` e `u_j=P(γ_j|U)`, sendo `U` coerente com o universo de candidatos recuperados pelo blocking. A contribuição individual FS é `ln(m_j/u_j)`. Para pares de evidências, avaliar `P(γ_i,γ_j|M)` e `P(γ_i,γ_j|U)`; o produto de marginais só é adequado quando a aproximação de independência condicional for sustentada. Medir dependência **não** implica automaticamente trocar para um estado conjunto: exigir suporte e desempenho validado. Um peso zero pode decorrer de política explícita de ausência ou da calibração, nunca de suposição arbitrária.

**Comparabilidade não é concordância:** nascimento divergente com valores utilizáveis nos dois registros é comparável e deve contribuir com a evidência negativa calibrada. A ausência do núcleo V8 recebe LLR neutro por política vigente; essa neutralidade não é extrapolada automaticamente a futuros atributos (ex.: nome social).

## 4. Conferência com código do commit de referência

| Componente / arquivo | Achado comprovado por leitura | Limite ou ação de revisão |
|---|---|---|
| `src/Jornada.Linkage.Core/FellegiSunterScoring.cs` | FS calcula prior, evidências do núcleo, log-odds e posterior; neutralidade de ausências conforme versão | Não comprova inclusão de todos os atributos elegíveis |
| `src/Jornada.Linkage.Core/ProbabilisticLinkagePolicy.cs` | Ranking, threshold, margem, segundo candidato e conflitos; `ApplyTermFrequency` consulta `TryGetPersonFirstName` e `TryGetMotherFirstName` em EXACT | Frequência de sobrenomes **não** aparece como ajuste direto nesse caminho |
| `src/Jornada.Contracts/NominalTermFrequencySnapshot.cs` | Recupera frequência do primeiro token para pessoa e mãe | Conferir contrato semântico antes de estender a sobrenomes |
| `src/Jornada.Contracts/SplinkCompatibleTermFrequency.cs` | Ajuste `weight * ln(referenceU/effectiveFrequency)`; `weight=0` neutraliza na função | Contrato V8 com TF habilitado exige peso positivo; distinguir função e política |
| `src/Jornada.Linkage.Parameters.Worker/IbgeCalibrationAttributeCatalog.cs` | Mapeamento IBGE tipado operacional contempla `name_first` e `mother_name_first` | `name_surnames`, `name_last` e equivalentes maternos não recebem automaticamente frequência oficial |
| `src/Jornada.Linkage.Parameters.Worker/CandidateEvidenceDependencyDiagnostic.cs` | Analisa independência condicional separadamente em Match/NonMatch; distância de variação total, informação mútua, suporte e grupos independentes | **Somente leitura**; features atuais `NOME`, `NOME_MAE`, `NASCIMENTO_CONJUNTO`; não ajusta `m/u`, score ou threshold |
| `src/Jornada.Linkage.Parameters.Worker/LinkageParametersWorker.cs` | Captura corpus, exige amostra mínima, estima parâmetros, usa referência IBGE, busca blocking, calibra decisão e gera RASCUNHO | Não foi demonstrada seleção calibrada de todas as dependências e atributos |
| `src/Jornada.Linkage.Core/FsDecisionThresholdCalibration.cs` | Seleção com orçamento de falsos positivos observados em VALIDATION/TEST (`MaxFp*BasisPoints`) | **Não** equivale a UCB unilateral de FDR implementado |
| `docs/Calibrador_Corpus_Sintetico_V2.md` | Corpus IBGE, degradações, verdade sintética, fluxo de ensaio e Worker real documentados | Documentação/CI não substituem execução e validação representativa |
| `docs/Linkage_IBGE_Name_Frequency.md` | Alerta sobre diferença entre tokenização interna e semântica oficial de sobrenomes | Reconciliar com DC-LK-02A sem atribuir frequência a tokens sem suporte |

**Interpretação central:** o problema não é ausência de decisão sobre dependências; a DC-LK-03 já decidiu. Existe diagnóstico implementado, mas não foi comprovada sua integração à seleção calibrada do contrato de evidências. A referência IBGE contempla sobrenomes na decisão canônica, porém o mapeamento e o TF operacional ainda não demonstram cobertura equivalente.

## 5. Resolução, segurança e governança

Blocking recupera candidatos; não comprova identidade. Comparadores produzem estados; FS calcula evidência; política de decisão aplica threshold, margem e conflitos. A exigência universal de “nome + outro atributo comparável” **não foi identificada** no caminho examinado. Se desejada como proteção conservadora, deve ser deliberada e versionada como política, com impacto em FP/FN/abstenção medido; não deve ser apresentada como teorema FS. Testar nome isolado, nome raro, EXACT/HIGH no mesmo contexto, nascimento discordante, atributos ausentes e segundo candidato.

O limite unilateral de FDR consta de especificação **candidata**, não deve ser descrito como gate operacional existente. O orçamento atual de FP observado não é automaticamente FDR certificado. A avaliação representativa permanece necessária; **não haverá revisão humana**. A rotulagem retrospectiva deverá aproveitar CPF tardio com âncora determinística independente da decisão probabilística avaliada, incluindo controles de vazamento e viés de seleção. A issue #31 deve ser reconciliada com essa decisão.

## 6. Plano de avaliação independente — sem redesenho prévio

1. **Inventariar ponta a ponta** atributos de identidade elegíveis, origem, comparadores, estados, disponibilidade, mapeamentos IBGE, parâmetros persistidos e evidências realmente consumidas pelo scorer.
2. **Auditar corpus IBGE**: marginais completas, município 3550308, mãe Brasil, distribuição de nascimento, suposições de dependência, perfis de degradação, fingerprints e separação de challenge set.
3. **Auditar estimadores**: `m/u` condicionados ao blocking, suficiência, independência de rótulos, prior, TF, estados conjuntos, efeitos da seleção por CPF.
4. **Auditar dependências**: verificar se diagnóstico alimenta escolha governada de evidências; não inferir correlação a priori; não converter diagnóstico em substituição automática sem suporte.
5. **Testar scorer/decisão**: EXACT versus HIGH com contexto e parâmetros congelados, nomes raros/comuns, apenas nome, mãe/nascimento ausentes ou divergentes, margem e múltiplos candidatos; medir FP/FN/abstenção.
6. **Conferência externa**: separar equivalência numérica C# × implementação independente **com mesma fórmula/parâmetros** da comparação de adequação estatística entre diferentes modelos; não exportar dados operacionais indevidamente.
7. **Registrar lacunas verificáveis** e correções mínimas com testes; versionar mudança de scorer/contrato e recalibrar thresholds/margens quando necessário. não criar segundo motor; V9 somente como versão técnica necessária e validada por antecipação.
8. **Planejar transição real**: acumulação de evidências sem mudar modelo ativo, rótulos retrospectivos por CPF tardio, com âncoras determinísticas independentes e sem revisão humana, suficiência, nova calibração e promoção governada.

## 7. Perguntas objetivas ao avaliador

- O conjunto de atributos calibrados cobre efetivamente todos os atributos de identidade elegíveis? Quais apenas constam no catálogo?
- A tokenização dos sobrenomes permite aplicar com segurança a semântica da estatística IBGE publicada?
- Quais dependências do corpus são observadas e quais são impostas pelo gerador? Como essa proveniência afeta a certificação?
- O diagnóstico condicional atual tem suporte para orientar seleção de evidências? Onde ocorre (ou falta) sua integração?
- O `u` e o TF refletem o universo efetivo de candidatos dos passes de blocking?
- A política atual pode resolver por nome isolado? Em quais parâmetros/cenários? Qual impacto de uma proteção adicional?
- Quais diferenças são falhas de implementação e quais são evoluções ainda não aprovadas?
- Qual é a menor mudança verificável para cumprir a arquitetura **já decidida**, sem duplicar código ou alterar o modelo ativo silenciosamente?

## 8. Critérios de conclusão

**Não declarar “implementado” por mera existência de classe ou documentação.** Para cada requisito: citar arquivo/método, contrato, teste executado, evidência de resultado, proveniência e eventual lacuna. Separar explicitamente **decisão vigente**, **código observado**, **especificação candidata**, **hipótese sintética** e **dependência de dados reais**.

**Limite deste parecer:** leitura de código/documentação no commit citado; **não** houve execução local de testes, SQL, ensaio de calibração ou Splink independente nesta rodada.

**Conclusão:** preservar o FS C# único e o bootstrap IBGE; completar a implementação do Calibrador para avaliar atributos e dependências com suporte, sem presunções estatísticas; reconciliar referências de sobrenomes e scorer; manter substituição progressiva por evidência real e governança explícita.

## 9. Esclarecimentos de decisões preexistentes e pendências objetivas (09/10/2026)

### 9.1. Corpus de 30.000 pessoas e substituição progressiva

O gerador `src/Jornada.Linkage.SyntheticCorpus/Program.cs` estabelece `--people` padrão **30.000**, e `scripts/dev-console-gold-synthetic.ps1` exige essa escala. Trata-se do tamanho operacional já decidido para o corpus primário, **não** de proposta de aumento. A suficiência local de estados raros e de pares efetivamente amostrados continua sendo verificada pelo Calibrador, sem reabrir a escala global. O corpus utiliza marginais IBGE; como as distribuições públicas de nome e sobrenome não especificam toda a distribuição conjunta, dependências geradas devem ter proveniência explícita. O bootstrap **pode selecionar parâmetros e representações** sobre o sintético: suas conclusões são provisórias e a dependência das hipóteses sintéticas **diminui conforme evidência real suficiente passa a sustentar novas calibrações**. Ingestões por si só não alteram o modelo ativo; novos RASCUNHOS e promoção permanecem governados.

### 9.2. Último sobrenome: decisão, extração e lacuna de integração

A diretriz de utilizar o **último sobrenome** e a frequência IBGE de `SOBRENOME` deve ser preservada. A metodologia de coleta do IBGE prioriza o último sobrenome quando nem todos são informados. O projeto já possui `BrazilianNameComponents.Project(...).LastContentSurname`, projeção `last_content_surname`, features `name_last` e `mother_name_last`, além de estimador IBGE com `IbgeNameStatisticKind.Surname`. Contudo, a inspeção do caminho operacional mostra que `IbgeCalibrationAttributeCatalog` mapeia apenas `name_first` e `mother_name_first`, enquanto `ProbabilisticLinkagePolicy.ApplyTermFrequency` aplica TF apenas aos primeiros nomes. Assim, **extração/referência existentes não equivalem a TF de último sobrenome já implantada no scorer**. Reconciliar explicitamente o gate de `Linkage_IBGE_Name_Frequency.md` com a diretriz de último sobrenome; registrar contrato de extração, ambiguidade residual, proveniência, validação e fallback seguro. Reutilizar componentes existentes, sem duplicar extratores.

### 9.3. Seleção de representações e dependências

O FS operacional permanece **único**. Para que o Calibrador compare V8, evidência de primeiro nome + último sobrenome com TF e eventual estado conjunto, essas representações precisam estar implementadas como candidatas. Versionar o contrato/scorer se a mudança for incompatível; **não** presumir antecipadamente a superioridade de qualquer candidato ou a correlação entre sobrenomes. O diagnóstico de dependência existente é somente leitura e deve ser conectado a uma seleção governada com validação TRAIN/VALIDATION/TEST. A calibração sintética inicial é permitida, mas não constitui prova de dependências populacionais reais.

### 9.4. Ausência de revisão humana e CPF tardio

**Não haverá revisão humana de pares**, por decisão arquitetural. Rótulos retrospectivos do estrato originalmente sem CPF deverão derivar de **CPF tardio** quando houver âncora determinística admissível, sem usar a própria resolução probabilística avaliada como verdade. Conferir a integração com o ledger semântico e distinguir pares Match de NonMatch, evitando viés de seleção e vazamento. A redação da issue #31 e de documentos preparatórios deve ser reconciliada; não criar requisito de revisão manual.

### 9.5. Ordem executiva das pendências

1. Executar testes com parâmetros efetivamente calibrados: **nome isolado raro/comum**, inversão **EXATO/HIGH**, mãe/nascimento divergentes, ausências, segundo candidato e margem. Não criar gate rígido de dois atributos sem evidência.
2. Formalizar a conciliação semântica do **último sobrenome** entre DC-LK-02A e o gate atual.
3. Completar o mapeamento IBGE e o TF do último sobrenome da pessoa e da mãe, com testes e versionamento apropriado.
4. Disponibilizar representações candidatas no FS único, integrar diagnóstico e seleção calibrada sem pesos/dependências fixos.
5. Validar o bootstrap de 30.000 e a transição progressiva governada para evidências reais; verificar CPF tardio sem revisão humana.

**Estado desta nota:** esclarecimento documental; nenhuma dessas integrações ou execuções de teste é declarada concluída apenas por esta atualização.

> **Decisões vigentes de 09/10/2026:** consultar a [consolidação canônica de bootstrap estatístico, último sobrenome, CPF tardio e substituição histórica](Decisoes_Canonicas_Identidade_Linkage_20260929.md#consolidação-decisória-de-09102026--bootstrap-estatístico-e-substituição-histórica). Esta referência prevalece sobre passagens preparatórias incompatíveis; não há segunda cópia normativa neste documento.


> **Norma vigente (09/10/2026) — FS, Splink, TF, IBGE e Calibrador:** consultar [DC-LK-TF](Decisoes_Canonicas_Identidade_Linkage_20260929.md#dc-lk-tf--norma-vigente-de-frequência-nominal-fs-e-calibrador-09102026). O peso TF zero é neutro (peso 1 aplica ajuste integral); os pesos e m/u devem ser estimados pelo Calibrador a partir do bootstrap sintético IBGE e, progressivamente, de evidência histórica real. Primeiro nome e último sobrenome significativo de pessoa e mãe devem participar do FS sem dupla contagem. V8 é referência histórica, não segunda implementação operacional. Em caso de divergência, prevalece a decisão canônica; este documento não certifica implementação concluída.
