# Requisitos Funcionais - Jornada do Cidadão - Fase 1

**Versão do documento:** 1.1  
**Data:** 10/09/2026  
**Base normativa:** Especificação Técnica Jornada v3.62  
**SolutionSchema:** SolutionSchema v3.70  
**Requisitos de Negócio:** Requisitos de Negócio Jornada v1.1  
**Estado de incorporação:** candidato técnico à consolidação Solution Engenharia v5.00; release/tag ainda não cortada  
**Status:** BASELINE FUNCIONAL CONSOLIDADO DA FASE 1
> **Leitura institucional.** Esta versão 1.1 é o baseline funcional consolidado e autossuficiente. O arquivo v1.0 permanece no repositório apenas para rastreabilidade histórica; não é necessário lê-lo cumulativamente com este documento. Referências históricas `RNF01` a `RNF33` foram normalizadas para `RNF-001` a `RNF-033` sem mudança semântica.


> Este documento descreve o que a solução deve fazer para materializar os Requisitos de Negócio. Ele evita detalhes de tecnologia sempre que possível; atributos de qualidade/restrições pertencem ao RNF e mecanismos de implementação pertencem ao RT.

## 1. Finalidade

Definir comportamentos observáveis da Jornada do Cidadão - Fase 1, com critérios de aceitação e rastreabilidade aos RN.

## 2. Regra de classificação

- **RN:** resultado ou necessidade institucional.
- **RF:** comportamento/serviço que a solução deve executar.
- **RNF:** qualidade, restrição, capacidade ou propriedade transversal.
- **RT:** escolha ou obrigação de engenharia para realizar RF/RNF.

## 3. Requisitos Funcionais

### 3.1 Integração, contratos e processamento

#### RF-001 - Receber Entrega padronizada da Fase 1

**Requisito funcional.** O sistema deve receber uma Entrega de um Gestor em envelope padronizado, contendo os artefatos cadastrais e factuais previstos pelo contrato vigente.

**Critério de aceitação funcional.** A Entrega válida é registrada e recebe identificador/estado de recepção; estrutura incompatível é recusada de forma explícita.

**Rastreabilidade de negócio.** RN-002, RN-003

**Rastreabilidade de qualidade/técnica.** RNF: RNF-003; RT: RT-010

#### RF-002 - Validar origem, autenticação e contexto contratual

**Requisito funcional.** O sistema deve identificar e autenticar o Gestor remetente e validar se Sistema de Origem, Tipo e versão informados pertencem ao contexto autorizado.

**Critério de aceitação funcional.** Origem incompatível ou não autorizada é recusada antes da materialização funcional.

**Rastreabilidade de negócio.** RN-002, RN-029, RN-034

**Rastreabilidade de qualidade/técnica.** RNF: —; RT: RT-013, RT-048

#### RF-003 - Validar contrato, schema e versão do conteúdo

**Requisito funcional.** O sistema deve validar Pessoas e Registros contra os contratos e versões aplicáveis, sem reinterpretar silenciosamente conteúdo incompatível.

**Critério de aceitação funcional.** Conteúdo fora do contrato é rejeitado com diagnóstico compatível com o estágio de processamento.

**Rastreabilidade de negócio.** RN-002, RN-014, RN-023, RN-035

**Rastreabilidade de qualidade/técnica.** RNF: RNF-003, RNF-013; RT: RT-006, RT-007, RT-008, RT-013

#### RF-004 - Aceitar Entrega sem fatos e atualização exclusivamente cadastral

**Requisito funcional.** O sistema deve aceitar Entregas com zero Benefícios/Serviços quando o envelope e as Pessoas necessárias estiverem válidos.

**Critério de aceitação funcional.** Uma carga exclusivamente cadastral pode concluir processamento sem criar fatos artificiais.

**Rastreabilidade de negócio.** RN-002

**Rastreabilidade de qualidade/técnica.** RNF: —; RT: RT-010, RT-011

#### RF-005 - Garantir cobertura das Pessoas referenciadas

**Requisito funcional.** O sistema deve assegurar que cada fato da Entrega possua a Pessoa/identificador de origem necessário ao processamento conforme o contrato.

**Critério de aceitação funcional.** Referências quebradas ou ausentes são identificadas antes da publicação funcional.

**Rastreabilidade de negócio.** RN-002, RN-003, RN-023

**Rastreabilidade de qualidade/técnica.** RNF: —; RT: RT-011

#### RF-006 - Registrar origem, período, versão, estado e linhagem

