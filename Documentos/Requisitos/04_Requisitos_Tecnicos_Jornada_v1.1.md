# Requisitos Técnicos - Jornada do Cidadão - Fase 1

**Versão do documento:** 1.1  
**Data:** 03/09/2026  
**Base normativa:** Especificação Técnica Jornada v3.62  
**SolutionSchema:** SolutionSchema v3.68  
**Família de requisitos relacionada:** RN v1.1; RF v1.0; RNF v1.0; Matriz de Rastreabilidade v1.0  
**Release de incorporação:** Solution Engenharia v3.98  
**Status:** BASELINE TÉCNICO - CAMADA DE REALIZAÇÃO DA HIERARQUIA RN -> RF/RNF -> RT

> Este documento consolida a camada de realização técnica da Fase 1. Os RF definem o comportamento da solução; os RNF definem atributos de qualidade e restrições; os RT especificam tecnologia, arquitetura, persistência, segurança, operação e mecanismos concretos para materializá-los. Ele não substitui a Base Normativa, DDL, contratos, ADRs nem runbooks.

## 1. Finalidade

Registrar, em linguagem de engenharia e com rastreabilidade, os requisitos que a implementação da Jornada do Cidadão - Fase 1 deve satisfazer para materializar os Requisitos de Negócio v1.1 e a Especificação Técnica v3.62. O documento transforma decisões normativas e de arquitetura em obrigações técnicas verificáveis, sem misturar necessidade de negócio com detalhe de implementação.

## 2. Posição na hierarquia de requisitos

A família documental da Fase 1 adota quatro camadas: **RN** (necessidade/resultado de negócio), **RF** (comportamento funcional da solução), **RNF** (qualidades e restrições) e **RT** (realização técnica). Os 65 RT deste baseline permanecem válidos; a versão 1.1 apenas os posiciona explicitamente na camada técnica e remete a rastreabilidade cruzada ao documento mestre.

A relação completa RN -> RF -> RNF -> RT -> evidência está em `Matriz_Rastreabilidade_Requisitos_Jornada_v1.0`.

## 3. Escopo técnico da Fase 1

- Plataforma .NET 8/C# 12 e SQL Server para a implementação normativa.
- Contratos JSON/OpenAPI/DDL versionados e configuração como código.
- Recepção ZIP v2, Bronze externa content-addressed, Processor assíncrono e estados operacionais.
- Resolução determinística e probabilística de identidade, Gold Pessoas, Gold Registros e governança de divergências.
- APIs de Pessoa, Registros e Possibilidades com autorização, projeção, auditoria e minimização.
- Referência Territorial, Serving/BI, segurança, proteção de dados, observabilidade, operação e continuidade.
- Testes reprodutíveis, restore locked, SQL Server real em Integration e gates de promoção para HML/Produção.

## 4. Princípios técnicos

- **Contratos antes da implementação:** interfaces compartilhadas são estáveis, versionadas e revisadas.
- **Fail closed em integridade e identidade:** dúvida de integridade, autorização ou conflito de identidade não é resolvida por heurística silenciosa.
- **Evidência e replay:** dados de entrada, decisões de identidade e publicações mantêm origem, versão e histórico suficientes para auditoria/reexecução.
- **Separação de responsabilidades:** API recebe, Processor materializa, Runner resolve fallback probabilístico, BI analisa e Gestores finalísticos mantêm decisão administrativa.
- **Operação observável:** locks, leases, filas, runs, retries e alertas são mensuráveis e recuperáveis.
- **Segurança por minimização:** segredos, payloads e identificadores civis não são propagados além do necessário.

## 5. Requisitos Técnicos


### 5.1 Plataforma e base tecnológica

#### RT-001 - Padronizar a plataforma de execução em .NET 8 e C# 12

**Requisito técnico.** Todo código C# da Fase 1 deve compilar para net8.0, com C# 12 definido de forma centralizada e sem divergência de linguagem entre projetos da Solution.

**Critério de aceitação técnica.** Build Release da Solution e dos projetos de teste conclui sem erro; o framework e a versão da linguagem são centralmente verificáveis.

**Rastreabilidade de negócio.** RN-001, RN-035

**Rastreabilidade normativa/técnica.** § 17, RNF01; § 18.1

#### RT-002 - Adotar SQL Server como tecnologia relacional normativa

**Requisito técnico.** A persistência relacional normativa da Fase 1 deve usar SQL Server, com DDL, constraints, procedures, views e testes de integração compatíveis com o motor homologado.

**Critério de aceitação técnica.** O DDL e o seed reais são aplicados em SQL Server e os testes de integração exercitam o mesmo mecanismo relacional previsto para a solução.

**Rastreabilidade de negócio.** RN-003, RN-035

**Rastreabilidade normativa/técnica.** § 17, RNF02 e RNF12; atualização v3.54

#### RT-003 - Manter a Solution autocontida quanto aos componentes normativos

**Requisito técnico.** A Solution não deve depender, para compilar ou executar o fluxo normativo, de componente histórico ausente de Jornada.sln ou de artefato externo não declarado.

**Critério de aceitação técnica.** Uma restauração e build limpos da Solution resolvem todos os projetos normativos e não exigem binários históricos não versionados.

**Rastreabilidade de negócio.** RN-001, RN-035

**Rastreabilidade normativa/técnica.** § 17, RNF18; § 18.1

#### RT-004 - Externalizar configuração e secrets por ambiente

**Requisito técnico.** Endpoints, credenciais técnicas, chaves, certificados, parâmetros de banco e demais secrets devem ser substituíveis por configuração de ambiente, sem recompilar aplicações e sem versionar segredo em repositório.

**Critério de aceitação técnica.** HML pode alterar secret/configuração sem rebuild; varredura do repositório não encontra chaves operacionais versionadas.

**Rastreabilidade de negócio.** RN-029, RN-030, RN-033

**Rastreabilidade normativa/técnica.** §§ 13.2 a 13.4; § 17, RNF17

