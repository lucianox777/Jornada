# Diretrizes consolidadas — identidade progressiva e apoio à decisão

**Data:** 26/09/2026. **Status:** diretriz de produto e arquitetura; distingue regras já existentes de alterações propostas. **Prioridade:** motor de linkage C# e identidade progressiva. Este documento consolida decisões de produto discutidas no ciclo de documentação do PR #492; não constitui evidência de implementação, build ou homologação.

## 1. Finalidade: melhor representação disponível

A Jornada é **sistema de apoio à decisão das Secretarias**, não mecanismo de concessão ou negativa automática de benefícios, nem certificadora de identidade civil. O núcleo mantém a **melhor representação estatística e incremental disponível** de uma pessoa segundo as evidências de identidade recebidas. Uma representação canônica é uma hipótese operacional atualizável, não promessa de vínculo perfeito ou definitivo. A Secretaria competente responde por sua decisão finalística, inclusive pela apreciação de documentos e circunstâncias do atendimento.

Preservar incerteza, alternativas, divergências, proveniência, datas, modelo utilizado e histórico de mudanças. `REFERENCIA` significa representação canônica operacional; `PROVISORIA` e `INDEFINIDA` não são erros ou exclusão de direitos. O score probabilístico não deve ser apresentado como certeza. Uma atualização pode alterar a melhor representação, o conjunto de candidatos, a confiança ou a explicação sem que a identidade canônica escolhida necessariamente mude.

## 2. Hierarquia de evidências por atributo

Na composição da Gold, entre evidências **admissíveis e disponíveis para o mesmo atributo**, a prioridade é:

1. Documentação apresentada mais recentemente.
2. Documentação apresentada anteriormente.
3. Autodeclaração mais recente.
4. Autodeclaração mais antiga.

A ordenação por recência ocorre **dentro da mesma classe**. Guardar separadamente, quando disponíveis, a data da apresentação/registro, a emissão do documento, a data do fato, o prazo de validade aplicável, a fonte e o responsável pelo registro. Não inferir que documento mais novo contém fato mais recente, nem aplicar validade universal de dez anos a documentos de espécies diferentes. A hierarquia é preferência de composição, **não** prova de exatidão. Preservar valores divergentes, sua origem e as circunstâncias de eventual retificação; campo ausente não apaga evidência anterior. Aplicar a regra **campo a campo**, não substituir toda a Pessoa pela última entrega.

**Diferença em relação à implementação:** `identidade.sp_recompor_gold_pessoa` já é a procedure canônica e a regra documentada atual prioriza conferência documental e recência; verificar se a distinção exata entre apresentação documental/autodeclaração e a data de apresentação existe no esquema. Implementar o ajuste que faltar e testar antes de declarar conformidade integral à hierarquia acima.

## 3. Motor de linkage e universo de atributos

Manter **um motor operacional C#**, reutilizando SQL Server, blocking, Processor, Runner, parâmetros e gates versionados, ledger e Gold. Traduzir e reutilizar recursos pertinentes do Splink: comparadores, Fellegi–Sunter, ajuste por frequência, explicação de contribuição por campo e estimação. A base de frequências de nomes do IBGE já integrada permanece como bootstrap; CIDACS-RL serve de referência metodológica para nomes e bases brasileiras, não substitui automaticamente o IBGE. Estudos comparativos existentes devem ser examinados antes de desenvolver novos benchmarks; executar comparação adicional apenas para lacunas que afetem decisões concretas de engenharia.

O linkage usa **somente sinais de identidade aprovados e contratados**: inicialmente CPF quando disponível, nome civil, data de nascimento e filiação materna quando informada. Outros atributos associados a certidões de nascimento, como filiação paterna e naturalidade, podem ser acrescentados quando constarem dos contratos e houver regras de comparação justificadas. Nome social é respeitado como referência de tratamento/exibição, preservando evidências de nome civil quando legitimamente necessárias. `codigoPessoaOrigem` continua opcional; `initial_uuid` é âncora de linhagem, nunca feature estatística. Benefícios, serviços, endereço e outros atributos não identitários não devem ser usados para pontuar identidade por conveniência.

A qualidade dos dados é variável e desconhecida a priori. Não pressupor que a presença de CPF, a ausência de CPF ou a Secretaria fornecedora determinem a qualidade dos demais atributos. Não criar testes de hipótese sobre suposta maturidade cadastral das fontes; verificar as propriedades do motor diante das evidências efetivamente recebidas.

## 4. Identidade progressiva e reprocessamento mínimo suficiente

Distinguir **entrega recebida**, **evidência de identidade alterada**, **reavaliação executada**, **melhor representação alterada** e **transição persistida**. Entregas idempotentes ou mudanças exclusivamente não identitárias não disparam novo scoring. Alterações nos sinais de identidade disparam reavaliação da observação. Novas referências ou mudanças de referências podem alterar o conjunto de candidatos de observações que não receberam nenhuma nova entrega: invalidar os pendentes e demais observações potencialmente afetadas pelos blocos antigos e novos. Mudanças de versão do modelo, comparadores, guards e blocking exigem reavaliação versionada do escopo afetado.

