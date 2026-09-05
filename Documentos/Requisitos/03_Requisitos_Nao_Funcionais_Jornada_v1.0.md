# Requisitos Não Funcionais - Jornada do Cidadão - Fase 1

**Versão do documento:** 1.0  
**Data:** 03/09/2026  
**Base normativa:** Especificação Técnica Jornada v3.62 - § 17  
**SolutionSchema:** SolutionSchema v3.68  
**Release de incorporação:** Solution Engenharia v3.98  
**Status:** BASELINE RNF CANÔNICO DA FASE 1

> Os 33 requisitos abaixo preservam o conteúdo normativo da Seção 17 da Especificação Técnica v3.62. A categorização e as relações com RN/RF/RT são organizacionais e não alteram sua semântica.

## 1. Finalidade

Separar atributos de qualidade, restrições e propriedades transversais dos comportamentos funcionais (RF) e das escolhas de realização (RT).

## 2. Requisitos Não Funcionais

### RNF01 - Plataforma

**Requisito normativo.** Todo código C# da Fase 1 deve compilar para net8.0 com C# 12 definido centralmente.

**Rastreabilidade de negócio.** RN-001, RN-035

**Rastreabilidade funcional.** RF-049

**Realização técnica principal.** RT-001

### RNF02 - Plataforma

**Requisito normativo.** SQL Server é a única tecnologia relacional normativa da Fase 1.

**Rastreabilidade de negócio.** RN-003, RN-035

**Rastreabilidade funcional.** —

**Realização técnica principal.** RT-002

### RNF03 - Capacidade

**Requisito normativo.** A API deve suportar payloads de até 10.000 Pessoas e 10.000 Registros por lote, com limite físico de corpo configurável.

**Rastreabilidade de negócio.** RN-002, RN-023, RN-030

**Rastreabilidade funcional.** RF-001, RF-003

**Realização técnica principal.** RT-012

### RNF04 - Confiabilidade

**Requisito normativo.** Escrita deve ser idempotente por cliente + Idempotency-Key.

**Rastreabilidade de negócio.** RN-003, RN-023, RN-029

**Rastreabilidade funcional.** RF-007

**Realização técnica principal.** RT-015

### RNF05 - Identidade/Privacidade

**Requisito normativo.** PESSOA_UUID usa UNIQUEIDENTIFIER aleatório, sem derivação de CPF.

**Rastreabilidade de negócio.** RN-005, RN-006, RN-030

**Rastreabilidade funcional.** RF-012, RF-019

**Realização técnica principal.** RT-023

### RNF06 - Confiabilidade

**Requisito normativo.** O Processor deve ser reiniciável sem duplicar materializações.

**Rastreabilidade de negócio.** RN-003, RN-012, RN-023, RN-029

**Rastreabilidade funcional.** RF-024

**Realização técnica principal.** RT-020

### RNF07 - Resiliência

**Requisito normativo.** Falha de enriquecimento geográfico não bloqueia Pessoa/Registro; ausência é reprocessável.

**Rastreabilidade de negócio.** RN-015, RN-016, RN-023, RN-024

**Rastreabilidade funcional.** RF-029, RF-031, RF-032

**Realização técnica principal.** RT-041

### RNF08 - Privacidade/Logs

**Requisito normativo.** Logs não podem conter payload integral nem identificadores civis desnecessários.

**Rastreabilidade de negócio.** RN-029, RN-030

**Rastreabilidade funcional.** —

**Realização técnica principal.** RT-049

### RNF09 - Observabilidade

**Requisito normativo.** HML deve possuir health check/heartbeat para componentes residentes e telemetria/status de execução para o Jornada.Linkage.Runner run-once.

**Rastreabilidade de negócio.** RN-023, RN-024, RN-033

**Rastreabilidade funcional.** RF-047

**Realização técnica principal.** RT-053

### RNF10 - Compatibilidade

**Requisito normativo.** Views serving.v_bi_* são contratos estáveis com a equipe Power BI.

**Rastreabilidade de negócio.** RN-025, RN-035

**Rastreabilidade funcional.** RF-041, RF-042, RF-049

**Realização técnica principal.** RT-044

### RNF11 - Configuração/Segurança

**Requisito normativo.** O PBIP deve abrir usando parâmetros de servidor/banco, sem credenciais versionadas.

**Rastreabilidade de negócio.** RN-025, RN-030, RN-033

**Rastreabilidade funcional.** RF-042

**Realização técnica principal.** RT-045

### RNF12 - Testabilidade

**Requisito normativo.** Testes de integração devem exercitar o DDL/seed reais de SQL Server.

**Rastreabilidade de negócio.** RN-003, RN-023, RN-029, RN-033, RN-035

**Rastreabilidade funcional.** —

**Realização técnica principal.** RT-002, RT-059

### RNF13 - Compatibilidade/Versionamento

**Requisito normativo.** Mudança incompatível de JSON Schema, API ou view Serving exige versionamento formal.

