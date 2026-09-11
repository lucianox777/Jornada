# Requisitos Não Funcionais - Jornada do Cidadão - Fase 1

**Versão do documento:** 1.1  
**Data:** 10/09/2026  
**Base normativa:** Especificação Técnica Jornada v3.62 - § 17  
**SolutionSchema:** SolutionSchema v3.70  
**Estado de incorporação:** candidato técnico à consolidação Solution Engenharia v5.00; release/tag ainda não cortada  
**Status:** BASELINE NÃO FUNCIONAL CONSOLIDADO DA FASE 1
> **Leitura institucional.** Esta versão 1.1 é o baseline não funcional consolidado e autossuficiente. O arquivo v1.0 permanece no repositório apenas para rastreabilidade histórica; não é necessário lê-lo cumulativamente com este documento. Referências históricas `RNF01` a `RNF33` foram normalizadas para `RNF-001` a `RNF-033` sem mudança semântica.

> **Rebaseline tecnológico v1.1.** Os RNF-001 a RNF-033 abaixo preservam a redação histórica da Especificação Técnica v3.62. O complemento RNF34-D, incorporado antes da primeira publicação da V1, esclarece a implantação operacional sem apagar essa origem: SQL Server 2022 continua sendo o baseline relacional obrigatório de desenvolvimento/CI e referência independente de ambiente; SQL Database in Microsoft Fabric pode hospedar o banco relacional operacional de HML/Produção quando homologado para a release exata; Lakehouse e SQL Analytics Endpoint permanecem analíticos/compatibilidade. Essa hospedagem não cria DDL, adapter ou regra funcional paralela.

> Os 33 requisitos abaixo preservam o conteúdo normativo da Seção 17 da Especificação Técnica v3.62. A categorização e as relações com RN/RF/RT são organizacionais e não alteram sua semântica.

## 1. Finalidade

Separar atributos de qualidade, restrições e propriedades transversais dos comportamentos funcionais (RF) e das escolhas de realização (RT).

## 2. Requisitos Não Funcionais

### RNF-001 - Plataforma

**Requisito normativo.** Todo código C# da Fase 1 deve compilar para net8.0 com C# 12 definido centralmente.

**Rastreabilidade de negócio.** RN-001, RN-035

**Rastreabilidade funcional.** RF-049

**Realização técnica principal.** RT-001

### RNF-002 - Plataforma

**Requisito normativo.** SQL Server é a única tecnologia relacional normativa da Fase 1.

**Rastreabilidade de negócio.** RN-003, RN-035

**Rastreabilidade funcional.** —

**Realização técnica principal.** RT-002

### RNF-003 - Capacidade

**Requisito normativo.** A API deve suportar payloads de até 10.000 Pessoas e 10.000 Registros por lote, com limite físico de corpo configurável.

**Rastreabilidade de negócio.** RN-002, RN-023, RN-030

**Rastreabilidade funcional.** RF-001, RF-003

**Realização técnica principal.** RT-012

### RNF-004 - Confiabilidade

**Requisito normativo.** Escrita deve ser idempotente por cliente + Idempotency-Key.

**Rastreabilidade de negócio.** RN-003, RN-023, RN-029

**Rastreabilidade funcional.** RF-007

**Realização técnica principal.** RT-015

### RNF-005 - Identidade/Privacidade

**Requisito normativo.** PESSOA_UUID usa UNIQUEIDENTIFIER aleatório, sem derivação de CPF.

**Rastreabilidade de negócio.** RN-005, RN-006, RN-030

**Rastreabilidade funcional.** RF-012, RF-019

**Realização técnica principal.** RT-023

### RNF-006 - Confiabilidade

**Requisito normativo.** O Processor deve ser reiniciável sem duplicar materializações.

**Rastreabilidade de negócio.** RN-003, RN-012, RN-023, RN-029

**Rastreabilidade funcional.** RF-024

**Realização técnica principal.** RT-020

### RNF-007 - Resiliência

