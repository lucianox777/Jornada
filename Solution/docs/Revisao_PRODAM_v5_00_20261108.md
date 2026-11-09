# Revisão contextual de PRODAM — especificação v5.00 (GOV-DOC-01)

**Escopo:** `Documentos/Especificacao_Tecnica_Jornada_v5.00_Visao_Unificada_SQL_Fabric_Alternativo_2026-10-29.md`. **Corte pré-alteração:** `afbefd4c47aa217b83465364548499e400c8ed2a`. **Data de revisão:** 08/11/2026 BRT (09/11 UTC). **Autoridade:** mandato superveniente de fechamento do PO. **Estado:** alterações **propostas em branch**, só integradas após merge e checks; não há validação contratual ou jurídica implícita.

## Método, resultados e alcance

Varredura contextual *case-insensitive* da ocorrência literal `PRODAM`, incluindo o nome composto, no arquivo Markdown original da `master`: **31 ocorrências em 31 linhas distintas**. Conferência por linha com o arquivo modificado: **23 neutralizadas**, **8 mantidas fundamentadamente**, **8 ocorrências remanescentes**; número de linhas do arquivo mantido (**3.256**). Contagens abrangem este arquivo Markdown, **não** todo o repositório, PDFs, binários, migrações SQL ou instrumentos contratuais. Não foi feita substituição global nem modificação de semântica técnica, `plan.json`, `REF`, migrations seladas, enum legado ou licença.

A distinção aplicada é entre **requisito funcional/arquitetural da Jornada** e **designação administrativa/contratual de operador**. Referências históricas/negativas foram mantidas. Requisitos de segurança, segredo, SQL, auditoria, governança, disponibilidade e aprovação **não foram relaxados**. Referências a `ENRIQUECIMENTO_PRODAM` são dados históricos e permanecem literais.

## Matriz integral das 31 referências originais