**Rastreabilidade de negócio.** RN-014, RN-025, RN-035

**Rastreabilidade funcional.** RF-003, RF-041, RF-042, RF-045, RF-049

**Realização técnica principal.** RT-008, RT-044

### RNF14 - Integridade semântica

**Requisito normativo.** A consulta de Registros e a de Possibilidades devem permanecer semanticamente e fisicamente distinguíveis.

**Rastreabilidade de negócio.** RN-011, RN-012, RN-013

**Rastreabilidade funcional.** RF-022, RF-026, RF-027, RF-036, RF-037

**Realização técnica principal.** RT-031, RT-038

### RNF15 - Escalabilidade

**Requisito normativo.** O Parameters Worker deve processar corpus populacional em streaming/agregação sem requerer carregamento integral em memória.

**Rastreabilidade de negócio.** RN-023, RN-024, RN-028, RN-035

**Rastreabilidade funcional.** —

**Realização técnica principal.** RT-057

### RNF16 - Auditabilidade/Modelo

**Requisito normativo.** Um modelo de linkage novo só pode substituir o ativo por transição de estado auditável.

**Rastreabilidade de negócio.** RN-014, RN-024, RN-029

**Rastreabilidade funcional.** RF-017, RF-045

**Realização técnica principal.** RT-029

### RNF17 - Configuração/Segurança

**Requisito normativo.** Autenticação e secrets devem ser substituíveis por configuração de HML sem recompilar aplicações.

**Rastreabilidade de negócio.** RN-029, RN-030, RN-033

**Rastreabilidade funcional.** —

**Realização técnica principal.** RT-004

### RNF18 - Manutenibilidade

**Requisito normativo.** A Solution não deve depender de componente histórico não incluído em Jornada.sln.

**Rastreabilidade de negócio.** RN-001, RN-035

**Rastreabilidade funcional.** RF-049

**Realização técnica principal.** RT-003

### RNF19 - Manutenibilidade/Paralelização

**Requisito normativo.** O projeto deve permitir paralelização das frentes por contratos definidos no primeiro mês.

**Rastreabilidade de negócio.** RN-033, RN-034, RN-035

**Rastreabilidade funcional.** RF-049

**Realização técnica principal.** RT-009

### RNF20 - Desempenho

**Requisito normativo.** A latência/SLA definitivo de Produção fica fora da Fase 1; HML deve medir p50/p95 e vazão para subsidiar dimensionamento.

**Rastreabilidade de negócio.** RN-023, RN-027, RN-033

**Rastreabilidade funcional.** RF-044

**Realização técnica principal.** RT-058

### RNF21 - Reprodutibilidade

**Requisito normativo.** Cada linkage_run deve capturar uma única versão imutável do modelo antes do primeiro score; nenhum run pode misturar modelo_id/modelo_versao.

**Rastreabilidade de negócio.** RN-003, RN-005, RN-024, RN-029

**Rastreabilidade funcional.** RF-006, RF-017

**Realização técnica principal.** RT-027

### RNF22 - Escalabilidade/Auditoria

**Requisito normativo.** Resultados probabilísticos históricos são append-only; para escala municipal não se persistem todos os candidatos comparados, apenas os dois melhores e a decisão por observação/run.

**Rastreabilidade de negócio.** RN-003, RN-005, RN-024, RN-029

**Rastreabilidade funcional.** RF-006, RF-017

**Realização técnica principal.** RT-027

### RNF23 - Operabilidade

**Requisito normativo.** O linkage probabilístico deve ser executado em Jornada.Linkage.Runner independente, run-once e schedulável, com modelo e parâmetros de execução congelados no linkage_run.

**Rastreabilidade de negócio.** RN-006, RN-007, RN-023, RN-028

**Rastreabilidade funcional.** RF-015, RF-016

**Realização técnica principal.** RT-026

### RNF24 - Consistência

**Requisito normativo.** A visibilidade dos resultados de um linkage_run deve ser logicamente atômica: lotes podem ser persistidos em transações independentes, mas somente status PUBLICADO alimenta identidade.v_vinculo_corrente.

**Rastreabilidade de negócio.** RN-003, RN-005, RN-024, RN-029

**Rastreabilidade funcional.** RF-006, RF-017

**Realização técnica principal.** RT-027

### RNF25 - Integridade/Fail-closed

**Requisito normativo.** Nenhum bloco de candidatos pode ser truncado silenciosamente; exceder o limite operacional deve falhar o run e exigir revisão explícita do blocking/configuração.

**Rastreabilidade de negócio.** RN-006, RN-024

**Rastreabilidade funcional.** RF-016

**Realização técnica principal.** RT-028

### RNF26 - Concorrência/Consistência

**Requisito normativo.** GENERATE_DRAFT do Parameters Worker e o Linkage Runner devem executar sob janela exclusiva do corpus integrado obtida pelo gate serial da Fase 1. A API/Bronze permanece disponível; o Processor termina o lote corrente e não inicia outro até a liberação. Não há requisito de SNAPSHOT isolation nem de ALLOW_SNAPSHOT_ISOLATION.