**Requisito funcional.** O sistema deve registrar metadados suficientes para rastrear cada Entrega, Pessoa e fato ao Gestor, Sistema de Origem, período de referência, contrato/versão e estados de processamento.

**Critério de aceitação funcional.** Consultas de auditoria conseguem remontar a origem e o encadeamento do processamento.

**Rastreabilidade de negócio.** RN-003, RN-029

**Rastreabilidade de qualidade/técnica.** RNF: RNF-021, RNF-022, RNF-024; RT: RT-010, RT-014, RT-019, RT-027, RT-043

#### RF-007 - Aplicar idempotência de recepção e processamento

**Requisito funcional.** O sistema deve reconhecer reenvio idêntico e impedir duplicação funcional, distinguindo reutilização divergente da mesma chave de idempotência.

**Critério de aceitação funcional.** Reenvio idêntico não duplica materializações; colisão divergente é tratada como conflito.

**Rastreabilidade de negócio.** RN-003, RN-023, RN-029

**Rastreabilidade de qualidade/técnica.** RNF: RNF-004; RT: RT-015

#### RF-008 - Armazenar e recuperar Bronze imutável

**Requisito funcional.** O sistema deve preservar os bytes originais da Entrega em camada Bronze durável e recuperável enquanto houver referência válida.

**Critério de aceitação funcional.** Uma Entrega aceita pode ser reaberta para verificação/reprocessamento sem depender do remetente.

**Rastreabilidade de negócio.** RN-003, RN-030, RN-031

**Rastreabilidade de qualidade/técnica.** RNF: RNF-032, RNF-033; RT: RT-016, RT-017

#### RF-009 - Processar Entregas de forma assíncrona

**Requisito funcional.** O sistema deve desacoplar recepção da materialização pesada, permitindo que o processamento continue após o aceite da Entrega.

**Critério de aceitação funcional.** A indisponibilidade momentânea do processamento não invalida uma recepção já confirmada.

**Rastreabilidade de negócio.** RN-002, RN-023, RN-035

**Rastreabilidade de qualidade/técnica.** RNF: RNF-026, RNF-028; RT: RT-018, RT-021, RT-022

#### RF-010 - Registrar resultado individual de Pessoas e Registros

**Requisito funcional.** O sistema deve registrar o resultado de processamento de cada Pessoa e Registro, incluindo retransmissão, inclusão, versionamento, rejeição ou reabertura quando aplicável.

**Critério de aceitação funcional.** É possível medir e auditar o destino de cada item processado.

**Rastreabilidade de negócio.** RN-003, RN-023, RN-024

**Rastreabilidade de qualidade/técnica.** RNF: —; RT: RT-019

### 3.2 Pessoa, identidade e evidência

#### RF-011 - Constituir Gold de Pessoas a partir da primeira fonte individualizável

**Requisito funcional.** O sistema deve permitir que a primeira fonte válida e individualizável constitua a referência Golden inicial da Pessoa.

**Critério de aceitação funcional.** A primeira fonte não fica em staging indefinido nem exige corroboração prévia de outra origem.

**Rastreabilidade de negócio.** RN-004, RN-009, RN-028

**Rastreabilidade de qualidade/técnica.** RNF: —; RT: RT-032

#### RF-012 - Criar e manter identificador canônico estável da Pessoa

**Requisito funcional.** O sistema deve atribuir e manter PESSOA_UUID canônico, preservando histórico e sucessão em operações governadas.

**Critério de aceitação funcional.** Correções de atributos não trocam o UUID sem operação de identidade explicitamente governada.

**Rastreabilidade de negócio.** RN-005

**Rastreabilidade de qualidade/técnica.** RNF: RNF-005; RT: RT-023, RT-030

#### RF-013 - Resolver CPF válido pela rota determinística

**Requisito funcional.** O sistema deve usar CPF válido como rota normal de resolução determinística da identidade, sujeito às travas de consistência cadastral.

**Critério de aceitação funcional.** CPF compatível resolve a identidade sem score probabilístico; divergência forte não é aceita silenciosamente.

**Rastreabilidade de negócio.** RN-005, RN-006

**Rastreabilidade de qualidade/técnica.** RNF: —; RT: RT-024

#### RF-014 - Materializar conflito de identidade para tratamento governado

**Requisito funcional.** O sistema deve registrar explicitamente conflito quando a resolução automática não puder atribuir uma Pessoa com segurança.

**Critério de aceitação funcional.** O conflito permanece consultável/auditável e não gera atribuição canônica indevida.

