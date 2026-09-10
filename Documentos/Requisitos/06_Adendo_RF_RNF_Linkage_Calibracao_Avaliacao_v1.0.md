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

### RF-052 — Utilizar dados do IBGE nos atributos semanticamente compatíveis

**Requisito funcional.** O Calibrador deve executar contra a base da Jornada e utilizar dados oficiais agregados do IBGE para cada atributo da Jornada para o qual exista informação IBGE semanticamente compatível e tecnicamente utilizável. A informação IBGE complementa a calibração do atributo; ela não define quais atributos da Jornada podem participar do linkage e não constitui verdade individual.

Atualmente, a integração conhecida de nomes deve ser aplicada aos componentes de nome compatíveis. O conjunto oficial `Nomes no Brasil` do Censo 2022 disponibiliza estatísticas para Brasil, Unidades da Federação e Municípios. Quando o município da observação for conhecido e compatível com a codificação territorial oficial, o Calibrador pode utilizar o recorte municipal correspondente como contexto estatístico adicional, mantendo também os recortes mais amplos necessários para comparação, fallback e estabilidade. Um atributo sem fonte IBGE equivalente continua podendo participar normalmente da calibração por evidência obtida na própria base da Jornada, sem enriquecimento IBGE artificial.

**Critério de aceitação.** Toda correspondência atributo Jornada → conjunto/estatística IBGE é explícita, versionada e registra fonte, snapshot, nível territorial e finalidade. A ausência de correspondência IBGE não elimina o atributo da calibração, não autoriza aproximação semântica e não impede a execução. Recorte territorial IBGE é evidência estatística contextual e não prova de residência ou identidade individual.

### RF-053 — Evitar novo snapshot IBGE quando a fonte não mudou

**Requisito funcional.** Antes de baixar e materializar novo snapshot de uma fonte IBGE, o sistema deve verificar se há evidência de alteração em relação ao último snapshot conhecido e evitar download/materialização redundante quando não houver mudança.

**Critério de aceitação.** A verificação deve usar metadados baratos disponíveis na origem — preferencialmente ETag e/ou Last-Modified e Content-Length. O número de bytes pode ser utilizado como sinal rápido, mas não como prova criptográfica de identidade. O snapshot incorporado deve possuir fingerprint/hash de conteúdo ou representação canônica para rastreabilidade. Se a origem não expuser validadores confiáveis, a rotina deve usar fallback documentado.

### RF-054 — Enriquecer o blocking com IBGE apenas onde houver correspondência válida

**Requisito funcional.** O otimizador de blocking deve poder incorporar frequências e demais estatísticas agregadas do IBGE exclusivamente nos atributos para os quais exista correspondência semântica explícita. Os demais atributos candidatos ao blocking continuam sendo avaliados pelas evidências do corpus da Jornada, sem receber peso ou estatística IBGE por aproximação.

Para nomes e sobrenomes, quando disponível, o otimizador pode comparar frequências em múltiplas escalas territoriais oficiais — por exemplo município, UF e Brasil — e selecionar a contribuição estatística que melhore as métricas de blocking no corpus de calibração sem degradar a avaliação independente.

**Critério de aceitação.** Para cada atributo enriquecido, a contribuição do IBGE, inclusive o nível territorial utilizado, é versionada e mensurável. O otimizador consegue comparar alternativa com e sem enriquecimento externo e não promove automaticamente uma regra que degrade os critérios definidos para o experimento. A lista de atributos compatíveis deve ser explícita e extensível quando novos conjuntos oficiais forem incorporados.

### RF-055 — Otimizar componentes de nome e data de nascimento no blocking

**Requisito funcional.** O espaço de busca do otimizador de blocking deve considerar, no mínimo, quando disponíveis na Jornada: nome completo normalizado, prenome/primeiro nome, sobrenome(s), último nome e componentes dia, mês e ano da data de nascimento. O otimizador deve selecionar a melhor combinação de um ou mais passes/campos segundo métricas objetivas de cobertura de vínculos verdadeiros, redução de candidatos e custo. Os componentes de nome devem receber enriquecimento IBGE quando houver estatística oficial compatível; os componentes de data ou quaisquer outros campos somente recebem enriquecimento IBGE se existir correspondência oficial semanticamente válida.

**Critério de aceitação.** A combinação escolhida é reproduzível a partir do corpus, configuração, snapshots externos efetivamente utilizados e versão do algoritmo; componentes ausentes são tratados explicitamente e não como concordância.