#### RT-005 - Vincular releases de engenharia a fonte Git identificável

**Requisito técnico.** Cada release de engenharia deve estar vinculada a commit/tag Git e possuir proveniência suficiente para reconstruir ou auditar o estado-fonte distribuído.

**Critério de aceitação técnica.** O pacote registra tag, commit, predecessor e bundle/fonte correspondente, sem atribuir ao ambiente de empacotamento evidências que não foram executadas nele.

**Rastreabilidade de negócio.** RN-014, RN-029, RN-033

**Rastreabilidade normativa/técnica.** atualização v3.54; governança § 15.2 e § 15.8


### 5.2 Contratos e versionamento

#### RT-006 - Versionar contratos de Pessoa por Gestor

**Requisito técnico.** Cada Gestor deve possuir contrato cadastral de Pessoa versionado em config/contracts/gestores/<GESTOR>/pessoa/vN, com metadados e JSON Schema correspondentes.

**Critério de aceitação técnica.** A ingestão seleciona o contrato pela versão declarada e rejeita payload incompatível com o schema ativado.

**Rastreabilidade de negócio.** RN-002, RN-014, RN-034, RN-035

**Rastreabilidade normativa/técnica.** § 5.2; §§ 6.1 a 6.3

#### RT-007 - Versionar contratos factuais por Tipo e Natureza

**Requisito técnico.** Cada Tipo de Benefício ou Serviço deve possuir versão contratual própria, com Natureza, metadados, pessoa.schema.json e registro.schema.json aplicáveis.

**Critério de aceitação técnica.** Mudança material de regra, vigência, medida ou contrato exige nova versão; o código de Tipo permanece estável.

**Rastreabilidade de negócio.** RN-002, RN-011, RN-014, RN-035

**Rastreabilidade normativa/técnica.** §§ 5.1 e 5.2; § 15.3

#### RT-008 - Impedir mutação incompatível de schema ativado

**Requisito técnico.** JSON Schema ativado é imutável; alteração incompatível de contrato, API ou view Serving deve produzir nova versão formal.

**Critério de aceitação técnica.** Comparação entre versões evidencia evolução explícita e não reutiliza versão histórica com semântica incompatível.

**Rastreabilidade de negócio.** RN-014, RN-035

**Rastreabilidade normativa/técnica.** § 5.2; § 17, RNF13; § 15.4

#### RT-009 - Estabilizar contratos compartilhados para trabalho paralelo

**Requisito técnico.** DDL contratual, OpenAPI/DTOs, interfaces públicas, schemas e views serving.v_bi_* devem ter ownership e revisão formal quando alterados, evitando edição cruzada informal entre frentes.

**Critério de aceitação técnica.** Mudança em fronteira compartilhada é rastreável por revisão/PR e comunicação aos consumidores; frentes podem evoluir em paralelo após o gate inicial.

**Rastreabilidade de negócio.** RN-033, RN-034, RN-035

**Rastreabilidade normativa/técnica.** §§ 18.1 e 18.2; § 17, RNF19


### 5.3 Ingestão e Bronze

#### RT-010 - Receber cada Entrega em envelope ZIP v2 único

**Requisito técnico.** A unidade externa de ingestão deve ser um ZIP v2 contendo exatamente manifest.json, pessoas.jsonl e registros.jsonl; registros.jsonl pode estar vazio.

**Critério de aceitação técnica.** O endpoint aceita entrega exclusivamente cadastral e entrega factual no mesmo envelope, validando a presença e os nomes canônicos dos três arquivos.

**Rastreabilidade de negócio.** RN-002, RN-003

**Rastreabilidade normativa/técnica.** § 6.1

#### RT-011 - Garantir cobertura mínima de Pessoas referenciadas pela Entrega

**Requisito técnico.** pessoas.jsonl deve conter as Pessoas incluídas/alteradas no período e todas as Pessoas referenciadas pelos fatos da mesma Entrega.

**Critério de aceitação técnica.** Nenhum Registro factual aceito referencia uma Pessoa ausente do contexto mínimo exigido pelo contrato da Entrega.

**Rastreabilidade de negócio.** RN-002, RN-003, RN-012

**Rastreabilidade normativa/técnica.** § 6.1

#### RT-012 - Aplicar limites físicos e proteção contra ZIP bomb

**Requisito técnico.** A implementação de referência deve impor limites de até 250 MB compactados, 2 GB descompactados e 64 KB para manifest.json, com orçamento descompactado aplicado durante a leitura.

**Critério de aceitação técnica.** Pacote que excede limites é rejeitado sem descompressão ilimitada nem alocação descontrolada.

**Rastreabilidade de negócio.** RN-023, RN-030

**Rastreabilidade normativa/técnica.** § 6.1; § 17, RNF03; limites da implementação de referência

#### RT-013 - Autenticar e autorizar a Entrega antes do processamento pesado

**Requisito técnico.** POST /api/v1/ingestao/entregas deve exigir Código do Gestor, chave de acesso, Idempotency-Key e contexto contratual coerente com o Gestor autenticado.

**Critério de aceitação técnica.** Tipo/Sistema de Origem não pertencente ao Gestor é recusado antes da validação pesada e antes da materialização funcional.

**Rastreabilidade de negócio.** RN-002, RN-029, RN-034

**Rastreabilidade normativa/técnica.** §§ 6.2 e 6.3; § 13

#### RT-014 - Validar integridade do payload por SHA-256 e nome canônico

**Requisito técnico.** A API deve calcular o SHA-256 dos bytes recebidos e exigir correspondência com o hash do filename canônico ENTREGA_<GESTOR>_<SISTEMA_ORIGEM>_v2_<sha256>.zip.

**Critério de aceitação técnica.** Divergência entre nome e conteúdo rejeita a chamada antes da recepção; a resposta não ecoa indevidamente o hash calculado.

**Rastreabilidade de negócio.** RN-003, RN-023, RN-029

