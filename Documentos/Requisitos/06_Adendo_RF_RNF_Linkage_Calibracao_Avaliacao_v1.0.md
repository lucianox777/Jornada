# Adendo de Requisitos — Linkage, Calibração, Avaliação e IBGE

**Versão:** 1.0  
**Data:** 09/09/2026  
**Escopo:** Jornada do Cidadão — Fase 1  
**Status:** NORMATIVO — extensão aditiva aos baselines RF v1.0 e RNF v1.0

> Este adendo registra novos requisitos sem renumerar os baselines históricos de 03/09/2026. Na próxima consolidação formal, estes requisitos deverão ser incorporados às novas versões dos documentos RF/RNF e à Matriz de Rastreabilidade.

## 1. Requisitos Não Funcionais

### RNF34 — Integração Contínua

**Requisito normativo.** O projeto deve utilizar Integração Contínua (CI). Toda alteração candidata à integração deve ser validada automaticamente, no mínimo, quanto a restauração de dependências, compilação e testes automatizados aplicáveis. Gates adicionais de contrato, DDL, segurança e empacotamento devem permanecer no CI quando existirem para o componente alterado.

**Critério de aceitação.** Uma alteração não é considerada homologada para integração enquanto os gates obrigatórios de CI associados ao seu escopo não concluírem com sucesso.

### RNF35 — Documentação e diagramas UML

**Requisito normativo.** Diagramas técnicos e funcionais normativos do projeto devem utilizar notação UML adequada ao objetivo do diagrama. As fontes dos diagramas devem permanecer versionadas junto ao código/documentação sempre que tecnicamente possível.

**Critério de aceitação.** Diagramas novos ou materialmente alterados que representem componentes, classes, sequências, estados, atividades, implantação ou casos de uso identificam o tipo UML empregado e não utilizam notação proprietária ambígua como substituto do modelo normativo.

### RNF36 — Ambiente tecnológico e reprodutibilidade de engenharia

**Requisito normativo.** O ambiente de engenharia da solução deve ser explicitamente definido e versionado quanto às tecnologias necessárias ao desenvolvimento, compilação, teste, execução, conteinerização, versionamento e consumo analítico. Inclui, conforme o componente: C#/.NET, Docker, Git/GitHub, SQL, Power BI Desktop/PBIP e ferramentas auxiliares de build/teste.

**Critério de aceitação.** Dependências de ambiente relevantes possuem versão, faixa compatível ou mecanismo reproduzível de instalação/configuração; credenciais e segredos não são incorporados à documentação nem ao repositório.

## 2. Requisitos Funcionais

### RF-051 — Paralelizar Calibrador e Avaliador quando vantajoso

**Requisito funcional.** O Calibrador e o Avaliador devem executar em paralelo as etapas independentes que admitam paralelismo seguro sempre que medição reproduzível demonstrar ganho de desempenho em relação à execução equivalente sequencial.

**Critério de aceitação.** A execução paralela deve produzir resultado funcional equivalente e determinístico para as mesmas entradas e versão de regras. Quando não houver ganho mensurável, houver risco de contenção, perda de determinismo ou aumento relevante de custo, a implementação pode manter execução sequencial e registrar a decisão de medição.

### RF-052 — Utilizar dados do IBGE como referência estatística externa

**Requisito funcional.** Calibração, avaliação e otimização do linkage devem utilizar dados oficiais agregados do IBGE sempre que houver conjunto aplicável, tecnicamente acessível e capaz de melhorar a discriminação, o blocking ou a avaliação sem transformar estatística populacional em verdade individual.

**Critério de aceitação.** Toda utilização registra fonte, versão/snapshot e finalidade; indisponibilidade do IBGE não autoriza fabricação de dados nem impede execução quando o desenho admitir fallback explícito.

### RF-053 — Evitar novo snapshot IBGE quando a fonte não mudou

