# Mapa de módulos — produto, operação e andaime de engenharia

**Objetivo:** impedir que harnesses, ferramentas de conferência e utilitários de engenharia sejam confundidos com runtime do produto.

## Natureza desta Solution

A árvore `Solution/` é uma **implementação de referência funcional executável** da Jornada: congela contratos, invariantes, comportamento esperado, testes de aceite e decisões de arquitetura suficientes para validar a solução.

Ela **não atribui ao repositório nem ao desenvolvimento de referência a responsabilidade pela operação produtiva municipal**. A engenharia de produção, implantação, operação, integração à infraestrutura corporativa e sustentação do sistema produtivo pertencem à **PRODAM**, conforme os instrumentos institucionais/contratuais aplicáveis. A SGM/SEPE mantém as decisões institucionais sobre a camada integrada; cada Gestor permanece responsável por seus sistemas e dados de origem.

Consequentemente, `Produto/runtime` abaixo significa **runtime da implementação de referência**, não declaração de que o mesmo executável, topologia, segredo, scheduler ou mecanismo de autenticação deva ser levado sem adaptação para HML/PRD.

## Critério

- **Produto/runtime:** participa da ingestão, processamento, identidade, Linkage operacional, serving/API ou operação necessária do sistema.
- **Operação:** executa manutenção/coordenação necessária em ambiente real, mas não decide regra de domínio por conta própria.
- **Andaime/evidência:** ensaio, conferência, avaliação independente, verificação ou tooling usado para provar o produto; não recebe autoridade operacional por estar no mesmo repositório.

## Projetos C#

| Projeto | Classe | Responsabilidade |
|---|---|---|
| `Jornada.Api` | Produto | Porta de entrada REST, autorização e ingestão/controle. |
| `Jornada.Contracts` | Produto | Contratos compartilhados e tipos canônicos. |
| `Jornada.Ingestion` | Produto | Inspeção/validação de pacotes de entrada. |
| `Jornada.Processor.Worker` | Produto | Bronze→Silver, identidade determinística/progressiva e publicação factual. |
| `Jornada.Bronze.Storage` | Produto | Persistência externa content-addressed da Bronze. |
| `Jornada.Pipeline.Coordination` | Produto | Coordenação/locks/leases do pipeline. |
| `Jornada.Operational.Sql` | Produto | Acesso SQL operacional compartilhado. |
| `Jornada.Linkage.Core` | Produto | Comparadores, Fellegi–Sunter e contratos do núcleo. |
| `Jornada.Linkage.Runner` | Produto condicionado | Execução do Linkage; ativação real continua sujeita a #31. |
| `Jornada.Linkage.Parameters.Worker` | Produto condicionado | Geração/validação/promoção governada de parâmetros/modelos. |
| `Jornada.Resultado.Api` | Produto | Superfície de resultados/consulta conforme autorização. |
| `Jornada.Bronze.Maintenance.Worker` | Operação | Manutenção governada da Bronze. |
| `Jornada.Operations.Maintenance.Worker` | Operação | Rotinas operacionais/maintenance. |
| `Jornada.Bronze.Verify` | Andaime/evidência | Verificação de integridade e provas operacionais; não é writer de domínio. |
| `Jornada.Ensaio` | Andaime/evidência | Orquestra ensaios técnicos; não substitui scheduler/runtime institucional. |
| `Jornada.Linkage.Conference` | Andaime/evidência | Segundo scorer/policy independente para conferência; proibido como resolvedor operacional. |
| `Jornada.Linkage.Evaluation` | Andaime/evidência | Export, avaliação e evidência estatística/técnica; não publica vínculo. |
| `Jornada.Linkage.SyntheticCorpus` | Andaime/evidência DEV | Gera corpus sintético determinístico e pode materializá-lo em pacotes via `Jornada.Ingestion`; truth permanece sidecar DEV. Nunca é dependência do Parameters.Worker/Runner nem entra no publish de Produção. |

## Outras áreas

| Área | Classe | Regra |
|---|---|---|
| `Solution/database` | Produto + contrato executável | DDL/migrations/procedures/triggers do runtime SQL Server. |
| `Solution/config` | Produto/configuração governada | Contratos e parâmetros versionados. |
| `Solution/openapi` | Contrato | Superfície pública executável/documentada. |
| `Solution/bi` | Produto analítico | Serving/BI; não decide identidade. |
| `Solution/scripts` | Andaime/operacional | Bootstrap, gates e materialização; script não vira regra de domínio por conveniência. |
| `Solution/tests` | Evidência | Prova regressão/invariantes; fixtures não são dado real. |
| `.github/workflows` | Supply chain | CI/release; não equivale a autorização institucional. |
| `Documentos` | Norma/documentação | Especificação, requisitos, anexos e revisões. |

## Fronteiras proibidas

1. `Jornada.Api` não vira Processor paralelo.
2. Conference/Evaluation/Ensaio não escrevem vínculo operacional para “facilitar teste”.
3. Scripts não duplicam algoritmo de identidade já canônico em C#/SQL.
4. BI/QC observam e classificam; não promovem modelo nem corrigem Pessoa.
5. Um harness verde prova engenharia dentro de seu escopo, não homologação populacional ou autorização de Produção.