**Rastreabilidade normativa/técnica.** §§ 6.2 e 6.3

#### RT-015 - Garantir idempotência por Gestor e Idempotency-Key

**Requisito técnico.** Mesma Idempotency-Key com mesmo conteúdo deve devolver o recebimento existente sem duplicar processamento; mesma chave com conteúdo diferente deve retornar conflito e gerar auditoria.

**Critério de aceitação técnica.** Testes comprovam replay idêntico sem duplicação e conflito 409 para reutilização divergente da chave.

**Rastreabilidade de negócio.** RN-003, RN-023, RN-029

**Rastreabilidade normativa/técnica.** § 6.3; § 17, RNF04

#### RT-016 - Persistir Bronze fora do SQL por chave content-addressed

**Requisito técnico.** Os bytes do ZIP Bronze devem residir em armazenamento externo durável, imutável e content-addressed por SHA-256; SQL Server persiste somente metadados e objeto_chave, sem VARBINARY(MAX).

**Critério de aceitação técnica.** Objeto é escrito antes da linha SQL, permanece na chave sha256/ab/cd/<hash>.zip e pode ser recuperado enquanto existir referência válida.

**Rastreabilidade de negócio.** RN-003, RN-029, RN-030, RN-031

**Rastreabilidade normativa/técnica.** § 6.4; § 17, RNF32

#### RT-017 - Falhar fechado em corrupção ou indisponibilidade da Bronze

**Requisito técnico.** Objeto canônico já existente deve ser revalidado por SHA-256; corrupção ou falha temporária de armazenamento deve ser tratada como falha de infraestrutura, sem aceitar silenciosamente conteúdo divergente.

**Critério de aceitação técnica.** Recepção retorna 503 + Retry-After quando aplicável; GC/expurgo físico só ocorre após confirmação coordenada de ausência de referências e carência.

**Rastreabilidade de negócio.** RN-003, RN-023, RN-029, RN-031

**Rastreabilidade normativa/técnica.** § 6.4; § 17, RNF33


### 5.4 Processor e resiliência

#### RT-018 - Executar processamento pesado de forma assíncrona ao depósito

**Requisito técnico.** A API deve realizar apenas validações de borda e persistência da Entrega; parsing completo, validação estrutural e materialização Silver/Gold devem ocorrer no Jornada.Processor.Worker.

**Critério de aceitação técnica.** POST de ingestão retorna 202 após recepção válida e o processamento pesado pode prosseguir independentemente da conexão do cliente.

**Rastreabilidade de negócio.** RN-002, RN-023, RN-035

**Rastreabilidade normativa/técnica.** § 6.2; § 6.6

#### RT-019 - Registrar resultado individual de Pessoas e Registros processados

**Requisito técnico.** Cada item processado deve gerar ocorrência de resultado individual em ingestao.item_processado, distinguindo inclusão, versionamento, retransmissão, exclusão e reabertura.

**Critério de aceitação técnica.** A observabilidade permite identificar retransmissão idêntica sem criar nova observação Silver e medir resultado por classe de item.

**Rastreabilidade de negócio.** RN-003, RN-023, RN-024

**Rastreabilidade normativa/técnica.** § 6.3

#### RT-020 - Manter persistência funcional transacional e atômica por lote

**Requisito técnico.** Materializações Silver, identidade, Gold e estados associados de um lote devem respeitar transação e rollback; falha intermediária não pode deixar publicação funcional parcial.

**Critério de aceitação técnica.** Fault injection após persistência parcial resulta em zero efeitos do cenário que deveria ter sido revertido.

**Rastreabilidade de negócio.** RN-003, RN-012, RN-023, RN-029

**Rastreabilidade normativa/técnica.** §§ 6.5 a 6.7; § 17, RNF06

#### RT-021 - Suportar lease, fencing, heartbeat, retry e estado POISON

**Requisito técnico.** O Processor deve reservar trabalho com lease/fencing token, renovar heartbeat, recuperar leases expirados, aplicar retry com backoff e encerrar em POISON após limite de tentativas.

**Critério de aceitação técnica.** Workers concorrentes não processam o mesmo lote válido simultaneamente; lease expirado é cercado e não pode persistir após nova reserva.

**Rastreabilidade de negócio.** RN-023, RN-029, RN-035

**Rastreabilidade normativa/técnica.** § 16.3; atualização v3.33

#### RT-022 - Serializar mutações do corpus integrado nos jobs críticos

**Requisito técnico.** Processor, Parameters GENERATE_DRAFT e Linkage Runner devem respeitar gate serial de corpus com locks/leases explícitos, permitindo recepção API/Bronze enquanto a materialização integrada está congelada.

**Critério de aceitação técnica.** Jobs analíticos não observam corpus parcialmente mutável; intenção exclusiva evita starvation e possui timeouts finitos.

**Rastreabilidade de negócio.** RN-023, RN-024, RN-028

**Rastreabilidade normativa/técnica.** § 17, RNF26 e RNF28; atualizações v3.35/v3.36


### 5.5 Identidade

#### RT-023 - Usar PESSOA_UUID aleatório e independente do CPF

**Requisito técnico.** O identificador canônico interno deve ser UNIQUEIDENTIFIER aleatório, sem derivação criptográfica ou determinística do CPF.

**Critério de aceitação técnica.** Alteração/correção de CPF ou atributos não exige troca do UUID já canônico, salvo operações governadas de fusão/separação.

**Rastreabilidade de negócio.** RN-005, RN-006, RN-030

**Rastreabilidade normativa/técnica.** § 7.2; § 17, RNF05

#### RT-024 - Resolver CPF válido pela rota determinística com trava de consistência

**Requisito técnico.** CPF válido deve ser a rota normal de resolução, mas a associação a identity_map existente deve falhar fechada quando o núcleo cadastral indicar divergência forte.

**Critério de aceitação técnica.** Conflito não produz atribuição canônica automática e é materializado para governança.