**Requisito normativo.** Falha de enriquecimento geográfico não bloqueia Pessoa/Registro; ausência é reprocessável.

**Rastreabilidade de negócio.** RN-015, RN-016, RN-023, RN-024

**Rastreabilidade funcional.** RF-029, RF-031, RF-032

**Realização técnica principal.** RT-041

### RNF-008 - Privacidade/Logs

**Requisito normativo.** Logs não podem conter payload integral nem identificadores civis desnecessários.

**Rastreabilidade de negócio.** RN-029, RN-030

**Rastreabilidade funcional.** —

**Realização técnica principal.** RT-049

### RNF-009 - Observabilidade

**Requisito normativo.** HML deve possuir health check/heartbeat para componentes residentes e telemetria/status de execução para o Jornada.Linkage.Runner run-once.

**Rastreabilidade de negócio.** RN-023, RN-024, RN-033

**Rastreabilidade funcional.** RF-047

**Realização técnica principal.** RT-053

### RNF-010 - Compatibilidade

**Requisito normativo.** Views serving.v_bi_* são contratos estáveis com a equipe Power BI.

**Rastreabilidade de negócio.** RN-025, RN-035

**Rastreabilidade funcional.** RF-041, RF-042, RF-049

**Realização técnica principal.** RT-044

### RNF-011 - Configuração/Segurança

**Requisito normativo.** O PBIP deve abrir usando parâmetros de servidor/banco, sem credenciais versionadas.

**Rastreabilidade de negócio.** RN-025, RN-030, RN-033

**Rastreabilidade funcional.** RF-042

**Realização técnica principal.** RT-045

### RNF-012 - Testabilidade

**Requisito normativo.** Testes de integração devem exercitar o DDL/seed reais de SQL Server.

**Rastreabilidade de negócio.** RN-003, RN-023, RN-029, RN-033, RN-035

**Rastreabilidade funcional.** —

**Realização técnica principal.** RT-002, RT-059

### RNF-013 - Compatibilidade/Versionamento

**Requisito normativo.** Mudança incompatível de JSON Schema, API ou view Serving exige versionamento formal.

**Rastreabilidade de negócio.** RN-014, RN-025, RN-035

**Rastreabilidade funcional.** RF-003, RF-041, RF-042, RF-045, RF-049

**Realização técnica principal.** RT-008, RT-044

### RNF-014 - Integridade semântica

**Requisito normativo.** A consulta de Registros e a de Possibilidades devem permanecer semanticamente e fisicamente distinguíveis.

**Rastreabilidade de negócio.** RN-011, RN-012, RN-013

**Rastreabilidade funcional.** RF-022, RF-026, RF-027, RF-036, RF-037

**Realização técnica principal.** RT-031, RT-038

### RNF-015 - Escalabilidade

**Requisito normativo.** O Parameters Worker deve processar corpus populacional em streaming/agregação sem requerer carregamento integral em memória.

**Rastreabilidade de negócio.** RN-023, RN-024, RN-028, RN-035

**Rastreabilidade funcional.** —

**Realização técnica principal.** RT-057

### RNF-016 - Auditabilidade/Modelo

**Requisito normativo.** Um modelo de linkage novo só pode substituir o ativo por transição de estado auditável.

**Rastreabilidade de negócio.** RN-014, RN-024, RN-029

**Rastreabilidade funcional.** RF-017, RF-045

**Realização técnica principal.** RT-029

### RNF-017 - Configuração/Segurança

**Requisito normativo.** Autenticação e secrets devem ser substituíveis por configuração de HML sem recompilar aplicações.

**Rastreabilidade de negócio.** RN-029, RN-030, RN-033

**Rastreabilidade funcional.** —

**Realização técnica principal.** RT-004

### RNF-018 - Manutenibilidade

**Requisito normativo.** A Solution não deve depender de componente histórico não incluído em Jornada.sln.

**Rastreabilidade de negócio.** RN-001, RN-035

**Rastreabilidade funcional.** RF-049