**Rastreabilidade de negócio.** RN-006, RN-024, RN-029

**Rastreabilidade de qualidade/técnica.** RNF: —; RT: RT-024, RT-030

#### RF-015 - Preservar Pessoa admitida sem CPF

**Requisito funcional.** O sistema deve manter processável a Pessoa cujo contrato admita ausência de CPF, em estado de identidade não resolvido ou pendente.

**Critério de aceitação funcional.** A ausência admitida de CPF não elimina a Pessoa nem cria CPF sintético.

**Rastreabilidade de negócio.** RN-007, RN-024

**Rastreabilidade de qualidade/técnica.** RNF: RNF-023; RT: RT-025, RT-026

#### RF-016 - Executar linkage probabilístico sobre identidades pendentes

**Requisito funcional.** O sistema deve permitir avaliar, por processo separado, Pessoas elegíveis ao fallback probabilístico.

**Critério de aceitação funcional.** O resultado probabilístico é versionado e não bloqueia a rota determinística.

**Rastreabilidade de negócio.** RN-006, RN-007, RN-024, RN-028

**Rastreabilidade de qualidade/técnica.** RNF: RNF-023, RNF-025; RT: RT-026, RT-028

#### RF-017 - Versionar e publicar modelos e runs de linkage

**Requisito funcional.** O sistema deve registrar modelo, parâmetros, universo e estado de cada linkage_run, publicando apenas resultados concluídos segundo a governança.

**Critério de aceitação funcional.** Um replay identifica o modelo/versão utilizados e não mistura versões dentro do mesmo run.

**Rastreabilidade de negócio.** RN-014, RN-024, RN-029

**Rastreabilidade de qualidade/técnica.** RNF: RNF-016, RNF-021, RNF-022, RNF-024; RT: RT-027, RT-029

#### RF-018 - Governar correção, fusão e separação de identidades

**Requisito funcional.** O sistema deve executar correções estruturais de identidade somente por operações governadas e auditáveis, preservando sucessão e histórico.

**Critério de aceitação funcional.** Operações inválidas são recusadas antes de mutação parcial e operações válidas deixam trilha completa.

**Rastreabilidade de negócio.** RN-005, RN-029

**Rastreabilidade de qualidade/técnica.** RNF: —; RT: RT-030

#### RF-019 - Manter núcleo de identidade separado dos atributos transversais

**Requisito funcional.** O sistema deve tratar CPF, nome, data de nascimento e nome da mãe como núcleo de identidade e manter contatos/endereço/outros atributos no modelo transversal apropriado.

**Critério de aceitação funcional.** Consultas e persistência distinguem núcleo fixo de atributos multivalorados/versionáveis.

**Rastreabilidade de negócio.** RN-008

**Rastreabilidade de qualidade/técnica.** RNF: RNF-005; RT: RT-023, RT-034

#### RF-020 - Registrar evidência para atributos transversais

**Requisito funcional.** O sistema deve registrar origem, método, temporalidade e evidência aplicáveis a valores transversais, incluindo verificação documental quando admitida.

**Critério de aceitação funcional.** A evidência pode ser auditada sem conferir precedência automática ao órgão que a produziu.

**Rastreabilidade de negócio.** RN-009, RN-010, RN-029

**Rastreabilidade de qualidade/técnica.** RNF: —; RT: RT-034

#### RF-021 - Recalcular valor Golden por evidência e temporalidade

**Requisito funcional.** O sistema deve selecionar/recompor o valor Golden transversal de acordo com catálogo de evidências, pertinência e temporalidade.

**Critério de aceitação funcional.** Mudança de evidência pode alterar o valor Golden sem apagar divergências históricas relevantes.

**Rastreabilidade de negócio.** RN-009, RN-010

**Rastreabilidade de qualidade/técnica.** RNF: —; RT: RT-034

### 3.3 Fatos, qualidade e Possibilidades

#### RF-022 - Materializar Benefícios e Serviços como fatos administrativos

**Requisito funcional.** O sistema deve persistir Benefícios Concedidos e Serviços Prestados em histórico factual próprio, distinto da Gold cadastral.

**Critério de aceitação funcional.** Fatos mantêm Tipo, origem, temporalidade e identificador estável da origem.

**Rastreabilidade de negócio.** RN-011

**Rastreabilidade de qualidade/técnica.** RNF: RNF-014; RT: RT-031, RT-033

#### RF-023 - Preservar fato válido sem atribuição canônica

**Requisito funcional.** O sistema deve manter fato finalístico válido mesmo quando PESSOA_UUID estiver ausente por identidade pendente ou conflitada.