**Rastreabilidade de negócio.** RN-005, RN-006, RN-023

**Rastreabilidade normativa/técnica.** §§ 7.3 a 7.5; atualizações v3.42/v3.46

#### RT-025 - Preservar Pessoas sem CPF em estado processável

**Requisito técnico.** Quando o contrato admitir CPF ausente, a Pessoa deve permanecer em Silver e em estado de identidade pendente/nao resolvido, sem descarte por ausência do documento.

**Critério de aceitação técnica.** Registro sem CPF admitido pode ser reavaliado posteriormente pelo linkage probabilístico.

**Rastreabilidade de negócio.** RN-007, RN-024

**Rastreabilidade normativa/técnica.** §§ 7.5, 7.9 e 7.13

#### RT-026 - Executar linkage probabilístico em Runner independente e run-once

**Requisito técnico.** O fallback probabilístico deve ser executado pelo Jornada.Linkage.Runner, independente do Processor, schedulável externamente e parametrizado por modo, escopo, batch e paralelismo.

**Critério de aceitação técnica.** O Processor determinístico continua operando quando o Runner/Parameters estiver indisponível; registros sem CPF permanecem pendentes.

**Rastreabilidade de negócio.** RN-006, RN-007, RN-023, RN-028

**Rastreabilidade normativa/técnica.** §§ 7.5 a 7.7; § 16.5; § 17, RNF23

#### RT-027 - Congelar modelo e universo de cada linkage_run

**Requisito técnico.** Cada linkage_run deve capturar uma única versão imutável do modelo antes do primeiro score e materializar o universo exato do run, sem misturar modelo_id/modelo_versao.

**Critério de aceitação técnica.** Resultados históricos são append-only e só runs PUBLICADOS alimentam a visão corrente.

**Rastreabilidade de negócio.** RN-003, RN-005, RN-024, RN-029

**Rastreabilidade normativa/técnica.** §§ 7.6 e 7.6.1; § 17, RNF21, RNF22 e RNF24

#### RT-028 - Aplicar blocking e limites sem truncamento silencioso

**Requisito técnico.** O linkage deve usar blocking definido, incluindo data de nascimento exata no baseline, e não pode truncar silenciosamente blocos de candidatos que excedam limite operacional.

**Critério de aceitação técnica.** Bloco acima do limite falha o run e exige revisão explícita da configuração; ausência de candidato é observável.

**Rastreabilidade de negócio.** RN-006, RN-024

**Rastreabilidade normativa/técnica.** §§ 7.4 a 7.7; § 17, RNF25

#### RT-029 - Versionar e ativar parâmetros probabilísticos por transição auditável

**Requisito técnico.** Parâmetros de linkage devem possuir ciclo de geração/validação/ativação versionado; um modelo novo só substitui o ativo por transição explícita e auditável.

**Critério de aceitação técnica.** Modelo em RASCUNHO/VALIDAÇÃO não passa a ativo por overwrite; replay referencia modelo/versão original.

**Rastreabilidade de negócio.** RN-014, RN-024, RN-029

**Rastreabilidade normativa/técnica.** § 7.6.1; § 17, RNF16

#### RT-030 - Governar fusão e separação histórica de identidades

**Requisito técnico.** Fusão/separação de UUIDs deve ocorrer apenas por operação governada, com preflight de consistência, sucessor canônico quando aplicável e migração auditável de identificadores ativos.

**Critério de aceitação técnica.** Fusão 1→N, cadeia inválida/cíclica, CPF divergente ou absorção parcial são recusadas por erro contratual antes de mutações.

**Rastreabilidade de negócio.** RN-005, RN-029

**Rastreabilidade normativa/técnica.** § 7.12; atualizações v3.58-v3.60


### 5.6 Gold, evidência e fatos

#### RT-031 - Separar fisicamente Gold Pessoas de Gold Registros

**Requisito técnico.** A Camada Ouro deve manter famílias canônicas distintas para identidade/atributos cadastrais e para histórico factual de Benefícios Concedidos e Serviços Prestados.

**Critério de aceitação técnica.** Fatos não são armazenados como atributos cadastrais e consultas distinguem Pessoa de Registro.

**Rastreabilidade de negócio.** RN-011, RN-013

**Rastreabilidade normativa/técnica.** § 8; § 17, RNF14

#### RT-032 - Permitir bootstrap evolutivo da Gold Pessoas

**Requisito técnico.** A primeira observação elegível de uma Pessoa, com individualização suficiente, deve poder constituir baseline Golden sem segunda fonte ou modelo probabilístico prévio.

**Critério de aceitação técnica.** BASELINE_FONTE_UNICA é estado Golden válido e pode ser posteriormente corroborado/corrigido por evidência superior.

**Rastreabilidade de negócio.** RN-004, RN-009, RN-028

**Rastreabilidade normativa/técnica.** §§ 8.1, 8.9 e 8.11

#### RT-033 - Preservar fato válido mesmo sem PESSOA_UUID canônico

**Requisito técnico.** Gold Registros deve aceitar fato finalístico válido com PESSOA_UUID ausente quando a identidade estiver pendente/conflitante, preservando o identificador declarado pela origem.

**Critério de aceitação técnica.** O fato permanece consultável/auditável sem forçar atribuição canônica indevida.

**Rastreabilidade de negócio.** RN-012, RN-023

**Rastreabilidade normativa/técnica.** § 8.8; atualização normativa v3.45

#### RT-034 - Aplicar precedência de evidência por catálogo e temporalidade

**Requisito técnico.** A seleção Golden de atributos transversais deve considerar evidência comprovada, pertinência, temporalidade, provenance e corroboração, sem precedência automática por Secretaria/Gestor.

**Critério de aceitação técnica.** Evidência documental válida pode substituir valor Golden e toda alteração mantém histórico/proveniência.

**Rastreabilidade de negócio.** RN-008, RN-009, RN-010, RN-029

