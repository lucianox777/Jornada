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

## Decisões de integração de 29/09/2026 — issue #614

- **IBGE, somente bootstrap inicial:** carregar uma vez conforme as regras vigentes na primeira inicialização; persistir parâmetros e metadados auditáveis. A partir daí, calibrar exclusivamente com as ingestões governadas. RF-052 verifica integridade e compatibilidade da primeira carga; não deve exigir referência IBGE ativa, recarga ou consulta nominal para cada GENERATE_DRAFT posterior. A regressão precisa separar inicialização sem bootstrap de operação posterior ao bootstrap.
- **Contratos:** preservar os contratos sintéticos atuais durante desenvolvimento e CI; usar contratos reais no Ensaio somente após prontidão técnica. Preparar scripts e checklist DEV, mas não condicionar merge ao ambiente DEV privado, nem executar limpeza no JornadaLocal.
- **Propriedade por superfície:** a frente integradora é proprietária exclusiva de CI, manifest/migrations compartilhados, versões centrais de pacotes e contratos transversais; reconcilia as mudanças compartilhadas da #602 com #607. #602 cuida da RF-052 e worker, #611 dos testes de processo real após #602, #607 da migração .NET 10 e #610 da documentação IBGE. A #609 mantém a matriz do Plano; alterações de arquivo compartilhado só pela frente integradora ou após transferência explícita.
- **Integração:** habilitar merge automático apenas para PR não draft, sem conflitos, com todas as verificações obrigatórias no HEAD exato, revisões e segurança satisfeitas. Revalidar dependentes após cada merge; não contornar falhas nem considerar checks de commits anteriores. DT-10 fica por último. A issue #614 é o registro de coordenação.