**Requisito funcional.** Antes de baixar e materializar novo snapshot de uma fonte IBGE, o sistema deve verificar se há evidência de alteração em relação ao último snapshot conhecido e evitar download/materialização redundante quando não houver mudança.

**Critério de aceitação.** A verificação deve usar metadados baratos disponíveis na origem — preferencialmente ETag e/ou Last-Modified e Content-Length. O número de bytes pode ser utilizado como sinal rápido, mas não como prova criptográfica de identidade. O snapshot incorporado deve possuir fingerprint/hash de conteúdo ou representação canônica para rastreabilidade. Se a origem não expuser validadores confiáveis, a rotina deve usar fallback documentado.

### RF-054 — Usar IBGE para melhorar o blocking

**Requisito funcional.** O otimizador de blocking deve poder incorporar frequências e demais estatísticas agregadas aplicáveis do IBGE para avaliar capacidade discriminativa e selecionar combinações de passes/campos, sempre preservando medidas obtidas no corpus da Jornada e a validação independente.

**Critério de aceitação.** A contribuição do IBGE é versionada e mensurável; o otimizador consegue comparar alternativa com e sem enriquecimento externo e não promove automaticamente uma regra que degrade os critérios de recall/redução definidos para o experimento.

### RF-055 — Otimizar componentes de nome e data de nascimento no blocking

**Requisito funcional.** O espaço de busca do otimizador de blocking deve considerar, no mínimo, os seguintes componentes quando disponíveis: nome completo normalizado, prenome/primeiro nome, sobrenome(s), último nome e componentes dia, mês e ano da data de nascimento. O otimizador deve selecionar a melhor combinação de um ou mais passes/campos segundo métricas objetivas de cobertura de vínculos verdadeiros, redução de candidatos e custo, podendo utilizar frequências do IBGE para nomes.

**Critério de aceitação.** A combinação escolhida é reproduzível a partir do corpus, configuração, snapshot IBGE e versão do algoritmo; componentes ausentes são tratados explicitamente e não como concordância.

### RF-056 — Versionar regras dinâmicas entre Calibrador e Avaliador

**Requisito funcional.** Toda regra dinâmica, combinação de blocking, limiar ou parâmetro promovido pelo Calibrador para avaliação deve ser publicado em um pacote imutável e versionado. O Avaliador deve consumir exatamente uma versão desse pacote por execução e registrar sua identidade nos resultados.

**Critério de aceitação.** Uma execução do Avaliador não pode misturar regras de versões distintas. O pacote deve identificar, no mínimo, versão/fingerprint das regras, versão do algoritmo/calibrador, configuração relevante e, quando utilizado, identidade do snapshot IBGE. Alteração material gera nova versão; replay com a mesma versão e mesmas entradas é reprodutível.

## 3. Regras de engenharia derivadas

1. O paralelismo é uma otimização subordinada à correção e à reprodutibilidade; não é permitido alterar semântica para obter throughput.
2. Estatística IBGE é evidência agregada auxiliar, não identificador civil nem ground truth individual.
3. `Content-Length` reduz downloads inúteis, mas dois conteúdos diferentes podem possuir o mesmo tamanho; por isso a identidade persistida do snapshot deve continuar baseada em fingerprint/hash.
4. O Avaliador é independente quanto ao corpus/medição, mas não quanto à definição da regra sob teste: ele deve testar a versão produzida/promovida pelo Calibrador sem reinterpretação silenciosa.

## 4. UML — fluxo normativo resumido

O diagrama UML versionado deste adendo está em `Solution/docs/uml/Linkage_Calibrador_Avaliador_IBGE.puml`.

## 5. Impacto esperado na próxima consolidação

A próxima versão consolidada da família de requisitos deve incorporar RNF34–RNF36 e RF-051–RF-056, atualizar as quantidades do Índice Mestre e da Matriz de Rastreabilidade e ajustar os gates que atualmente verificam contagens fixas dos baselines históricos.