**Rastreabilidade normativa/técnica.** §§ 8.3 a 8.7; § 15.5


### 5.7 API e acesso

#### RT-035 - Autorizar antes da leitura funcional

**Requisito técnico.** Endpoints de identidade, Pessoa, Registros e Possibilidades devem autorizar por credencial autenticada, scope e recurso antes de executar a leitura funcional.

**Critério de aceitação técnica.** Credencial sem autorização recebe 401/403 sem leitura de dados; autorização não depende de vínculo prévio da Pessoa com a credencial.

**Rastreabilidade de negócio.** RN-018, RN-019, RN-020, RN-029

**Rastreabilidade normativa/técnica.** § 10.6; § 17, RNF29

#### RT-036 - Não criar UUID por consulta de resolução

**Requisito técnico.** A API de resolução deve consultar identidade existente; operações de consulta não podem criar automaticamente nova Pessoa/UUID.

**Critério de aceitação técnica.** Consulta de CPF inexistente retorna estado apropriado sem mutação do mapa de identidade.

**Rastreabilidade de negócio.** RN-005, RN-018, RN-022

**Rastreabilidade normativa/técnica.** § 10.2

#### RT-037 - Projetar Pessoa pelo schema autorizado da credencial

**Requisito técnico.** A consulta cadastral deve aplicar cumulativamente o JSON Schema autorizado da credencial e eventuais restrições negativas versionadas do Gestor responsável.

**Critério de aceitação técnica.** Campo fora da projeção autorizada não é retornado; ausência de restrição ativa mantém a projeção municipal padrão prevista.

**Rastreabilidade de negócio.** RN-018, RN-019, RN-020, RN-022

**Rastreabilidade normativa/técnica.** § 10.4.1; § 10.6

#### RT-038 - Separar fisicamente Registros de Possibilidades

**Requisito técnico.** APIs, DTOs e projeções de Registros e Possibilidades devem permanecer semanticamente e fisicamente distinguíveis.

**Critério de aceitação técnica.** Nenhum endpoint de Possibilidades é utilizado como fonte factual de Benefício Concedido/Serviço Prestado e a resposta explicita a natureza calculada.

**Rastreabilidade de negócio.** RN-011, RN-012, RN-013

**Rastreabilidade normativa/técnica.** §§ 10.4.2, 10.4.3 e 10.5; § 17, RNF14

#### RT-039 - Auditar todo acesso individual e em lote

**Requisito técnico.** Cada acesso individual deve registrar credencial/Gestor, rota, recurso e UUID-alvo; consulta em lote deve registrar todos os UUIDs do evento sem persistir CPF, nome, payload, access key ou finalidade declarada.

**Critério de aceitação técnica.** Trilha de auditoria permite reconstruir quem acessou qual recurso e quando, sem duplicar conteúdo pessoal desnecessário.

**Rastreabilidade de negócio.** RN-029, RN-030

**Rastreabilidade normativa/técnica.** §§ 10.8, 13.5 e 15.6; § 17, RNF30

#### RT-040 - Aplicar rate limit pós-autenticação e proxy confiável

**Requisito técnico.** Rate limiting funcional deve usar CredentialId + classe de endpoint; X-Forwarded-For só pode ser aceito quando Forwarded Headers estiver configurado com proxies explicitamente confiáveis.

**Critério de aceitação técnica.** Sem lista confiável, middleware de forwarded headers permanece desabilitado e o endereço observado na conexão é usado.

**Rastreabilidade de negócio.** RN-029, RN-030

**Rastreabilidade normativa/técnica.** § 13.6; § 17, RNF29 e RNF31


### 5.8 Referência Territorial

#### RT-041 - Modelar Referência Territorial separada de endereço civil

**Requisito técnico.** A solução deve manter Referência Territorial como conceito transversal próprio, separado de ENDERECO_RESIDENCIAL e ENDERECO_CORRESPONDENCIA, inclusive quando existir apenas Distrito/Subprefeitura.

**Critério de aceitação técnica.** Consultas/BI territoriais usam a referência selecionada e não confundem endereço civil com referência de política pública.

**Rastreabilidade de negócio.** RN-015, RN-025

**Rastreabilidade normativa/técnica.** § 11; atualizações v3.28-v3.32; § 17, RNF07

#### RT-042 - Proibir inferência territorial automática por local de atendimento

**Requisito técnico.** Unidade/local de atendimento não pode substituir automaticamente a Referência Territorial da Pessoa; ACOLHIMENTO_INSTITUCIONAL exige declaração explícita da fonte de que aquele local é a referência no período.

**Critério de aceitação técnica.** Nenhuma rotina deriva referência territorial somente do local onde o serviço foi prestado.

**Rastreabilidade de negócio.** RN-015, RN-016, RN-021

**Rastreabilidade normativa/técnica.** § 11; atualização v3.28

#### RT-043 - Preservar snapshot territorial utilizado pelos fatos

**Requisito técnico.** Quando houver territorialização, o fato Gold/Serving deve referenciar o snapshot exato de Referência Territorial aplicável, com natureza, situação geográfica e provenance preservadas.

**Critério de aceitação técnica.** Mudança futura da referência não reescreve retrospectivamente o snapshot territorial associado ao fato histórico.

**Rastreabilidade de negócio.** RN-003, RN-016, RN-017, RN-025

**Rastreabilidade normativa/técnica.** § 11; atualizações v3.31-v3.32


### 5.9 BI e indicadores

#### RT-044 - Tratar views serving.v_bi_* como contratos estáveis

**Requisito técnico.** Views serving.v_bi_* devem constituir contrato estável entre engenharia e Power BI; alteração incompatível exige versionamento formal e comunicação.

**Critério de aceitação técnica.** PBIP/BI não depende de tabela interna não contratada quando existe view Serving canônica.

**Rastreabilidade de negócio.** RN-025, RN-035

**Rastreabilidade normativa/técnica.** § 12.7; § 17, RNF10 e RNF13