**Critério de aceitação funcional.** O fato permanece disponível com sujeito/identificador declarado pela origem.

**Rastreabilidade de negócio.** RN-012

**Rastreabilidade de qualidade/técnica.** RNF: —; RT: RT-033

#### RF-024 - Reprocessar e recompor materializações de forma idempotente

**Requisito funcional.** O sistema deve permitir reprocessamento/recomposição controlada de Silver, identidade e Gold sem duplicar estado de negócio.

**Critério de aceitação funcional.** Replay do mesmo conjunto produz estado funcional equivalente e rastreável.

**Rastreabilidade de negócio.** RN-003, RN-023, RN-028

**Rastreabilidade de qualidade/técnica.** RNF: RNF-006; RT: RT-020, RT-032, RT-033

#### RF-025 - Executar controles de qualidade e registrar seus resultados

**Requisito funcional.** O sistema deve aplicar regras de qualidade pertinentes à Entrega, Pessoa e Registro e tornar os resultados observáveis.

**Critério de aceitação funcional.** Falhas/bloqueios de QC são identificáveis por regra/versão e não desaparecem no agregado.

**Rastreabilidade de negócio.** RN-023, RN-024, RN-029

**Rastreabilidade de qualidade/técnica.** RNF: —; RT: RT-019, RT-046, RT-064

#### RF-026 - Calcular Possibilidades por avaliadores versionados

**Requisito funcional.** O sistema deve executar regras de Possibilidade versionadas sobre dados elegíveis, registrando resultado e versão do avaliador.

**Critério de aceitação funcional.** O resultado COMPATIVEL é reproduzível pela versão da regra e não é convertido em fato/concessão.

**Rastreabilidade de negócio.** RN-013, RN-014

**Rastreabilidade de qualidade/técnica.** RNF: RNF-014; RT: RT-038, RT-064

#### RF-027 - Separar Possibilidades de Registros

**Requisito funcional.** O sistema deve armazenar e consultar Possibilidades em estrutura e endpoint semanticamente distintos dos fatos administrativos.

**Critério de aceitação funcional.** Nenhuma Possibilidade aparece como Benefício/Serviço ocorrido.

**Rastreabilidade de negócio.** RN-011, RN-013

**Rastreabilidade de qualidade/técnica.** RNF: RNF-014; RT: RT-038

#### RF-028 - Expor cobertura, pendência e incerteza

**Requisito funcional.** O sistema deve disponibilizar indicadores que permitam distinguir cobertura de identidade, pendências, conflitos e qualidade para interpretação analítica.

**Critério de aceitação funcional.** BI/serving conseguem diferenciar população resolvida, não resolvida e outras dimensões de incerteza previstas.

**Rastreabilidade de negócio.** RN-024, RN-025

**Rastreabilidade de qualidade/técnica.** RNF: —; RT: RT-046

### 3.4 Referência Territorial

#### RF-029 - Receber e armazenar Referência Territorial própria

**Requisito funcional.** O sistema deve tratar Referência Territorial como informação temporal própria, distinta de endereço residencial/correspondência.

**Critério de aceitação funcional.** O vínculo territorial registra natureza, situação e origem aplicáveis.

**Rastreabilidade de negócio.** RN-015, RN-016, RN-021

**Rastreabilidade de qualidade/técnica.** RNF: RNF-007; RT: RT-041, RT-043

#### RF-030 - Selecionar Referência Territorial por precedência explícita

**Requisito funcional.** O sistema deve aplicar a precedência territorial definida entre declaração explícita, evidência e recência.

**Critério de aceitação funcional.** A seleção territorial é determinística e rastreável aos candidatos avaliados.

**Rastreabilidade de negócio.** RN-015, RN-017

**Rastreabilidade de qualidade/técnica.** RNF: —; RT: RT-041, RT-043

#### RF-031 - Não inferir residência por local de atendimento

**Requisito funcional.** O sistema não deve transformar automaticamente endereço de equipamento/atendimento em Referência Territorial da Pessoa.

**Critério de aceitação funcional.** Apenas situações explicitamente admitidas pela origem/regra podem produzir referência territorial.

**Rastreabilidade de negócio.** RN-016, RN-021

**Rastreabilidade de qualidade/técnica.** RNF: RNF-007; RT: RT-042

#### RF-032 - Preservar snapshot territorial utilizado pelo fato

**Requisito funcional.** O sistema deve preservar, quando aplicável, o recorte territorial associado ao fato no momento relevante.