**Realização técnica principal.** RT-003

### RNF-019 - Manutenibilidade/Paralelização

**Requisito normativo.** O projeto deve permitir paralelização das frentes por contratos definidos no primeiro mês.

**Rastreabilidade de negócio.** RN-033, RN-034, RN-035

**Rastreabilidade funcional.** RF-049

**Realização técnica principal.** RT-009

### RNF-020 - Desempenho

**Requisito normativo.** A latência/SLA definitivo de Produção fica fora da Fase 1; HML deve medir p50/p95 e vazão para subsidiar dimensionamento.

**Rastreabilidade de negócio.** RN-023, RN-027, RN-033

**Rastreabilidade funcional.** RF-044

**Realização técnica principal.** RT-058

### RNF-021 - Reprodutibilidade

**Requisito normativo.** Cada linkage_run deve capturar uma única versão imutável do modelo antes do primeiro score; nenhum run pode misturar modelo_id/modelo_versao.

**Rastreabilidade de negócio.** RN-003, RN-005, RN-024, RN-029

**Rastreabilidade funcional.** RF-006, RF-017

**Realização técnica principal.** RT-027

### RNF-022 - Escalabilidade/Auditoria

**Requisito normativo.** Resultados probabilísticos históricos são append-only; para escala municipal não se persistem todos os candidatos comparados, apenas os dois melhores e a decisão por observação/run.

**Rastreabilidade de negócio.** RN-003, RN-005, RN-024, RN-029

**Rastreabilidade funcional.** RF-006, RF-017

**Realização técnica principal.** RT-027

### RNF-023 - Operabilidade

**Requisito normativo.** O linkage probabilístico deve ser executado em Jornada.Linkage.Runner independente, run-once e schedulável, com modelo e parâmetros de execução congelados no linkage_run.

**Rastreabilidade de negócio.** RN-006, RN-007, RN-023, RN-028

**Rastreabilidade funcional.** RF-015, RF-016

**Realização técnica principal.** RT-026

### RNF-024 - Consistência

**Requisito normativo.** A visibilidade dos resultados de um linkage_run deve ser logicamente atômica: lotes podem ser persistidos em transações independentes, mas somente status PUBLICADO alimenta identidade.v_vinculo_corrente.

**Rastreabilidade de negócio.** RN-003, RN-005, RN-024, RN-029

**Rastreabilidade funcional.** RF-006, RF-017

**Realização técnica principal.** RT-027

### RNF-025 - Integridade/Fail-closed

**Requisito normativo.** Nenhum bloco de candidatos pode ser truncado silenciosamente; exceder o limite operacional deve falhar o run e exigir revisão explícita do blocking/configuração.

**Rastreabilidade de negócio.** RN-006, RN-024

**Rastreabilidade funcional.** RF-016

**Realização técnica principal.** RT-028

### RNF-026 - Concorrência/Consistência

**Requisito normativo.** GENERATE_DRAFT do Parameters Worker e o Linkage Runner devem executar sob janela exclusiva do corpus integrado obtida pelo gate serial da Fase 1. A API/Bronze permanece disponível; o Processor termina o lote corrente e não inicia outro até a liberação. Não há requisito de SNAPSHOT isolation nem de ALLOW_SNAPSHOT_ISOLATION.

**Rastreabilidade de negócio.** RN-023, RN-024, RN-028

**Rastreabilidade funcional.** RF-009, RF-048

**Realização técnica principal.** RT-022

### RNF-027 - Escalabilidade

**Requisito normativo.** A geração de parâmetros não pode exigir carregamento integral da Gold em memória nem persistir frequências de alta cardinalidade por modelo no baseline; profiling de escala deve ocorrer no SQL Server e m/u por amostras limitadas.

**Rastreabilidade de negócio.** RN-023, RN-024, RN-028, RN-035

**Rastreabilidade funcional.** —

**Realização técnica principal.** RT-057

### RNF-028 - Concorrência/Disponibilidade

