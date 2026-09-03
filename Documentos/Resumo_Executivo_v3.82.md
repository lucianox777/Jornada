# Nova Jornada do Cidadão - Resumo Executivo

**Base Normativa:** v3.62  
**Solution Engenharia:** v3.82  
**Data:** 2 de setembro de 2026

## 1. Estado técnico

A Nova Jornada do Cidadão encerrou a etapa **Build + Unit** como fronteira principal de engenharia. A evidência registrada no ciclo atual é de **99/99 testes Unit aprovados**. A próxima incerteza técnica está onde mocks e revisão estática não bastam: procedures, constraints, transações, locks, concorrência e consistência final no SQL Server real.

## 2. Nova fronteira: Integration

A suíte Integration passa a utilizar **SQL Server 2022 real**, de forma automatizada e isolada. Localmente, Testcontainers sobe uma instância descartável; em CI/HML, a mesma suíte pode usar uma conexão SQL externa.

O objetivo é provar em execução real:

- criação e evolução do schema;
- procedures, views e funções;
- PK, FK, UNIQUE, CHECK e índices;
- COMMIT, ROLLBACK e atomicidade;
- locks, timeout e concorrência entre conexões independentes;
- idempotência das operações críticas;
- invariantes de identidade e fatos após falhas ou corrida;
- ausência de dependência em estado residual de execução anterior.

## 3. Infraestrutura v3.82

A infraestrutura usa **Testcontainers.MsSql 4.14.0** e SQL Server 2022 CU26.

Imagem padrão, fixada por tag e digest:

`mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04@sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89`

Sem `JORNADA_TEST_SQL_CONNECTION`, a suíte sobe o container e cria `JornadaIntegration_<guid>`.

Com conexão SQL externa, a v3.82 também cria, por padrão, **banco exclusivo por execução** no servidor informado. O uso literal de um banco externo exige decisão explícita:

`JORNADA_TEST_SQL_USE_EXISTING_DATABASE=true`

Esse modo é apropriado apenas quando CI/HML já fornece banco dedicado ao job.

## 4. Sessão SQL e application locks

A connection string publicada para os testes utiliza `Pooling=false`. A decisão evita reuso de sessão física em cenários que dependem de application locks de sessão.

## 5. Concorrência controlada

A suíte diferencia concorrência desejada de interferência acidental. O runner Integration é serializado; quando o objetivo é testar contenção ou corrida, o próprio caso cria múltiplas `SqlConnection`/tasks deliberadamente.

## 6. Smoke tests do Database Engine

A camada de infraestrutura prova:

- SQL Server major version 16 (SQL Server 2022);
- banco isolado e pooling desabilitado;
- rollback transacional;
- enforcement de CHECK com erro 547;
- lock real entre conexões independentes;
- LOCK_TIMEOUT com erro 1222;
- restauração do estado após rollback.

Esses testes não substituem os testes de domínio. Eles comprovam que o laboratório é um SQL Server real com comportamento compatível com as premissas da Solution.

## 7. Papel de HML/PRODAM

Testcontainers reduz a dependência de HML para demonstrar que o código de banco funciona. HML continua necessária para:

- compatibilidade com a plataforma efetivamente provisionada;
- segurança, identidade, rede e configurações institucionais;
- integração com BI e componentes externos;
- calibração e volumetria;
- performance, carga e contenção em escala;
- homologação operacional.

## 8. Critério de aceite de Integration

Integration poderá ser considerada encerrada quando:

- o banco nascer de artefatos versionados e ambiente limpo;
- a suíte rodar repetidamente sem preparação manual;
- procedures e constraints críticas tiverem testes positivos e negativos;
- falhas intermediárias provarem rollback e ausência de estado parcial;
- cenários concorrentes validarem invariantes e estado final;
- não houver Assert.Ignore mascarando precondições determinísticas críticas;
- o resultado for registrado em TRX ou equivalente;
- o mesmo conjunto de testes puder ser apontado para HML sem bifurcar a lógica.

## 9. Estado executivo

| Frente | Estado | Observação |
|---|---|---|
| Build | Encerrada | Compilação estabilizada |
| Unit | Encerrada | 99/99 testes Unit |
| Integration | Próxima fronteira | Infraestrutura SQL real incorporada; falta executar a suíte e tratar achados |
| Concorrência | Infraestrutura pronta | Colisões criadas deliberadamente por teste |
| E2E | Posterior | Após estabilização de Integration |
| Performance/Load | Posterior | HML segue importante para volumetria e contenção |

## 10. Síntese

A v3.82 muda o método de validação: a Jornada passa a testar no próprio Database Engine as garantias que sustentam identidade, fatos e correções governadas. O próximo artefato técnico relevante é a **primeira execução completa da suíte Integration contra SQL Server real**, com TRX e correção dos defeitos que essa execução revelar.
