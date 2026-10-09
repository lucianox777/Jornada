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
6. **CPF:** vínculo determinístico e possível fonte de pares positivos; não presumir que disponibilidade de CPF represente qualidade dos demais atributos. Evidência independente sem CPF (#31) será necessária para avaliar viés e segurança populacional.
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

O limite unilateral de FDR consta de especificação **candidata**, não deve ser descrito como gate operacional existente. O orçamento atual de FP observado não é automaticamente FDR certificado. A issue #31 continua condicionante de avaliação representativa antes de resolução probabilística real.

## 6. Plano de avaliação independente — sem redesenho prévio

1. **Inventariar ponta a ponta** atributos de identidade elegíveis, origem, comparadores, estados, disponibilidade, mapeamentos IBGE, parâmetros persistidos e evidências realmente consumidas pelo scorer.
2. **Auditar corpus IBGE**: marginais completas, município 3550308, mãe Brasil, distribuição de nascimento, suposições de dependência, perfis de degradação, fingerprints e separação de challenge set.
3. **Auditar estimadores**: `m/u` condicionados ao blocking, suficiência, independência de rótulos, prior, TF, estados conjuntos, efeitos da seleção por CPF.
4. **Auditar dependências**: verificar se diagnóstico alimenta escolha governada de evidências; não inferir correlação a priori; não converter diagnóstico em substituição automática sem suporte.
5. **Testar scorer/decisão**: EXACT versus HIGH com contexto e parâmetros congelados, nomes raros/comuns, apenas nome, mãe/nascimento ausentes ou divergentes, margem e múltiplos candidatos; medir FP/FN/abstenção.
6. **Conferência externa**: separar equivalência numérica C# × implementação independente **com mesma fórmula/parâmetros** da comparação de adequação estatística entre diferentes modelos; não exportar dados operacionais indevidamente.
7. **Registrar lacunas verificáveis** e correções mínimas com testes; versionar mudança de scorer/contrato e recalibrar thresholds/margens quando necessário. Não criar V9 por antecipação.
8. **Planejar transição real**: acumulação de evidências sem mudar modelo ativo, rótulos independentes sem CPF (#31), suficiência, nova calibração e promoção governada.

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