Usar fila de invalidação deduplicada por observação e versões de evidência/modelo, com motivo rastreável. Recalcular somente o universo afetado, com mecanismo excepcional de reconciliação para identificar dependências perdidas. A chegada tardia de CPF pode alterar a representação pela rota determinística, preservando conflitos e a história.

**Gravar apenas quando houver mudança semântica na representação**, não necessariamente só quando mudar o UUID. A comparação deve incluir status, vínculo/UUID, motivo, alternativas, conflitos e mudanças relevantes de confiança/explicação segundo contrato versionado; definir tolerância para evitar gravações por ruído numérico irrelevante. Manter separadas trilhas de ingestão, execução diagnóstica e transições de identidade. A otimização não elimina o ledger append-only nem atos de atendimento. Recompor somente as pessoas Gold afetadas, preservando atomicidade e rastreabilidade.

A identidade canônica não deve ser fundida por mera transitividade de pares semelhantes: uma nova observação pode aproximar duas referências incompatíveis sem provar que ambas são a mesma pessoa. O objetivo é melhor representação governada com incerteza explícita, não eliminar todos os casos não resolvidos.

## 5. Atendimento no balcão

O atendimento é a última etapa do ciclo e mantém, por enquanto, a confirmação governada existente. Como evolução opcional, oferecer busca de até cinco candidatos semelhantes usando **somente sinais de identidade**, reutilizando comparadores e blocking do motor C#. `FHIR Patient/$match` é referência para contrato de busca, score e classificação de candidatos; um adaptador FHIR pode ser oferecido quando útil, sem impor semântica clínica a todas as Secretarias. Candidato sugerido não é vínculo certificado. A responsabilidade pela decisão de atendimento é finalística; registrar autoria individual quando a integração de identidade corporativa estiver disponível. Confirmações do balcão não são automaticamente amostra representativa para treinar parâmetros do motor.

## 6. Ensaio e evidência

DEV → Ensaio com uma ou várias Secretarias → HML → Produção. Ensaio e HML devem manter paridade funcional e operacional, com diferenças planejadas apenas nas massas de dados e nos valores ambientais próprios. Cada Secretaria prepara e fornece os dados conforme seu contrato e governança, inclusive massas sintéticas derivadas de bases reais quando autorizadas; não presumir anonimato por serem sintéticas.

O Ensaio pode produzir **evidência estatística e operacional forte** quando a fidelidade das massas, a verdade de referência independente, a cobertura e a metodologia sustentarem as conclusões. Executar três ou mais ondas para verificar atualização da melhor representação, chegada tardia de CPF, referências novas de outras Secretarias, homônimos, correções, replays, conflitos, reavaliação seletiva e gravação apenas por mudança. Medir precisão/recall quando houver verdade de referência adequada, sem exigir certeza de vínculo em produção. Registrar limitações da evidência, não extrapolar a populações não cobertas nem inferir maturidade das Secretarias. O Ensaio pode instruir a #31 pelos gates formais aplicáveis; não exige repetir automaticamente um estudo válido em HML apenas pela mudança de etapa.

## 7. Ordem de implementação e fronteiras

1. Consolidar contratos, invariantes e testes do motor C# inspirado no Splink; revisar estudos existentes antes de experimentos externos.
2. Corrigir a reavaliação seletiva por mudanças **e dependências do lado candidato**, e a persistência por mudança semântica, preservando auditoria.
3. Ajustar a composição Gold para a hierarquia documental/autodeclaratória por atributo, se o esquema e a procedure atuais ainda não a reproduzirem.
4. Executar Ensaio em ondas com Secretarias participantes; qualificar evidência para a #31 e registrar limitações.
5. Tipar contratos da API e, posteriormente, oferecer busca opcional de semelhantes inspirada no FHIR para o balcão.
6. BI ampliado e transparência ao cidadão são posteriores ao núcleo; preservar apenas métricas indispensáveis à engenharia e ao Ensaio.

**Pendências não resolvidas por este documento:** conferência de implementação da hierarquia, definição de mudança semântica e tolerâncias, estratégia completa de invalidação, retenção/expurgo, aprovações institucionais e gates de promoção. Nenhuma delas deve ser declarada concluída sem alteração de código, testes e evidência apropriada.

## Documentos relacionados

- `Gold_Pessoa_Universo_CPF.md`: composição atual da Gold e diferenças normativas.
- `Nucleo_Linkage_Identidade_Progressiva.md`: arquitetura do motor e reprocessamento.
- `Estudo_Comparativo_Linkage_Identidade_Progressiva.md`: Splink, Senzing, Dedupe e outras referências.
- `Ensaio_Secretarias_Paridade_HML.md`: contratos, paridade, evidência e critérios do Ensaio.
- `Governanca_Tecnica_Readiness.md`, `Runbook_Operacao.md`, `Runbook_Testes_Tecnicos.md`: gates e execução.