**Rastreabilidade de negócio.** RN-023, RN-024, RN-028

**Rastreabilidade funcional.** RF-009, RF-048

**Realização técnica principal.** RT-022

### RNF27 - Escalabilidade

**Requisito normativo.** A geração de parâmetros não pode exigir carregamento integral da Gold em memória nem persistir frequências de alta cardinalidade por modelo no baseline; profiling de escala deve ocorrer no SQL Server e m/u por amostras limitadas.

**Rastreabilidade de negócio.** RN-023, RN-024, RN-028, RN-035

**Rastreabilidade funcional.** —

**Realização técnica principal.** RT-057

### RNF28 - Concorrência/Disponibilidade

**Requisito normativo.** Recebimento e jobs analíticos podem coexistir: commits da API continuam em Ingestão/Bronze, enquanto a materialização Silver/Gold/identidade é serializada. Parameters GENERATE_DRAFT e Runner declaram intenção exclusiva antes de aguardar o lote corrente do Processor drenar, impedindo starvation por novos lotes. Os comandos SQL críticos devem possuir timeout finito e a HML deve calibrar tempo de drenagem, duração dos jobs e backlog operacional.

**Rastreabilidade de negócio.** RN-023, RN-024, RN-028

**Rastreabilidade funcional.** RF-009, RF-048

**Realização técnica principal.** RT-022

### RNF29 - Segurança/Autorização

**Requisito normativo.** Endpoints de identidade, Pessoa, Registros e Possibilidades devem autorizar pela credencial, scope e recurso antes da leitura funcional; nenhuma credencial autorizada depende de vínculo prévio da Pessoa. Cardinalidade é propriedade do endpoint e rate limits pós-autenticação usam CredentialId + classe de endpoint.

**Rastreabilidade de negócio.** RN-018, RN-019, RN-020, RN-029, RN-030

**Rastreabilidade funcional.** RF-033, RF-034, RF-035, RF-038, RF-040

**Realização técnica principal.** RT-035, RT-040

### RNF30 - Auditoria/Privacidade

**Requisito normativo.** Todo acesso individual deve gerar trilha auditável com credencial/Gestor/rota/recurso e UUID-alvo; consultas em lote devem registrar todos os UUIDs do evento sem persistir CPF, nome, payload, access key ou finalidade declarada.

**Rastreabilidade de negócio.** RN-029, RN-030

**Rastreabilidade funcional.** RF-039

**Realização técnica principal.** RT-039

### RNF31 - Segurança de rede

**Requisito normativo.** A aplicação não deve confiar diretamente em X-Forwarded-For. Forwarded Headers só pode ser habilitado com proxies explicitamente confiáveis por ambiente; sem lista de proxies, o middleware permanece desabilitado e o rate limit usa o endereço observado na conexão.

**Rastreabilidade de negócio.** RN-029, RN-030

**Rastreabilidade funcional.** RF-040

**Realização técnica principal.** RT-040

### RNF32 - Armazenamento/Durabilidade

**Requisito normativo.** Os bytes do ZIP Bronze não devem ser persistidos em VARBINARY(MAX) no banco funcional. Devem residir em armazenamento externo durável por chave lógica content-addressed derivada do SHA-256, com escrita idempotente objeto-antes-da-linha e chave relativa independente da raiz física.

**Rastreabilidade de negócio.** RN-003, RN-029, RN-030, RN-031

**Rastreabilidade funcional.** RF-008

**Realização técnica principal.** RT-016

### RNF33 - Integridade/Recuperabilidade

**Requisito normativo.** A Bronze externa deve falhar fechada quanto à integridade e ao ciclo de vida: objeto canônico existente é revalidado por SHA-256; falha temporária/corrupção na recepção retorna HTTP 503 com Retry-After; GC/expurgo físico só ocorre sob coordenação exclusiva por objeto após confirmação de zero referências e carência; todo objeto referenciado por backup SQL restaurável deve permanecer recuperável. Geradores homologados devem possuir perfil de ZIP determinístico para deduplicação de reenvios integrais inalterados.

**Rastreabilidade de negócio.** RN-003, RN-023, RN-029, RN-031

**Rastreabilidade funcional.** RF-008, RF-046

**Realização técnica principal.** RT-017

## 3. Observações de aceite

- RNF de HML/Produção dependem das evidências do ambiente correspondente; a validação local não as substitui.
- RNF de segurança e governança podem exigir evidência técnica e ato institucional.
- Mudança no texto canônico de RNF exige alteração formal da Base Normativa; este documento apenas organiza a rastreabilidade.

## 4. Controle de versão

| Versão | Data | Síntese | Incorporação |
|---|---|---|---|
| 1.0 | 03/09/2026 | Extração organizada dos 33 RNF normativos da Seção 17 da Especificação Técnica v3.62, com rastreabilidade RN/RF/RT. | Solution Engenharia v3.98 |