**Critério de aceitação funcional.** Recomposição cadastral posterior não reescreve silenciosamente o contexto territorial histórico do fato.

**Rastreabilidade de negócio.** RN-003, RN-016, RN-017

**Rastreabilidade de qualidade/técnica.** RNF: RNF-007; RT: RT-043

### 3.5 APIs, acesso e auditoria

#### RF-033 - Resolver identidade por endpoint autorizado sem criar UUID

**Requisito funcional.** O sistema deve permitir resolução autorizada de identidade já existente sem criar Pessoa nova como efeito colateral da consulta.

**Critério de aceitação funcional.** Consulta sem identidade resolvida retorna resultado negativo/pendente conforme contrato e não materializa UUID.

**Rastreabilidade de negócio.** RN-018, RN-022

**Rastreabilidade de qualidade/técnica.** RNF: RNF-029; RT: RT-035, RT-036

#### RF-034 - Consultar Pessoa por UUID

**Requisito funcional.** O sistema deve disponibilizar consulta individual da Pessoa canônica aos consumidores autorizados.

**Critério de aceitação funcional.** Resposta identifica a Pessoa e seus campos permitidos sem exigir acesso direto às tabelas Gold.

**Rastreabilidade de negócio.** RN-018, RN-026

**Rastreabilidade de qualidade/técnica.** RNF: RNF-029; RT: RT-035, RT-037

#### RF-035 - Projetar Pessoa conforme credencial, recurso e política

**Requisito funcional.** O sistema deve projetar somente os campos autorizados para a credencial, incluindo exceções negativas setoriais aplicáveis.

**Critério de aceitação funcional.** Duas credenciais com políticas distintas podem receber projeções distintas da mesma Pessoa.

**Rastreabilidade de negócio.** RN-018, RN-019, RN-020, RN-021, RN-030

**Rastreabilidade de qualidade/técnica.** RNF: RNF-029; RT: RT-035, RT-037

#### RF-036 - Consultar Registros por Pessoa

**Requisito funcional.** O sistema deve disponibilizar consulta dos fatos administrativos associados à Pessoa conforme autorização e cardinalidade do endpoint.

**Critério de aceitação funcional.** Registros retornados preservam Tipo/origem/temporalidade e não incluem Possibilidades.

**Rastreabilidade de negócio.** RN-018, RN-026

**Rastreabilidade de qualidade/técnica.** RNF: RNF-014; RT: RT-038

#### RF-037 - Consultar Possibilidades separadamente

**Requisito funcional.** O sistema deve disponibilizar consulta de Possibilidades em endpoint próprio e com ressalva semântica adequada.

**Critério de aceitação funcional.** Resposta não mistura Possibilidades com Registros nem as apresenta como direito/concessão.

**Rastreabilidade de negócio.** RN-013, RN-018, RN-026

**Rastreabilidade de qualidade/técnica.** RNF: RNF-014; RT: RT-038

#### RF-038 - Aplicar autorização, scopes, recursos e restrições negativas

**Requisito funcional.** O sistema deve autorizar cada operação pela credencial, scope e recurso antes da leitura ou escrita funcional.

**Critério de aceitação funcional.** Operação não autorizada é negada sem depender de vínculo prévio da Pessoa com o Gestor consumidor.

**Rastreabilidade de negócio.** RN-019, RN-020, RN-021, RN-029, RN-030

**Rastreabilidade de qualidade/técnica.** RNF: RNF-029; RT: RT-035, RT-037, RT-048

#### RF-039 - Auditar acessos individuais e em lote

**Requisito funcional.** O sistema deve gerar trilha de auditoria para consultas protegidas e atos sensíveis, com os identificadores mínimos definidos.

**Critério de aceitação funcional.** É possível atribuir o acesso à credencial/Gestor/rota/recurso sem armazenar segredos ou payload desnecessário.

**Rastreabilidade de negócio.** RN-029, RN-030

**Rastreabilidade de qualidade/técnica.** RNF: RNF-030; RT: RT-039

#### RF-040 - Aplicar controle de taxa por credencial e classe de endpoint

**Requisito funcional.** O sistema deve limitar consumo conforme credencial e classe do endpoint após autenticação.

**Critério de aceitação funcional.** Rate limit não usa identidade civil como chave e respeita a configuração de proxy confiável do ambiente.

**Rastreabilidade de negócio.** RN-018, RN-029, RN-030

**Rastreabilidade de qualidade/técnica.** RNF: RNF-029, RNF-031; RT: RT-040