**Requisito normativo.** Recebimento e jobs analíticos podem coexistir: commits da API continuam em Ingestão/Bronze, enquanto a materialização Silver/Gold/identidade é serializada. Parameters GENERATE_DRAFT e Runner declaram intenção exclusiva antes de aguardar o lote corrente do Processor drenar, impedindo starvation por novos lotes. Os comandos SQL críticos devem possuir timeout finito e a HML deve calibrar tempo de drenagem, duração dos jobs e backlog operacional.

**Rastreabilidade de negócio.** RN-023, RN-024, RN-028

**Rastreabilidade funcional.** RF-009, RF-048

**Realização técnica principal.** RT-022

### RNF-029 - Segurança/Autorização

**Requisito normativo.** Endpoints de identidade, Pessoa, Registros e Possibilidades devem autorizar pela credencial, scope e recurso antes da leitura funcional; nenhuma credencial autorizada depende de vínculo prévio da Pessoa. Cardinalidade é propriedade do endpoint e rate limits pós-autenticação usam CredentialId + classe de endpoint.

**Rastreabilidade de negócio.** RN-018, RN-019, RN-020, RN-029, RN-030

**Rastreabilidade funcional.** RF-033, RF-034, RF-035, RF-038, RF-040

**Realização técnica principal.** RT-035, RT-040

### RNF-030 - Auditoria/Privacidade

**Requisito normativo.** Todo acesso individual deve gerar trilha auditável com credencial/Gestor/rota/recurso e UUID-alvo; consultas em lote devem registrar todos os UUIDs do evento sem persistir CPF, nome, payload, access key ou finalidade declarada.

**Rastreabilidade de negócio.** RN-029, RN-030

**Rastreabilidade funcional.** RF-039

**Realização técnica principal.** RT-039

### RNF-031 - Segurança de rede

**Requisito normativo.** A aplicação não deve confiar diretamente em X-Forwarded-For. Forwarded Headers só pode ser habilitado com proxies explicitamente confiáveis por ambiente; sem lista de proxies, o middleware permanece desabilitado e o rate limit usa o endereço observado na conexão.

**Rastreabilidade de negócio.** RN-029, RN-030

**Rastreabilidade funcional.** RF-040

**Realização técnica principal.** RT-040

### RNF-032 - Armazenamento/Durabilidade

**Requisito normativo.** Os bytes do ZIP Bronze não devem ser persistidos em VARBINARY(MAX) no banco funcional. Devem residir em armazenamento externo durável por chave lógica content-addressed derivada do SHA-256, com escrita idempotente objeto-antes-da-linha e chave relativa independente da raiz física.

**Rastreabilidade de negócio.** RN-003, RN-029, RN-030, RN-031

**Rastreabilidade funcional.** RF-008

**Realização técnica principal.** RT-016

### RNF-033 - Integridade/Recuperabilidade

**Requisito normativo.** A Bronze externa deve falhar fechada quanto à integridade e ao ciclo de vida: objeto canônico existente é revalidado por SHA-256; falha temporária/corrupção na recepção retorna HTTP 503 com Retry-After; GC/expurgo físico só ocorre sob coordenação exclusiva por objeto após confirmação de zero referências e carência; todo objeto referenciado por backup SQL restaurável deve permanecer recuperável. Geradores homologados devem possuir perfil de ZIP determinístico para deduplicação de reenvios integrais inalterados.

**Rastreabilidade de negócio.** RN-003, RN-023, RN-029, RN-031

**Rastreabilidade funcional.** RF-008, RF-046

**Realização técnica principal.** RT-017

## 3. Observações de aceite

- RNF de HML/Produção dependem das evidências do ambiente correspondente; a validação local não as substitui.
- RNF de segurança e governança podem exigir evidência técnica e ato institucional.
- Mudança no texto canônico de RNF exige alteração formal da Base Normativa; este documento preserva o texto histórico RNF-001..RNF-033 e registra os complementos aditivos pré-publicação de forma explícita.

## 4. Controle de versão