| Linha original e atual | Classe | Decisão | Trecho da referência original | Justificativa e impacto |
| ---: | --- | --- | --- | --- |
| 25 | HISTÓRICA / METADADO | Manter | `Operador de tecnologia: PRODAM-SP` | Identifica o operador declarado na edição histórica; não fixa o runtime à contratação. |
| 35 | PÚBLICO-ALVO | Neutralizar | `Destina-se a equipes técnicas da PRODAM-SP, equipes técnicas e fornecedores de software dos Gestores finalísti` | Equipes destinatárias não são requisito de fornecedor. |
| 65 | NEGATIVA FUNCIONAL | Manter | `• Referência Territorial da Pessoa, com natureza classificada e snapshot histórico de Distrito/Subprefeitura: ` | Registra que inexiste chamada online à entidade; não cria dependência. |
| 95 | ADMINISTRATIVA / LEGAL | Manter | `\| Lei Federal 14.133/2021, art. 75, IX \| Contratação direta da PRODAM-SP \|` | Referência a modalidade de contratação; preserva contexto, sem impor contratação ao produto. |
| 108 | RESPONSABILIDADE LGPD | Neutralizar | `\| PRODAM-SP \| Operador de dados na acepção da LGPD. Implementa e opera a plataforma. \|` | A função de operadora decorre da atribuição formal e do instrumento jurídico, não do código. |
| 134 | HISTÓRICA / CRONOGRAMA | Manter | `• a PRODAM-SP avançará na implementação da plataforma de integração;` | Redação histórica de previsão de atuação institucional. |
| 368 | ARQUITETURA / SCHEDULER | Neutralizar | `A arquitetura não exige microserviços estritos. Jornada.Api, Jornada.Processor.Worker, Jornada.Operations.Main` | Agendamento deve ser homologado para o ambiente, não depender de fornecedor específico. |
| 372 | ARQUITETURA / SCHEDULER | Neutralizar | `A arquitetura não exige microserviços estritos. Jornada.Api, Jornada.Processor.Worker, Jornada.Operations.Main` | Mesma exigência de independência de fornecedor na versão duplicada da arquitetura. |
| 564 | OPERAÇÃO / SCHEDULER | Neutralizar | `O executável aceita parâmetros de linha de comando e deve ser acionado pelo scheduler corporativo homologado p` | Runbook e gate SQL permanecem; a homologação é da governança da infraestrutura. |
| 1378 | LEGADO SQL / ENUM | Manter | `ORIGEM_GEOGRAFIA,        # ORIGEM (ENRIQUECIMENTO_PRODAM apenas legado histórico)` | `ENRIQUECIMENTO_PRODAM` é valor histórico de upgrade, não modo operacional novo. |
| 1705 | GOVERNANÇA / RESPONSABILIDADES | Neutralizar | `A SGM/SEPE é a área requisitante. O Programa Reencontro coordena o projeto e a articulação institucional, sem ` | Atribuições operacionais dependem da designação vigente, sem exclusividade nominal. |
| 2003 | NEGATIVA FUNCIONAL | Manter | `Jornada valida e persiste; não existe chamada Pessoa-a-Pessoa à PRODAM na Fase 1` | Fluxo declara ausência de consulta externa; não estabelece integração ao fornecedor. |
| 2009 | LEGADO SQL / ENUM | Manter | `• natureza_referencia_territorial e origem_geografia são dimensões independentes. A natureza é declarada pela ` | Valor histórico já persistido; trocar literal quebraria compatibilidade/auditoria. |
| 2200 | SEGURANÇA / CREDENCIAIS | Neutralizar | `A implementação concreta da chave pode usar API key corporativa, HMAC, certificado ou outro mecanismo aprovado` | Autorização do mecanismo pertence à infraestrutura aprovada do ambiente. |
| 2275 | SEGURANÇA / HML | Neutralizar | `Segredos e tokens nunca são gravados em arquivos de remessa, logs, HTML de relatório, pacotes, CI pública nem ` | Aceite e inventário cabem à governança e operação designadas, mantendo todos os controles. |
| 2283 | RESPONSABILIDADE LGPD | Neutralizar | `Gestores finalísticos como controladoras dos respectivos dados; PRODAM-SP como operadora; encarregado municipa` | Não presumir papel legal de operadora sem designação formal. |
| 2350 | GOVERNANÇA / OPERADOR | Neutralizar | `O Programa Reencontro coordena o projeto e sua articulação institucional, sem papel operacional na Jornada. A ` | Executante da plataforma definido por instrumento, não por hardcode institucional. |
| 2470 | OPERAÇÃO / SCHEDULER | Neutralizar | `A Solution mantém Jornada.Pipeline.Coordination como biblioteca sem executável coordenador. Agendamento, recor` | Scheduler aprovado por governança ambiental, independente da contratada. |
| 2725 | MATRIZ / ATRIBUIÇÕES | Neutralizar | `\| 9 \| Primeiros Tipos de Benefício e Serviço + contratos JSON Schema \| Gestores / SPE / PRODAM \|` | Planejamento do contrato de benefícios por gestores, SPE e operação designada. |
| 2726 | MATRIZ / ATRIBUIÇÕES | Neutralizar | `\| 10 \| Convenção final das interfaces QC e IPossibilityEvaluator \| PRODAM / Arquitetura \|` | Desenho técnico de interfaces não é propriedade de um fornecedor. |
| 2729 | MATRIZ / SLA | Neutralizar | `\| 13 \| RPO/RTO/SLA definitivos para eventual Produção \| PRODAM / Governança \|` | RPO/RTO/SLA decididos com governança e operação designada, sem metas fictícias. |
| 2730 | MATRIZ / GEO | Neutralizar | `\| 14 \| Catálogo técnico oficial de Distrito/Subprefeitura e política de referenciaMalha a serem fornecidos/v` | O catálogo territorial é fornecido/validado pelos gestores e governança territorial. |
| 2731 | MATRIZ / IMPLANTAÇÃO | Neutralizar | `\| 15 \| Planejamento operacional da primeira ingestão cadastral de Pessoas por algum Gestor (contrato, volume` | Planejamento depende de gestores, SPE e operação designada. |
| 2732 | MATRIZ / SEGURANÇA | Neutralizar | `\| 16 \| Mecanismo corporativo de Código + chave, armazenamento/validação, rotação, revogação e rate limiting ` | Identidades corporativas são acordadas com segurança/infraestrutura e operação designada. |
| 2734 | MATRIZ / IA | Neutralizar | `\| 18 \| Desenho futuro do módulo de validação independente com IA \| SPE / PRODAM / Governança \|` | Desenho futuro é da governança e operação, sem exclusividade comercial. |
| 2735 | MATRIZ / CONTRATOS | Neutralizar | `\| 19 \| Schemas cadastrais iniciais por Gestor e política de projeção de Pessoa para credenciais de Gestor. \` | Schemas e projeções são definidos por gestores e governança, operação executa. |
| 2737 | MATRIZ / SLA DE DADOS | Neutralizar | `\| 21 \| SLA real de recebimento por Tipo/versão: monitorar_atraso, prazo_recebimento_dias e marco_atraso_codi` | Contrato de SLA por tipo pertence aos gestores e governança com execução designada. |
| 2738 | MATRIZ / PROXY | Neutralizar | `\| 22 \| Topologia de gateway/balanceador e lista de proxies confiáveis por ambiente para normalização segura ` | Gateway e proxies confiáveis são responsabilidade da operação e segurança designadas. |
| 2739 | MATRIZ / SIGILO | Neutralizar | `\| 23 \| Homologar quais Tipos reais de Serviço são casas-abrigo-sigilosas e podem ativar origina_endereco_cas` | Homologação de serviços sigilosos permanece com SMADS/gestores e operação designada. |
| 3069 | LEGADO SQL / ENUM | Manter | `• Origem da Geografia — na Fase 1, a classificação Distrito/Subprefeitura usada pelo pipeline é fornecida pela` | Identificador de proveniência persistida para replay; não produzir em novos fluxos. |
| 3163 | MATRIZ / SLA | Neutralizar | `\| 16–17 — Operação e RNF \| Cronograma, SLA, qualidade de serviço, níveis de disponibilidade e suporte. \| Di` | PO/governança operacional definem os SLAs reais após medição HML. |

## Validação e limitações

- Inspeção textual feita comparando **cada uma das 31 linhas** contra a `master afbefd4c`; nenhuma linha sem a palavra procurada foi editada, e a contagem de linhas permaneceu idêntica.
- Não se pode afirmar que dependências externas de HML/PROD, autenticação corporativa ou obrigações legais tenham desaparecido. As referências mantidas preservam contexto e valores legados; os textos neutralizados tornam o requisito tecnológico independente da pessoa jurídica.
- A issue [#378](https://github.com/lucianox777/Jornada/issues/378) conserva decisão externa de autenticação e separação ambiental. Esse requisito continua **PENDENTE FORA DE DEV** até designação e integração efetivas.
- As 51 menções editoriais nos oito PDFs identificadas na [#1344](https://github.com/lucianox777/Jornada/issues/1344) permanecem **PENDENTES / P2**, sem edição ou exclusão. A neutralização de binários não pode ser inferida desta revisão Markdown.
- **Critério de conclusão deste arquivo:** PR com diff exato, verificações documentais do HEAD e integração via proteção de `master`; sem isso, classificar `NOT RUN` para teste do conjunto integrado.

**Rastreabilidade:** [mandato de fechamento](Decisao_V9_Fechamento_Controlado_20261108.md), [relatório a–m](Relatorio_V9_Fechamento_Integracao_20261108.md), [índice GOV-DOC-01](Indice_Decisoes_Vigentes.md). Histórico preservado no Git, sem reescrita.