### 3.6 Serving, BI e tempestividade

#### RF-041 - Publicar contratos Serving estáveis para consumo analítico

**Requisito funcional.** O sistema deve disponibilizar views serving.v_bi_* como interface estável para o modelo analítico.

**Critério de aceitação funcional.** Alteração incompatível da interface Serving é versionada e comunicada.

**Rastreabilidade de negócio.** RN-025, RN-035

**Rastreabilidade de qualidade/técnica.** RNF: RNF-010, RNF-013; RT: RT-044

#### RF-042 - Disponibilizar BI municipal de Pessoas, fatos, qualidade e território

**Requisito funcional.** A solução deve alimentar o BI da Fase 1 com dimensões e métricas previstas para Pessoas, fatos, qualidade, cobertura, Possibilidades e território.

**Critério de aceitação funcional.** Painéis conseguem representar o escopo municipal sem depender de consulta individual nominativa.

**Rastreabilidade de negócio.** RN-024, RN-025, RN-030

**Rastreabilidade de qualidade/técnica.** RNF: RNF-010, RNF-011, RNF-013; RT: RT-044, RT-045, RT-046

#### RF-043 - Manter consulta individual fora do Power BI

**Requisito funcional.** A solução deve manter consultas individualizadas nos endpoints operacionais autorizados, e não no modelo semântico padrão do BI.

**Critério de aceitação funcional.** O PBIP padrão não é usado como mecanismo de consulta nominativa de Pessoa.

**Rastreabilidade de negócio.** RN-026, RN-030

**Rastreabilidade de qualidade/técnica.** RNF: —; RT: RT-047

#### RF-044 - Medir tempestividade e atraso de recebimento

**Requisito funcional.** O sistema deve calcular/fornecer elementos para medir SLA de recebimento por Tipo/versão e período de referência.

**Critério de aceitação funcional.** Indicadores distinguem prazo esperado, recebimento e atraso segundo regra versionada.

**Rastreabilidade de negócio.** RN-027, RN-025, RN-034

**Rastreabilidade de qualidade/técnica.** RNF: RNF-020; RT: RT-058

### 3.7 Governança operacional e evolução

#### RF-045 - Versionar contratos, regras, QC, SLA e avaliadores

**Requisito funcional.** O sistema deve manter versões imutáveis/auditáveis quando houver mudança material de contrato ou regra de negócio operacional.

**Critério de aceitação funcional.** Histórico continua interpretável pela versão que estava vigente quando o evento foi processado.

**Rastreabilidade de negócio.** RN-014, RN-029, RN-035

**Rastreabilidade de qualidade/técnica.** RNF: RNF-013, RNF-016; RT: RT-005, RT-006, RT-007, RT-008, RT-029

#### RF-046 - Executar retenção, consolidação e expurgo somente por política aprovada

**Requisito funcional.** O sistema deve permitir configurar e executar ciclo de retenção/expurgo por classe de ativo apenas quando a governança correspondente estiver habilitada.

**Critério de aceitação funcional.** Rotina desabilitada não expurga dados; ativação é parametrizada, auditável e preserva dependências necessárias.

**Rastreabilidade de negócio.** RN-031, RN-029, RN-030

**Rastreabilidade de qualidade/técnica.** RNF: RNF-033; RT: RT-017, RT-051

#### RF-047 - Disponibilizar saúde, status e telemetria operacional

**Requisito funcional.** A solução deve expor sinais de saúde, execução, backlog e falhas necessários à operação e ao aceite técnico.

**Critério de aceitação funcional.** Operação consegue distinguir indisponibilidade de componente, atraso de pipeline e falha funcional.

**Rastreabilidade de negócio.** RN-023, RN-029, RN-033

**Rastreabilidade de qualidade/técnica.** RNF: RNF-009; RT: RT-053, RT-054, RT-061, RT-062, RT-063, RT-064

#### RF-048 - Orquestrar jobs run-once e janelas exclusivas sem bloquear recepção

**Requisito funcional.** A solução deve permitir agendamento externo dos jobs recorrentes e coordenar janelas exclusivas do corpus quando exigidas, mantendo a recepção Bronze disponível.

**Critério de aceitação funcional.** Jobs críticos respeitam gates/leases e não observam corpus parcialmente mutável.

**Rastreabilidade de negócio.** RN-023, RN-028, RN-033

**Rastreabilidade de qualidade/técnica.** RNF: RNF-026, RNF-028; RT: RT-022, RT-055, RT-056