| Versão | Data | Síntese | Incorporação |
|---|---|---|---|
| 1.0 | 03/09/2026 | Extração organizada dos 33 RNF normativos da Seção 17 da Especificação Técnica v3.62, com rastreabilidade RN/RF/RT. | Solution Engenharia v3.98 |
| 1.1 | 10/09/2026 | Consolidação autossuficiente dos complementos RNF34-A..D; RNF34-D distingue o baseline SQL Server obrigatório de DEV/CI da hospedagem operacional homologável em SQL Database in Microsoft Fabric, mantendo Lakehouse/SQL Analytics Endpoint no papel analítico. | Candidato técnico Solution Engenharia v5.00 |

## Complementos não funcionais incorporados na v1.1

Os quatro identificadores aditivos `RNF34-A` a `RNF34-D` permanecem estáveis nesta versão para não quebrar rastreabilidade já publicada no change-set. A política canônica para os 33 requisitos históricos é `RNF-001` a `RNF-033`. Uma futura renumeração integral dos aditivos, se decidida, deve ser feita como rebaseline explícito e com atualização conjunta da matriz.

### Complemento v1.1 de RNF-012 - Testabilidade e regressão

Além do requisito original de que os testes de integração exercitem DDL/seed reais de SQL Server quando aplicável, **toda mudança funcional, estatística, de contrato, persistência, integração, segurança ou regra de identidade deve possuir regressões automatizadas nos níveis unitário e de integração compatíveis com o risco alterado**.

Uma alteração não pode ser considerada pronta para merge quando houver comportamento alterado sem regressão unitária correspondente ou sem prova integrada do caminho afetado. Quando um nível não for tecnicamente aplicável, a exceção deve ser explícita e justificada no próprio change-set; ausência silenciosa de teste não é aceita.

Para algoritmos de identidade/Linkage, a regressão integrada deve provar também as propriedades de segurança pertinentes, inclusive ausência de publicação/ativação indevida, conservação de fatos e comportamento fail-closed quando esses invariantes fizerem parte do escopo.

### RNF34-A - Sincronização documental do change-set

Toda mudança que altere comportamento, contrato, configuração, operação, segurança, modelo estatístico, requisito ou evidência deve atualizar **no mesmo change-set** toda documentação diretamente afetada, incluindo, conforme aplicável: README do componente, runbook, requisitos e matriz de rastreabilidade, especificação técnica/arquitetura corrente, contrato de configuração/evidência e documentação de segurança/governança.

Documentação conhecida como obsoleta ou contraditória bloqueia o estado Ready. A revisão documental faz parte da definição de pronto; não é atividade posterior ao merge. ADR é exigido apenas quando o projeto optar por preservar histórico decisório pós-publicação; para a V1 ainda não publicada, decisões consolidadas podem ser incorporadas diretamente à especificação corrente.

### RNF34-B - Integração contínua obrigatória

Todo código e toda alteração de infraestrutura, banco, contrato, algoritmo ou documentação verificável do projeto devem passar por **CI automatizada** antes do merge. O CI deve executar os gates aplicáveis ao change-set, incluindo build, regressões unitárias, regressões de integração, verificações estáticas/segurança e validações documentais quando existirem.

Um PR não pode ser considerado pronto nem integrado quando um gate obrigatório do HEAD exato estiver ausente, falhando, cancelado de forma não justificada ou ainda em execução. Gates dispensados por condição explícita devem permanecer distinguíveis de gates executados com sucesso. Verificação manual não substitui gate automatizável de CI.

### RNF34-C - Diagramas em padrão UML

Diagramas técnicos e arquiteturais mantidos como documentação normativa ou de engenharia devem usar **notação UML compatível com o tipo de visão representada**, por exemplo: componente, sequência, atividade, estado, classes, implantação ou casos de uso.

DER/DRE e outras notações de modelagem de dados podem existir como anexos físicos auxiliares, mas **não são UML e não substituem o diagrama UML normativo**. A estrutura de identidade/linkage deve possuir diagrama de classes UML e o processo de resolução de identidade deve possuir diagrama de atividade UML, ambos incorporados aos artefatos normativos de entrega em **DOCX e PDF**.