#### RT-045 - Parametrizar o PBIP sem credenciais versionadas

**Requisito técnico.** O projeto Power BI deve abrir com parâmetros de servidor/banco e não deve conter credenciais operacionais versionadas.

**Critério de aceitação técnica.** Arquivo PBIP pode ser apontado para local/HML por parâmetro e secrets permanecem externos.

**Rastreabilidade de negócio.** RN-025, RN-030, RN-033

**Rastreabilidade normativa/técnica.** § 12.7; § 17, RNF11

#### RT-046 - Expor qualidade, cobertura e incerteza no BI

**Requisito técnico.** Dashboards devem mostrar ingestão, qualidade de pacotes, identidade por origem, QC, pendências, territorialidade, retransmissões e estados de incerteza, sem ocultar denominadores e coberturas.

**Critério de aceitação técnica.** Indicadores distinguem resolvido determinístico, probabilístico, não resolvido, conflito e ausência de candidato quando aplicável.

**Rastreabilidade de negócio.** RN-023, RN-024, RN-025

**Rastreabilidade normativa/técnica.** §§ 12.6 e 19.1 a 19.8

#### RT-047 - Manter consulta individual fora do Power BI

**Requisito técnico.** Consulta individual de Pessoa, Registros e Possibilidades deve ocorrer por APIs/sistemas finalísticos; o BI é camada analítica e não mecanismo de consulta operacional individualizada.

**Critério de aceitação técnica.** PBIP não precisa expor tela operacional de consulta individual; sistemas autorizados usam API.

**Rastreabilidade de negócio.** RN-018, RN-025, RN-026

**Rastreabilidade normativa/técnica.** § 12.2; princípios da Camada 5


### 5.10 Segurança, privacidade e governança

#### RT-048 - Autenticar integrações e consumidores por código + chave/certificado conforme perfil

**Requisito técnico.** A solução deve exigir autenticação técnica para ingestão e consultas, com credenciais segregadas por Gestor/Tipo e scopes mínimos aplicáveis.

**Critério de aceitação técnica.** Chave inválida não acessa recurso; rotação e substituição de secret são possíveis sem mudança de código.

**Rastreabilidade de negócio.** RN-018, RN-029, RN-033

**Rastreabilidade normativa/técnica.** §§ 13.2 e 13.3

#### RT-049 - Minimizar dados em logs e telemetria

**Requisito técnico.** Logs não podem conter payload integral, access key, CPF/nome desnecessário ou outros identificadores civis além do mínimo operacional.

**Critério de aceitação técnica.** Revisão de logs de erro, API e auditoria evidencia uso de identificadores técnicos/UUID quando suficiente.

**Rastreabilidade de negócio.** RN-029, RN-030

**Rastreabilidade normativa/técnica.** §§ 13.5 e 14.5; § 17, RNF08

#### RT-050 - Manter CPF restrito às camadas operacionais autorizadas

**Requisito técnico.** Na Fase 1, CPF pode existir nas estruturas operacionais necessárias à identidade, mas não deve ser propagado a observabilidade/BI como dimensão nem exposto sem autorização contratual.

**Critério de aceitação técnica.** Dashboards e métricas de cobertura de CPF usam contagens/status sem revelar valores individuais.

**Rastreabilidade de negócio.** RN-006, RN-024, RN-030

**Rastreabilidade normativa/técnica.** § 14.5.1; atualização v3.30

#### RT-051 - Aplicar retenção e expurgo por classe de ativo

**Requisito técnico.** Bronze, Gold, evidências, auditoria e estruturas derivadas devem possuir política de retenção compatível com finalidade, base legal e governança, incluindo cinco anos para registros de acesso/auditoria conforme baseline.

**Critério de aceitação técnica.** Expurgo é executável sem quebrar recuperabilidade referencial; Produção só inicia com parâmetros de retenção aprovados.

**Rastreabilidade de negócio.** RN-029, RN-030, RN-031

**Rastreabilidade normativa/técnica.** § 14.4; tabela de retenção; § 15

#### RT-052 - Proteger estruturalmente endereço de casa-abrigo sigilosa

**Requisito técnico.** ENDERECO_CASA_ABRIGO_SIGILOSA deve ter origem exclusiva em Tipo SERVICO habilitado, não gerar marcador público de Pessoa protegida, não alimentar territorialização e não ser projetado cross-Gestor.

**Critério de aceitação técnica.** Testes de projeção e territorialização comprovam ausência de vazamento desse atributo para consumidores não autorizados.

**Rastreabilidade de negócio.** RN-021, RN-030

**Rastreabilidade normativa/técnica.** atualização normativa v3.57


### 5.11 Operação e requisitos não funcionais

#### RT-053 - Disponibilizar health checks, heartbeat e telemetria operacional

**Requisito técnico.** Componentes residentes devem possuir health check/heartbeat e componentes run-once devem registrar status/telemetria de execução, inclusive linkage_run.

**Critério de aceitação técnica.** HML permite detectar componente indisponível, run estagnado, backlog e lease expirado sem inspeção manual do banco.

**Rastreabilidade de negócio.** RN-023, RN-024, RN-033

**Rastreabilidade normativa/técnica.** § 16.4; § 17, RNF09

#### RT-054 - Manter PipelineWatchdog somente observacional

**Requisito técnico.** Alertas como LINKAGE_RUN_STALE, MODEL_GENERATION_STALE, PROCESSOR_LEASE_EXPIRED e PROCESSOR_BACKLOG_OLD devem informar incidente sem autorizar mutação automática de estado funcional.

**Critério de aceitação técnica.** Disparo de alerta não executa correção cadastral, publicação ou reprocessamento destrutivo por conta própria.

**Rastreabilidade de negócio.** RN-022, RN-023, RN-029

**Rastreabilidade normativa/técnica.** § 16.4; atualização v3.53

#### RT-055 - Usar scheduler corporativo externo para jobs recorrentes

