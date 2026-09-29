# DP-01 — Desenvolvimento paralelo por superfícies independentes

**Estado:** diretriz de desenvolvimento. **Destino:** .NET 10 LTS (DT-02); não criar etapa intermediária em .NET 9.

## Regra

Mudanças sem sobreposição de superfície, contrato instável ou dependência de execução **devem avançar simultaneamente**, em branches e PRs independentes. A ordem de merge é determinada por prontidão dos gates, não pela ordem de abertura. Nenhuma frente deve aguardar outra sem dependência documentada.

## Superfície e declaração obrigatória de PR

Cada PR informa: (1) módulos/arquivos alterados; (2) contratos públicos, esquema SQL, migrations e interfaces afetados; (3) dependências de outras PRs; (4) testes específicos e gates transversais; (5) riscos de concorrência, rollback e documentação atualizada. Alterações em CI compartilhada, versões centrais de pacotes, contratos, migrations e scorer são superfícies compartilhadas até prova de independência.

## Frentes paralelas iniciais

- **DT-02 / .NET 10:** SDK, TargetFramework, pacotes e compatibilidade de build; coordenar atualizações Dependabot e execução DT-14 antes de declarar paridade do scorer.
- **RF-052 / referência IBGE:** concluir a PR #602, migração em banco legado, concorrência de troca da referência, testes SQL/C# e gates de GENERATE_DRAFT; não ativar modelos legados sem revalidação explícita.
- **Avaliação sintética:** corpus e gabarito independentes, famílias reservadas, métricas por estrato e conferência matemática; não alterar o scorer durante TEST congelado.
- **Trilha 4:** reavaliação temporal de identidades e regressão multi-ondas; mudanças no contrato de identidade exigem sincronização com outras frentes.
- **Documentação e diagramas:** podem evoluir em paralelo, desde que descrevam comportamento implementado ou sinalizem claramente o estado proposto.

## Coordenação e merge

1. Usar branches independentes a partir da master mais recente. PRs empilhadas apenas quando houver dependência real; declarar a relação explicitamente.
2. Não editar simultaneamente o mesmo contrato, migration, arquivo de configuração central ou regra de negócio sem designar uma PR proprietária e publicar um contrato de integração.
3. Executar testes locais específicos durante o desenvolvimento; GitHub Actions executa os gates obrigatórios. Aprovação parcial não equivale a aprovação da PR.
4. Integrar PRs prontas assim que os checks exigidos, revisões, segurança e compatibilidade estiverem satisfeitos. Não forçar merge de draft, conflitos ou falhas. Após cada merge, verificar a master e revalidar PRs que dependam da superfície alterada.
5. Se conflitos repetidos revelarem acoplamento estrutural, abrir refatoração pequena com fronteira e testes próprios. Não bloquear todas as frentes aguardando uma refatoração global.
6. Registrar no plano de desenvolvimento a matriz de frentes, dependências e estado dos gates; atualizar documentação na mesma PR que altera o comportamento.

## Critério de independência

Duas PRs podem seguir em paralelo quando suas alterações não se sobrepõem e seus contratos consumidos permanecem estáveis. Arquivos distintos não garantem independência se ambos mudarem o mesmo esquema, protocolo, algoritmo, formato persistido ou pipeline de publicação. A ausência de conflito Git não substitui teste de integração.

## Consolidação aprovada em 29/09/2026 — integração central

A frente **integradora única** é proprietária de `.github/workflows/`, versões centrais de pacotes, migrations/manifests compartilhados e contratos transversais. As demais frentes evoluem superfícies exclusivas; conflitos de contrato passam pela integradora. A ordem é por prontidão real dos gates, com DT-10 por último. Não reiniciar Actions ainda ativas por timeout de consulta: acompanhar o mesmo run e verificar seus jobs. O GitHub não tem auto-merge nativo habilitado neste repositório; a integradora pode executar merge pela API somente após validar HEAD, gates aplicáveis, revisões, conflitos e dependências.

**IBGE:** bootstrap inicial carregado uma vez, com integridade e proveniência auditáveis; calibrações posteriores derivam da ingestão da Jornada, sem exigir snapshot ativo a cada `GENERATE_DRAFT`. Preservar os contratos sintéticos atuais; contratos reais serão carregados no Ensaio. Preparar DEV isolado sem executar Ensaio integral até prontidão técnica; nunca resetar `JornadaLocal` ou sua referência preservada.

**Documentação:** correções exclusivamente Markdown aprovadas pelo mantenedor podem ser aplicadas diretamente à `master`, sem aguardar a CI de código, após revisão de coerência e confirmação do commit. Isso **não** significa que um workflow GitHub existente deixará de disparar: otimização de triggers/path filters é alteração separada de CI, proprietária da integradora. Fluxos: [blocking e seleção de registro](Fluxos_Blocking_Selecao_Registro.md). Acompanhar [issue #614](https://github.com/lucianox777/Jornada/issues/614).

**Decisões adicionais de 29/09:** a frente integradora deve preservar o IBGE como insumo de **bootstrap único para parâmetros nominais e dedução dos índices/passes iniciais de blocking**, evoluindo posteriormente com distribuições ingeridas da Jornada. O modelo de observação/identidade aceita NULL em **todos os atributos**, inclusive CPF, nome, mãe, nascimento e `codigoPessoaOrigem`; somente o contrato versionado da Secretaria estabelece requisitos de admissão específicos. Validar migrations, contratos, API, Processor, projeções e testes de casos com ausência parcial/total antes de declarar implementação. Ver [fluxos detalhados](Fluxos_Blocking_Selecao_Registro.md).