Diagramas informais podem existir como apoio visual, mas não substituem o diagrama UML quando o artefato documenta relações, fluxos, estados ou arquitetura usados para decisão técnica. Sempre que o comportamento ou arquitetura representada mudar, o diagrama UML correspondente deve ser atualizado no mesmo change-set conforme RNF34-A. A ferramenta de autoria pode variar, mas o destinatário não pode depender de PlantUML, Mermaid ou software específico de modelagem para ler a documentação. A versionabilidade e a reprodutibilidade devem ser preservadas no próprio processo de geração dos DOCX/PDF e nos artefatos editáveis já adotados pelo projeto.

### RNF34-D - Ambiente tecnológico e reprodutibilidade

O desenvolvimento, teste, integração e operação da Jornada devem adotar ambiente tecnológico explícito, versionado e reprodutível. A implementação de serviços e algoritmos deve usar **C#/.NET** conforme a versão suportada pelo repositório; **Git** é o sistema de controle de versão; **Docker** deve ser usado para ambientes descartáveis e testes integrados quando o componente possuir dependências containerizáveis; e artefatos de BI devem ser produzidos/validados em **Power BI Desktop** quando esse for o formato de entrega.

**Microsoft SQL Server é a tecnologia relacional normativa da Jornada.** SQL Server 2022 Developer/Testcontainers constitui o baseline obrigatório de desenvolvimento, CI, DDL canônico, prontidão e validação independente de ambiente; a edição Developer não é definida como tecnologia de Produção. **SQL Database in Microsoft Fabric pode hospedar o banco relacional operacional de HML/Produção quando a release exata estiver homologada nesse alvo**, preservando o mesmo contrato Microsoft SQL, o mesmo `OperationalSqlAdapter`/`Microsoft.Data.SqlClient` e sem DDL ou regra funcional paralela. Para fins deste baseline consolidado, essa hospedagem gerenciada não redefine o requisito histórico RNF-002 como uma segunda linha funcional concorrente. **Lakehouse e SQL Analytics Endpoint permanecem no escopo analítico/compatibilidade e não são fonte de verdade operacional implícita.** PostgreSQL pode ser exercitado como provider operacional paralelo exclusivamente nos escopos explicitamente suportados e versionados, inclusive calibração/avaliação de Linkage, sem substituir a tecnologia relacional normativa.

Versões de SDK, imagens de container e demais dependências automatizáveis devem ser fixadas ou controladas de forma reproduzível no CI. Dependências exclusivamente locais, como Power BI Desktop quando não houver runner compatível, devem ter versão mínima/suportada documentada e procedimento de validação rastreável. Diferenças entre ambiente local e CI não podem alterar silenciosamente regras funcionais ou resultados estatísticos.

Os identificadores `RNF34-A`, `RNF34-B`, `RNF34-C` e `RNF34-D` são aditivos e passam a integrar o corpus vigente, embora preservem a forma alfanumérica até futura rebaseline integral.

## Aplicação consolidada ao Linkage

O calibrador e o avaliador devem usar o mesmo contrato versionado de **blocking dinâmico por observação**, atualmente materializado por `BirthBlockingPlan` e pelas políticas versionadas que vierem a selecionar seus passes/atributos. Nenhum deles pode manter uma regra paralela fixa que produza universo de candidatos semanticamente diferente sem versionamento, regressões unitárias/integradas e atualização documental conjunta.

A paralelização do calibrador e do avaliador deve ser limitada, configurável e condicionada a ganho mensurável, mantendo equivalência determinística/reprodutível com a execução serial. O uso de blocking dinâmico em calibração/avaliação não constitui, isoladamente, homologação estatística nem autorização de ativação probabilística. A issue #31 continua exigindo corpus representativo, avaliação independente, falsos vínculos/calibração e aprovação institucional.