**Requisito técnico.** A Fase 1 não deve implementar scheduler próprio; recorrência e encadeamento de Parameters, Runner e verificações run-once devem usar mecanismo corporativo homologado com logs, retry limitado e não sobreposição.

**Critério de aceitação técnica.** Runbook registra comando, argumentos, início/fim, exit code e identidade técnica do disparo.

**Rastreabilidade de negócio.** RN-033, RN-034

**Rastreabilidade normativa/técnica.** § 16.6

#### RT-056 - Degradar sem bloquear o caminho determinístico

**Requisito técnico.** Indisponibilidade do Parameters Worker/Runner não pode bloquear recepção nem processamento determinístico de Pessoas com CPF válido; pendências sem CPF aguardam reexecução posterior.

**Critério de aceitação técnica.** Falha do componente probabilístico não transforma carga determinística válida em indisponibilidade sistêmica.

**Rastreabilidade de negócio.** RN-006, RN-007, RN-023, RN-028

**Rastreabilidade normativa/técnica.** § 16.5

#### RT-057 - Processar corpus de escala sem carregamento integral em memória

**Requisito técnico.** Parameters Worker e rotinas analíticas de escala devem usar streaming/agregação SQL e amostras limitadas, evitando materializar toda a Gold em memória ou persistir frequências de alta cardinalidade desnecessárias.

**Critério de aceitação técnica.** Teste/harness de escala demonstra memória limitada e ausência de dependência de carregamento populacional integral.

**Rastreabilidade de negócio.** RN-023, RN-024, RN-028, RN-035

**Rastreabilidade normativa/técnica.** § 17, RNF15 e RNF27; atualização v3.55

#### RT-058 - Medir desempenho em HML antes de definir SLA de Produção

**Requisito técnico.** HML deve medir p50, p95, vazão, duração de estágios, backlog e tempos dos jobs; SLA definitivo de Produção não é fixado pela Fase 1 sem evidência de dimensionamento.

**Critério de aceitação técnica.** Relatório de HML contém métricas de capacidade suficientes para subsidiar sizing e metas posteriores.

**Rastreabilidade de negócio.** RN-023, RN-027, RN-033

**Rastreabilidade normativa/técnica.** § 17, RNF20; §§ 16.4 e 19.1


### 5.12 Testes, release e aceite

#### RT-059 - Exercitar DDL e seed reais em testes de integração

**Requisito técnico.** A suíte Integration deve aplicar o DDL e o seed reais de SQL Server, em banco isolado por execução, para detectar incompatibilidades do motor, constraints, procedures e reentrada do schema.

**Critério de aceitação técnica.** Execução local canônica cria banco descartável e os testes não dependem de banco compartilhado preexistente.

**Rastreabilidade de negócio.** RN-023, RN-029, RN-033

**Rastreabilidade normativa/técnica.** § 17, RNF12; atualizações v3.54/v3.55

#### RT-060 - Restaurar dependências em modo locked para validação de release

**Requisito técnico.** A validação local de release deve restaurar Solution, Unit e Integration em --locked-mode antes de build/test, garantindo que o grafo resolvido corresponda aos packages.lock.json versionados.

**Critério de aceitação técnica.** Alteração não declarada no grafo falha o restore locked; release registra separadamente evidência executada externamente e claims do ambiente de empacotamento.

**Rastreabilidade de negócio.** RN-014, RN-029, RN-033

**Rastreabilidade normativa/técnica.** engenharia reprodutível derivada de atualização v3.54; baseline Solution v3.95/v3.96

#### RT-061 - Validar SQL Server local por imagem fixada em digest

**Requisito técnico.** O ambiente local de integração deve usar a imagem SQL Server homologada por tag + digest; o script deve reutilizar cache quando íntegro e realizar pull somente quando ausente ou quando a recuperação controlada do engine exigir.

**Critério de aceitação técnica.** Validação informa imagem/digest, confirma engine real e não interpreta falha funcional de teste como corrupção da imagem.

**Rastreabilidade de negócio.** RN-023, RN-033

**Rastreabilidade normativa/técnica.** atualização v3.54; baseline de engenharia v3.95

#### RT-062 - Verificar integridade básica do engine SQL antes da Integration

**Requisito técnico.** O preflight local deve confirmar Docker Engine, conexão SQL, ProductVersion e DBCC CHECKDB(master), com uma única tentativa controlada de re-pull quando houver evidência de falha do próprio engine.

**Critério de aceitação técnica.** Falha de constraint/procedure da aplicação não dispara reinstalação; falha de engine/cache pode acionar recuperação segura e auditável.

**Rastreabilidade de negócio.** RN-023, RN-029, RN-033

**Rastreabilidade normativa/técnica.** baseline de engenharia v3.95; operação § 16

#### RT-063 - Tratar warnings e testes como gates de qualidade

**Requisito técnico.** CI/validação de release deve compilar em Release com warnings controlados como erro quando configurado e executar Unit + Integration relevantes antes de promoção.

**Critério de aceitação técnica.** Build 0 erros e suítes aprovadas são pré-condição de baseline técnico; exceções precisam de registro formal e não podem ser mascaradas.

**Rastreabilidade de negócio.** RN-023, RN-029, RN-033

**Rastreabilidade normativa/técnica.** atualização v3.30; § 18.4

#### RT-064 - Demonstrar fluxo ponta a ponta em HML antes de Produção

**Requisito técnico.** A HML deve demonstrar autenticação, ingestão v2, Bronze→Silver→Gold/Serving, identidade, Referência Territorial, QC, APIs, Possibilidades, Power BI, logs, métricas, auditoria, watchdog e scheduler corporativo.

**Critério de aceitação técnica.** Promoção para Produção requer evidência dos critérios de aceite técnico da Fase 1 e dos gates de governança ainda externos à validação local.

**Rastreabilidade de negócio.** RN-023, RN-025, RN-029, RN-031, RN-032, RN-033, RN-034