### RF-056 — Versionar regras dinâmicas entre Calibrador e Avaliador

**Requisito funcional.** Toda regra dinâmica, combinação de blocking, limiar ou parâmetro promovido pelo Calibrador para avaliação deve ser publicado em um pacote imutável e versionado. O Avaliador deve consumir exatamente uma versão desse pacote por execução e registrar sua identidade nos resultados.

O Calibrador pode ser executado repetidamente e gerar novas versões candidatas de regras a partir da base e dos snapshots externos aplicáveis. A geração de uma nova versão não deve modificar versões anteriores nem fazer o Avaliador misturar regras entre versões.

**Critério de aceitação.** Uma execução do Avaliador não pode misturar regras de versões distintas. O pacote deve identificar, no mínimo, versão/fingerprint das regras, versão do algoritmo/calibrador, configuração relevante e identidade de cada snapshot IBGE efetivamente utilizado. Alteração material gera nova versão; replay com a mesma versão e mesmas entradas é reprodutível.

### RF-057 — Suporte físico indexado para blocking dinâmico

**Requisito funcional.** O blocking dinâmico deve dispor de acesso indexado aos valores utilizados para geração de candidatos. Como estratégia preferencial para atributos derivados e combinações variáveis, a solução deve utilizar uma projeção operacional reconstruível de chaves de blocking, contendo a identidade da Pessoa, a versão de normalização, o atributo lógico e o valor normalizado. Essa projeção deve possuir índice estável que permita localizar candidatos por versão de normalização, atributo e valor sem exigir novo DDL a cada versão de ruleset.

Campos que já possuam índice físico adequado na Gold podem continuar usando esse acesso direto quando ele for mais simples e eficiente. O Calibrador pode recomendar suporte físico adicional quando medição mostrar necessidade, mas não deve criar ou remover índices livremente durante a busca de parâmetros. Particionamento não deve ser introduzido apenas porque a política de blocking é dinâmica; exige evidência própria de que índices/projeção não atendem adequadamente a escala, manutenção ou desempenho.

**Critério de aceitação.** Todo atributo candidato possui origem física explícita: coluna direta ou projeção materializada derivada. A projeção é reconstruível a partir da Gold, não constitui nova verdade cadastral e registra a versão de normalização. O acesso de lookup possui índice estável equivalente a `(normalizacao_versao, atributo, valor_normalizado, pessoa_uuid)`. Mudança da combinação de blocking não exige DDL por ruleset. Qualquer criação/remoção adicional de índice ocorre por migração/deploy controlado e passa pelos gates de DDL/CI dos SGBDs suportados.

## 3. Regras de engenharia derivadas

1. O paralelismo é uma otimização subordinada à correção e à reprodutibilidade; não é permitido alterar semântica para obter throughput.
2. Estatística IBGE é evidência agregada auxiliar, não identificador civil nem ground truth individual.
3. A existência de um campo na Jornada não implica que ele possua enriquecimento IBGE; a correspondência deve ser semanticamente válida e cadastrada explicitamente.
4. A inexistência de dado IBGE compatível não exclui o campo do Calibrador ou do otimizador de blocking.
5. `Content-Length` reduz downloads inúteis, mas dois conteúdos diferentes podem possuir o mesmo tamanho; por isso a identidade persistida do snapshot deve continuar baseada em fingerprint/hash.
6. O Avaliador é independente quanto ao corpus/medição, mas não quanto à definição da regra sob teste: ele deve testar a versão produzida/promovida pelo Calibrador sem reinterpretação silenciosa.
7. Blocking dinâmico não implica particionamento dinâmico nem DDL por versão. Para atributos derivados e combinações variáveis, deve-se preferir projeção de chaves com índice estável.
8. Dados da projeção de blocking são derivados e reconstruíveis; não devem competir com a Gold como fonte de verdade.
9. Recortes municipais/UF do IBGE podem refinar a raridade estatística de nomes, mas o município não deve ser inferido a partir do nome nem usado como verdade individual.

## 4. UML — fluxo normativo resumido

O diagrama UML versionado deste adendo está em `Solution/docs/uml/Linkage_Calibrador_Avaliador_IBGE.puml`.

## 5. Impacto esperado na próxima consolidação

A próxima versão consolidada da família de requisitos deve incorporar RNF34–RNF36 e RF-051–RF-057, atualizar as quantidades do Índice Mestre e da Matriz de Rastreabilidade e ajustar os gates que atualmente verificam contagens fixas dos baselines históricos.