#### RF-049 - Incorporar novos Gestores e Tipos por configuração e versionamento

**Requisito funcional.** A solução deve permitir incluir nova origem, Tipo de Benefício ou Tipo de Serviço por contratos, catálogos, autorização e configuração versionados.

**Critério de aceitação funcional.** A expansão não exige redesenho estrutural nem quebra histórico de versões anteriores.

**Rastreabilidade de negócio.** RN-034, RN-035

**Rastreabilidade de qualidade/técnica.** RNF: RNF-001, RNF-010, RNF-013, RNF-018, RNF-019; RT: RT-001, RT-003, RT-006, RT-007, RT-008, RT-009, RT-044

#### RF-050 - Operar a Fase 1 sem dependência de biometria

**Requisito funcional.** Todas as funções normativas da Fase 1 devem operar sem biometria; eventual mecanismo biométrico futuro deve entrar como evolução específica.

**Critério de aceitação funcional.** Ingestão, identidade, Gold, APIs e BI funcionam integralmente sem dado ou componente biométrico.

**Rastreabilidade de negócio.** RN-036

**Rastreabilidade de qualidade/técnica.** RNF: —; RT: RT-065

## 4. Fora de escopo funcional

- Decisão administrativa de concessão/suspensão/cancelamento de benefício.
- Edição do cadastro transacional dos Gestores pela Jornada.
- Consulta nominativa pelo Power BI.
- Biometria como requisito de funcionamento da Fase 1.
- SLA definitivo de Produção antes das medições de HML.

## 5. Controle de versão

| Versão | Data | Síntese | Incorporação |
|---|---|---|---|
| 1.0 | 03/09/2026 | Baseline inicial de 50 RF derivados dos 36 RN e da Especificação Técnica v3.62. | Solution Engenharia v3.98 |

## Requisitos funcionais incorporados na v1.1

### RF-051 - Executar calibrador e avaliador com paralelismo quando vantajoso

**Requisito funcional.** O calibrador e o avaliador devem explorar paralelismo sempre que a operação puder ser executada independentemente e houver evidência de ganho de desempenho no ambiente-alvo, sem alterar determinismo, reprodutibilidade, isolamento do corpus ou resultados estatísticos.

**Critério de aceitação funcional.** A execução paralela deve produzir resultado semanticamente equivalente à execução serial para a mesma versão de regras, corpus e semente/configuração. O grau de paralelismo deve ser limitado e configurável. Paralelismo que degrade desempenho, aumente contenção ou comprometa reprodutibilidade não deve ser habilitado por padrão. A decisão serial/paralela deve ser sustentada por benchmark reproduzível no ambiente-alvo ou equivalente.

**Rastreabilidade de negócio.** RN-023, RN-024, RN-028, RN-035

**Rastreabilidade de qualidade/técnica.** RNF: RNF-015, RNF-020, RNF-021, RNF34-A, RNF34-B; RT: RT-057, RT-058

### RF-052 - Usar frequências agregadas oficiais do IBGE como evidência de blocking quando aplicável

**Requisito funcional.** O mecanismo de otimização de blocking deve poder usar dados oficiais agregados do IBGE, versionados e com proveniência verificável, para melhorar discriminação/priorização de combinações de nome, prenome, sobrenome e último nome sempre que a fonte estiver disponível, tecnicamente compatível e demonstrar ganho mensurável sem reduzir recall além dos limites aprovados.

**Critério de aceitação funcional.** A ausência ou indisponibilidade do IBGE não pode tornar o Linkage incorreto: o sistema deve operar com fallback versionado. O uso do IBGE deve registrar versão/fingerprint da fonte e nunca tratá-la como verdade individual nem usar CPF da fonte externa. Sempre que uma frequência oficial aplicável estiver disponível, o otimizador deve considerá-la como evidência candidata e registrar se ela foi usada ou por que foi descartada.

**Rastreabilidade de negócio.** RN-005, RN-014, RN-023, RN-024, RN-029

**Rastreabilidade de qualidade/técnica.** RNF: RNF-013, RNF-016, RNF-021, RNF-027, RNF34-A; RT: RT-027, RT-028, RT-029, RT-057

### RF-053 - Considerar componentes de nome e nascimento no blocking otimizado