**Rastreabilidade normativa/técnica.** § 18.4; §§ 13 a 16

#### RT-065 - Manter biometria fora do núcleo técnico da Fase 1

**Requisito técnico.** A Fase 1 não deve depender de biometria para constituir, consultar ou manter a Pessoa canônica; eventual biometria futura deve ser tratada como evolução específica, com contrato, governança e proteção próprios.

**Critério de aceitação técnica.** Build, ingestão, resolução determinística, Gold e APIs funcionam sem componente biométrico; nenhuma ausência de biometria bloqueia o fluxo normativo da Fase 1.

**Rastreabilidade de negócio.** RN-036

**Rastreabilidade normativa/técnica.** § 20 - Fora de escopo; diretriz de evolução da identidade


## 6. Rastreabilidade resumida RN -> RT (legado compatível)

| Requisito de Negócio | Requisitos Técnicos relacionados |
|---|---|
| RN-001 | RT-001, RT-003 |
| RN-002 | RT-006, RT-007, RT-010, RT-011, RT-013, RT-018 |
| RN-003 | RT-002, RT-010, RT-011, RT-014, RT-015, RT-016, RT-017, RT-019, RT-020, RT-027, RT-043 |
| RN-004 | RT-032 |
| RN-005 | RT-023, RT-024, RT-027, RT-030, RT-036 |
| RN-006 | RT-023, RT-024, RT-026, RT-028, RT-050, RT-056 |
| RN-007 | RT-025, RT-026, RT-056 |
| RN-008 | RT-034 |
| RN-009 | RT-032, RT-034 |
| RN-010 | RT-034 |
| RN-011 | RT-007, RT-031, RT-038 |
| RN-012 | RT-011, RT-020, RT-033, RT-038 |
| RN-013 | RT-031, RT-038 |
| RN-014 | RT-005, RT-006, RT-007, RT-008, RT-029, RT-060 |
| RN-015 | RT-041, RT-042 |
| RN-016 | RT-042, RT-043 |
| RN-017 | RT-043 |
| RN-018 | RT-035, RT-036, RT-037, RT-047, RT-048 |
| RN-019 | RT-035, RT-037 |
| RN-020 | RT-035, RT-037 |
| RN-021 | RT-042, RT-052 |
| RN-022 | RT-036, RT-037, RT-054 |
| RN-023 | RT-012, RT-014, RT-015, RT-017, RT-018, RT-019, RT-020, RT-021, RT-022, RT-024, RT-026, RT-033, RT-046, RT-053, RT-054, RT-056, RT-057, RT-058, RT-059, RT-061, RT-062, RT-063, RT-064 |
| RN-024 | RT-019, RT-022, RT-025, RT-027, RT-028, RT-029, RT-046, RT-050, RT-053, RT-057 |
| RN-025 | RT-041, RT-043, RT-044, RT-045, RT-046, RT-047, RT-064 |
| RN-026 | RT-047 |
| RN-027 | RT-058 |
| RN-028 | RT-022, RT-026, RT-032, RT-056, RT-057 |
| RN-029 | RT-004, RT-005, RT-013, RT-014, RT-015, RT-016, RT-017, RT-020, RT-021, RT-027, RT-029, RT-030, RT-034, RT-035, RT-039, RT-040, RT-048, RT-049, RT-051, RT-054, RT-059, RT-060, RT-062, RT-063, RT-064 |
| RN-030 | RT-004, RT-012, RT-016, RT-023, RT-039, RT-040, RT-045, RT-049, RT-050, RT-051, RT-052 |
| RN-031 | RT-016, RT-017, RT-051, RT-064 |
| RN-032 | RT-064 |
| RN-033 | RT-004, RT-005, RT-009, RT-045, RT-048, RT-053, RT-055, RT-058, RT-059, RT-060, RT-061, RT-062, RT-063, RT-064 |
| RN-034 | RT-006, RT-009, RT-013, RT-055, RT-064 |
| RN-035 | RT-001, RT-002, RT-003, RT-006, RT-007, RT-008, RT-009, RT-018, RT-021, RT-044, RT-057 |
| RN-036 | RT-065 |

## 7. Critérios gerais de aceite técnico

- O requisito técnico deve possuir evidência verificável por teste, inspeção de artefato, observabilidade, runbook ou homologação, conforme sua natureza.
- Gates locais não substituem HML, Produção, RIPD, calibração ou governança quando o requisito depender desses ambientes/atos.
- Uma aprovação de runtime vale somente para o commit/release e ambiente documentados; alteração funcional relevante exige nova evidência.
- Teste alterado para refletir o contrato correto deve continuar provando o comportamento, sem simplesmente remover a asserção que detectou o defeito.
- Falha de componente de infraestrutura deve ser distinguida de falha funcional da aplicação; recuperação automática não pode mascarar erro de negócio/DDL.

## 8. Fora de escopo deste documento

- Reescrever a Especificação Técnica v3.62 ou redefinir regra normativa.
- Fixar SLA definitivo de Produção antes das medições de HML.
- Aprovar RIPD, retenção de Produção, regras setoriais ou calibração probabilística dependente de dados reais.
- Substituir runbooks, ADRs, contratos JSON/OpenAPI, DDL ou documentação de operação detalhada.
- Transformar Possibilidades em decisão administrativa, direito ou fato ocorrido.

## 9. Controle de versão

| Versão | Data | Síntese | Incorporação |
|---|---|---|---|
| 1.0 | 03/09/2026 | Baseline inicial de 65 requisitos técnicos, com rastreabilidade aos 36 Requisitos de Negócio v1.0 e à Especificação Técnica v3.62. | Solution Engenharia v3.97 |
| 1.1 | 03/09/2026 | Reclassificação documental: RT passa a ser explicitamente a camada de realização de RF/RNF; conteúdo técnico dos 65 RT preservado e matriz completa externalizada. | Solution Engenharia v3.98 |