**Requisito funcional.** O universo candidato deve permitir regras de blocking que considerem separadamente **nome completo, prenome, sobrenome, último nome** e os componentes **dia, mês e ano** da data de nascimento, inclusive combinações entre esses atributos. O otimizador de blocking deve avaliar as combinações candidatas e selecionar a melhor política segundo critérios versionados de recall, redução do espaço de pares, custo, tamanho de blocos, ganho incremental, dependência/redundância e risco de falso vínculo. Quando existirem frequências oficiais do IBGE aplicáveis aos componentes nominais, elas devem integrar a avaliação das combinações candidatas.

**Critério de aceitação funcional.** A política selecionada deve identificar explicitamente os atributos/passes utilizados, a versão do otimizador, o corpus/evidência, as métricas de comparação e o motivo da seleção. Nenhuma combinação pode ser escolhida apenas por correlação ou apenas por reduzir candidatos; recall de vínculos verdadeiros e risco de falso vínculo são gates obrigatórios.

**Rastreabilidade de negócio.** RN-005, RN-006, RN-014, RN-023, RN-024

**Rastreabilidade de qualidade/técnica.** RNF: RNF-016, RNF-021, RNF-025, RNF-027, RNF34-A; RT: RT-027, RT-028, RT-029, RT-057

### RF-054 - Preservar a semântica oficial dos nomes do IBGE

**Requisito funcional.** Os dados de nomes e sobrenomes provenientes do IBGE devem preservar a grafia e a frequência oficiais publicadas. A preparação técnica pode aplicar somente transformações necessárias à comparação computacional, como tratamento versionado de caixa, espaços e acentuação, sem colapsar grafias distintas por regras fonéticas, duplicação de letras ou equivalências inventadas não presentes na fonte oficial.

**Critério de aceitação funcional.** O valor original e sua frequência oficial devem permanecer recuperáveis para auditoria. Toda representação técnica derivada deve registrar versão da transformação e permitir rastreamento até o valor original. Mudanças de normalização exigem nova versão e regressões antes de uso real.

**Rastreabilidade de negócio.** RN-005, RN-014, RN-024, RN-029, RN-030

**Rastreabilidade de qualidade/técnica.** RNF: RNF-013, RNF-016, RNF-021, RNF-030, RNF34-A; RT: RT-029, RT-049, RT-057

### RF-055 - Reutilizar no avaliador a regra dinâmica versionada produzida pelo calibrador

**Requisito funcional.** Toda política dinâmica de blocking utilizada pelo calibrador deve ser materializada como artefato versionado e imutável de regras/configuração e o avaliador deve consumir exatamente essa mesma versão, sem reconstrução semântica independente.

**Critério de aceitação funcional.** O relatório de avaliação deve registrar o identificador/fingerprint da política recebida do calibrador e falhar fechado quando a política estiver ausente, incompatível ou divergente. Calibrador e avaliador executados sobre o mesmo corpus e política devem concordar sobre a elegibilidade de cada par candidato.

**Rastreabilidade de negócio.** RN-003, RN-014, RN-024, RN-029

**Rastreabilidade de qualidade/técnica.** RNF: RNF-013, RNF-016, RNF-021, RNF34-A; RT: RT-027, RT-029, RT-057

### RF-056 - Evitar snapshots redundantes da base oficial do IBGE

**Requisito funcional.** Antes de baixar e publicar um novo snapshot de dados oficiais do IBGE, o processo de aquisição deve verificar se a origem mudou. `Content-Length` deve ser usado como pré-verificação barata quando disponível, combinado preferencialmente com validadores HTTP como `ETag` e `Last-Modified`. Igualdade apenas do número de bytes não é evidência suficiente para declarar o conteúdo idêntico.

**Critério de aceitação funcional.** Quando validadores fortes ou combinação confiável de metadados provarem que a origem não mudou, o download deve ser evitado. Quando os metadados forem ausentes, inconclusivos ou divergentes, o conteúdo deve ser baixado para área temporária e comparado por SHA-256 com o snapshot vigente. Um novo snapshot imutável só pode ser publicado/versionado quando o fingerprint do conteúdo efetivamente mudar. A proveniência deve registrar URI, tamanho, validadores disponíveis, SHA-256 e instante de aquisição/verificação.

**Rastreabilidade de negócio.** RN-014, RN-023, RN-024, RN-029

**Rastreabilidade de qualidade/técnica.** RNF: RNF-013, RNF-020, RNF-021, RNF34-A, RNF34-B; RT: RT-057

## Governança dos requisitos incorporados na v1.1

Os RF-051 a RF-056 não autorizam ativação probabilística em Produção. Otimização, dados IBGE e paralelismo somente podem integrar uma política real após regressões unitárias/integradas, avaliação estatística independente e aprovação institucional conforme a issue #31.
