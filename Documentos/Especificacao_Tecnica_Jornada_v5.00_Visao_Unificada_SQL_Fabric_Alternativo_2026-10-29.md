# Jornada — Especificação Técnica v5.00 (visão unificada)

> **Cópia textual para consulta e revisão no Git.** O arquivo Word (`.docx`) preservado na Biblioteca de arquivos do ChatGPT é o artefato de apresentação original, contendo diagramas e formatação completa. Esta transcrição preserva o conteúdo textual de referência **salvo retificações e adendos datados no Git**, não reproduz os elementos gráficos nem a diagramação do Word. Estado: candidata à aprovação institucional; sua inclusão no Git não constitui homologação. A retificação de 30/10/2026 do item 28 e o adendo sobre o agente ainda exigem incorporação ao DOCX oficial quando houver próxima edição aprovada; não alegar que o DOCX de origem foi alterado.
>
> **Origem Word:** `Especificacao_Tecnica_Jornada_v5.00_Visao_Unificada_SQL_Fabric_Alternativo_2026-10-29.docx`  
> **Biblioteca:** `/Jornada/Especificacao_Tecnica_Jornada_v5.00_Visao_Unificada_SQL_Fabric_Alternativo_2026-10-29.docx`  
> **SHA-256 da origem Word:** `0f8be8a82b15e0279ea0bd9aa7d9b75767afd27dff75df4bda981e1d5af7fe07`  
> **Consolidação:** 29/10/2026. **Observação:** a opção SQL Database in Microsoft Fabric como alternativa de hospedagem em HML/PROD é decisão expressa no documento, mas sua homologação depende do HEAD/ambiente real.

---

Especificação Técnica

Integração de Benefícios Concedidos e Serviços Prestados / Jornada

Processo SEI: 6011.2026/0000303-0
Área requisitante: Secretaria Executiva de Projetos Estratégicos (SGM/SEPE)
Operador de tecnologia: PRODAM-SP
Edição: v5.00 — Especificação Técnica Unificada
Consolidação de referência: 29 de outubro de 2026
Estado documental: candidata à aprovação formal; arquitetura e requisitos unificados

### 1. Finalidade do documento

Este documento especifica a arquitetura, os componentes, as interfaces e as regras de operação da plataforma de integração de dados de Benefícios Concedidos e Serviços Prestados da PMSP na Fase 1, preservando BENEFICIO e SERVICO como Naturezas do catálogo para evolução futura da Jornada.

Destina-se a equipes técnicas da PRODAM-SP, equipes técnicas e fornecedores de software dos Gestores finalísticos e às áreas de governança, proteção de dados e auditoria da PMSP.

Este é o documento único de especificação técnica e visão funcional corrente da Jornada, com regras, componentes, modelos, contratos, riscos, dívidas técnicas, frentes de desenvolvimento e critérios de aceite. As regras descritas como obrigatórias são o alvo de conformidade; cada entrega em aberto mantém sua condição expressa. A aprovação e a vigência institucional dependem do procedimento formal competente, não da emissão deste arquivo.

#### 1.1. Estados de decisão, evidência e implantação

DECIDIDO identifica uma regra arquitetural ou funcional aprovada; IMPLEMENTADO identifica a capacidade efetivamente presente no produto; MEDIDO identifica uma execução com artefatos e proveniência; AVALIADO identifica avaliação independente posterior à seleção automática; ATIVADO identifica uma autorização explícita de governança vinculada a ambiente, dossiê, segurança e snapshot. Um resultado de calibração não ativa o modelo. Este documento não impõe teto estatístico percentual de risco antecipado: a avaliação mostra os riscos observados para decisão posterior da governança. A política inferior V9 não está autorizada em produção na data de consolidação.

#### 1.2. Regra de interpretação integral

Este é o contrato técnico único para arquitetura, decisões atuais, responsabilidades, fluxos, DTs, frentes, riscos e critérios de aceite. Referências a arquivos de implementação e evidências servem à auditoria; não exigem outra especificação para entender uma regra. O status transitório de branches, PRs, SHAs e jobs fica somente no capítulo 26. O controle documental integra as decisões presentes, sem narrar versões superadas como alternativas vigentes.

### 2. Escopo

#### 2.1. Está no escopo

• Ingestão padronizada em envelope único de Pessoas, Benefícios Concedidos e Serviços Prestados dos Gestores finalísticos, admitindo períodos sem fato e atualizações exclusivamente cadastrais

• Resolução de identidade e manutenção de identificador canônico da pessoa

• A arquitetura Medallion utiliza uma única autoridade transacional Microsoft SQL por implantação. Em desenvolvimento e CI, a referência é SQL Server 2022 Developer/LocalDB em ambiente isolado. Para HML e Produção, são alternativas de hospedagem o Microsoft SQL Server institucional ou o SQL Database in Microsoft Fabric (serviço de banco de dados transacional), conforme seleção e homologação do ambiente. Ambos usam o contrato SQL canônico, sem bifurcar regras de negócio. OneLake, Lakehouse e SQL Analytics Endpoint não são requisitos de execução da Jornada.

• Detecção de divergências de identidade e recepção, pela ingestão, de evidência de conferência documental produzida nos sistemas dos Gestores

• Disponibilização de dados consolidados por APIs REST

• Disponibilização de serviços de consulta da Jornada para consumo por sistemas finalísticos autorizados

• Referência Territorial da Pessoa, com natureza classificada e snapshot histórico de Distrito/Subprefeitura: a origem/Gestor declara vínculo DOMICILIAR, ACOLHIMENTO_INSTITUCIONAL ou REFERENCIA_TERRITORIAL_DECLARADA e é responsável pela territorialização. ENDERECO_RESIDENCIAL e REFERENCIA_TERRITORIAL devem informar situacaoGeografia; RESOLVIDA exige Distrito, Subprefeitura e referenciaMalha, e os demais estados qualificam explicitamente a ausência. A Fase 1 não realiza chamada online Pessoa-a-Pessoa à PRODAM. Não se recria entidade autônoma Território de Referência nem API geográfica pública.

• Business Intelligence e indicadores de políticas sociais

• Governança, auditoria e conformidade com a LGPD

#### 2.2. Não está no escopo

• Substituição de qualquer sistema transacional dos Gestores

• Concessão, suspensão, cancelamento ou pagamento de benefício

• Atendimento direto ao munícipe pela SGM/SEPE

• Unificação de meios de pagamento

• Edição direta do cadastro transacional de qualquer Gestor pela Jornada; correções cadastrais permanecem no sistema de origem. A exceção é a correção governada da identidade canônica da própria Jornada, que apenas reassocia evidências/vínculos internos mediante ato e decisão institucional explícitos, sem editar a base finalística.

• Emissão ou custódia de documentos civis

A plataforma constitui camada de consolidação, referência de identidade e interoperabilidade sobrejacente aos sistemas existentes. Nenhum sistema transacional interno dos Gestores é descontinuado.

### 3. Contexto institucional e papéis

#### 3.1. Fundamento normativo

| Norma | Aplicação |
| --- | --- |
| Lei Federal 13.709/2018 (LGPD) | Proteção de dados pessoais; art. 11 para dados sensíveis |
| Lei Federal 14.534/2023 | CPF como número único e suficiente de identificação em bancos de dados de serviços públicos |
| Lei Federal 14.133/2021, art. 75, IX | Contratação direta da PRODAM-SP |
| Decreto Municipal 60.663/2021 | Competência da SGM sobre governança de dados; interoperabilidade; níveis de compartilhamento; Cadastro Base de Pessoas |
| Decreto Municipal 59.000/2019, alterado pelo 60.038/2020 | Competência da SPE para coordenação intersetorial |
| Decreto Municipal 59.767/2020, alterado pelo 65.617/2026 | Regulamentação municipal da LGPD; modelo descentralizado de encarregado |
| Resolução SGM/CCGD 2/2022 | Compartilhamento de dados entre órgãos municipais |
| Portaria SGM 121/2026 | Designação do encarregado da SGM |

#### 3.2. Papéis

| Papel | Atribuição |
| --- | --- |
| SGM/SEPE | Área requisitante; gestão contratual e governança institucional da plataforma. |
| Programa Reencontro | Coordenação do projeto e articulação institucional; sem papel operacional na Jornada, não administra credenciais e não responde pelos dados finalísticos. |
| PRODAM-SP | Operador de dados na acepção da LGPD. Implementa e opera a plataforma. |
| Gestores finalísticos | Controladoras dos dados que cedem. Decidem sobre concessão e sobre providências decorrentes de divergências. Agentes habilitados podem produzir atos de verificação presencial documental com validade municipal para os atributos abrangidos pela evidência, conforme política única da plataforma. |
| Encarregado municipal (DPO) | Supervisão de conformidade com a LGPD. |
| Procuradoria Geral do Município | Análise jurídica dos instrumentos de compartilhamento. |
| Controladoria Geral do Município | Conformidade e auditoria. |

#### 3.3. Gestores finalísticos no escopo

SMADS, SEHAB, SMDET, SME, SMT, SMS e SMDHC.

A arquitetura é extensível a novos Gestores cedentes sem alteração estrutural.

#### 3.4. Diretrizes institucionais de implantação

A implantação observará as seguintes diretrizes institucionais:

• todos os beneficiários e usuários de serviços e benefícios sociais municipais deverão possuir cadastro padronizado contendo, no mínimo, CPF, nome completo, data de nascimento e nome da mãe, observadas as regras de transição para bases legadas e situações excepcionais previstas em norma;

• cada Gestor manterá interlocutor técnico formalmente designado para integração, qualidade cadastral e cumprimento do cronograma;

• os cadastros e projetos dos Gestores serão diagnosticados e organizados por cronograma compatível com a situação de cada origem, utilizando prioritariamente os indicadores do Quality Control Plane;

• biometria e identificação facial serão incorporadas de forma evolutiva, inicialmente nos serviços e benefícios com atendimento estável e infraestrutura adequada, conforme regras de proteção de dados, segurança, consentimento/base legal quando aplicável e atos normativos específicos;

• biometria e identificação facial constituem mecanismos de verificação/autenticação de identidade e não substituem o núcleo cadastral de CPF, nome completo, data de nascimento e nome da mãe, nem integram o núcleo Golden como atributos civis;

• a PRODAM-SP avançará na implementação da plataforma de integração;

• resoluções e demais atos necessários à padronização, governança, compartilhamento e procedimentos operacionais serão expedidos pela SGM conforme competência institucional.

#### 3.5. Referência metodológica

A camada de resolução de identidade tem como referência o SDLE — Social Data Linkage Environment, da Statistics Canada, particularmente quanto a dois princípios:

1. Separação entre depositário de identificadores e dados de análise. No modelo canadense, os identificadores pessoais residem no Derived Record Depository, e as chaves de ligação em um Key Registry apartado dos dados analíticos.

2. Combinação de ligação determinística e probabilística para constituição e manutenção do depositário.

Ambos são incorporados nesta especificação nos itens 7 e 8.

### 4. Visão geral da arquitetura

┌───────────────────────────────────────────────────────────────┐
│ CAMADA 1 — Sistemas transacionais dos Gestores                │
│ Pessoas, Benefícios Concedidos e Serviços Prestados           │
└────────────────────────────┬──────────────────────────────────┘
                             │ APIs JSON
                             ▼
                         Jornada.Api
                             │
          ┌──────────────────┴──────────────────┐
          ▼                                     ▼
  Bronze externo                         BANCO OPERACIONAL SQL
  ZIP SHA-256 imutável                   Silver / Gold / identidade
                                         controles / auditoria / Serving
                                         ├─ DEV/CI: SQL Server local
                                         └─ HML/PROD (alternativas):
                                            SQL Server institucional OU
                                            SQL Database in Fabric
                                                 │
                                                 ▼
                                      APIs / Workers / Power BI
                                      (consultas Serving autorizadas)

A arquitetura mantém separados o cadastro transversal e os fatos administrativos, mas ambos possuem representação canônica na Camada Ouro. A Gold Pessoas consolida a referência cadastral da pessoa. A Gold Registros consolida os fatos já ocorridos de Benefícios Concedidos e Serviços Prestados que tenham contrato transversal explícito na Jornada, preservando origem, Tipo, temporalidade, identidade estável no sistema de origem e particularidades de cada Natureza.

A Gold Unificada de Pessoas, denominada neste documento simplesmente Gold Pessoas, contém duas classes de atributos:

1. Núcleo preferencial de identidade: CPF, nome civil, data de nascimento e nome da mãe, todos representáveis como ausentes; somente o contrato versionado de cada fonte define obrigatoriedade de ingresso. O linkage não pode inferir valores inexistentes nem decidir apenas por igualdade de nome.

2. Atributos transversais, que não pertencem naturalmente a um único Gestor, como endereço, telefone e e-mail, promovidos à Gold somente quando houver comprovação válida segundo a política do atributo. O catálogo declara cardinalidade SINGLE ou MULTI e a regra de chave de instância. Na Fase 1, TELEFONE_CONTATO e EMAIL_CONTATO são MULTI; valores normalizados distintos podem permanecer simultaneamente vigentes.

Benefícios Concedidos e Serviços Prestados não são atributos da Gold Pessoas. Depois de validados quanto ao contrato e à regra factual, são materializados na Gold Registros independentemente do estado da resolução de identidade. O sujeito declarado da origem e o CPF declarado, quando existente, são preservados na versão factual; PESSOA_UUID representa somente a atribuição canônica corrente e pode estar ausente.

Regra de acesso: os Gestores não recebem acesso direto de leitura ou escrita às tabelas físicas da Gold Pessoas. O Pessoa é comum às credenciais GESTOR, BENEFICIO e SERVICO que possuam os scopes aplicáveis, sem exigência de observação ou fato prévio da Pessoa. Credenciais de Tipo continuam limitadas ao próprio código de recurso e ao pessoa.schema.json autorizado. O serviço CPF→UUID é capacidade de resolução de identidade e pode ser usado por qualquer tipo de credencial que receba explicitamente jornada.identidade.resolve; ele não constitui acesso direto à Gold. Restrições setoriais são exceções negativas da projeção e nunca barreiras de existência da Pessoa.

Pessoa. A fronteira está formalizada por serving.v_pessoa e contém PESSOA_UUID, CPF, status do CPF, nome completo, data de nascimento, nome da mãe, fontes_distintas, estado_concordancia e atualizado_em. Esse conjunto constitui a identificação municipal compartilhada por padrão entre credenciais autorizadas e não é reduzido por restrições de projeção. Atributos transversais, Registros e Possibilidades permanecem fora desse núcleo e podem ser reduzidos por exceção versionada do Gestor responsável. Alterar o conjunto do núcleo exige mudança explícita da view e desta especificação.

#### 4.1. CPF, UUID, Gold e evidência

O CPF válido é a âncora determinística primária de identidade. A PESSOA_UUID é identificador técnico interno estável, sem derivação ou codificação do CPF; o mapeamento CPF→UUID é protegido, versionado e auditável. CPF inexistente no mapa pode constituir identidade; CPF conhecido somente recebe nova observação após o controle de consistência do núcleo (nome civil, nascimento e, quando disponível, mãe). Divergência material ou impossibilidade de confrontar o núcleo geram conflito explícito, sem reaproveitamento silencioso do UUID.

Fluxo operacional: (1) fonte e contrato são validados; (2) CPF válido segue resolução determinística e trava de consistência; (3) ausência de CPF admitida no contrato segue para observação pendente e, quando elegível, para o Runner V9 com modelo ATIVO; (4) blocking multipasse recupera identidades candidatas sem truncamento silencioso; (5) Rank pontua identidades com o único scorer FS C# e TF calibrado; (6) Resolve aceita, abstém ou indica conflito por identidade e por observação, considerando margem entre identidades; (7) a publicação transacional atualiza vínculo corrente, com evento e versão de evidência. Um processo pendente não exige recadastro nem perda de fato administrativo.

A Gold Pessoas é a representação municipal corrente, revisável e com evidência, não determinação civil definitiva. Cada valor guarda proveniência, estado, data e tipo de evidência. Nenhuma Secretaria tem precedência por ser a primeira fonte. Para os atributos comprovados por documento, a ordenação é documento com data de expedição mais recente, documento com data anterior, autodeclaração mais recente e autodeclaração mais antiga, respeitando o contrato de evidência, a vigência e os conflitos; a data do atendimento não substitui a data do documento.

Vinculação por CPF não recebe score probabilístico artificial. A V9 utiliza LLR/posterioridade e fronteiras superior e inferior versionadas; um valor isolado de score, inclusive 1,0, não constitui certeza de identidade, requisito universal de publicação ou condição suficiente de autorização individualizada. A regra de consumo é dada por identidade resolvida, proveniência, contratos, escopos e políticas de uso.

A identidade tem ciclo de vida progressivo. O UUID não muda por simples atualização cadastral. Fusão histórica mantém alias e sucessor canônico; separação e correção exigem decisão governada e ledger imutável. CPF tardio pode alterar a assinatura semântica do vínculo, sempre com preservação de observações e resultado anterior para auditoria. Não há adjudicação manual obrigatória de pares probabilísticos.

Benefícios e Serviços validados são materializados na Gold Registros mesmo quando a identidade está pendente ou em conflito. Atribuição canônica, existência do fato e elegibilidade de consulta são três dimensões distintas. O resultado da atribuição factual está explicado integralmente no item 8.15.

#### A Gold Unificada é constituída evolutivamente, sem exigir base-verdade externa prévia. A primeira fonte que individualize uma Pessoa já materializa a Gold. CPF válido sem associação ativa constitui um PESSOA_UUID; CPF já conhecido reutiliza o UUID somente quando o núcleo recebido não aciona a trava de divergência forte. Observação em CPF_COMPARTILHADO_SUSPEITO permanece fora da Gold até regularização/evidência posterior. Registros excepcionais sem CPF entram como NAO_RESOLVIDO/PENDENTE_PROBABILISTICO no processamento normal e podem ser submetidos ao score probabilístico operacional quando uma execução sob demanda for acionada com modelo ATIVO.

A primeira fonte integrada estabelece uma Gold válida para os registros individualizados. O estado BASELINE_FONTE_UNICA descreve somente que há uma única fonte independente comparável naquele momento; não significa staging, quarentena, dado provisório ou espera por segunda fonte. Corroboração passa a existir apenas quando outra fonte independente comparável é incorporada.

A primeira fonte integrada estabelece o baseline inicial da Gold para os registros que puderem ser individualizados dentro daquela origem. Isso não representa corroboração independente nem prova de qualidade: representa apenas o ponto de partida versionado, com provenance explícita.

PRIMEIRA ENTREGA ZIP v2 ELEGÍVEL DE UM GESTOR
          ↓
POST /api/v1/ingestao/entregas
          ↓
ZIP: manifest.json + pessoas.jsonl + registros.jsonl (admite registros.jsonl vazio)
          ↓
validação do contrato cadastral versionado + normalização + individualização por CPF válido
          ↓
CPF novo: constitui UUID | CPF conhecido: verifica consistência do núcleo antes do vínculo
          ↓
UUIDs iniciais / GOLD BASELINE somente para identidades RESOLVIDAS
          ↓
Parameters Worker gera estatísticas populacionais e parâmetros m/u sobre corpus Gold imóvel durante janela exclusiva + vínculos CPF_DETERMINISTICO resolvidos

Quando uma segunda e as demais fontes independentes são incorporadas, seus registros são ligados aos UUIDs existentes ou originam novos UUIDs. A partir desse momento, a plataforma mede a concordância entre fontes para cada atributo comparável.

Exemplo simplificado para o atributo NOME:

SMADS   JOSE SILVA
SMS     JOSE SILVA
→ 2 de 2 fontes concordam com o valor Golden = 100%

SMADS   JOSE SILVA
SMS     JOAO SILVA
→ 1 de 2 fontes concorda com o valor Golden = 50%

Com novos Gestores, a medida evolui da mesma forma. A concordância é um indicador de convergência, não uma votação soberana para escolher a verdade cadastral. Evidência documental comprovada pode prevalecer sobre o consenso estatístico e provocar recálculo das divergências das demais fontes.

Devem permanecer conceitualmente distintos:

• score de linkage probabilístico — força estatística da associação registro de origem → PESSOA_UUID no caminho LINKAGE_PROBABILISTICO, produzido por execução identificada e versionada do Jornada.Linkage.Runner para registros sem CPF; não se aplica à rota determinística por CPF;

• concordância de atributo — proporção de fontes independentes comparáveis que concordam com o valor Golden vigente;

• corroboração — quantidade de fontes independentes que sustentam o valor vigente;

• evidência comprovada — ato ou fonte aceita pela política municipal capaz de qualificar ou corrigir o valor Golden.

Quando houver apenas uma fonte independente para um atributo, a concordância independente é registrada como não aplicável / ainda não mensurável, e não como 100% de qualidade. Se o Gestor piloto possuir múltiplos Benefícios ou integrações com cadastros próprios, a mesma métrica pode ser calculada no âmbito intra-Gestor desde o início.

#### 4.3. Plano de Controle de Qualidade e Observabilidade (Quality Control Plane)

Os controles de qualidade dos dados e do próprio processo de integração são componentes fundacionais da plataforma e constituem a primeira capacidade funcional a ser desenvolvida. Não são um painel de auditoria acrescentado ao final do projeto. Devem existir antes da formação operacional da Gold, dos indicadores de política pública e das APIs de consumo finalístico.

O Quality Control Plane é transversal às camadas Bronze, Prata e Ouro e deve permitir responder, continuamente, a quatro perguntas distintas:

1. Qualidade da ingestão: o Gestor está entregando os dados na frequência prevista, no formato contratado, com volume e atualidade conhecidos?

2. Qualidade da Pessoa: os quatro atributos nucleares — CPF, nome completo, data de nascimento e nome da mãe — apresentam completude, coerência e comportamento estatístico adequados?

3. Qualidade do linkage: a plataforma está conseguindo relacionar observações de Pessoa entre benefícios e Gestores com comportamento estatístico conhecido e monitorado?

4. Qualidade do Registro: a versão do Tipo possui regras declaradas, implementação de Quality Control associada e resultados auditáveis para os Benefícios ou Serviços recebidos?

QUALITY CONTROL PLANE
INGESTÃO: frequência, volume, atualidade, contratos e erros. IDENTIDADE: CPF determinístico, evidência V9, blocking multipasse, Rank/Resolve, pendências e cobertura. REGISTROS: regras QC versionadas, fatos e estados de atribuição. PRODUTOS: painéis de qualidade da Jornada e SGM/SEPE. Os controles são transversais e antecedem a publicação de indicadores de política pública.

##### 4.3.1. Controles mínimos obrigatórios

| Domínio | Controles mínimos |
| --- | --- |
| Ingestão e estrutura | entrega esperada/recebida; atraso; última entrega válida; idade do dado; schema; domínios; duplicidades; quarentena; rejeição; integridade ZIP/CRC; variação anormal de volume |
| Profiling estatístico da Pessoa | frequência de nomes, sobrenomes, nomes de mãe, datas de nascimento, tokens e combinações; cobertura, validade e duplicidade de CPF; valores raros, ausência e padrões de coocorrência/interação |
| Coerência intra-Gestor | comparação da mesma Pessoa entre benefícios/integrações do mesmo Gestor; divergência por atributo e benefício; colisões de CPF; mudanças abruptas de representação |
| Coerência inter-Gestores | concordância e divergência entre Gestores para observações ligadas ao mesmo PESSOA_UUID; quantidade de fontes independentes corroborantes |
| Temporalidade | data_referencia de cada snapshot; idade do dado; defasagem entre Gestores/benefícios; vigência da versão do benefício e das regras aplicadas |
| Referência Territorial | cobertura de referência territorial; natureza DOMICILIAR/ACOLHIMENTO_INSTITUCIONAL/REFERENCIA_TERRITORIAL_DECLARADA; consistência Distrito→Subprefeitura; distribuição territorial; SEM_REFERENCIA_TERRITORIAL; evolução temporal da cobertura e proibição de inferência por local de atendimento |
| Linkage | cobertura por PESSOA_UUID; percentual CPF_DETERMINISTICO; pendências PENDENTE_PROBABILISTICO; percentual LINKAGE_PROBABILISTICO; NAO_RESOLVIDO; CONFLITO; distribuição de scores segmentada por modelo_versao; fronteiras superior/inferior V9; margem; linkage_run_id/tipo/status; duração e contagens por run; taxa de reavaliação/replay. |
| Gold | baseline de fonte única; número de fontes corroborantes; concordância por atributo; evidência comprovada; histórico de alteração; divergências abertas |
| Quality Control do Registro | documento de regras em linguagem natural; versão e vigência; implementação QC por Tipo/versão; status IMPLEMENTADO ou NAO_IMPLEMENTADO; cobertura de validação específica; VALIDO, DIVERGENTE e NAO_VERIFICAVEL; regras com maior incidência de divergência |
| Operação | latência; tempo de processamento; replay; falhas; disponibilidade; backlog e tendência dos indicadores de qualidade |

O profiling da Gold e das observações Silver fornece dados de cobertura, frequência, qualidade e estrutura dos blocos. O Calibrador V9 usa referências IBGE no bootstrap sintético clean, parâmetros m/u e TF com suporte e proveniência, e recebe dados reais por snapshots congelados para recalibração somente solicitada pelo operador. A ausência de modelo calibrado não impede CPF determinístico, ingestão ou materialização de fatos. Nenhuma frequência de IBGE é presumida distribuição conjunta pessoa+mãe+nascimento.

##### 4.3.2. Ordem obrigatória de implantação

Para cada novo Gestor ou Tipo de Registro, a sequência mínima é:

1. cadastro do Gestor, Tipo e versão vigente
          ↓
2. documento de regras + implementação QC por Tipo/versão
   (IMPLEMENTADO ou NAO_IMPLEMENTADO)
          ↓
3. contrato de dados + ingestão + controle de frequência
          ↓
4. profiling estatístico da Pessoa
          ↓
5. coerência intra-Gestor entre registros/integrações
          ↓
6. linkage / resolução de identidade
          ↓
7. concordância inter-Gestores, quando houver fontes independentes
          ↓
8. atualização da Gold e métricas de corroboração
          ↓
9. QC específico do Registro e produtos analíticos

A ausência de fonte independente não impede o baseline do primeiro Gestor, mas impede interpretar sua autoconsistência como corroboração externa.

##### 4.3.3. Gate de qualidade para produtos de consumo

Nenhum indicador individualizado ou produto de política pública é homologado sem permitir identificar a origem, Gestor/Tipo, data de referência, atualidade, qualidade do contrato, completude de CPF, cobertura de blocking, métodos CPF_DETERMINISTICO e V9, estados ATRIBUIDA/PENDENTE_IDENTIDADE/CONFLITO_IDENTIDADE, política/modelo, evidência de avaliação e cobertura do QC específico. Os indicadores exibem denominadores, estratos, universo e limitações. Distrito e Subprefeitura só são exibidos se houver referência territorial classificada e declarada, com cobertura.

Quando o QC de uma versão estiver em NAO_IMPLEMENTADO, a implementação padrão poderá retornar tecnicamente VALIDO para permitir processamento, mas esses registros não podem ser apresentados como concessões validadas contra as regras do Tipo. O BI deve exibir separadamente a cobertura de QC específico implementado.

O Módulo de Qualidade e Convergência Cadastral descrito no item 12 é a superfície analítica desses controles. Seus mecanismos de coleta, cálculo e persistência devem ser entregues antes do BI de políticas públicas.

#### 4.4. Arquitetura de implementação da Fase 1 e stack tecnológica

A Fase 1 adota componentes independentes, uma única solução C# de engenharia em .NET 10, contratos versionados e uma implementação relacional Microsoft SQL. A baseline DEV/CI usa SQL Server local; HML/Produção podem usar Microsoft SQL Server ou SQL Database in Microsoft Fabric, por configuração e homologação da implantação. O sequenciamento de entrega é por gates e evidência, não por prazo fixo presumido ou maturidade inferida de uma CI.

• C# correspondente ao .NET 10 / .NET 8 (net10.0) para os componentes executáveis, bibliotecas compartilhadas e testes automatizados;

• ASP.NET Core 8 para a Jornada.Api;

• Autoridade relacional operacional única por implantação para metadados Bronze, Silver, Gold, identidade, qualidade, auditoria e Serving: SQL Server 2022 Developer/LocalDB para DEV/CI; em HML/Produção, Microsoft SQL Server institucional OU SQL Database in Microsoft Fabric, como alternativas de banco transacional. A escolha de hospedagem não altera regras de negócio, modelo de dados ou DDL canônico; o uso de OneLake/SQL Analytics Endpoint não é necessário.

• Microsoft Power BI, com fonte de desenvolvimento em PBIP + TMDL + PBIR, para os dashboards da Fase 1;

• JSON + JSON Schema como padrão único de intercâmbio e validação estrutural entre Gestores e plataforma.

Todos os projetos C# devem herdar TargetFramework=net10.0 e LangVersion=12.0 de configuração central da Solution. Dependências devem ser explicitamente versionadas e atualizações de runtime não podem ocorrer de forma implícita entre ambientes.

A integração dos Gestores é API-first e API-only na arquitetura normativa da Fase 1. Os sistemas transacionais e os frontends de atendimento permanecem sob domínio dos Gestores finalísticos; a Jornada não fornece frontend transacional, portal individual nem aplicação central de atendimento. O sistema finalístico registra a alteração em sua própria base e publica a observação correspondente para a Jornada por integração confiável e idempotente. Quando a alteração nasce de uma transação de atendimento, a forma recomendada é gravar o dado setorial e o evento de outbox na mesma transação local, com publicação posterior à Jornada; não se exige nem se recomenda dual-write síncrono entre a base finalística e a base da Jornada.

A Solution de implementação possui quatro componentes executáveis centrais, duas bibliotecas compartilhadas e um projeto de testes. Não existe gerador de chaves na Solution; as credenciais sintéticas de Development são fixtures pré-geradas e versionadas, carregadas somente nesse ambiente:

Jornada.sln
│
├── src/
│   ├── Jornada.Contracts
│   ├── Jornada.Pipeline.Coordination
│   ├── Jornada.Bronze.Storage
│   ├── Jornada.Bronze.Maintenance.Worker
│   ├── Jornada.Operations.Maintenance.Worker
│   ├── Jornada.Ingestion
│   ├── Jornada.Api
│   ├── Jornada.Processor.Worker
│   ├── Jornada.Linkage.Parameters.Worker
│   └── Jornada.Linkage.Runner
├── tests/
│   └── Jornada.Tests
├── database/
│   ├── Jornada_Fase1.sql
│   ├── Jornada_HML_Observabilidade.sql
│   └── Jornada_Seed_Dev.sql
├── config/
│   ├── contracts/
│   ├── catalog/atributos-transversais.json
│   └── security/test-access-keys.json   # somente Development
├── tests/fixtures/ingestao/
├── bi/
│   ├── Jornada.pbip
│   ├── Jornada.Report/
│   └── Jornada.SemanticModel/
└── docs/
    ├── API.md
    ├── Runbook_Operacao.md
    └── Bronze_Operacao.md

A pasta docs/legacy-examples pode preservar exemplos históricos apenas para consulta técnica. Seu conteúdo é não normativo, não integra a Jornada.sln e não define canal suportado, requisito de implantação ou contrato vigente.

A arquitetura não exige microserviços estritos. Jornada.Api, Jornada.Processor.Worker, Jornada.Operations.Maintenance.Worker, Jornada.Linkage.Parameters.Worker e Jornada.Linkage.Runner são unidades executáveis independentes, com responsabilidades delimitadas e implantação separável. Jornada.Pipeline.Coordination é biblioteca interna compartilhada pelos processos que coordenam o corpus; não possui executável e não agenda jobs. Jornada.Bronze.Storage abstrai o ZIP imutável para API/Processor/manutenção. O Runner opera run-once quando acionado; scheduler corporativo homologado pela PRODAM define recorrência. Todos compartilham a mesma autoridade SQL Server relacional por contas segregadas e mínimo privilégio. A configuração de conexão/autenticação não bifurca o domínio. API e Processor compartilham o mesmo armazenamento Bronze durável.

##### 4.4.1. Princípio de serviços independentes

A arquitetura não exige microserviços estritos. Jornada.Api, Jornada.Processor.Worker, Jornada.Operations.Maintenance.Worker, Jornada.Linkage.Parameters.Worker e Jornada.Linkage.Runner são unidades executáveis independentes, com responsabilidades delimitadas e implantação separável. Jornada.Pipeline.Coordination é biblioteca interna compartilhada pelos três processos de retaguarda que coordenam o corpus; não possui OutputType executável e não agenda jobs. Jornada.Bronze.Storage abstrai o armazenamento imutável do ZIP e é compartilhada por API/Processor/manutenção. O Runner é run-once e schedulável; o agendamento, recorrência e encadeamento pertencem ao scheduler corporativo homologado pela PRODAM. Todos compartilham a mesma persistência relacional operacional Microsoft SQL por contas técnicas segregadas e menor privilégio; em HML/Produção o alvo preferencial é ambiente analítico Fabric quando homologado e, em Development/CI, SQL Server 2022 Developer. A diferença de ambiente é resolvida por configuração de conexão/autenticação, sem bifurcação das regras de negócio. API e Processor também compartilham a mesma raiz Bronze durável em HML/Produção.

Bronze, Prata e Ouro são estados/camadas dos dados, não aplicações. Na Bronze, o SQL preserva a referência, hash, tamanho, content-type e lineage; os bytes do ZIP residem em armazenamento externo imutável. O Processor executa o pipeline normal e a resolução determinística por CPF; não executa score probabilístico massivo. Na Fase 1, a materialização Silver/Gold é globalmente serial: cada lote do Processor possui o gate do corpus e GENERATE_DRAFT/Runner podem tomar janela exclusiva. A API e a recepção/persistência Bronze ficam fora desse gate. Geração de parâmetros pertence ao Parameters Worker e execução de scores/replays pertence exclusivamente ao Jornada.Linkage.Runner.

##### 4.4.2. Fluxo técnico canônico

##### 4.4.3. Banco relacional Microsoft SQL: SQL Server ou SQL Database in Microsoft Fabric

A Jornada.Api permanece a porta lógica e normativa de entrada e de consulta da plataforma. Sistemas finalísticos não recebem permissão de escrita direta no banco operacional; a API aplica autenticação, autorização, contratos, idempotência e auditoria, enquanto Processor, Linkage e Workers executam suas responsabilidades por contas técnicas segregadas e de menor privilégio.

Em HML e Produção, a autoridade relacional transacional da Jornada pode ser hospedada em (a) Microsoft SQL Server institucional ou (b) SQL Database in Microsoft Fabric. As opções são alternativas, não bancos concorrentes: cada implantação seleciona um único banco operacional, fonte de verdade de Silver, Gold, identidade, qualidade, auditoria e Serving. A API e os Workers compartilham a implementação C#, o OperationalSqlAdapter, Microsoft.Data.SqlClient, contratos, DDL e procedures; diferenças de conexão, autenticação e operação pertencem à configuração e à homologação do ambiente. O SQL Database in Fabric é o serviço de banco de dados, não o SQL Analytics Endpoint.

DEV, testes ordinários de integração e CI utilizam SQL Server 2022 Developer, Testcontainers ou LocalDB descartável, com DDL canônico e o mesmo código Microsoft.Data.SqlClient; a edição Developer não designa licença produtiva. A compatibilidade funcional com SQL Database in Microsoft Fabric foi exercitada em ambiente real na linha de engenharia v4.00: 58 de 58 testes Integration aprovados, inclusive cenário concorrente, usando o mesmo adapter e DDL. Essa evidência documenta compatibilidade funcional daquele estado, mas não constitui aferição de desempenho/custo nem homologação automática do HEAD atual; quando Fabric for escolhido para HML/Produção, os gates aplicáveis à versão implantada devem ser executados no respectivo ambiente.

O uso do SQL Database in Microsoft Fabric refere-se exclusivamente ao serviço transacional de banco de dados SQL. OneLake, Lakehouse e SQL Analytics Endpoint não integram as dependências obrigatórias da Jornada e não são alternativas ao banco operacional escolhido. Nenhuma camada analítica é necessária para que a API, os Workers, a Silver, a Gold e as procedures operem.

Segurança, identidade, credenciais, escopos, conectividade privada, autorização de leitura/escrita, recuperação, capacidade, concorrência e observabilidade devem ser demonstrados no banco efetivamente adotado em HML/Produção, seja SQL Server institucional, seja SQL Database in Microsoft Fabric. Uma evidência histórica de compatibilidade de testes não substitui a homologação operacional da implantação concreta; não criar lógica de negócio distinta por destino SQL.

### 4.5. Resiliência operacional do Processor

OneLake, Lakehouse e SQL Analytics Endpoint são recursos separados do SQL Database in Microsoft Fabric e não são exigidos pela solução transacional. Se a instituição vier a contratar alguma camada analítica, ela terá autorização, requisitos de privacidade e homologação específicos, sem alterar a autoridade do banco operacional escolhido nem a arquitetura do Linkage.

Falhas inesperadas são tratadas por retry controlado com backoff exponencial. tentativa_count é incrementado na aquisição do lease; proxima_tentativa_em impede retry imediato em loop; recuperacao_count contabiliza leases expirados recuperados. Ao atingir MaxProcessingAttempts, o Lote passa a POISON e a Entrega é tratada como QUARENTENA pelo status agregado. Erros determinísticos de pacote/contrato continuam fora dessa política: pacote inválido é REJEITADO e contrato referenciado inexistente vai diretamente para QUARENTENA.

A varredura de recuperação ocorre periodicamente, não apenas no startup. Falha do heartbeat cancela o processamento local por segurança. As métricas de tentativas, recuperações de lease e lotes POISON são expostas em serving.v_bi_qualidade_envios e no modelo semântico QualidadeEnvios. Esta evolução é interna à plataforma e não altera o envelope ZIP, os schemas enviados pelos Gestores nem a API de ingestão.

SISTEMA FINALÍSTICO DO GESTOR
  │ frontend + regras de negócio + base setorial
  │ declaração de geografia e evento de outbox
  └── Integrador/Apoio homologado → Jornada.Api
       ├── POST /api/v1/identidade/resolver
       ├── POST /api/v1/ingestao/entregas
       └── GET de Pessoa/Registros/Possibilidades autorizado
Jornada.Api → staging efêmero → Bronze SHA-256 → Jornada.Processor.Worker
  ├── valida ZIP e contrato, persiste Silver e fatos Gold
  ├── CPF confiável → âncora CPF e identidade canônica
  └── sem CPF → Linkage probabilístico V9 quando aprovado ou pendência
BANCO OPERACIONAL ÚNICO POR AMBIENTE:
  DEV/CI: SQL Server Developer/LocalDB isolado.
  HML/PROD: SQL Server institucional OU SQL Database in Microsoft Fabric.
  Mesmos contratos, DDL, procedures, adapter e regras de negócio.
Power BI: consulta às views Serving autorizadas do banco selecionado.
OneLake e SQL Analytics Endpoint não são requisitos da Jornada.

#### 4.6. Estrutura documental integrada e artefatos de engenharia

Esta especificação incorpora diretamente as regras funcionais, arquiteturais, de operação, segurança, qualidade, decisões estatísticas, dívida técnica e plano de trabalho. Códigos-fonte, esquemas, artefatos de teste, configurações, diagramas executáveis e comprovantes de CI continuam disponíveis no repositório como evidência reproduzível de implementação; não são uma segunda especificação que precise ser lida para interpretar estes requisitos.

#### 4.7. Especificação técnica dos componentes

##### 4.7.1. Convenções comuns aos componentes .NET 10

Todos os componentes C# devem observar:

• net10.0, C# correspondente ao .NET 10 e nullable reference types habilitados;

• configuração por appsettings*.json, variáveis de ambiente e secretRef, sem segredo real em repositório;

• CancellationToken nas operações de I/O e nos loops de Worker;

• HttpClientFactory para integrações externas;

• logs estruturados com correlation_id, sem CPF, nome, payload integral, corpo de requisição/resposta, headers de autenticação, token, chave ou segredo;

• métricas e heartbeat/health check compatíveis com a infraestrutura de HML;

• timestamps persistidos como datetimeoffset e tratados com timezone explícito;

• retries apenas para falhas transitórias, com backoff e limite; erro de contrato não entra em retry infinito;

• idempotência explícita para processamento, materializações e chamadas de recepção;

• serviços acessando a persistência relacional operacional Microsoft SQL por contas técnicas distintas e princípio do menor privilégio;

• testes unitários independentes de banco e testes de integração ordinários com SQL Server Developer/LocalDB descartável; quando SQL Database in Microsoft Fabric for escolhido para HML/Produção, executar no ambiente de destino os gates funcionais e operacionais aplicáveis à implantação, sem exigir serviço analítico;

##### 4.7.2. Jornada.Api

Objetivo: constituir o único ponto oficial de integração máquina-a-máquina da Fase 1, concentrando autenticação por código + chave de acesso, resolução de identidade, ingestão cadastral e de fatos, consulta de status e APIs de retorno. A Jornada.Api não hospeda frontend, páginas de atendimento ou lógica transacional dos Gestores.

Responsabilidades:

1. exigir em todos os endpoints funcionais um contexto de acesso autenticado por código + chave: normalmente Código do Gestor + chave do Gestor; no endpoint cadastral de Pessoa, admitir também as sobrecargas Código do Tipo de Benefício + chave própria daquele Benefício e Código do Tipo de Serviço + chave própria daquele Serviço;

2. resolver o contexto de acesso e aplicar o scope genérico antes de receber o corpo; na ingestão, receber o ZIP no staging configurado por IngestionStaging:RootPath com hash incremental, ler apenas o manifest.json para descobrir o recurso, autorizar Natureza/Tipo e somente então executar descompactação/validação pesada e persistência; remover o temporário no caminho normal e aplicar limpeza por idade como fallback; dado pessoal não autorizado não deve ser materializado pela borda;

3. receber, no endpoint único de Entregas, o ZIP v2 autocontido com manifest.json, pessoas.jsonl e registros.jsonl; o contexto Natureza+Tipo+versão é opcional quando registros.jsonl estiver vazio e obrigatório quando houver fatos;

4. exigir Idempotency-Key, Content-Disposition com nome canônico e limites/contratos corretos; Pessoas usam Gestor+sistema de origem+versão cadastral e fatos usam, adicionalmente, Natureza+Tipo+versão quando registros.jsonl contiver conteúdo;

5. calcular SHA-256 incremental durante a recepção, sem carregar o ZIP inteiro em byte[]; exigir correspondência com o hash do nome canônico; gravar idempotentemente o ZIP no armazenamento Bronze definitivo sob objeto_chave content-addressed; somente depois persistir ingestao.entrega/lote e bronze.entrega_arquivo com metadados, hash, tamanho e chave lógica;

6. retornar 202 Accepted para ingestão válida, sem executar linkage/QC/Gold durante a requisição HTTP;

7. fornecer estado de processamento da entrega;

8. resolver a identidade segundo o capítulo 7: CPF válido pelo caminho determinístico com trava de consistência; CPF ausente admitido por contrato registra pendência e torna-se elegível ao Runner V9 quando houver evidência e modelo autorizado. O Processor não treina, não agenda Calibrador e não devolve score ou candidatos de outro cidadão; o fato administrativo válido continua elegível à materialização independentemente da atribuição de UUID.

9. fornecer projeções cadastrais da Gold por UUID individual ou conjunto de UUIDs, retornando somente os atributos permitidos pelo JSON Schema associado à credencial apresentada; UUID absorvido por FUSAO_HISTORICA é resolvido ao sucessor canônico e a resposta preserva, em metadados, o UUID originalmente solicitado;

10. fornecer Histórico de Registros, Benefícios Concedidos, Serviços Prestados e Possibilidades Compatíveis em superfícies distintas, sempre segundo a autorização do contexto de acesso;

11. aplicar rate limiting em duas camadas: proteção bruta de borda por IP normalizado e limite institucional após autenticação, particionado por CredentialId e classe de endpoint; nenhum header público não autenticado cria partição própria. Quando houver gateway/balanceador, RemoteIpAddress só pode ser reconstituído por ForwardedHeadersMiddleware a partir de proxies explicitamente confiáveis. Auditar rota, credencial, Gestor responsável, status HTTP, duração, volume e correlation id, sem corpo, CPF do munícipe, headers de autenticação ou segredo. X-Jornada-Agente-CPF, quando informado pela finalística, é consumido antes do restante do pipeline, validado e substituído exclusivamente por HMAC na trilha restrita.

Fronteira: a API não normaliza Prata, não chama o serviço geográfico municipal, não recalcula linkage, não executa QC de negócio e não decide concessão. A Jornada também não substitui o sistema de registro do Gestor finalístico. Alterações cadastrais, concessões, atendimentos e demais atos administrativos são persistidos no sistema setorial competente; a Jornada recebe a observação para integração, histórico e análise. Essas responsabilidades pertencem ao Processor e ao Gestor finalístico.

Falhas:

• 400 — envelope ou parâmetro estrutural inválido;

• 401 — cliente não autenticado;

• 403 — cliente autenticado sem scope/recurso aplicável;

• 404 — recurso/UUID/entrega não disponível àquela política de acesso;

• 409 — conflito de idempotência ou reuso inconsistente de identificador;

• 413 — limite do ZIP excedido;

• 422 — conteúdo semanticamente incompatível com contrato ativo quando a validação puder ser feita na borda;

• 429 — limite de chamadas excedido;

• 5xx — falha técnica da plataforma, com correlation id e sem vazamento de dados sensíveis.

##### 4.7.3. Jornada.Processor.Worker

Objetivo: executar o pipeline interno determinístico desde o payload Bronze até Gold/Serving.

Entrada: lotes PENDENTE persistidos pela Jornada.Api e respectivos ZIPs imutáveis acessados pelo objeto_chave da Bronze externa.

Pipeline obrigatório:

1. reservar atomicamente o próximo lote elegível;

2. validar JSON Schema: pessoas.jsonl sempre pelo Gestor + versão do contrato cadastral; quando registros.jsonl contiver fatos, validar também cada Registro conforme Natureza, Tipo e versão declarados no manifesto;

3. materializar/normalizar observações Prata;

4. executar profiling e controles de qualidade cadastral;

5. para cada ENDERECO_RESIDENCIAL ou REFERENCIA_TERRITORIAL, exigir situacaoGeografia explícita. Quando RESOLVIDA, validar Distrito/Subprefeitura e referenciaMalha declarados pelo Gestor; quando FORA_MUNICIPIO, SEM_ENDERECO_APTO ou NAO_RESOLVIDA_ORIGEM, geografia deve permanecer nula. A plataforma nunca escolhe a natureza nem substitui automaticamente residência/referência por local de atendimento, unidade de serviço ou equipamento; o snapshot recebido é preservado historicamente;

6. resolver identidade no fluxo normal: CPF válido por IDENTITY_MAP; CPF ausente em hipótese admitida permanece PENDENTE_PROBABILISTICO, sem disparar score automaticamente;

7. gerar ou associar PESSOA_UUID, mantendo CPF-UUID versionado na IDENTITY_MAP e registrando divergências cadastrais dos demais atributos;

8. executar QC específico do Registro por Tipo/versão quando aplicável;

9. materializar Gold e Serving somente quando os gates aplicáveis forem satisfeitos, mantendo nas views operacionais apenas a versão lógica VIGENTE de cada fato;

10. para Pessoa e Registro, usar a chave estável no namespace do sistema de origem e hash canônico semântico do conteúdo. A canonicalização normaliza Unicode em NFC, representa números pelo valor (por exemplo 600 = 600.0 = 600.00 = 6e2), trata propriedade opcional null como ausência, remove campos técnicos definidos pelo domínio e ordena somente arrays declarados como conjuntos; arrays semanticamente sequenciais preservam ordem. Em Pessoa, atualizadoEmOrigem isoladamente não cria versão, enquanto mudança de evidência de negócio permanece material. Mesmo conteúdo é retransmissão idempotente; conteúdo diferente gera automaticamente a próxima versão interna, sem número fornecido pelo Gestor;

11. recalcular/materializar Possibilidades quando houver perfil ou regra aplicável alterada;

12. atualizar estados da Entrega/Lote e métricas operacionais.

A implementação de referência materializa globalmente um lote por vez, mesmo que existam múltiplas instâncias do Worker. Antes de reservar o lote, o Processor tenta obter o gate do corpus; se houver intenção exclusiva de Parameters/Runner, não reserva nada e volta ao polling. Reserva atômica, READPAST/UPDLOCK, lease/fencing, heartbeat e transação Serializable de persistência permanecem como defesas internas. O paralelismo de materialização é decisão evolutiva de fase, não mudança de contrato externo.

Idempotência em duas camadas: reprocessar o mesmo lote_id não pode duplicar materializações; e reenviar a mesma chave de origem com o mesmo conteúdo canônico não cria nova observação lógica. Para a mesma chave, conteúdo diferente cria automaticamente nova versao_interna na Jornada. Replay interno, retransmissão idêntica e nova versão lógica são estados distintos e auditáveis.

Falhas transitórias: indisponibilidade do staging, storage Bronze ou outro recurso externo deve ser tratada como falha de infraestrutura, com retry/503 conforme a superfície. Ausência territorial não é resolvida online pela Jornada: deve chegar qualificada pela origem em situacaoGeografia, permitindo medir NAO_RESOLVIDA_ORIGEM, FORA_MUNICIPIO e SEM_ENDERECO_APTO sem inventar geografia. Falhas de schema/contrato podem rejeitar o lote ou registros conforme política de atomicidade definida por contrato.

##### 4.7.4. Jornada.Linkage.Parameters.Worker

Responsabilidade: o único Calibrador V9 em C# estima, explora e congela de forma reproduzível os parâmetros do motor Fellegi–Sunter operacional. O serviço de parâmetros não lê diretamente bases finalísticas e não decide vínculos de produção. Ele consome snapshots e amostras autorizadas, com manifestos, hashes, fontes, recortes, unidades amostrais e distribuição de evidências, e publica um RASCUNHO de modelo completo para avaliação e eventual ativação governada.

Bootstrap inicial: usar uma massa sintética clean de 30.000 identidades derivada das marginais IBGE congeladas e de hipóteses sintéticas declaradas. O Calibrador determina automaticamente os parâmetros de comparação, m/u, prior, pesos de TF, nível composto nominal, passes e parâmetros de blocking, limiares superior e inferior e demais margens/tolerâncias necessárias à decisão. Não existe mínimo fixo de 5.000 pares, prior manual, peso nominal fixo ou critério obrigatório de risco percentual predefinido. A suficiência é medida por estados, estratos e grupos independentes; baixa evidência produz INCONCLUSIVO, regularização formal do desenho ou abstenção, e não valores inventados.

Sobrenomes: a frequência IBGE de sobrenome conta presença em qualquer posição divulgada, não uma posição final observada. O valor pode ser proxy inicial do último sobrenome significativo identificado por BrazilianNameComponents.LastContentSurname, com essa limitação, recorte, denominador, método de aproximação, frequência efetiva e fingerprint registrados no snapshot; o Calibrador avalia sua compatibilidade com o parâmetro u condicionado ao universo deduplicado pós-blocking. Não somar duas vezes o mesmo acordo nominal de nome integral, primeiro nome e sobrenome.

O bootstrap do aplicativo verifica e carrega o modelo/snapshot aprovado em ref; NÃO executa treinamento, não seleciona novos parâmetros nem promove o modelo. Novas calibrações só são disparadas por solicitação explícita de operador autorizado. **Três operações distintas devem permanecer separadas no contrato e na interface de governança:** (1) o operador autorizado **solicita** nova calibração, decidindo se ela será executada; (2) uma vez solicitada, o Calibrador **explora, compara, escolhe e registra automaticamente** a configuração e os melhores parâmetros reproduzíveis, inclusive limiares, sem pedir ao operador que selecione o melhor score ou imponha teto percentual a priori; (3) após a avaliação independente, a autoridade competente — que pode incluir o operador formalmente designado — **autoriza ou recusa o uso operacional** do modelo resultante em ato distinto. Nenhuma das três operações implica a aprovação tácita das outras. O diagnóstico de suficiência DT-23 é automático e somente leitura, recomenda reavaliar a necessidade de calibração quando surgem dados reais com suporte suficiente, mas nunca realiza recalibração sozinho. Cada rodada utiliza dados, rótulos, extrações e períodos versionados; evidência histórica real substitui progressivamente a evidência sintética onde houver suporte independente.

O Calibrador examina a fronteira superior recall × FDR e a inferior falsas criações × pendências em problemas distintos. Para o limiar superior usa menor distância normalizada ao ideal da fronteira; para o inferior recomenda o joelho normalizado, com faixas e finalistas de Pareto. Não se impõem tetos arbitrários de falsos vínculos, percentuais fixos de risco ou filtro ex ante para determinar quais candidatos o algoritmo pode descobrir. A avaliação independente posterior apresenta estimativas, incerteza, estratos, riscos e limitações para decisão de governança; o TEST não pode ser usado para escolher retrospectivamente parâmetros.

Escala e concorrência: os snapshots de corpus e a publicação respeitam locks e leases transacionais, sem parar a API/Bronze. Estatísticas necessárias são calculadas no banco operacional Microsoft SQL selecionado, e a extração para o Calibrador é limitada por streaming/paginação. Cada execução congela um único modelo, regras, snapshot, normalizador e ambiente; modelos históricos não são sobrescritos. As políticas de publicação somente mudam após ato explícito e auditoria técnica, com mecanismo de reversão. Não há scheduler semanal de treinamento obrigatório.

##### 4.7.5. Jornada.Linkage.Runner

Objetivo: executar incrementalmente ou por comando operacional a resolução probabilística V9 como processo independente, sem recalibrar parâmetros e sem bloquear a ingestão Bronze. O Runner aplica blocking multipasse, Rank por identidade, Resolve com limiares superior/inferior e política governada, registro de motivos de abstenção, idempotência e publicação transacional.

Parâmetros: tipo_run (ON_DEMAND, INCREMENTAL, REPLAY, FULL ou MODEL_VALIDATION), snapshot/modelo ATIVO e congelado, escopo por observação/fonte/data, batch_size, max_parallelism, correlation_id, solicitante, justificativa e política de publicação. Um modelo em preview não é automaticamente selecionável para produção; modo MODEL_VALIDATION não publica.

A execução cria linkage_run em PREPARANDO, congela modelo_id/modelo_versao, escopo e o campo pessoa_observacao_id_high_watermark e então materializa identidade.linkage_run_item antes de passar a EXECUTANDO. Novas observações recebidas pela API ficam em Bronze e não são materializadas no corpus enquanto o Runner possui a janela exclusiva; uma nova avaliação exige outro linkage_run.

Para escala de milhões de Pessoas, o Runner segura a janela exclusiva do corpus durante o run inteiro, mas não mantém uma única transação SQL de horas. A criação do cabeçalho e a materialização de identidade.linkage_run_item ocorrem em transação curta ReadCommitted, com FreezeUniverseCommandTimeoutSeconds=900. Depois, cada lote de scoring e persistência usa sua própria transação. linkage_run_item permanece obrigatório para reprodutibilidade/auditoria do universo exato, independentemente do mecanismo de isolamento. Se o comando de materialização exceder o timeout, a transação é revertida e o run não inicia; o scheduler deve reagendar após verificar carga/índices ou calibrar o valor homologado.

Atomicidade é de publicação: linkage_resultado é append-only, porém identidade.v_vinculo_corrente considera somente resultados pertencentes a runs PUBLICADO. Ao final, uma transação curta confere elegíveis, avaliados e quantidade de resultados e altera o cabeçalho do run para PUBLICADO; esse commit torna todo o conjunto visível de uma só vez.

Run FALHOU ou CANCELADO nunca altera o vínculo corrente, ainda que alguns lotes físicos tenham sido persistidos. MODEL_VALIDATION termina CONCLUIDO_SEM_PUBLICACAO. Resultados não publicados permanecem disponíveis para auditoria técnica/reexecução conforme retenção.

O executável aceita parâmetros de linha de comando e deve ser acionado pelo scheduler corporativo homologado pela PRODAM ou, em HML/incidente, manualmente conforme runbook. A Solution não contém scheduler nem processo coordenador executável; a ordem, precondições, retry e evidência operacional estão declarados em Solution/docs/Runbook_Operacao.md. O gate SQL permanece autoridade fail-closed mesmo diante de disparo indevido concorrente.

##### 4.7.6. Jornada.Contracts

Biblioteca compartilhada, sem lógica de infraestrutura, contendo DTOs, enums e interfaces estáveis da Solution: contratos de API; IAccessContextResolver; IPolicyEngine; IContractResolver; IPersonProjectionService; contratos de consultas de Registros e Possibilidades; interfaces de QC e IPossibilityEvaluator. A territorialização da Fase 1 pertence ao contrato da origem e não a uma porta de enriquecimento online. O contexto de acesso deve distinguir credencial de GESTOR, BENEFICIO e SERVICO, sem transportar a chave em objetos de domínio ou logs.

4.6.7. Jornada.Pipeline.Coordination

Biblioteca interna de infraestrutura que implementa o gate serial da Fase 1 por sp_getapplock. Não contém contratos externos nem dados de domínio. Usa conexão coordenadora dedicada, LockOwner=Session, pooling/enlistment desabilitados, heartbeat de posse, LostToken fail-closed, liberação explícita e dois recursos: Jornada.Pipeline.ExclusiveRequest para intenção/prioridade de jobs analíticos e Jornada.Pipeline.Corpus para exclusão da materialização. O Processor a usa por lote; Parameters GENERATE_DRAFT e Runner a usam pelo job inteiro.

##### 4.7.8. Jornada.Operations.Maintenance.Worker

Jornada.Operations.Maintenance.Worker é executável independente de manutenção operacional. Hospeda rotinas de retenção governadas por Enabled e watchdog estritamente observacional: identifica estagnação, lease expirado, backlog antigo e falhas, sem concluir/descartar/reprocessar lotes ou publicar modelos automaticamente. Reinícios devem preservar fencing, idempotência e recuperação.

Os limiares PipelineWatchdog:* distribuídos em appsettings.json são defaults técnicos, desabilitados por padrão, e devem ser calibrados em HML. O watchdog não tenta inferir lock órfão: application locks com owner Session são liberados quando a sessão física termina; estados de negócio/execução persistidos são a superfície observável.

Alterações incompatíveis em contratos públicos devem ser versionadas e comunicadas às frentes C#, Banco e BI.

##### 4.7.9. Jornada.Tests

Projeto NUnit com duas responsabilidades distintas:

O corpus é serializado por contrato operacional da Fase 1, adequada a entregas periódicas. Não existe requisito genérico de ALLOW_SNAPSHOT_ISOLATION para o pipeline coordenado por leases; perder sessão coordenadora equivale a perder exclusividade e deve cancelar trabalho. Se a arquitetura evoluir para paralelismo enquanto Parameters/Runner executam, o protocolo de consistência deve ser revisado formalmente, com row-versioning/SNAPSHOT ou mecanismo equivalente quando necessário.

• Integration — testes reais de procedures, constraints, migrations, controle de autorização e views sobre SQL Server isolado como baseline ordinária; a execução histórica 58/58 no SQL Database in Microsoft Fabric comprova compatibilidade funcional da linha então testada. Para HML/Produção, a homologação se aplica ao banco efetivamente selecionado, sem dependência do Analytics Endpoint.

Jornada_Seed_Dev.sql é também a massa sintética determinística que popula bancos locais de desenvolvimento e fornece dados iniciais para o Power BI, evitando duas massas divergentes.

##### 4.7.10. Power BI e conexão direta ao Serving do banco operacional

O fonte do Power BI da Fase 1 fica diretamente em `bi/`:

bi/Jornada.pbip
bi/Jornada.Report/                 # PBIR / layout do relatório
bi/Jornada.SemanticModel/          # TMDL / modelo semântico
bi/.gitignore                       # ignora .pbi/, localSettings.json, cache.abf e PBIX

O modelo semântico deve consumir exclusivamente contratos estáveis de serving.v_bi_* no banco operacional selecionado, sem exigir OneLake ou SQL Analytics Endpoint. Para desenvolvimento, os parâmetros apontam para SQL Server 2022 Developer local em Docker (`localhost,14333` / `JornadaLocal`); credenciais permanecem no estado local do Power BI Desktop e não são versionadas. Em HML/Produção, a conexão é configurada diretamente para as views Serving autorizadas do Microsoft SQL Server institucional ou do SQL Database in Microsoft Fabric, conforme a alternativa adotada e os controles de autenticação e acesso homologados.

A estrutura segue o Power BI Project: Jornada.pbip é o arquivo de entrada; Jornada.Report contém definition.pbir, report.json, pages e visuals; Jornada.SemanticModel contém definition.pbism e os arquivos TMDL. Pastas .pbi/localSettings.json, cache.abf e PBIX são locais/geradas e não são versionadas. Os arquivos PBIP/PBIR/TMDL constituem o código-fonte oficial, mas a promoção exige abrir, validar e salvar o projeto em Microsoft Power BI Desktop na versão homologada pelo ambiente municipal. Validação estrutural de JSON/TMDL em CI não substitui esse gate. A publicação no Power BI Service/Gateway e o refresh pertencem à infraestrutura corporativa.

##### 4.7.11. Git, ambiente local e rastreabilidade de release

O repositório Git é a fonte oficial dos artefatos editáveis da Jornada: código C#, DDL/seed, JSON Schemas, PBIP/PBIR/TMDL, diagramas, documentação e scripts. Releases são produzidas a partir de commit/tag identificados; o ZIP é artefato derivado e deve registrar GitCommit/GitTag em RELEASE_INFO.txt. Segredos, .env, credenciais locais do Power BI e artefatos binários PBIX/PBIT não são versionados.

O ambiente local mínimo usa SQL Server 2022 Developer em Docker Compose, com MSSQL_PID=Developer e ACCEPT_EULA=Y, exclusivamente para desenvolvimento/teste/demonstração; não constitui configuração ou licenciamento de Produção. Solution/docker-compose.yml, .env.example e scripts/local-db.* criam JornadaLocal, aplicam DDL/seed e suportam a suíte Integration sem exigir instalação local do SQL Server e sem exigir capacidade, workspace, credencial ou connection string Microsoft Fabric. API e workers podem permanecer no host para depuração; scheduler, IdP, Gateway, storage corporativo e demais dependências de HML/Produção não são simulados pelo Compose.

O procedimento local é normatizado em Solution/docs/Runbook_Desenvolvimento_Local.md; o fluxo Git/tag/release em Solution/docs/Runbook_Git_Release.md. Alterações em PBIP/PBIR/TMDL continuam sujeitas ao gate de abertura, validação e salvamento em Microsoft Power BI Desktop homologado antes da tag de release.

#### 4.8. Banco relacional, migrações e readiness: contrato corrente

##### Autoridade relacional e implantação

A solução é API-first na fronteira entre Secretarias e Jornada. O tráfego institucional ingressa por Jornada.Api e pelos contratos versionados; não existe escrita setorial direta na Silver, Gold, identidade ou tabelas do Processor. Há uma única autoridade relacional transacional por ambiente: SQL Server 2022 Developer/LocalDB em DEV/CI; Microsoft SQL Server institucional ou SQL Database in Microsoft Fabric em HML/Produção, conforme a implantação. Ambas as alternativas produtivas devem usar uma semântica funcional única, um adapter e um conjunto canônico de migrations e stored procedures. A edição Developer não constitui designação de licença para Produção.

O SQL Database in Microsoft Fabric está expressamente admitido como alternativa de hospedagem do banco transacional da Jornada em HML/Produção, não como componente obrigatório de toda implantação. O histórico de engenharia registra 58/58 testes Integration executados com êxito em banco Fabric real na linha v4.00, inclusive concorrência, usando adapter e DDL comuns; tal evidência não deve ser apagada nem confundida com homologação do HEAD atual ou de desempenho/custo de Produção. A seleção do destino determina o plano de conectividade, identidade, segurança, provisionamento e aceite correspondente. OneLake e SQL Analytics Endpoint são serviços diferentes e não são necessários para executar a Jornada.

##### DDL canônico, instalação e migrações

A fonte editável de base é Solution/database/Jornada_Fase1.sql. O artefato SQLCMD consolidado Solution/database/Jornada_Fase1_v3.70.sql envolve essa base e executa, na ordem do manifesto Solution/database/migrations/manifest.txt, as migrations requeridas. Instalar apenas Jornada_Fase1.sql não equivale ao esquema completo de um HEAD atual. O produto deve verificar, em banco descartável, a equivalência semântica entre instalação limpa, upgrade e DDL consolidado: objetos, permissões, procedures, constraints, índices, versão do schema e assinatura/fingerprint estrutural. Uma divergência entre os hashes efetivamente medidos e CANDIDATE_INFO.json bloqueia fechamento e merge; nunca atualizar o hash por suposição.

Migrations já incorporadas são imutáveis e sua ordem de manifesto permanece canônica. Correções novas são aditivas e devem ter testes físicos de instalação limpa, upgrade, compatibilidade com base legada, rollback quando previsto, idempotência e ausência de drift. Guardas de SEM_CANDIDATO e política V9 devem ser implantados sem sobrepor procedures ou regras transitórias; qualquer discrepância entre inventário, schema, test count e binários reprova o gate. PRs e runs exemplificativos estão no capítulo 26.

##### Readiness operacional

Liveness de processo não comprova readiness. A implantação deve verificar conexão SQL, versão da base, objetos e procedures esperados, disponibilidade de configurações, health de workers, store de segredos e, em HML/Produção, identidade corporativa e autorização institucional. Sob erro de identidade/segredo/contrato de schema, readiness falha fechado; não é permitido usar chave DEV, cache alternativo não autorizado ou endpoint analítico para simular banco operacional. A API e workers devem manter health, heartbeat, correlação e logs com classes de falha, preservando dados pessoais fora de logs operacionais de acesso amplo.

#### 4.9. Contratos, limites, desempenho e falha fechada

Contratos JSON Schema: novas versões vN podem ser acrescentadas com API/Processor em execução. O catálogo permanece dinâmico; validator/projeção é carregado sob demanda e cacheado por caminho/versionamento. Uma versão ATIVA guarda também o SHA-256 aprovado dos bytes de pessoa.schema.json e, quando aplicável, registro.schema.json. API e Processor verificam esse digest contra o arquivo físico e falham fechado em divergência, inclusive após restart/deploy. Correção incompatível exige nova vN; substituir silenciosamente os bytes de uma versão aprovada não é permitido.

ZIP: a API conserva a varredura integral descompactada antes do aceite como barreira contra Zip Bomb. No Processor, o preflight lê manifest/shape sem varredura cega; o teto global de 2 GiB é contabilizado na mesma passagem de leitura dos JSONL, reduzindo uma descompressão completa.

Bronze: buffers de 1 MiB usam ArrayPool<byte> e são devolvidos com clearArray=true. O staging de 64 KiB permanece fora da LOH e não é alterado por essa otimização.

Auditoria: permanece síncrona/durável; não é deslocada para Channel em memória. A API emite AuditPersistenceMs para medir p50/p95/p99 em HML antes de qualquer mudança de semântica.

Retenção: ItemProcessedRetention permanece Enabled=false e DetailRetentionDays=0 na distribuição. Habilitação sem janela positiva explícita falha no startup. Teste SQL dedicado comprova que a última RETRANSMITIDO por identidade de origem não é expurgada.

Operação: modo_carga_inicial é não preemptivo. HML deve definir threshold para orfao_mais_antigo_em e medir Pessoas/hora, CPU/descompressão, GC/LOH, I/O Bronze, SQL waits e Query Store; não há política automática de REBUILD/REORGANIZE.

Cache dinâmico/append-only de contratos versionados; parse do Processor com limite descompactado na própria passagem; ArrayPool limpo na Bronze; retenção fail-safe sem default destrutivo; teste da última RETRANSMITIDO; territorialização vigente OriginOnly; telemetria de latência de auditoria para HML.

Completa o compartilhamento municipal por padrão para todas as credenciais autorizadas, removendo a última fronteira por relacionamento prévio das credenciais de Tipo; transforma finalidade em catálogo/versionamento relacional; estrutura e valida exceções negativas de projeção; formaliza o Pessoa; aceita CPF opcional do agente declarado pela finalística, armazenando somente HMAC versionado; e minimiza serving.v_bi_api para excluir identificadores individuais.

#### 4.10. Replay determinístico e janela de concessão

ref.tipo_registro_versao representa o contrato operacional versionado do Tipo, e não apenas a versão física do JSON Schema. Alteração material de regra, inclusive regime_vigencia ou janela permitida de concessão, pode criar nova versão sem mudança de schema. O reprocessamento/replay técnico de uma Entrega usa sempre o tipo_registro_versao_id originalmente gravado; mudança normativa posterior não substitui silenciosamente o contrato histórico. Aplicação retroativa de nova norma a fatos já processados constitui reavaliação governada explícita, distinta de replay técnico.

Quando uma janela de concessão está configurada e dataInicioConcessao é nula, o QC estrutural registra NAO_VERIFICAVEL / CONCESSAO_JANELA_NAO_VERIFICAVEL_V1. No BI, concessao_na_janela_permitida fica NULL para esse caso e o percentual Concessao na Janela % usa somente linhas verificáveis no denominador; Concessao Janela Verificavel % expõe a cobertura da avaliação. Ausência de janela configurada não exige data de início para essa regra específica.

#### A fonte oficial de artefatos editáveis e de distribuição é o repositório Git institucional autorizado. Cada entrega congelada possui tag, commit imutável, manifestos de conteúdo, instalador e evidências para reproduzir o produto, sem transformar hash ou número de execução em regra de negócio.

O repositório Git passa a ser a fonte oficial dos fontes editáveis da distribuição. O pacote de release registra commit SHA e tag imutável em RELEASE_INFO.txt; MANIFESTO_ARQUIVOS.txt e SHA256SUMS.txt continuam sendo evidências geradas do artefato, não substitutos do histórico Git.

Solution/docker-compose.yml passa a oferecer SQL Server 2022 Developer exclusivamente para desenvolvimento/teste local. `.env` é local e ignorado; `.env.example` contém somente valores sintéticos. Scripts PowerShell e shell criam/resetam JornadaLocal, aplicam DDL/seed e executam a suíte completa com JORNADA_TEST_SQL_CONNECTION.

O ambiente Docker local não reproduz Produção nem altera o contrato do scheduler corporativo. Power BI Desktop continua obrigatório; os parâmetros locais do PBIP passam a `localhost,14333` e `JornadaLocal`, sem versionar credenciais.

### 5. Camada 1 — Gestores e integração por API

Os sistemas transacionais dos Gestores permanecem sob domínio setorial. A Jornada não exige tecnologia interna específica, mas exige capacidade de integração HTTPS com a Jornada.Api.

O contrato externo da Fase 1 é REST sobre HTTPS. A ingestão usa um único arquivo application/zip contendo JSON/JSONL contratados; consultas e demais operações usam JSON. Não há protocolo normativo alternativo para depósito.

Cada Gestor integrado deve possuir Código do Gestor e chave de acesso próprios. Tipos de Benefício e Tipos de Serviço que necessitem de projeção cadastral própria podem possuir credenciais próprias, sempre vinculadas ao Gestor responsável e à versão contratual correspondente. Assim, a consulta de Pessoa pode ser autorizada por Código do Gestor + chave, Código do Tipo de Benefício + chave do Benefício ou Código do Tipo de Serviço + chave do Serviço. A API valida o par código+chave e resolve o contexto institucional antes de selecionar o schema.

A integração típica possui duas direções:

Gestor ──POST──► Jornada.Api /api/v1/ingestao/entregas
  │              Código do Gestor + chave de acesso
  │              ZIP v2 único: manifest + pessoas + registros
  │              registros.jsonl pode estar vazio
  │              pessoas = incluídas/alteradas no período + todas as referenciadas pelos fatos
  │              filename canônico contém SHA-256 do ZIP
  │
  └──GET/POST──► Pessoa por UUID, Registros e Possibilidades autorizadas

Benefício autorizado ──GET/POST──► Pessoa por UUID
  │  Código do Tipo de Benefício + chave do Benefício
  └──────────────────────────────► projeção conforme pessoa.schema.json do Tipo

Serviço autorizado ─────GET/POST──► Pessoa por UUID
  │  Código do Tipo de Serviço + chave do Serviço
  └──────────────────────────────► projeção conforme pessoa.schema.json do Tipo

O cadastro padronizado continua contendo cpf, nomeCompleto, dataNascimento e nomeMae. Para identificação/interoperabilidade, o CPF é suficiente e é a chave primária externa; os demais três atributos continuam obrigatórios para qualidade, corroboração e divergência e compõem o modelo probabilístico operacional nos casos excepcionais sem CPF. cpf pode ser null apenas nas exceções controladas do item 7, acompanhado por cpfAusenteMotivo.

#### 5.1. Tipos de Benefício e de Serviço

O Tipo de Benefício e o Tipo de Serviço identificam programas/serviços concretos por código alfanumérico imutável de exatamente quatro caracteres maiúsculos [A-Z0-9], por exemplo AA01 e CRA1. BENEFICIO e SERVICO são Naturezas do catálogo factual comum ref.tipo_registro. Cada versão em ref.tipo_registro_versao aponta para os contratos aplicáveis e declara tipo_medida. Para BENEFICIO, a versão também declara regime_vigencia = PRAZO_DETERMINADO ou PRAZO_INDETERMINADO e pode declarar data_inicio_permitida_concessao/data_fim_permitida_concessao; para SERVICO, regime_vigencia = NAO_APLICAVEL e não há janela de concessão. Mudança material cria nova versão e não altera o código do Tipo.

Regra terminológica e estrutural. BENEFICIO e SERVICO permanecem as duas Naturezas do catálogo factual comum. Na Fase 1, a ocorrência ligada a uma Pessoa é denominada Benefício Concedido ou Serviço Prestado. O catálogo ref.tipo_registro continua genérico para que a Fase 2 possa tratar Benefícios Disponíveis, Serviços Disponíveis, ofertas e regras de elegibilidade sem confundir disponibilidade com fato já ocorrido. Não existe uma terceira Natureza técnica denominada Atendimento.

Mudança de regras, vigência ou contrato não altera o código; cria nova versão.

Regra de extensibilidade factual da Fase 1. A inclusão de novo Tipo de Benefício ou Serviço não cria tabela por programa: catálogo e contratos são parametrizados por ref.tipo_registro/ref.tipo_registro_versao. A Silver factual é comum e a Gold permanece especializada por Natureza. O canônico de Benefício Concedido usa data_inicio_concessao, data_fim_concessao, data_evento_concessao e valor_concedido; nomes genéricos são deliberadamente evitados para não conflitar com futuros fatos de Benefício Pago/Recebido. Campo novo só é promovido quando formalmente contratado e modelado no canônico da respectiva Natureza.

Cada versão de Tipo pode declarar monitoramento de atraso de recebimento por monitorar_atraso, prazo_recebimento_dias e marco_atraso_codigo. Valores admitidos na Fase 1: DATA_EVENTO_CONCESSAO, DATA_INICIO_CONCESSAO, DATA_FIM_CONCESSAO, DATA_HORA_SERVICO e SOURCE_AS_OF. Os três primeiros são exclusivos do vocabulário de Benefício Concedido. Alterar prazo ou marco exige nova versão ou alteração formal da versão ainda não ativada; o instante oficial recebido_em é atribuído exclusivamente pela API da Jornada no aceite da Entrega e persistido em UTC.

Para regras baseadas em calendário municipal, o instante recebido_em é convertido explicitamente para o fuso institucional de São Paulo antes de extrair a data civil (data_recebimento_sla). O indicador distingue latência total (dias_para_recebimento) de atraso líquido (dias_atraso). Registros sem o marco configurado são classificados como SEM_MARCO; marco posterior à data operacional de recebimento é DATA_FUTURA. Esses estados são problemas de qualidade e não são contabilizados como atraso.

#### 5.2. Biblioteca de Contratos de Validação

Os contratos são mantidos como fonte versionada na Solution, sem número no nome do arquivo; a versão reside no diretório:

config/contracts/gestores/SMS/pessoa/v1/
  pessoa.json
  pessoa.schema.json

config/contracts/gestores/SMADS/pessoa/v1/
  pessoa.json
  pessoa.schema.json

config/contracts/registros/AA01/v1/
  registro.json
  pessoa.schema.json
  registro.schema.json

config/contracts/registros/CRA1/v1/
  registro.json
  pessoa.schema.json
  registro.schema.json

Em config/contracts/gestores/<GESTOR>/pessoa/vN, pessoa.json descreve o contrato cadastral do Gestor e pessoa.schema.json define a entrada/projeção cadastral autorizada. Em config/contracts/registros/<CODIGO>/vN, registro.json registra Natureza, metadados e referências da versão; pessoa.schema.json define a projeção de Pessoa autorizada à credencial do Tipo; registro.schema.json define o fato da Natureza. Schemas são fechados por allowlist (additionalProperties=false).

Schema ativado é imutável. Alteração incompatível exige nova versão.

#### 5.3. Limites de responsabilidade, contratos e ingestão

##### Papéis, contratos e independência dos fatos

As Secretarias (SMADS, SEHAB, SMDET, SME, SMT, SMS, SMDHC e outras autorizadas) permanecem responsáveis por sistemas finalísticos, veracidade e correções de origem, catálogo de seus eventos e fatos. A Jornada ingere e integra Pessoas, Benefícios Concedidos, Serviços Prestados e dados complementares, mas não concede, paga, cancela ou corrige diretamente benefício transacional. O contrato versionado de cada Gestor, sistema/base, tipo factual e remessa determina campos obrigatórios, tipos, código de origem, periodicidade, datas e critérios de aceite. Não existe exigência universal de que toda Pessoa informe simultaneamente CPF, nome civil, nome da mãe e nascimento; faltas legítimas são representadas como ausência e diminuem a evidência de resolução.

A relação idPessoaEntrega conecta Pessoa e registros exclusivamente no ZIP em processamento e nunca é convertida artificialmente em identidade municipal. codigoPessoaOrigem é opcional quando permitido pelo contrato; sua ausência não autoriza inventar chave persistente, initial_uuid ou histórico da origem. codigoRegistroOrigem é obrigação do contrato versionado de cada tipo factual, não do Processor como regra global. Fatos válidos persistem materializados e rastreáveis mesmo quando a resolução canônica da Pessoa é PENDENTE, INDEFINIDA ou CONFLITO, mantendo vínculo à observação da remessa, estado de atribuição e dados de origem, sem exigir pessoa_uuid artificial. Registros futuros da mesma fonte passam por idempotência e versionamento, e não substituem fatos antigos sem a semântica contratual correspondente.

##### Pipeline e fronteiras de escrita

O integrador de cada Gestor fecha o dado finalístico e um evento de outbox na própria transação local, quando essa estratégia for adotada. A aplicação de apoio prepara o envelope. Jornada.Api valida autenticação, autorização, versão e integridade inicial, recebe o ZIP e o correlaciona a um recibo. A Bronze conserva o conteúdo bruto imutável por SHA-256, metadados de recepção, proveniência, contrato e identificadores de rastreamento. A área staging é efêmera, não uma nova fonte de verdade. O Processor valida integralmente e materializa Silver/controle; Linkage recupera candidatos e decide identidades conforme o modelo aprovado; Gold/Serving consolida visões e APIs para consumidores autorizados. Leitura analítica e BI partem das projeções permitidas e nunca substituem controles da publicação transacional.

O lease e a exclusão de execução devem ser explícitos e concorrentes de forma segura; snapshots de corpus e manifestos imutáveis são ligados ao linkage_run_id antes do score. O Worker deve processar uma vez o corpo admitido, registrar falhas por item e permitir retentativa idempotente. Falha de contrato, hash divergente, ZIP inválido e prova de proveniência insuficiente não entram em retry infinito; falham com relatório e sem publicação parcial silenciosa. Retentativas de falhas transitórias são limitadas, com backoff e limites por remessa. O histórico de ingestão e publicação é append-only onde declarado no DDL, evitando mutação de evidência original.

##### Territorialização e sigilo

A origem/Gestor declara REFERENCIA_TERRITORIAL e ENDERECO_RESIDENCIAL com semânticas distintas, natureza, situação geográfica e versão de referência; não cabe ao Processor consultar serviço geográfico online e substituir silenciosamente a declaração institucional. SEM_ENDERECO_APTO não significa SEM_ENDERECO_FIXO_DECLARADO; null não é sinônimo de nenhum dos dois. A referência selecionada para BI é derivada de regra explícita, com snapshot de malha, Distrito/Subprefeitura e tempo de vigência. Naturezas como domiciliar, acolhimento, institucional prisional, serviço de atendimento e logradouro de pernoite precisam de classificação e política de projeção. ENDERECO_CASA_ABRIGO_SIGILOSA não fornece sinal de identidade e nunca deve ser exposto como referência fina a outros Gestores, qualquer que seja a completude da malha.

#### 5.4. Aplicativo independente de Apoio às Secretarias

##### Produto, instalabilidade e separação da Solution

O Apoio às Secretarias é produto autônomo, atualmente organizado em Produtos/ApoioSecretarias, fora da Solution Jornada.sln. Deve manter fronteiras de build, configuração, distribuição, suporte e credenciais separadas do runtime central. O objetivo é um executável instalável em Windows, com interface simples e CLI correspondente, capaz de selecionar UM arquivo ou UMA pasta de arquivos de origem, aplicar mapeamento de campos parametrizado e validado, produzir sempre o pacote canônico compactado/renomeado e, apenas quando habilitado, transmiti-lo à API remota. A implementação documentada em 29/10 é protótipo funcional V0.3 em Python/Tkinter, com núcleo comum à CLI e modo container sem GUI; o conjunto não deve ser apresentado como produto institucional homologado ou instalador da Jornada.

A escolha de arquivo/pasta, perfil, saída e opção de envio é explícita. O perfil JSON versionado e aprovado por SHA-256 identifica gestor, sistemaOrigem, dataReferencia, regra de nomes, papéis das fontes, mapeamento campo-a-campo, schema Pessoa, schema factual e código/versão dos tipos. O sistema não descobre por heurística que o campo X de uma Secretaria é CPF, nome ou filiação; a semântica e a elegibilidade de campos pertencem ao contrato homologado. CSV UTF-8 e JSONL são os formatos de origem implementados no protótipo. A pasta inteira corresponde a uma remessa e todos os seus arquivos devem corresponder exatamente a uma regra do perfil; arquivos sobressalentes ou mudanças de conteúdo impedem a publicação.

##### Validação integral, publicação e fail-closed

O processamento valida primeiro a seleção completa: enumeração e integridade estáveis, arquivos previstos, UTF-8/JSONL, schema Draft 2020-12, formatos de datas, tipos, presença/ausência, campos extras/faltantes, hashes de perfil e schemas, vínculos idPessoaEntrega, consistência Pessoa×Registro, CPF e motivo de ausência exigido por contrato. Cada linha inválida gera erro localizado (arquivo, linha, classe, motivo) e impede toda a remessa; linha vazia dentro de JSONL factual produz JSONL_LINHA_VAZIA, salvo arquivo factual legitimamente vazio. O app revalida enumeração, bytes, perfil e contratos imediatamente antes de publicar, recusando ORIGEM_ALTERADA, PERFIL_ALTERADO e CONTRATO_ALTERADO. O diretório de saída não pode estar dentro da origem, para impedir autoinclusão. Originais jamais são editados.

Após validação completa, o app gera sempre e somente um ZIP autocontido e determinístico ENTREG A_<GESTOR>_<SISTEMA_ORIGEM>_v2_<sha256>.zip, com exatamente manifest.json, pessoas.jsonl e registros.jsonl na raiz; a grafia operacional do prefixo é ENTREGA_, sem espaço. registros.jsonl existe mesmo sem fatos. O conteúdo compactado é idêntico com o mesmo input/contrato, e a chave idempotente é derivada dos mesmos bytes efetivamente transmitidos. O app mantém os pacotes e relatórios JSON/HTML (inclusive execução rejeitada); reporta contagem de arquivos, linhas, papéis, hashes, códigos de erro, data, remessa e, no envio, status HTTP e recibo. Falha no envio não apaga o ZIP nem gera segunda remessa diferente.

##### Transmissão, credenciais e reenvio

A opção enviar é false por padrão. Com true no perfil ou flag CLI explícita, o app usa apenas destino HTTPS homologado, sem aceitar redirecionamentos HTTP, e envia o mesmo ZIP com X-Jornada-Gestor, X-Jornada-Access-Key e Idempotency-Key: sha256:<hash>. Segredos ficam fora do perfil, ZIP, relatório e logs, por meio de ambiente seguro ou secret manager; não devem constar da linha de comando em estação compartilhada nem de imagem Docker. A opção --nao-enviar prevalece para proteger ensaios em DEV. A indicação RECEBIDO significa aceite HTTP do envelope e não garante PROCESSADO pelo Processor, Linkage ou Gold. Reenvio deve validar novamente o ZIP e seu manifesto e transmitir os bytes já gerados, jamais recomprimir para inventar identidade nova de remessa.

##### Artefatos, deployment e critérios de aceite

O protótipo descreve geração de ApoioSecretarias.exe (GUI) e ApoioSecretariasCLI.exe (CLI) via PyInstaller Windows x64, instalador em %LOCALAPPDATA%\\Jornada\\ApoioSecretarias e atalho de área de trabalho; a desinstalação remove os binários sem apagar dados/ZIPs do Gestor. Há execução opcional em Docker sem GUI, com entrada e perfil montados read-only e saída montada separada. O README documenta 30 testes locais na V0.3, mas o build Windows, a imagem Docker, a CI do repositório independente e o envio à API real não estavam homologados nessa fotografia. Critérios para entrega institucional: repositório independente configurado, builds reproduzíveis assinados, teste no Windows alvo, segurança de credenciais, amostras reais autorizadas e contratos por Secretaria, upload HTTPS integrado em HML, rastreabilidade remessa a recibo/processamento, auditoria do mapeamento e recuperação de falhas. Nenhum teste com dados reais pode ser executado sem autorização.

### 6. Camada 2 — Recepção por API e Bronze

#### 6.1. Unidade externa de recepção: Entrega ZIP

Uma Entrega é exatamente uma chamada externa de ingestão feita por um Gestor e usa um único envelope v2, independentemente de conter ou não fatos. O ZIP sempre contém manifest.json, pessoas.jsonl e registros.jsonl. pessoas.jsonl deve conter, no mínimo, as Pessoas incluídas ou alteradas no período de referência e todas as Pessoas relacionadas aos Benefícios Concedidos ou Serviços Prestados informados na mesma Entrega. registros.jsonl pode ter zero registros: isso é válido em carga inicial/cadastral e também quando não houve fato do Tipo no período. Toda Entrega identifica codigoSistemaOrigem; pessoas.jsonl exige codigoPessoaOrigem e, quando houver fatos, registros.jsonl exige codigoRegistroOrigem. O cliente não envia número de versão, loteSeq ou loteTotal; a Jornada gera versões internas e ingestao.lote permanece detalhe interno.

Conteúdo mínimo e exato do ZIP v2:

manifest.json
pessoas.jsonl
registros.jsonl

Os três nomes são canônicos e obrigatórios. registros.jsonl pode ser um arquivo de zero bytes. manifest.json contém formatoVersao=2, pessoaSchemaVersao, codigoSistemaOrigem e dataReferencia. natureza, codigoTipo e tipoVersao devem estar todos ausentes/nulos em entrega exclusivamente cadastral sem contexto factual, ou todos preenchidos quando houver fatos; também podem estar todos preenchidos com registros.jsonl vazio para declarar explicitamente zero fatos daquele Tipo no período. codigoTipo possui exatamente quatro caracteres [A-Z0-9]. O sistema finalístico não envia versaoRegistroOrigem nem versaoPessoaOrigem.

Regra de Pessoas: o arquivo contém, no mínimo, inclusões e alterações cadastrais do período e todas as Pessoas referenciadas pelos fatos da Entrega. Reenvio idêntico é permitido e é tratado como confirmação idempotente, sem nova versão lógica.

Código do Gestor + chave autenticam a chamada. codigoSistemaOrigem identifica o sistema finalístico que atribuiu as chaves locais e deve pertencer ao Gestor autenticado. pessoas.jsonl sempre seleciona config/contracts/gestores/<GESTOR>/pessoa/vN. Quando houver contexto factual, Natureza + codigoTipo + tipoVersao selecionam também config/contracts/registros/<CODIGO>/vN; o Tipo deve pertencer ao Gestor autenticado. O ZIP não aceita subdiretórios nem arquivos extras e é preservado integralmente na Bronze.

Limites iniciais da implementação de referência: até 250 MB compactados, até 2 GB descompactados e manifest.json até 64 KB. Limites operacionais podem ser revistos sem alterar o modelo conceitual; particionamento interno do processamento não é transferido ao cliente.

Regra de versionamento independente: o sufixo v2 do envelope ZIP identifica a estrutura física de transporte (manifest.json, pessoas.jsonl e registros.jsonl) e sua política de integridade. O manifesto identifica separadamente pessoaSchemaVersao e tipoVersao, que podem ser v1 ou outra versão autorizada pelo catálogo. Um ZIP v2 pode transportar Pessoa v1 sem conversão automática de contrato; falha de schema continua fail-closed e não se admite inferir a versão do schema a partir do nome do ZIP.

#### 6.2. Endpoint de depósito

POST /api/v1/ingestao/entregas        # ZIP v2 único: manifest + pessoas + registros

Headers mínimos:

X-Jornada-Gestor: <CODIGO_GESTOR>
X-Jornada-Access-Key: <CHAVE_DO_GESTOR>
Idempotency-Key: <valor único no contexto do Gestor>
Content-Type: application/zip
Content-Disposition: attachment; filename="<NOME_CANONICO>.zip"

Código do Gestor e chave de acesso são obrigatórios e devem corresponder. O nome canônico é ENTREGA_<GESTOR>_<SISTEMA_ORIGEM>_v2_<sha256>.zip. Quando o manifesto declarar contexto factual, o código de Tipo deve pertencer ao Gestor autenticado.

A API executa validações de borda de baixo custo, valida estrutura do ZIP/manifesto, limites compactado/descompactado, recalcula o SHA-256 e confere o hash declarado no filename, gera entrega_id, persiste o ZIP imutável e agenda o processamento. Em divergência do nome canônico, a resposta é genérica e não ecoa o SHA calculado. O trabalho pesado é assíncrono no Jornada.Processor.Worker, que pode criar lotes técnicos internos.

Resposta típica:

{
  "entregaId": "...",
  "status": "RECEBIDA",
  "payloadSha256": "...",
  "bytesRecebidos": 12345,
  "recebidoEm": "..."
}

com HTTP 202 Accepted.

#### 6.3. Idempotência e integridade do payload

Idempotency-Key é obrigatório e único no contexto do Gestor autenticado. A plataforma calcula payload_sha256 sobre os bytes do ZIP recebido. O mesmo SHA-256 integra obrigatoriamente o nome canônico do arquivo; divergência entre nome e bytes rejeita a chamada antes da recepção.

Regras:

• mesma Idempotency-Key + mesmo conteúdo já aceito → devolver o recebimento existente sem duplicar processamento;

• mesma Idempotency-Key + conteúdo diferente → 409 Conflict e evento de auditoria;

• retificação, alteração ou exclusão é uma nova chamada com nova Idempotency-Key e recebe novo entrega_id; a Entrega histórica não é sobrescrita;

• lote_seq/lote_total, quando usados, pertencem exclusivamente a ingestao.lote interno e nunca são controlados pelo cliente;

• o estado da Entrega reflete processamento do pacote como unidade externa, independentemente da quantidade de lotes técnicos internos.

Identidade lógica do conteúdo: no namespace de codigoSistemaOrigem, (codigoPessoaOrigem) identifica a Pessoa na origem e (codigoRegistroOrigem) identifica o fato. A Jornada calcula hash canônico sobre o conteúdo de negócio. Mesma chave + mesmo hash = retransmissão sem nova versão lógica; mesma chave + hash diferente = próxima versao_interna. Para Registro, operacao deve ser INCLUSAO, ALTERACAO, RETIFICACAO ou EXCLUSAO. ALTERACAO preserva a história real do fato; RETIFICACAO mantém a versão anterior para auditoria, mas a retira da série analítica; EXCLUSAO declara explicitamente que o registro não deve permanecer vigente. A ausência em carga posterior nunca equivale a exclusão.

Confirmação e observabilidade: cada Pessoa e Registro processado gera uma ocorrência em ingestao.item_processado, com resultado INCLUIDO, VERSIONADO, RETRANSMITIDO, EXCLUIDO ou REABERTO. Uma retransmissão idêntica não cria nova observação Silver, mas atualiza ultima_recepcao_em da identidade de origem e confirma, de forma monotônica, a maior ultima_referencia_recebida. Isso permite medir reenvio redundante e distinguir ausência de atualização de uma confirmação recente do mesmo conteúdo.

#### 6.4. Bronze imutável

O ZIP aceito é persistido integralmente fora do banco relacional operacional Microsoft SQL em armazenamento Bronze imutável, por meio de Jornada.Bronze.Storage. A chave lógica é content-addressed e derivada do SHA-256 já validado, no formato sha256/ab/cd/<sha256>.zip; bronze.entrega_arquivo persiste explicitamente objeto_chave, payload_sha256, tamanho_bytes, nome_arquivo, content_type e recebido_em, sem VARBINARY(MAX). A escrita é objeto-antes-da-linha: o upload/gravação é idempotente e create-if-absent, valida novamente SHA-256/tamanho e publica atomicamente o arquivo no destino; somente depois a transação SQL referencia a chave. Falha SQL após a gravação pode gerar objeto órfão seguro, sujeito a coleta posterior; nunca se cria deliberadamente uma linha apontando para objeto inexistente. Em Development, RootPath relativo é resolvido contra a raiz da Solution. Em HML/Produção, BronzeStorage:RootPath deve ser caminho absoluto de armazenamento compartilhado, durável e acessível por todas as instâncias de Jornada.Api e Jornada.Processor.Worker. A chave persistida é sempre relativa e não incorpora a raiz física, permitindo mudar/montar o storage sem regravar vínculos no banco. O arquivo não é movido após processamento: permanece na mesma chave por toda a retenção; muda apenas o estado relacional da Entrega/Lote. Expurgo físico exige inexistência de referências remanescentes à objeto_chave e deve considerar coleta de órfãos.

A Bronze não é uma cópia da Gold nem sofre “correções” ou movimentações posteriores. O objeto ZIP é imutável e permanece em sua chave content-addressed por toda a retenção. Reprocessamentos geram novos estados/resultados derivados, preservando a evidência de entrada.

Integridade. PutIfAbsentAsync não aceita um objeto canônico existente apenas pelo tamanho: reabre o objeto e recalcula seu SHA-256. Divergência física de tamanho/hash é BRONZE_INTEGRIDADE_DIVERGENTE, falha de infraestrutura. Na API, indisponibilidade ou integridade divergente responde 503 Service Unavailable com Retry-After; no Processor, objeto divergente ou ausente não é classificado como erro do Gestor e entra em QUARENTENA, enquanto indisponibilidade temporária é reagendada.

Ciclo de vida. A API adquire application lock Shared por SHA-256 antes da escrita/validação do objeto e o mantém até o commit da referência em bronze.entrega_arquivo. Jornada.Bronze.Maintenance.Worker usa o mesmo recurso em Exclusive, confirma COUNT_BIG(*)=0 e só então remove objeto canônico órfão após OrphanGraceHours; temporários .tmp antigos também são coletados. Processo futuro de retenção/expurgo deve reutilizar o mesmo protocolo.

Deduplicação determinística. A chave content-addressed elimina nova gravação somente quando os bytes do ZIP são idênticos. Geradores homologados da Fase 1 devem fixar ordem e nomes das três entradas, bytes de conteúdo, timestamp canônico e versão homologada do gerador/runtime de compressão. DeterministicIngestionZipWriter é a implementação de referência e possui teste de reprodutibilidade; ZIPs arbitrários com a mesma informação lógica não são assumidos como byte a byte iguais.

Backup e restauração. Não se exige snapshot temporal idêntico entre SQL e storage; objetos extras após restaurar o banco são inofensivos. A propriedade obrigatória é que toda objeto_chave referenciada por qualquer backup SQL ainda restaurável permaneça recuperável no storage. Retenção, backup/replicação, versionamento/soft-delete/imutabilidade e testes de restore devem ser dimensionados para preservar essa propriedade.

#### 6.5. Estados operacionais

ingestao.lote, exclusivamente interno, utiliza no mínimo:

**PENDENTE
VALIDANDO
PROCESSANDO
PROCESSADO
REJEITADO
QUARENTENA**

ingestao.entrega utiliza:

**RECEBIDA
VALIDANDO
PROCESSANDO
PROCESSADA
REJEITADA
QUARENTENA**

Falha transitória pode ser reprocessada internamente sem novo protocolo do Gestor; falha de contrato exige nova Entrega/retificação. Lotes técnicos internos não são superfície pública. A completude da Entrega é derivada: somente é completa quando existem lotes 1..N sem lacunas, todos declaram o mesmo lote_total=N e todos estão PROCESSADO. A procedure ingestao.sp_recalcular_entrega propaga esse resultado para o status da Entrega e para serving.registro_integrado.entrega_completa.

#### 6.6. Validação estrutural no Processor

O Processor abre o ZIP persistido, revalida SHA/nome/manifesto e processa o envelope único. pessoas.jsonl é sempre validado pelo contrato cadastral do Gestor. Se registros.jsonl tiver conteúdo, o Processor exige Natureza + codigoTipo + tipoVersao e valida o arquivo factual pelo contrato correspondente. Se registros.jsonl estiver vazio, não há fato a materializar; o processamento cadastral continua normalmente. A implementação de referência contém validador explícito do subconjunto JSON Schema usado na Fase 1 e rejeita keywords não suportadas em vez de ignorá-las silenciosamente. A validação factual é fechada e não admite saco genérico de atributos específicos.

A evolução preferencial é por catálogo/contrato. Novo Tipo normalmente não altera DDL; novo atributo transversal de Pessoa não altera gold.pessoa; nova Natureza futura reutiliza ref/Silver/Serving e acrescenta especialização Gold somente quando sua semântica exigir.

#### 6.7. Preservação do contexto de origem

Cada observação Silver preserva lote_id interno, gestor_id, sistema_origem_id e source_as_of. A Entrega preserva codigoSistemaOrigem, a versão cadastral do Gestor e Natureza + Tipo + versão factual. A chave de Pessoa é única por (sistema_origem_id, codigo_pessoa_origem); a chave de Registro é única por (sistema_origem_id, codigo_registro_origem). Um mesmo Gestor pode possuir vários sistemas de origem sem colisão de identificadores locais.

#### 6.8. Retorno de status

GET /api/v1/ingestao/entregas/{entregaId} exige Código do Gestor + chave do Gestor e retorna estado da Entrega, recebimento, última atualização e falha resumida quando aplicável. A API não expõe lote_seq/lote_total internos.

### 7. Camada 2 — Resolução de identidade (Prata)

#### 7.1. Princípio

A ingestão recebe os dados como declarados pela origem. A resolução de identidade ocorre na transição Bronze → Prata e adota duas rotas: determinística por CPF válido como caminho normal; probabilística apenas para ausência excepcional de CPF ou situações formalmente elegíveis de regularização. Não existe fila humana para escolher candidatos probabilísticos.

A resolução é automática. Não existe fila humana para escolher entre candidatos probabilísticos no fluxo normal. Registros sem evidência suficiente permanecem não resolvidos, são preservados e podem ser reprocessados quando surgirem novos dados ou nova versão do modelo.

A atividade humana aparece em outro ponto: verificação de evidência cadastral, especialmente no atendimento presencial. O agente não escolhe um UUID entre candidatos; ele confirma atributos mediante documentação e esse ato pode corrigir a referência Golden.

#### 7.2. Identificador canônico

Cada pessoa física reconhecida recebe um UUID versão 4, gerado aleatoriamente e persistido.

Regras:

• UUID não é derivado do CPF nem de qualquer atributo da pessoa;

• UUID nunca é reutilizado;

• alteração ou correção de atributo da mesma pessoa não altera o UUID;

• UUID não é segredo e não substitui autenticação ou autorização;

• somente a constatação de que registros pertencem a pessoas distintas pode exigir separação de vínculos ou UUIDs.

#### 7.3. Mapa de identidade

Tabela lógica segregada, correspondente ao Key Registry do modelo SDLE:

IDENTITY_MAP

uuid | tipo | identificador | vigencia_inicio | vigencia_fim | fonte | score | versao_modelo

A IDENTITY_MAP relaciona UUID a identificadores externos observados — com CPF como identificador primário de interoperabilidade e NIS, CNS e identificadores legados como auxiliares — preservando a história das associações.

Características obrigatórias:

• múltiplas observações e vigências são permitidas;

• a associação CPF-UUID é determinística no caminho normal, única entre vínculos ativos, corrigível e versionada quando houver evidência formal de atribuição errada;

• procedência e versão do modelo são registradas;

• armazenamento segregado e com acesso mais restritivo;

• não é exposta nas APIs de consumo.

Nomes não são tratados como identificadores técnicos da IDENTITY_MAP.

#### 7.4. Representação técnica e distância para comparação

A plataforma preserva na Gold o valor destinado a apresentação. A comparação utiliza uma representação técnica derivada, sem alterar o valor Golden.

Transformações de representação permitidas antes do cálculo:

• normalização Unicode;

• comparação sem distinção entre maiúsculas e minúsculas;

• remoção de acentos e diacríticos somente para comparação;

• remoção de espaços no início e no fim;

• redução de múltiplos espaços internos a um único espaço;

• CPF reduzido a 11 dígitos;

• data representada em YYYY-MM-DD.

Essas transformações definem equivalência de representação. Além delas, o motor de linkage pode calcular distância ou similaridade parcial entre nomes e nomes de mãe. Uma diferença textual não precisa ser convertida em equivalência para contribuir probabilisticamente para o score.

Exemplo:

JOSÉ DA SILVA   → JOSE DA SILVA
José  da Silva  → JOSE DA SILVA

Esses valores possuem distância zero no espaço de comparação. Já:

**JOÃO DA SILVA
JOSÉ DA SILVA**

não são equivalentes e possuem distância maior que zero, mas a diferença é avaliada conjuntamente com CPF, data de nascimento, nome da mãe e frequência dos termos na população.

Transformações semânticas não são aplicadas como equivalência determinística nesta versão:

• fonetização;

• correção automática de erro de digitação;

• remoção de partículas como DA, DE, DO, DOS, DAS;

• expansão automática de abreviações;

• declaração fixa de equivalências como SOUZA = SOUSA;

• reordenação automática de palavras do nome.

Essas diferenças podem afetar a similaridade estatística, mas não são silenciosamente reescritas como se os valores fossem idênticos.

#### 7.5. Estratégia de resolução

O núcleo de atributos comparáveis compreende CPF, nome completo civil, nome da mãe e data de nascimento, mas nenhum deles é universalmente obrigatório na observação de entrada: cada contrato de Gestor determina suas próprias exigências. Uma pessoa com CPF válido usa a rota determinística com verificações de consistência; CPF ausente segue a V9 probabilística somente se o contrato e a evidência permitirem. NIS, CNS, RG e outros identificadores são fontes auxiliares qualificadas, nunca identificadores municipais primários alternativos.

A estratégia probabilística é progressiva e incremental: a observação Silver preserva origem e versões; o blocking multipasse recupera identidades candidatas por combinações configuráveis de nomes, último sobrenome e nascimento, incluindo variantes elegíveis; Rank compara identidades, não apenas linhas duplicadas; Resolve usa margem entre identidades e condições calibradas para aceitar ou abster; a publicação set-based preserva precedência determinística, invariantes de CPF, idempotência e ledger. Correspondência apenas por nome é proibida; nome mais mãe ou nome mais nascimento podem constituir a evidência comparável mínima somente quando o modelo treinado e as demais políticas admitirem o cenário.

A formação de uma identidade sem CPF não é consequência automática de bloco vazio. Ausência verdadeira de candidato, falha de cobertura, conjunto truncado, escore inconclusivo e identidade potencialmente vinculável são hipóteses diferentes e devem produzir motivos distintos, conservadores e testáveis. A política de criação e abstenção do Gold é avaliada no próprio Runner incremental físico.

#### 7.6. Score probabilístico operacional e calibração versionada

O único scorer operacional é Fellegi–Sunter em C#, na arquitetura V9 nominal composta. A comparação usa nome completo da pessoa e da mãe, primeiro nome e último sobrenome significativo em estados compostos mutuamente exclusivos ou modelagem conjunta calibrada; preserva informação de nomes intermediários, partículas e agnomes, sem assumir independência entre evidências derivadas. A contribuição por estado é LLR = ln(m/u). Um fator de Bayes igual a 1 produz LLR zero, mas uma divergência observada não é automaticamente neutra. O TF aplica uma correção calibrada à frequência nominal no universo declarado, sem equiparar a marginal IBGE de presença em qualquer posição à probabilidade empiricamente medida de último sobrenome.

Há um único Calibrador de modelo C#, integrado ao mesmo contrato de scorer e blocking. O bootstrap sintético clean de 30.000 identidades produz o primeiro snapshot de parâmetros descobertos automaticamente; não depende de 5.000 pares entre Gestores nem de uma Gold previamente populada. Na continuidade, dados reais, inclusive CPF tardio, fornecem rótulos retrospectivos com independência e proveniência verificáveis; seu uso para estimar novos parâmetros requer solicitação expressa de recalibração pelo operador, após diagnóstico de suficiência, e não ocorre pela passagem de uma semana ou durante o startup.

O conjunto versionado de parâmetros inclui níveis e comparadores, m/u condicionados à união de passes do blocking, distribuição efetiva de candidatos, prior, pesos e mínimos de TF, configuração de passes, margem entre identidades, limiar superior de aceitação e limiar inferior de criação/abstenção. Não existe T_LINKAGE único como fronteira suficiente nem decisão score = 1,0. Limiar superior e inferior são aprendidos por objetivos distintos de Pareto; o Calibrador escolhe o melhor candidato segundo método reproduzível e apresenta diagnóstico das alternativas sem impor um orçamento percentual fixo de erros durante a busca.

Registros probabilísticos guardam vínculo candidato/aceito, motivo de abstenção, identidade-base, versão de modelo, fit, snapshot, normalização, score técnico, evidências, run e estado de publicação; a rota determinística guarda método CPF_DETERMINISTICO sem score sintético. O histórico é append-only; cada execução usa exclusivamente a versão congelada na abertura, e um resultado parcial, FALHOU ou CANCELADO nunca atualiza a visão de vínculo corrente. **Contrato físico atual:** cada linha de `identidade.linkage_resultado` tem a coluna obrigatória `score_melhor DECIMAL(9,8) NOT NULL` (com domínio [0,1]); o melhor candidato e o segundo candidato são referências opcionais, e `score_segundo` é opcional. Quando não houver candidato elegível, o valor técnico convencionado para `score_melhor` não é prova de correspondência nem autoriza criar ou vincular Pessoa; a ausência deve ser distinguida pelo UUID de candidato nulo, motivo, proveniência do blocking e guardas de publicação. Isto não altera o schema nem aplica score ao vínculo determinístico por CPF.

Os dados de teste separam TRAIN, etapas internas de seleção, VALIDATION e TEST inédito. É proibido reutilizar como TEST cego um conjunto já observado para escolher candidato. O Calibrador não decide a autorização de uso: operador autorizado, que integra a governança, revisa dossiê e resultados e autoriza ato específico; os gates técnicos VALIDATE e ACTIVATE verificam coerência, imutabilidade, estado e autenticidade da decisão, sem substituir a decisão humana.

#### 7.6.1. Geração versionada dos parâmetros probabilísticos

O ciclo de calibração começa com corpus e referências congelados, declaração de hipóteses de geração e identificação de estratos, construção de estados comparáveis e medição de suporte por grupo independente. O Calibrador estima m/u, prior, frequências efetivas, pesos TF e níveis compostos; avalia variantes de blocking, custos, falsos vínculos e cobertura por fonte, e calcula os candidatos de limiar superior e inferior por suas fronteiras Pareto próprias. O resultado é um snapshot RASCUNHO imutável, com fingerprint, relatórios, valores concretos dos parâmetros, diagnósticos de suficiência e justificativa matemática da seleção.

O critério de seleção do limiar superior é a menor distância normalizada à solução ideal na fronteira recall × FDR pós-Resolve. O limiar inferior é selecionado no joelho normalizado da fronteira falsas criações Gold × pendências, considerando três ordens de ingestão, abstenções, fusões e fragmentação. O Calibrador pode registrar finalistas conservador, joelho e agressivo para análise, mas não transfere ao operador a escolha manual de limiares. A escolha de modelo é computacional e reprodutível; a decisão de ativá-lo é humana e governada.

Não há amostra fixa universal de 5.000 pares, scheduler semanal de calibração, parâmetro manual de tolerância a falso vínculo, teto de risco percentual antecipado ou fallback silencioso quando falta evidência. O diagnóstico de insuficiência de evidência ou ausência de modelo válido produz bloqueio/abstenção explícitos, preservando a rota determinística de CPF e os fatos válidos. O TEST independente é avaliado depois da seleção e não modifica a configuração congelada.

Persistir dados de proveniência: versão de algoritmo, código e normalizador, snapshot de referência e Gold, hashes do corpus e do gerador, grupos independentes, estatísticas m/u e TF, estados do blocking, candidatos e Pareto, decisões e aprovações, datas, operadores, produto de relatório e reprodutibilidade. Cada versão pode ser GERANDO, RASCUNHO, VALIDADO, ATIVO, INATIVO ou FALHOU, sem pular estados ou ativar por simples gravação de tabela.

#### 7.7. Limiar de linkage e critérios de uso por risco

O modelo probabilístico possui duas fronteiras aprendidas com funções diferentes. A superior decide aceitação de vínculo quando a evidência e a margem entre identidades forem suficientes; a inferior governa a distinção entre nova identidade e pendência/abstenção, sem criar pessoa por alegada ausência de candidato quando a cobertura de blocking não estiver demonstrada. O intervalo intermediário representa incerteza e deve produzir motivo rastreável.

Fluxo lógico: CPF válido → decisão determinística protegida; CPF ausente elegível → blocking multipasse → Rank de identidades → Resolve com superior, margem e conflitos → vínculo aceito OU pendência/abstenção OU criação de identidade pelo contrato inferior explicitamente autorizado. O contrato inferior fica OFF por padrão até haver modelo V9 ATIVO, dossiê e autorização DT-15 vinculados à política e gates físicos; essa configuração é independente da exploração estatística do Calibrador.

Não há score artificial 1,0 para CPF, nem score 1,0 como evidência civil absoluta ou limiar universal de uso. As APIs de consulta não expõem candidatos alternativos, score, LLR, posterioridade, CPF ou ranking em uma interface semicega para operadores não autorizados. A autorização de consumo individual depende de credencial, finalidade legal, escopos, projeção e controle de qualidade; o BI apresenta a composição de vínculos por método, pendência e cobertura, sem escolher um corte arbitrário pós-teste.

O operador não seleciona o melhor limiar manualmente. Sua função é solicitar a calibração e, como participante da governança, decidir expressamente sobre autorização de uso com base no modelo descoberto e na avaliação independente posterior. VALIDATE e ACTIVATE são atos e gates técnicos distintos, registrados e protegidos por hashes e antifraude. Se a evidência for insuficiente, conservar o estado atual sem inventar aceite.

#### 7.8. Conferência documental distribuída e atualização pela fonte

A Gold Pessoas é referência operacional vigente, mas a Jornada não substitui o cadastro transacional dos Gestores. A correção administrativa é feita no sistema de origem e chega à plataforma como nova observação auditável.

A conferência documental é uma capacidade distribuída dos Gestores. Quando a pessoa apresenta documentação aceita pelo procedimento do Gestor, o próprio sistema setorial registra o ato, atualiza o cadastro sob sua responsabilidade e publica nova observação de Pessoa para a Jornada, preferencialmente por padrão transactional outbox.

Não existe hierarquia fixa entre Gestores para fins de comprovação. A precedência continua baseada na qualidade/pertinência da evidência e na temporalidade, e não na Secretaria que enviou o dado. A Jornada recebe metadados da conferência por campo; não recebe a imagem do documento.

Fluxo:

Cadastro do Gestor diverge ou precisa ser confirmado
          ↓
pessoa apresenta documentação ao Gestor
          ↓
Gestor confere e atualiza seu sistema transacional
          ↓
mesma transação grava evento/outbox quando disponível
          ↓
integração envia nova observação PESSOA + metadados de conferência
          ↓
Silver preserva origem/evidência
          ↓
Processor reaplica precedência e atualiza a projeção Gold
          ↓
divergências e pendências são recalculadas

A correção não é edição manual direta da Gold nem uma transação distribuída entre bancos. A integração usa idempotência e `sourceTransactionId` para correlacionar a alteração realizada no sistema do Gestor com a observação recebida pela Jornada.

Caso duas evidências válidas e materialmente contraditórias não possam ser ordenadas pela política do atributo, o sistema registra CONFLITO_DE_EVIDENCIA e preserva ambas. A resolução depende de nova evidência ou de regra de governança; não há adjudicação manual de par probabilístico na Jornada.

#### 7.9. Pendências persistentes de identidade (zona cinzenta)

PENDENTE_PROBABILISTICO, NAO_RESOLVIDO e CONFLITO são estados legítimos e podem persistir por tempo indeterminado. A plataforma não exibe candidatos probabilísticos para um operador escolher e não cria uma tela de regularização cadastral.

A governança deve acompanhar estoque e envelhecimento das pendências, no mínimo por Gestor, motivo, data de entrada e faixas 0-30, 31-90, 91-180, 181-365 e 365+ dias, incluindo P50/P95 de idade quando houver volume suficiente.

Uma pendência pode ser encerrada por nova observação do Gestor, conferência documental na fonte, surgimento de CPF/identificador confiável, nova fonte independente ou nova execução de modelo probabilístico versionado.

A ausência de solução não autoriza associação forçada. Registros que nunca recebem evidência suficiente permanecem fora das APIs intersetoriais individualizadas e são mensurados como risco de qualidade/integração.

Dados de negócio ligados a identidade incerta não são expostos para facilitar a resolução. A busca ativa ou atendimento para obtenção de documento é processo do Gestor, não função cadastral da Jornada.

A Jornada pode devolver ao Gestor indicadores de pendência referentes aos registros que ele originou, sem revelar candidatos alternativos de identidade.

Nenhum agente escolhe manualmente um PESSOA_UUID entre candidatos probabilísticos; o vínculo continua determinístico por CPF ou resultado reproduzível de modelo versionado.

A view `serving.v_bi_pendencias_identidade` é a superfície analítica da zona cinzenta e deve privilegiar contagens, motivo e envelhecimento, respeitando as regras de acesso e minimização.

Diferenças exclusivamente de representação não geram nova versão da Gold. A normalização de nomes segue o item 7.4; a normalização de endereço segue o item 8.5.

Não existe fila operacional de adjudicação na Jornada. O BI mantém uma fila analítica de pendências de identidade para governança, sem seleção manual de candidato, com prioridade, idade e referência territorial quando disponível.

#### 7.10. Registro da conferência documental na integração

Para cada campo do núcleo efetivamente conferido, `pessoas.jsonl` pode registrar `campoCodigo`, `evidenciaTipo`, `referenciaEvidencia` e `verificadoEm`; a observação pode trazer `sourceTransactionId` para correlação idempotente com a alteração no sistema do Gestor.

pessoa_observacao_id
gestor_id
campo_codigo
evidencia_tipo
referencia_evidencia
verificado_em
source_transaction_id

A Jornada não custodia imagem do documento para executar este fluxo. O sistema do Gestor preserva a trilha do agente/atendimento segundo sua política; a Jornada mantém somente os metadados necessários à precedência, auditoria técnica e provenance da observação.

#### 7.11. Procedência e histórico da identidade

A procedência do núcleo é reconstruível a partir das observações Silver, vínculos de identidade e conferências documentais por campo. Para atributos transversais, `gold.pessoa_atributo` mantém SCD2 explícito com fonte, evidência, verificação, precedência e vigência.

pessoa_observacao_id
fonte_gestor_id
source_transaction_id
campo_codigo
tipo_evidencia
referencia_evidencia
verificado_em
metodo_resolucao/linkage_run quando aplicável

A visão corrente da Gold apresenta o valor vigente; a reconstrução de origem continua possível por Silver/evidências e o histórico explícito dos atributos transversais permanece em Gold SCD2.

#### 7.12. Fusão e separação de UUIDs

• Fusão preserva os UUIDs envolvidos e registra qual UUID passa a ser canônico;

• UUID absorvido permanece resolvível como alias técnico do sobrevivente;

• separação (split/unmerge) é operação prevista desde a modelagem;

• toda fusão e separação é versionada, auditável e reprocessável;

• correção de vínculo nunca altera a Bronze; recalcula as camadas derivadas.

#### 7.13. Regras invioláveis

7.13.1. Divergência superveniente de atributo nunca revoga automaticamente o UUID.

7.13.2. Score, LLR/posterior, limiares superior/inferior e listas de candidatos são evidências internas do único modelo V9, não autorização civil de identidade; API pública CPF→UUID não os expõe. A rota determinística por CPF não recebe score artificial.

7.13.3. Incerteza interna nunca é apresentada ao profissional como fato sobre outro Gestor.

7.13.4. CPF válido é a rota determinística normal. Para o fallback probabilístico sem CPF, nome completo, data de nascimento e nome da mãe constituem o núcleo mínimo comparável; novos atributos somente podem ser incorporados ao modelo por versão formal e governança.

7.13.5. A observação com CPF ausente pode ser admitida pelo contrato da fonte e permanecer pendente. A criação de um novo UUID sem CPF, quando cabível, depende da política inferior aprendida, autorização governada e prova de cobertura de blocking. Não resulta apenas da falta de CPF ou de bloco vazio.

7.13.6. A concordância entre fontes não substitui evidência comprovada. Maioria de fontes declaradas não pode impedir correção Golden sustentada por evidência de maior precedência.

#### 7.14. Identidade progressiva, ancoragem e documentos múltiplos

##### Duas identidades com finalidades distintas

initial_uuid é aleatório e imutável e só identifica a Pessoa persistente de origem quando há chave estável (base_pessoa_origem_id, codigo_pessoa_origem). canonical_uuid referencia a identidade municipal corrente, quando resolvida, e pode ser objeto de transição atômica, explícita, versionada e auditada. initial_uuid não é evidência de igualdade de pessoas nem feature de blocking/score; também não é o idPessoaEntrega, identificador transitório do ZIP. Estados PROVISORIA, REFERENCIA e INDEFINIDA descrevem identidade progressiva, enquanto RESOLVIDO, SEM_CANDIDATO e CONFLITO descrevem resultado de resolução de uma observação. É erro criar initial_uuid fictício para remessa sem código persistente ou impor canonical_uuid a fato ainda não resolvido.

O UUID histórico não é reciclado, apagado ou atribuído à pessoa remanescente por conveniência. Mesmo que a Pessoa não possua fatos correntes, sua história de atribuições e eventos de decisão permanece rastreável; não entra na contagem de Pessoas correntes da Gold/BI por simples persistência histórica. Fusão histórica 2→1 exige sucessor e preserva todos os vínculos, atribuições e rastros do absorvido; divisão exige os destinos corretos por evidência e não uma atribuição automática com base em quem tinha mais registros.

##### Âncora permanente CPF → UUID

CPF estruturalmente válido admitido sob política governada utiliza identidade.cpf_ancora como autoridade determinística permanente: um CPF para no máximo um UUID e um UUID para no máximo um CPF. A âncora sobrevive à ausência de fatos, à chegada de mais dados e à futura revisão probabilística; não pode ser tomada ou transferida por score, data de inserção ou ordem dos lotes. Uma observação que declara CPF já ancorado a outra pessoa, com divergência robusta em núcleo de nome/mãe/nascimento, entra em CONFLITO/CPF_COMPARTILHADO_SUSPEITO com suspensão dos vínculos afetados, preservando observações e fatos. Não rebaixar automaticamente a pendência, ignorar sinais contraditórios nem alterar a âncora sem ato governado.

Se duas pessoas distintas foram agrupadas em A sem CPF ancorado, a divisão governada atribui dois UUIDs canônicos próprios B e C (novos ou anteriores admissíveis), deixando A exclusivamente histórico, sem herdeiro automático. Se A possui CPF admitido, A permanece com o titular da âncora; o outro recebe sua própria âncora ou UUID novo. Se aparecem dois CPFs válidos distintos, cada titular conserva seu UUID de âncora; o modelo probabilístico não os funde. CPF tardio pode produzir ALTERACAO_SEMANTICA no ledger de identidade com comparação anterior/posterior, mas não licencia reancoragem de um CPF que já pertença a outra identidade.

##### Identificadores secundários e documentos múltiplos

RG tradicional precisa de UF/emissor e versão de documento; CIN pode trazer CPF mas não é sinônimo de todo RG; NIS/PIS/PASEP/NIT formam família canônica NIS com proveniência preservada. NIS e RG são indícios/identificadores secundários: não são âncoras nem selecionam UUID municipal automaticamente, não criam identity_map e não competem com CPF. Uma Pessoa pode manter vários RGs históricos, documentos de identidade, NIS e instâncias documentais; impedir unicidade indevida 1:1. Reuso do mesmo NIS ou documento em pessoas diversas gera alerta de qualidade/consistência, não fusão silenciosa. Ausência de CPF não impede fatos válidos: só impede as transições que exigem âncora, devendo Linkage utilizar a evidência elegível e abster-se se mínima/completa não estiver presente.

##### Não depender de revisão manual caso a caso para o Linkage

O motor de vinculação executa decisões conservadoras, determinísticas ou probabilísticas governadas, com possibilidade de abstenção técnica e pendência; não há fila de revisão humana de cada par como etapa necessária do algoritmo. O que continua exigindo aprovação humana/institucional é a governança dos modelos, decisões excepcionais de identidade tomadas por autoridade competente, acesso a casos sensíveis, divulgação de resultados, TEST, release e HML/Produção. Essa distinção evita transformar a avaliação estatística e a segurança jurídica em um suposto workflow manual de linking de cada cidadão.

#### 7.15. Algoritmo probabilístico: arquitetura V9

A V9 nominal conjunta constitui o único desenho corrente do scorer operacional Fellegi–Sunter C#. Primeiro nome, nome completo e último sobrenome significativo da pessoa e da mãe integram estados comparativos conjuntos ou mutuamente exclusivos, evitando dupla contagem. Jaro e outras medidas podem atuar em filtros rápidos, mas a decisão de identidade é do scorer parametrizado e do Resolve, nunca da simples comparação de nome.

O componente BrazilianNameComponents.LastContentSurname projeta último sobrenome significativo; partículas, agnomes, nomes compostos e ausência devem receber tratamento declarado. O IBGE publica a frequência de um sobrenome por presença em qualquer posição dos sobrenomes informados, não por posição final. Essa frequência pode servir como proxy inicial de último sobrenome para o TF com ressalva estatística expressa e fingerprint no snapshot. O Calibrador deve comparar seu denominador, unidade e frequência efetiva ao u condicionado no universo de candidatos; a proxy não é medição censitária de sobrenome final. Guardas de cobertura do blocking e estados raros prevalecem sobre extrapolação de marginais.

O blocking é recuperação de candidatos e pode usar múltiplos passes por nome completo, primeiro nome, sobrenome, mãe, nascimento e variantes civilmente admissíveis. Deve medir recall de cobertura, cardinalidade, custo SQL e ausência de truncamento. Rank agrega observações/aliases por identidade-base; Resolve usa margens entre identidades, evidência mínima, CPF e abstenção técnica. A criação de identidade depende de política inferior e ausência de candidato efetivamente comprovada. A V9 é uma versão técnica de contrato, não um segundo motor. Splink externo é comparação opcional de referência, não runtime obrigatório.

#### 7.16. Calibrador C# único, bootstrap e parâmetros

O Calibrador C# único descobre automaticamente todo o conjunto de parâmetros do modelo, inclusive pesos TF, m/u, prior, comparadores, passos de blocking, limiares superior e inferior e tolerâncias estimadas. Ele seleciona a configuração de acordo com a evidência e as fronteiras de Pareto, sem teto arbitrário de risco estipulado previamente, sem escolha humana de peso ou limiar e sem substituição por fórmula fixa. O resultado da seleção é determinístico em relação ao snapshot, código, parâmetros de exploração e corpus.

O bootstrap de parâmetros usa 30.000 identidades sintéticas clean, com seed 42, gerador e hipóteses versionados, recortes e marginais IBGE de prenomes/sobrenomes, modelo etário diário declarado e verdade sintética conhecida. Não se fabricam famílias artificiais sem suporte; compartilhar sobrenome não significa parentesco. A margem teórica de amostragem aleatória simples não certifica a representatividade do corpus sintético. O número de pessoas não garante suporte de todas as combinações, nem FDR municipal.

O IBGE é referência de bootstrap para nominalidade e planejamento dos passes/índices iniciais. O município de São Paulo 3550308 é recorte candidato para nomes e sobrenomes da pessoa quando houver cobertura comprovada; para nome materno, a referência inicial pode ser Brasil/FEMININO para prenomes e Brasil/TODOS para sobrenomes. Ausência de informação no recorte nunca autoriza fallback invisível para outra geografia. Toda fonte possui período, tabela, extração, normalização, hash e limitação metodológica.

A inicialização da aplicação verifica/carrega snapshot aprovado em ref; não recalibra, não deriva parâmetros novos e não promove RASCUNHO. O operador autorizado pode solicitar nova calibração quando julgar necessário, particularmente após indicação read-only de suficiência DT-23; a coleta adicional de dados não executa recalibração por si só. Modelos anteriores permanecem íntegros para auditoria e reprodução.

A exploração estatística não utiliza teto fixo de falsa ligação, meta percentual pré-imposta ou orçamento ex ante. O teste independente posterior estima risco observado e incerteza por unidade de identidade-base, por estrato, em ordens físicas e com métricas de cobertura/abstenção. A governança, da qual o operador faz parte, examina resultados e riscos para decidir sobre ativação, podendo rejeitar modelo mesmo que seja o melhor matematicamente disponível. A escolha dos parâmetros continua sendo responsabilidade do Calibrador.

#### 7.17. Diagnóstico de suficiência e recalibração manual

DT-23 observa, periodicamente e sem alterar modelos, se há quantidade e diversidade de evidências novas para eventual recalibração. O diagnóstico usa grupos independentes, distribuição por estrato, suporte m/u, cobertura e contradições; relatório indica SUFICIENTE, INSUFICIENTE ou INDETERMINADO segundo regras técnicas de suporte, não inventa rótulos nem classifica pares dependentes como independentes.

A decisão de executar a calibração é do operador autorizado, nunca do monitor, do scheduler ou da ingestão. A execução solicitada gera novo RASCUNHO para avaliação independente e decisão de ativação governada. Nenhum sinal DT-23 ativa modelo. O avaliador e parte da prévia em DEV existem; monitoramento periódico integrado e prova com fonte real ainda são pendências de implementação.

#### 7.18. Publicação governada e integridade DT-10/DT-15

A autorização de política de limiar inferior V9 é distinta do cálculo estatístico do limiar. A publicação por lote é set-based, transacional e idempotente; exige locks, rollback integral em falha, priorização determinística por CPF, snapshot e fit coerentes. A política inferior fica OFF por padrão em ambientes sem autorização governada.

Ato de ativação legítimo: a decisão de governança deve identificar operador autorizado, dossiê atual, aprovação ACTIVATE, modelo e snapshot ATIVO, fit, parâmetros superior e inferior e digest canônico da política. A procedure de autorização recalcula digest a partir dos valores efetivos e o vincula ao mesmo ato; INSERT direto em tabela de autorização não substitui essa procedure nem permite publicação. Alteração após aprovação invalida o contrato de ativação e aborta sem alteração parcial, inclusive sob concorrência.

Os gates físicos precisam comprovar quatro cenários positivos de publicação, negativos de adulteração, ausência de candidato inventada, modelo divergente, política OFF, repetição idempotente, concorrência, rollback, replay e ausência de evento parcial. O código instalado, a versão de schema, a ordem das migrations e o checksum do manifesto devem ser compatíveis. A evidência de testes em DEV não autoriza automaticamente HML/Produção, cujos controles institucionais de identidade e acesso são específicos.

O operador integra a governança: pode solicitar calibração e, quando possuir competência/autorização para tanto, participar do ato de decisão sobre modelo. Os atos de solicitação de treino, seleção automática de parâmetros, aprovação do dossiê, VALIDATE e ACTIVATE são logicamente separados e registrados no ledger. Não existe revisão manual de pares como condição para treinar ou resolver.

#### 7.19. Proteções literais de ausência de candidato

O código SQL que identifica motivos literais SEM_CANDIDATO_ precisa escapar o sublinhado em LIKE, que no SQL significa curinga de um caractere. Usar comparação de prefixo literal sem ambiguidades, por exemplo LIKE N'SEM[_]CANDIDATO[_]%', ou alternativa parametrizada equivalente. Uma decisão de ausência contraditória com candidatos realmente persistidos falha fechada e não pode criar identidade nova.

As proteções devem existir tanto na publicação escalar quanto no lote, com testes físicos de prefixo válido, variantes SEMXCANDIDATO_, SEM_CANDIDATOX, candidato persistido, erro transacional, replay, lote misto, conservação do estado após falha e controle de permissões efetivas do escritor. Verificação por texto de migration não substitui execução da procedure no banco instalado.

#### 7.20. Busca conjunta de limiares e seu contrato de experimento

O Calibrador executa dois problemas estatísticos relacionados, porém distintos. PRIMEIRO — seleção do limiar SUPERIOR do linkage: construir o Pareto entre recall das decisões aceitas pelo Resolve e FDR de falsos vínculos aceitos; normalizar os dois objetivos em orientação adequada e escolher a solução de menor distância ao ponto ideal, de maneira determinística e versionada. A análise utiliza taxas reais do experimento, denominadores declarados, grupos independentes e métricas por estrato, sem teto percentual arbitrário que elimine candidatos antes de medir a fronteira.

SEGUNDO — seleção do limiar INFERIOR da política Gold: construir o Pareto entre falsas criações físicas de identidades e pendências/abstenções no Runner incremental; selecionar o joelho da fronteira normalizada, conservar faixas de robustez e finalistas para transparência e medir sensibilidade a três ordens de ingestão no mesmo corpus. O limiar inferior não é um substituto do superior, e a pior ordem não pode ser ocultada.

O Calibrador descobre ambos os limiares e os demais parâmetros automaticamente; não há veto antecipado de risco nem seleção manual da configuração final. Uma grade upper×lower pode ser utilizada como recurso técnico exploratório ou análise de sensibilidade, mas a otimização conjunta de quatro objetivos não é o contrato decisório normativo. Novas propostas somente substituem esse contrato mediante decisão explícita do PO e revisão da especificação. Nenhum valor experimental, incluindo 0,05, é limiar universal ou é ativado pela simples conclusão de treino.

#### 7.21. TEST final, confiança e governança do risco

TRAIN serve para ajustar e selecionar parâmetros e registrar candidatos Pareto. VALIDATION examina a estabilidade do resultado sem reescrever a seleção. TEST é avaliação independente de corpus inédito em relação à decisão já congelada; os mesmos rótulos, observações e grupos não podem ser reapresentados como TEST inédito após terem orientado a escolha. A integridade do protocolo — congelamento de seed, dados, gabarito, versão de código, grupos independentes e métricas — deve ser demonstrada antes de abrir o conjunto, mas NÃO existe obrigação de estabelecer limites percentuais de risco previamente à exploração ou à validação.

A avaliação posterior informa falsos vínculos aceitos FP/(TP+FP), recall, precisão, falso novo UUID, pendências, FDR por estrato e fonte, fragmentação, impureza de Gold, B-cubed convencional e com cobertura, disponibilidade de candidatos, condições de abstenção e incerteza. Pares que compartilham a mesma identidade-base não são Bernoulli independentes; métodos de intervalo e estimativas devem respeitar dependência entre grupos e o desenho da amostra. Boa performance sintética não comprova qualidade municipal nem cria limite de confiança representativo.

O Calibrador escolhe os parâmetros; o operador autorizado e as instâncias de governança examinam os resultados de validação independente e os riscos concretos, registram aceitação ou recusa fundamentada e eventualmente autorizam ativação. Não há teto fixo ou critério de aceitação estatística imposto a priori neste documento. Rejeição ou evidência inconclusiva mantém o modelo vigente e a política inferior OFF. A distinção entre decisão estatística automática e ato administrativo de aprovação deve ser expressa na página master de modelos; o Monitor é somente leitura.

A aprovação técnica VALIDATE, o ato de governança e ACTIVATE devem estar vinculados ao mesmo dossiê com hash e snapshot, inclusive quando o operador for também a autoridade governante designada. Alteração do dossiê após o ato invalida a autorização. Toda promoção para dados reais ainda exige evidência adequada, controle de acesso e habilitação do ambiente; teste sintético positivo isolado não autoriza uso operacional.

#### 7.22. Trava de consistência CPF

CPF já conhecido passou a exigir confronto conservador do núcleo antes de nova atribuição canônica. Divergência forte produz CONFLITO / CPF_COMPARTILHADO_SUSPEITO sem UUID; o fato finalístico válido continua materializado na Gold com a referência de sujeito declarada pela fonte e estado CONFLITO_IDENTIDADE, ficando suspensa apenas sua atribuição à Jornada individual.

#### 7.23. Conflitos de identidade e autorização de correção

O estado do conflito sobe para identidade.identity_map; CPF EM_CONFLITO não resolve UUID. identidade.identity_map_estado_evento preserva transições. identidade.correcao_identidade e identidade.correcao_identidade_item registram a decisão institucional e a procedure identidade.sp_aplicar_correcao_identidade realiza reassociação/fusão/separação auditável, recompõe as projeções canônicas e a atribuição dos fatos já existentes, sem reabrir lotes e sem inferência automática de titularidade.

Versões ATIVAS dos contratos passam a registrar SHA-256 aprovado; API e Processor verificam o digest em runtime, de modo que um restart/deploy não legitima alteração in-place. O Power BI padrão deixa de importar pessoa_uuid, usando views de contagens distintas pré-agregadas e incluindo os disclaimers normativos de Possibilidades e Monetário. Os parâmetros a calibrar em HML são consolidados em Solution/docs/HML_Parametros.md.

### 8. Camada 2 — Consolidação (Ouro)

### A Camada Ouro da Jornada possui duas famílias canônicas e semanticamente distintas: (1) Gold Pessoas, para identidade e atributos cadastrais transversais; e (2) Gold Registros, para o histórico factual transversal de Benefícios Concedidos e Serviços Prestados. A existência de Gold Registros não transforma fatos de política pública em atributos cadastrais da pessoa.

#### 8.1. Gold Unificada de Pessoas

A Gold Unificada de Pessoas (GOLD_PESSOAS) é a melhor representação municipal disponível do cadastro transversal da pessoa produzida pela plataforma.

A Gold Pessoas não consolida Benefícios, Serviços ou demais fatos de política pública como atributos cadastrais. Esses fatos, quando abrangidos por contrato transversal da Jornada, pertencem à Gold Registros descrita no item 8.8.

Sua visão lógica corrente é composta por:

PESSOA_UUID

# Núcleo de identidade — fixo
CPF
NOME
DATA_NASCIMENTO
NOME_MAE

# Atributos transversais — catálogo versionado
ENDERECO_RESIDENCIAL {
  CEP,
  COD_LOG,
  NUMERO,
  COMPLEMENTO,
  LOGRADOURO_NORMALIZADO,
  BAIRRO,
  MUNICIPIO,
  UF
}
REFERENCIA_TERRITORIAL {
  NATUREZA,                # DOMICILIAR | ACOLHIMENTO_INSTITUCIONAL | REFERENCIA_TERRITORIAL_DECLARADA
  DISTRITO,
  SUBPREFEITURA,
  ORIGEM_GEOGRAFIA,        # ORIGEM (ENRIQUECIMENTO_PRODAM apenas legado histórico)
  REFERENCIA_MALHA,
  RESOLVIDO_EM
}
TELEFONE_CONTATO
EMAIL_CONTATO
...

O PESSOA_UUID é chave técnica estável. O CPF válido ancora resolução determinística com trava de consistência. Para registros sem CPF, a V9 única pode resolver por evidência nominal conjunta, mãe, nascimento e TF calibrados, com blocking multipasse e seleção por identidade; não depende de execução somente sob demanda. Ausência de dados não autoriza associar apenas pelo nome. Atributos transversais seguem evidência, cardinalidade e proveniência, separadamente dos fatos administrativos.

A Gold Pessoas é evolutiva: pode nascer como baseline de uma única fonte e ganhar progressivamente corroboração independente, divergências observadas e evidências comprovadas à medida que novos Gestores são integrados.

#### 8.2. Núcleo de identidade

O núcleo é fixo:

CPF
Nome
Data de Nascimento
Nome da Mãe

Esses campos não obedecem à regra simples de “mais recente ganha”. Uma alteração pode significar mudança legítima, correção cadastral ou erro de identificação. O valor Golden é selecionado pela combinação de resolução estatística, procedência, corroboração e evidências aceitas.

Para registros resolvidos por CPF, uma diferença isolada de nome, nascimento ou nome da mãe não altera a identidade; ela é tratada como divergência cadastral. Para registros do fallback sem CPF, essas diferenças participam do score probabilístico.

Quando existir evidência presencial/documental válida de maior precedência, ela pode corrigir o valor Golden mesmo que a maioria das fontes declaradas ainda apresente valor anterior ou incorreto. A concordância dessas fontes é então recalculada em relação à nova referência.

#### 8.3. Atributos transversais

Atributos transversais são dados cadastrais que não pertencem naturalmente à competência exclusiva de um Gestor e não definem, por si, a identidade da pessoa.

Exemplos iniciais:

**ENDERECO_RESIDENCIAL
REFERENCIA_TERRITORIAL
TELEFONE_CONTATO
EMAIL_CONTATO**

A lista é controlada pelo Catálogo de Atributos Transversais. Na Fase 1, o catálogo é materializado em ref.atributo_transversal e também versionado como Configuration as Code em config/catalog/atributos-transversais.json. silver.pessoa_atributo_observacao registra valor, source_record_id, fonte, status/evidência e datas; gold.pessoa_atributo mantém histórico SCD2 dos valores comprovados; gold.v_pessoa_atributo_corrente expõe o valor vigente. Assim, novo atributo transversal exige catálogo/contrato, não ALTER TABLE gold.pessoa.

REFERENCIA_TERRITORIAL é conceito transversal distinto de ENDERECO_RESIDENCIAL. Para evitar ambiguidade semântica, ENDERECO_RESIDENCIAL designa o endereço cadastral de residência informado pela fonte; Endereço de Referência designa o endereço, quando existente, que sustenta a Referência Territorial efetivamente selecionada para territorialização e visualização. Eles podem coincidir quando o endereço de residência é usado como fallback DOMICILIAR, mas não são sinônimos. A referência territorial pode ser domiciliar, institucional ou declarada diretamente em nível territorial. Sua natureza não constitui prova de domicílio civil e deve ser declarada pela fonte autorizada; na Fase 1 Distrito/Subprefeitura e referenciaMalha são fornecidos pelo Gestor quando situacaoGeografia=RESOLVIDA; a Jornada não escolhe nem completa online qual local representa a Pessoa.

Para esses atributos, a regra de prevalência é:

prevalece o valor comprovado mais recente.

A recência deve refletir a data efetiva ou de referência da evidência, quando essa data existir. verificado_em registra quando o agente ou sistema realizou a verificação e não deve, sozinho, tornar uma evidência antiga mais recente do que outra que já representava situação posterior. O Catálogo de Atributos Transversais define, por atributo e tipo de evidência, qual campo temporal determina precedência.

A data de chegada do lote (ingested_at) e a data de reenvio não determinam prevalência.

#### 8.4. Comprovação de atributo transversal

Cada observação transversal possui, no mínimo:

pessoa_uuid
tipo_atributo
valor
fonte
source_record_id
status_evidencia      # DECLARADO | COMPROVADO
verificado_em         # obrigatório quando COMPROVADO
referencia_evidencia # data efetiva/referência, quando aplicável
tipo_evidencia
atualizado_em_origem
ingested_at

Somente COMPROVADO é elegível para substituir o valor corrente da Gold.

Exemplo, usando a data de referência da evidência como critério de precedência:

referencia_evidencia  fonte  valor   status
2026-01-10            SMS    Rua A   COMPROVADO
2026-07-15            SME    Rua B   DECLARADO
2026-08-20            SMADS  Rua C   COMPROVADO

Resultado Golden:

ENDERECO_RESIDENCIAL = Rua C
referencia_evidencia = 2026-08-20

Rua B é preservada como observação declarada, mas não substitui valor comprovado.

A comprovação pode decorrer de documento apresentado, fonte institucional admitida ou outro mecanismo autorizado no catálogo. A plataforma registra a evidência e sua classificação; não pressupõe que toda informação recebida de um Gestor seja automaticamente comprovada. Evidências presenciais produzidas por Gestores diferentes possuem a mesma natureza municipal quando executadas por agente habilitado sob a mesma política e para o mesmo tipo de evidência.

#### 8.5. Endereço domiciliar: CEP para resolução e COD_LOG como referência municipal do logradouro

Para ENDERECO_RESIDENCIAL, que representa endereço domiciliar e não qualquer referência territorial, o CEP é a chave principal de entrada e resolução postal. A plataforma considera disponível um Serviço Corporativo de Endereçamento da PMSP, responsável por resolver um CEP válido em componentes postais normalizados e, para logradouros cobertos pela base municipal, retornar o COD_LOG correspondente.

Contrato lógico mínimo:

CEP
COD_LOG                         # chave municipal canônica do logradouro, quando aplicável
TIPO_LOGRADOURO
LOGRADOURO_NORMALIZADO
BAIRRO
MUNICIPIO
UF

A distinção é intencional:

• CEP é a chave preferencial para localizar/resolver o endereço informado ou constante da evidência;

• COD_LOG é a chave municipal canônica do logradouro quando o serviço PMSP a disponibilizar;

• a Jornada não mantém catálogo próprio de abreviações e equivalências textuais de logradouro.

Assim, grafias como Av. Paulista, Avenida Paulista e AV PAULISTA não são comparadas por regras próprias da aplicação: o CEP é resolvido pelo serviço corporativo e a equivalência do logradouro é determinada pelo COD_LOG retornado.

Para identificar materialmente o endereço domiciliar, a comparação utiliza preferencialmente:

**COD_LOG + NUMERO + COMPLEMENTO**

Quando COD_LOG não for aplicável ou não estiver disponível, utiliza-se a referência postal resolvida pelo serviço corporativo, mantendo o CEP como chave de resolução.

Regras:

• mesmo COD_LOG elimina divergência meramente textual de logradouro;

• NUMERO é componente material e deve ser comparado de forma estruturada;

• COMPLEMENTO é relevante quando presente em ambas as evidências; sua ausência em uma evidência não invalida automaticamente complemento anteriormente comprovado;

• CEP e COD_LOG não constituem prova de domicílio nem, isoladamente, definem Referência Territorial; normalização postal, comprovação do endereço e classificação do vínculo territorial são controles distintos;

• LOGRADOURO_NORMALIZADO, bairro, município e UF podem ser armazenados na Gold para apresentação, georreferenciamento e busca, mas são derivados da referência de endereçamento;

• quando o CEP não constar da evidência de um ENDERECO_RESIDENCIAL ou de uma referência institucional baseada em endereço postal, o atendimento pode obtê-lo por busca assistida antes da consolidação. Preferencialmente utiliza-se mecanismo corporativo da PMSP; como contingência operacional, o agente pode consultar o site ou serviço oficial de busca de CEP dos Correios para localizar o CEP e, em seguida, validá-lo no Serviço Corporativo de Endereçamento da PMSP. Referência Territorial declarada diretamente por Distrito/Subprefeitura não exige criação artificial de CEP ou logradouro;

A comparação pode resultar em:

**EQUIVALENTE
DIVERGENTE
INDETERMINADO**

EQUIVALENTE não gera nova versão da Gold. DIVERGENTE somente promove nova versão quando a diferença for material, a evidência for válida e possuir precedência temporal. INDETERMINADO preserva o valor corrente até surgir evidência suficiente.

#### 8.6. Ausência, semântica e conflito

Regras obrigatórias:

• NULL ou campo ausente significa não informado e não apaga valor Golden anterior. Para atributo MULTI, a omissão de uma instância em observação posterior não encerra outras instâncias correntes; retirada/invalidação exige operação/evidência explícita;

• exclusão ou invalidação explícita de valor deve possuir operação própria e evidência correspondente;

• atributos de semânticas diferentes não competem entre si: ENDERECO_RESIDENCIAL representa o endereço cadastral de residência; REFERENCIA_TERRITORIAL representa o vínculo territorial selecionável para análise e pode ter natureza DOMICILIAR, ACOLHIMENTO_INSTITUCIONAL ou REFERENCIA_TERRITORIAL_DECLARADA; ENDERECO_CORRESPONDENCIA permanece outro atributo. O Endereço de Referência, quando existir, pertence semanticamente à referência territorial selecionada e não deve ser obtido pela camada de visualização lendo ENDERECO_RESIDENCIAL diretamente;

• se dois valores distintos estiverem COMPROVADO com o mesmo instante de precedência e não houver regra adicional capaz de desempatar, registra-se CONFLITO_TRANSVERSAL; nenhum deles substitui automaticamente o valor Golden vigente até nova evidência.

#### 8.7. Histórico e provenance

A Gold corrente de atributos transversais é exposta por gold.v_pessoa_atributo_corrente. A chave corrente é (pessoa_uuid, atributo_codigo, atributo_instancia_chave): atributos SINGLE usam uma instância única; atributos MULTI podem possuir várias instâncias correntes. O histórico e a evidência permanecem em gold.pessoa_atributo e silver.pessoa_atributo_observacao, associados à identidade e ao vínculo de origem.

Estruturas principais: GOLD.PESSOA + GOLD.PESSOA_ATRIBUTO (SCD2) + SILVER.PESSOA_ATRIBUTO_OBSERVACAO + IDENTITY_MAP + LINKAGE_HISTORY.

Deve ser possível responder, para cada valor Golden: quem informou, qual source_record_id o originou, quando foi verificado, qual referência de evidência determinou precedência, qual valor foi substituído e qual regra de catálogo estava vigente.

#### 8.8. Gold Registros — histórico factual transversal da Jornada

A Gold Registros contém os fatos de política pública explicitamente contratados para integração transversal. Na Fase 1, suas Naturezas são exclusivamente BENEFICIO e SERVICO. O catálogo, a Silver e o Serving são generalizados por Natureza; a Gold factual permanece fisicamente especializada porque os atributos de negócio e as invariantes são diferentes. Benefício registra concessão/vigência, situação e medidas aplicáveis; Serviço registra data/hora, unidade e situação. Contratos factuais da Fase 1 são fechados e não possuem extensão genérica por atributos_json.

Não existe tabela técnica genérica de Atendimento. gold.beneficio_concedido e gold.servico_prestado são as estruturas Golden especializadas e canônicas dos fatos da Fase 1. A palavra atendimento somente aparece quando fizer parte da semântica de um Tipo de Serviço ou de um procedimento humano da plataforma.

Registros é a projeção transversal do Histórico da Jornada. gold.v_registros_jornada une somente versões VIGENTES de Benefícios Concedidos e Serviços Prestados por PESSOA_UUID nos campos semanticamente comuns — Natureza, Tipo, Gestor, ocorrência, data de referência, situação e procedência — sem misturar atributos específicos. As versões HISTORICO, RETIFICADO e EXCLUIDO permanecem auditáveis, mas não entram nas métricas factuais padrão.

Somente dados abrangidos por contrato transversal aprovado são promovidos à Gold Registros. Conteúdo clínico de prontuário, atributos internos de matrícula, renda setorial não contratada e outros detalhes de domínio não são centralizados por inferência: permanecem na origem, ainda que possam produzir fatos mínimos autorizados para a Jornada.

#### 8.9. Uso da Gold pelos Gestores

A Camada Gold da Jornada existe para:

• oferecer, pela Gold Pessoas, uma referência municipal evolutiva do núcleo de identidade;

• oferecer, pela Gold Pessoas, referência corrente dos atributos transversais comprovados;

• permitir detecção de divergências e convergência progressiva das bases setoriais;

• oferecer, pela Gold Registros, o histórico factual transversal de Benefícios Concedidos e Serviços Prestados por PESSOA_UUID, sustentando APIs, Jornada e análises agregadas sem misturar semânticas de Benefício e Serviço.

A Gold não impõe automaticamente seus valores aos sistemas finalísticos e não constitui decisão administrativa vinculante. Cada Gestor decide, conforme sua competência e a governança aplicável, se e como utilizar a referência Golden em processo individualizado.

Os Gestores não utilizam a Gold como sistema transacional nem recebem permissão direta para atualizar seus valores.

A referência cadastral corrente é consumida pelas APIs de Pessoa. O Serviço de Divergências apenas retorna indícios institucionais ao Gestor e registra seu desfecho; não é caminho de edição. O Gestor decide e executa eventual correção em sua própria base de origem. Quando uma evidência válida demonstrar que a Gold está incorreta, aplica-se o fluxo controlado do item 7.8.

#### 8.10. Escrita e atualização

Toda atualização automática derivada de dados de origem percorre Bronze → Prata → Gold.

Atos de conferência documental, inclusive os realizados presencialmente no sistema finalístico, retornam à Jornada em `pessoas.jsonl`, por `conferenciasDocumentais`, e percorrem Bronze → Silver → Gold. Em nenhum caso um Gestor executa UPDATE direto na tabela Golden nem registra evidência por endpoint paralelo de escrita cadastral.

#### 8.11. Concordância, corroboração e convergência evolutiva

Para cada atributo Golden e UUID, a plataforma deve manter métricas auxiliares de corroboração sem poluir a visão corrente de consumo.

Para uma fonte independente comparável, define-se como concordante o valor que, após a normalização específica do atributo, é equivalente ao valor Golden vigente.

Quando existirem pelo menos duas fontes independentes comparáveis, a concordância do atributo pode ser expressa por:

CONCORDANCIA_ATRIBUTO =
  fontes independentes concordantes com o valor Golden
  ----------------------------------------------------
  fontes independentes com valor comparável

Exemplos:

2 fontes / 2 concordantes → 100%
2 fontes / 1 concordante  → 50%
3 fontes / 2 concordantes → 66,7%

Com uma única fonte independente, o estado é BASELINE_FONTE_UNICA e a concordância independente é NAO_APLICAVEL, ainda que a cobertura do cadastro da fonte piloto seja integral.

A unidade de independência deve ser catalogada. Cópias administrativas do mesmo cadastro não devem ser contadas como evidências independentes apenas por terem sido entregues por integrações diferentes.

As métricas mínimas por atributo devem permitir derivar:

n_fontes_comparaveis
n_fontes_concordantes
concordancia_percentual
fontes_divergentes
status_corroboração

A concordância não determina sozinha o valor Golden. A regra de formação da Gold considera, nesta ordem conceitual:

1. evidências comprovadas e sua precedência segundo o catálogo;

2. consistência temporal e provenance;

3. força estatística do linkage e das observações disponíveis;

4. corroboração entre fontes independentes.

Assim, um valor comprovado pode permanecer Golden mesmo com concordância minoritária; o baixo percentual passa a indicar que as demais bases precisam convergir.

A evolução dessas métricas ao longo do tempo constitui indicador de convergência cadastral municipal.

| Código | Descrição |
| --- | --- |
| obito | Indício de óbito para pessoa com benefício ou serviço ativo |
| duplicidade | Indício de mais de um registro da mesma pessoa no mesmo Gestor |
| sobreposicao | Benefícios de naturezas incompatíveis concedidos simultaneamente |
| inconsistencia_interna | Violação de regra de negócio detectada após a ingestão |

#### 8.12. Catálogo documental, evidências e precedência dos atributos

##### Núcleo e campos parcialmente disponíveis

O núcleo informacional preferencial para resolução inclui nome civil, nome da mãe, nascimento e CPF, mas a obrigatoriedade de cada campo é definida pelo contrato versionado da fonte. Pessoa v3 admite nomeMae null e essa nulabilidade deve percorrer JSON, Processor, Silver, Gold, índices e scoring sem preenchimento falso. Com evidência incompleta, apenas passes aplicáveis podem ser executados; igualdade só do nome, em particular nome frequente, não constitui suporte suficiente para identidade aceita. Uma hipótese pode exigir nome mais nascimento ou nome mais mãe, ou outros sinais calibrados; ausência de suporte mínimo conduz a abstenção. O calibrador pode recomendar uma política de evidência mínima por estrato com erros e custo medidos, mas não presumir dois campos inventados no lugar dos que faltam.

##### Catálogo documental relacional e cardinalidade N:N

Tipos e versões de documento são referência governada. ref.tipo_documento descreve código, classe DOCUMENTO/AUTODECLARACAO, vigência e versão; ref.tipo_documento_atributo declara quais atributos cada tipo pode comprovar; o cadastro pode ser evoluído sem recompilar C# mediante controle de mudança. A instância de documento guarda tipo, identificador, data própria de expedição/emissão ou declaração, data do atendimento em campo separado, proveniência e status de validade. A relação documento_evidencia_valor é N:N: o mesmo documento pode comprovar vários atributos, e o mesmo atributo pode ter vários documentos divergentes. A data pertence à instância documental, não a cada coluna de atributo e nunca é inferida da data do atendimento. Duas certidões emitidas em dias diferentes são duas evidências, ainda que tenham o mesmo tipo. Uma Pessoa pode manter múltiplos RGs simultaneamente.

O bootstrap do catálogo pode ter referências a RG, CIN, comprovante CPF, certidão de nascimento, CNS e título de eleitor, por tipo/versão; a cobertura de campos depende do conteúdo efetivamente presente e validado. CPF eventualmente presente no RG é evidência do campo CPF, não autorização para supor que todo RG possua CPF. O mesmo raciocínio vale para filiação do e-Título, do CNS e de versões documentais antigas. Seed inicial idempotente não reativa tipos desabilitados nem altera decisões Gold históricas; mudança de cobertura precisa de versão, aprovação e teste de migração.

##### Precedência por atributo e por data da evidência

Para cada atributo Gold, filtrar apenas evidências válidas que realmente comprovem aquele atributo; ordenar DOCUMENTO válido mais recente, DOCUMENTO válido anterior, AUTODECLARACAO mais recente e AUTODECLARACAO anterior. A antiguidade de documento e autodeclaração é determinada pela data própria da evidência, nunca pela data de ingestão, chegada da remessa ou atendimento. Não usar um RG para alterar um atributo que seu tipo/versão não comprova nem inferir que uma evidência mais recente de telefone supera a data de nascimento comprovada em outra. Empate, data ausente e inconsistência exigem regra contratual explícita, com evento e proveniência do valor vencedor, sem fallback temporal silencioso.

A Gold guarda a observação original e a seleção de referência por atributo, incluindo classe, versão de catálogo, fonte, documento, instante e regra de desempate. Divergências não são apagadas: podem sustentar revisão posterior e a auditoria de uma ALTERACAO_SEMANTICA. A falta de um atributo na projeção de consumo não prova ausência na Bronze/Silver/Gold; controle de acesso é aplicado depois da escolha canônica e pode ocultar valores sensíveis. Para telefone brasileiro, a normalização corrente TELEFONE_BR_CANONICO_V2 e para e-mail EMAIL_CANONICO_V2 seguem os contratos históricos já registrados na texto principal, com upgrade/replay convergentes e sem sobrescrever o valor de origem.

##### Nome civil/social e identidade de apresentação

O nome de referência é um campo de apresentação de serving e atendimento, não entrada de blocking ou peso FS por si só. Quando houver NOME_SOCIAL válido e declarado/comprovado para a Pessoa, a apresentação pode lhe dar prioridade sobre o civil nos termos da política vigente e do Decreto municipal nº 58.228/2018; o nome civil fica disponível internamente a perfis autorizados e é preservado com proveniência. Bloqueio e calibração poderão comparar variantes civis e sociais como evidência técnica, mas não as presumem independentes, não atribuem dois pesos integrais ao mesmo fenômeno e exigem regras/versões próprias. Nome de referência exibido não altera âncora CPF, canonical_uuid ou a hierarquia documental de nome civil.

#### 8.13. Política Gold, limiar inferior e estudos físicos

A política Gold é separada do limiar superior de vínculo. O limiar inferior controla a possibilidade de criação de identidade quando não se encontrou vínculo suficientemente fundamentado, sempre com proteção contra candidato perdido, falsa ausência, lote truncado e evidência insuficiente. Não são admissíveis novos UUIDs por mero bloco vazio nem criação repetida em reprocessamentos.

O Calibrador recomenda o limiar inferior pelo Pareto falsas criações físicas × pendências/abstenções, com escolha de joelho normalizado e faixas de robustez. A proposta é avaliada no Runner SQL incremental real, não exclusivamente por simulador estático, sobre três ordens predeterminadas e bancos novos por ordem. Resultados comparam observações por IDs estáveis da origem, não pelos UUIDs locais de SQL que mudam conforme ordem de inserção. Contagens de pendência, falso vínculo, falsa criação, fragmentação e fusão e B-cubed com/sem cobertura devem ser publicadas com denominadores e unidade amostral.

A investigação TRAIN pode usar uma massa sintética clean e outra coorte sintética independente como teste de generalização; ambas permanecem evidência sintética, não homologação estatística municipal. A pior ordem e eventuais regressões locais devem ser descritas. O candidato experimental de estudo não constitui valor operacional obrigatório nem autorização para religar a política inferior.

Uma decisão de aceitação do novo modelo pertence à governança. Ela é posterior à descoberta automática do parâmetro e à avaliação independente, e não pode ser substituída por aprovação visual do dashboard, revisão manual de cada par ou alteração direta da procedure. A publicação continua protegida pelo contrato de autorização e pelos gates DT-10/DT-15.

#### 8.14. Persistência por mudança semântica e reavaliação de RESOLVIDOS

O Gold é uma projeção revisável de identidades e fatos, não registro civil infalível. A publicação é transacional e deve respeitar precedência determinística, ausência de candidatos verdadeira, conflitos CPF, assinatura e idempotência. O ledger semântico distingue INICIAL de ALTERACAO_SEMANTICA, preserva versões, run/snapshot/fit/proveniência, trilha anterior e decisão posterior. Bronze por conteúdo SHA-256, snapshots de arquivos em NAS quando habilitados e ligação create-once por manifesto permitem replay técnico se completo, porém guardas de versão, retenção, GC, recuperação, custo e replay histórico ainda não devem ser proclamados concluídos sem evidências novas.

DT-22, reavaliação extraordinária de RESOLVIDOS sem CPF por nova evidência ou modelo, está POSTERGADA e não é substituída por uma Trilha 4 contínua. O modo INCREMENTAL não cobre genericamente terceiros RESOLVIDOS indiretamente afetados pela chegada de candidatos de outra fonte; o replay histórico corrente tem escopo próprio. Não prometer autocorreção completa de todos os RESOLVIDOS em cada chegada. Quando DT-22 vier a ser implementada, exigir escopo governado, cohort/snapshot, respeito a CPF/UUID, dry-run, prova de conservação, custo, retomada, testes de falso merge/separação e decisão de autorização antes de mutar Gold. O atraso da DT-22 deve ser visível como risco residual, sem invalidar o projeto nem confundi-lo com revisão humana par a par.

#### 8.15. Materialização dos fatos independentemente da identidade

O fato administrativo válido é persistido independentemente de o sujeito possuir identidade canônica resolvida. A Gold Registros mantém o evento, a chave estável de origem, Tipo, Natureza, datas, versão, conteúdo contratado e proveniência; o vínculo à Pessoa é uma dimensão adicional e revisável. Não descartar, duplicar, negar direito ou inventar associação pelo simples estado do linkage.

ATRIBUIDA — o Registro possui atribuição canônica corrente de PESSOA_UUID, por CPF determinístico ou por vínculo probabilístico autorizado e publicado; sua individualização em APIs ainda depende de autorização, projeção e regra institucional. A atribuição é distinta da existência do fato: ela pode ser revisada sem criar outra concessão ou prestação.

PENDENTE_IDENTIDADE — o Registro foi aceito, mas o sujeito de origem ainda não possui PESSOA_UUID confiável, por ausência de CPF ou insuficiência de evidência/abstenção. O fato permanece em contagens factuais e métricas de cobertura, mas não deve ser somado como Pessoa individualizada nem exposto a um UUID hipotético. Evidência posterior pode ligar o fato ao UUID correto, com histórico da mudança.

CONFLITO_IDENTIDADE — existem evidências materialmente contraditórias, como suspeita de CPF compartilhado, fusão conflitante ou decisão impedida por regras de consistência. O Registro e a referência original são conservados; a atribuição individual fica suspensa e o conflito entra em auditoria, até correção de origem ou decisão institucional governada. Não usar conflito como pretexto para suprimir ocorrência administrativa já validada.

Esses três estados qualificam a atribuição do fato à identidade e NÃO são equivalentes a estados do job de ingestão, a motivos do Runner ou a estados da identidade progressiva. O BI apresenta total de fatos válidos, fatos atribuídos, pendentes e em conflito, com denominadores, sem equiparar fatos à contagem de Pessoas. A query serving deve respeitar data de vigência, fusão histórica e as políticas de acesso.

#### 8.16. Benefício concedido: interpretação, versões e eventos

O contrato de Benefício mantém o compartilhamento aprovado e usa nomes factuais únicos. O contrato JSON deixa de aceitar aliases dataInicio/dataFim/dataEvento/valorMonetario/valor e adota exclusivamente dataInicioConcessao, dataFimConcessao, dataEventoConcessao e valorConcedido. O canônico SQL/API/BI acompanha a mesma semântica. ref.tipo_registro_versao passa a armazenar regime_vigencia e janela permitida de concessão; o QC estrutural valida janela, inversão temporal e obrigatoriedade de data_fim_concessao conforme regime sem se confundir com o QC específico da política.

BI factual único: Benefícios, Serviços e Registros expõem estado_atribuicao_identidade na própria entidade factual; FatosIdentidade é removida. Registros passa a representar todos os fatos VIGENTE, enquanto métricas de Pessoas consideram somente ATRIBUIDA.

As telas devem explicar explicitamente a diferença: Fatos contabilizam todas as ocorrências vigentes declaradas; Pessoas contabilizam somente fatos com identidade canônica atribuída.

Retenção Bronze por Entrega: a linhagem SQL é preservada e a disponibilidade do payload evolui DISPONIVEL -> EXPURGO_PENDENTE -> EXPURGADO. O objeto físico content-addressed só é removido quando nenhuma referência DISPONIVEL o protege.

Pós-restore executável: Jornada.Bronze.Verify verifica existência, tamanho e SHA-256 de toda objeto_chave ainda DISPONIVEL e falha com código não zero diante de ausência, divergência ou indisponibilidade do storage.

Reverse proxy: KnownProxies aceita IPs explícitos e KnownNetworks aceita CIDR IPv4/IPv6; configuração inválida falha no startup.

Os fluxos críticos de resolução compreendem CPF conhecido, CPF válido com contradição, Pessoa sem CPF com Rank/Resolve e abstenção e correção governada. São decisões independentes do recebimento de fato e todas exigem trilha versionada e auditável.

Permanecem fora da implementação unilateral da Solution apenas dependências de identidade corporativa, catálogos e regras reais dos Gestores, decisões jurídicas/institucionais, valores finais de retenção e calibrações/ensaios de HML.

#### 8.17. Semântica finalística e cobertura documental

A nomenclatura pública deixa de sugerir a existência de um componente organizacional específico para a Pessoa. A regra técnica permanece: a Pessoa compartilhada em âmbito municipal é comum às credenciais autorizadas, sem fronteira populacional por Gestor ou Tipo. A view pública correspondente passa a ser serving.v_pessoa; instalações existentes removem a view de nomenclatura anterior por migração guardada.

A SGM/SEPE é a área requisitante. O Programa Reencontro coordena o projeto e a articulação institucional, sem papel operacional na plataforma, sem administração de credenciais e sem responsabilidade pelos dados finalísticos. PRODAM e Gestores mantêm as responsabilidades operacionais já especificadas.

O modelo físico relacional deve representar ref.gestor, identidade.vinculo_fonte, catálogos e resultados de linkage, qualidade, auditoria e divergências. As superfícies de API/Console/BI são especificadas no corpo deste documento; fontes PBIR, SQL e DER são artefatos de execução e de teste.

O objeto público de identificação passa a ser denominado simplesmente Pessoa e a view correspondente passa a ser serving.v_pessoa. “Municipal” descreve o âmbito de compartilhamento, não o nome do objeto. O Programa Reencontro é apresentado exclusivamente como coordenação do projeto e articulação institucional, sem papel operacional na Jornada.

#### 8.18. Cardinalidade e referências de sucessão

O catálogo de atributos transversais passa a declarar cardinalidade e regra de chave de instância. TELEFONE_CONTATO e EMAIL_CONTATO são MULTI; valores normalizados distintos coexistem como instâncias correntes independentes. ENDERECO_RESIDENCIAL, REFERENCIA_TERRITORIAL, NOME_SOCIAL e ENDERECO_CASA_ABRIGO_SIGILOSA permanecem SINGLE. A simples omissão de um telefone/e-mail em observação posterior não o invalida.

FUSAO_HISTORICA 2→1 passa a registrar pessoa_uuid_sucessor no UUID absorvido e status FUNDIDO. Identificadores ativos migram ao UUID canônico com eventos auditáveis; CPFs ativos divergentes fazem a operação falhar fechada. A função identidade.fn_pessoa_uuid_canonico resolve a sucessão e as APIs aceitam UUID histórico absorvido, consultam o canônico e preservam o UUID originalmente solicitado em metadados.

Ausência de atributo na projeção não deve ser interpretada como ausência na origem ou na Gold. JSON Schema e regras de projeção podem suprimir campos; a API não revela a existência de dado protegido por placeholder. Essa regra é particularmente importante para ENDERECO_CASA_ABRIGO_SIGILOSA.

#### 8.19. Fusão histórica, telefone e qualidade

FUSAO_HISTORICA passa a ser exaustiva por UUID absorvido. Antes de qualquer mutação da aplicação do caso, todos os vínculos correntes RESOLVIDO do UUID de origem que será absorvido devem estar incluídos no caso e convergir para o mesmo UUID canônico. Se restar vínculo resolvido fora do caso ou destinado a outro UUID, a procedure falha fechado com erro 51119; uma fusão pedida não pode terminar silenciosamente como reclassificação parcial.

TELEFONE_CONTATO passa a usar a regra versionada TELEFONE_BR_CANONICO_V2. Para números brasileiros, DDD+número com 10/11 dígitos recebe DDI 55; +55, 0055 e E.164 brasileiro sem sinal convergem à mesma atributo_instancia_chave. Número internacional não brasileiro deve trazer prefixo internacional explícito (+ ou 00), e número local sem DDD falha fechado.

Na atualização do modelo, chaves nacionais legadas de telefone em Silver/Gold são canonizadas. Se representações com e sem DDI coexistirem como instâncias correntes do mesmo número, a evidência de maior precedência permanece corrente e apenas a duplicata lógica é encerrada; o histórico não é apagado. EMAIL_CONTATO permanece MULTI, mas a regra corrente é EMAIL_CANONICO_V2; chaves legadas EMAIL_CASEFOLD_V1 são recomputadas a partir do valor original e qualquer valor incompatível bloqueia o upgrade para saneamento explícito.

#### 8.20. Preflight de fusão e normalização telefônica

FUSAO_HISTORICA mantém a semântica 2→1 exaustiva e passa a validar, antes da primeira mutação da aplicação, as três salvaguardas específicas da fusão: 51119 para exaustividade e convergência ao mesmo destino, 51117 para sucessão canônica inválida/cíclica e 51118 para CPFs ativos divergentes. A mesma origem não pode ser repartida entre dois destinos, ainda que todas as observações do caso tenham sido classificadas.

O upgrade de TELEFONE_CONTATO deixa de transformar apenas a chave legada já persistida. Silver e Gold recomputam atributo_instancia_chave a partir do valor original pela função ref.fn_telefone_br_canonico_v2, equivalente à regra runtime TELEFONE_BR_CANONICO_V2. Dessa forma, valores legados com prefixo internacional explícito 00, como 00 55 11 99999-0001, convergem para 5511999990001. Valor histórico incompatível com o contrato V2 interrompe o upgrade para saneamento explícito.

A normalização de TELEFONE_CONTATO exige migração explícita convergente: Silver, Gold e replay recalculam TELEFONE_BR_CANONICO_V2 a partir do valor de origem. O helper TELEFONE_DIGITOS_V1 não altera a política vigente.

#### 8.21. Normalização de e-mail e convergência de replay

EMAIL_CANONICO_V2 substitui EMAIL_CASEFOLD_V1 como regra corrente de chave de instância de EMAIL_CONTATO. A mudança é versionada; a regra V1 permanece apenas como referência histórica e não é alterada silenciosamente.

Upgrade e replay corrente devem convergir: Silver e Gold recalculam EMAIL_CONTATO a partir do valor original por EMAIL_CANONICO_V2. Valor legado incompatível interrompe o upgrade; não existe fallback silencioso para LOWER/LTRIM/RTRIM.

A equivalência SQL/C# é parte do contrato técnico: trim explícito [TAB, LF, CR, SPACE, NBSP], exatamente um @, lowercase apenas ASCII A–Z e nenhuma normalização Unicode implícita.

A contradição histórica do catálogo foi eliminada: TELEFONE_CONTATO usa TELEFONE_BR_CANONICO_V2. TELEFONE_DIGITOS_V1 permanece apenas como helper legado e não é regra corrente.

### 9. Serviço de Divergências — Fase 1 e evoluções

#### 9.1. Finalidade na Fase 1

A Fase 1 implementa uma fila institucional de divergências geradas pela própria Jornada. A fila informa ao Gestor responsável a existência de um indício e permite registrar o desfecho institucional. O serviço não é cadastro paralelo, não edita a base de origem e não constitui caminho de escrita direta na Silver ou na Gold.

Na Fase 1, a tipologia automaticamente produzida pela Solution é `DIVERGENCIA_IDENTIDADE`. O serviço não executa comparação cadastral genérica, não produz convergência automática e não altera Benefício Concedido, Serviço Prestado ou atributos da Pessoa.

#### 9.2. Conteúdo mínimo e minimização

A listagem entrega somente os elementos necessários ao tratamento institucional: identificador da divergência, tipologia, motivo, identificadores técnicos de observação/origem quando aplicáveis, correlation_id e instante de abertura. A superfície pública não devolve CPF, PESSOA_UUID, conteúdo factual ou valores cadastrais de outros Gestores.

O detalhe necessário para compreender a representação corrente da Pessoa é obtido pelas APIs de Pessoa autorizadas. O envelope de `GET /api/v1/pessoas/{uuid}` e da consulta em lote separa `dados` do schema selecionado e `metadados` de interpretação da Gold (`fontesDistintas`, `estadoConcordancia`, `atualizadoEm`).

#### 9.3. Caminhos de correção

O Serviço de Divergências notifica e registra desfecho; não corrige o dado. Correção cadastral ou nova evidência é realizada no sistema finalístico e retorna pela ingestão de nova observação de Pessoa. Correção de fato utiliza `ALTERACAO` ou `RETIFICACAO` em nova Entrega. Conflitos de identidade usam exclusivamente as interfaces governadas próprias de identidade, com ato, justificativa e trilha append-only.

`conferenciasDocumentais` em `pessoas.jsonl` é o único caminho da Fase 1 para materializar nova conferência documental produzida pelo Gestor. Não existe `POST /api/v1/pessoas/{uuid}/verificacoes`.

#### 9.4. Interfaces implementadas

GET  /api/v1/divergencias?limit=
POST /api/v1/divergencias/{divergenciaId}/desfecho

`GET /api/v1/divergencias` retorna apenas divergências em estado `ABERTA` do Gestor autenticado; `limit` é opcional, tem padrão 100 e é limitado pela implementação a 1..500. Não existe, na Fase 1, endpoint de detalhe `GET /api/v1/divergencias/{eventoId}` nem filtros públicos por tipologia/situação.

O POST de desfecho aceita o estado institucional previsto pelo contrato de referência (`RESOLVIDA` ou `DESCARTADA`), além de desfecho e observação. O encerramento da fila não substitui a correção do dado na origem nem o fluxo governado de identidade.

#### 9.5. Consulta da Pessoa e convergência operacional

Não existe endpoint `/api/v1/pessoas/{uuid}/convergencia`. Para não recadastrar do zero, o sistema finalístico consulta `GET /api/v1/pessoas/{uuid}` ou `POST /api/v1/pessoas/consulta`, que devolvem a representação corrente autorizada da Pessoa, atributos transversais, conferências documentais visíveis no contexto e metadados de corroboração da Gold.

Se o sistema consumidor desejar confrontar sua própria base com a representação corrente da Jornada, pode realizar essa comparação localmente. A Jornada não trata a Gold como verdade absoluta: ela é a melhor representação corrente segundo as evidências e regras vigentes, e pode ser superada por evidência posterior válida.

#### 9.6. Evolução de Fase 2 — comparação cadastral assistida

Uma comparação cadastral centralizada, somente leitura, pode ser avaliada em fase posterior para confrontar a última observação conhecida do Gestor com a representação corrente da Jornada, explicitando diferenças sem produzir alteração de dados. Se implementada, deve ser nomeada como comparação cadastral, não como convergência, e deverá definir normalização, temporalidade, múltiplas origens do mesmo Gestor, restrições de projeção e regras de evidência.

#### 9.7. Evolução de Fase 2 — tipologias operacionais

Tipologias como óbito, duplicidade, sobreposição e inconsistência interna não são geradas pela Solution de Referência da Fase 1. Sua implementação exige fonte autorizada, proveniência, regra de correspondência, deduplicação/versionamento do evento, ciclo de vida, testes e governança próprios. Permanecem FORA_DE_ESCOPO da Fase 1.

#### 9.7.1. Diretriz para tipologia óbito — Fase 2

Se a tipologia óbito for implementada na Fase 2, informação de óbito deverá ser tratada como indício cadastral para verificação, nunca como afirmação automática de falecimento pela Jornada. Falso positivo possui consequência assimétrica e jamais autoriza suspensão, cancelamento ou bloqueio automático de benefício ou serviço; a decisão administrativa permanece com o Gestor responsável, mediante procedimento competente.

A futura integração deverá priorizar CPF válido como identificador primário e transportar somente o mínimo necessário. Não deverá expor causa do óbito, local de ocorrência ou dado clínico além do indispensável ao tratamento institucional. A mensagem apresentada pela interface finalística deverá preservar o sentido: “Há informação que requer verificação cadastral. Esta plataforma não afirma o óbito da pessoa. Nenhum benefício ou serviço deve ser interrompido automaticamente com base apenas neste alerta.”

O uso de referência de óbito oriunda da SMS depende de instrumento de compartilhamento, análise jurídica e desenho técnico específicos da Fase 2. A Fase 1 não contém contrato, carga, worker ou regra que alimente `qualidade.divergencia_gestor` com essa tipologia.

#### 9.8. Evolução de Fase 2 — notificações externas

A Solution de Referência da Fase 1 não envia correio eletrônico nem outra notificação externa de divergências. Se esse recurso for adotado em fase posterior, a notificação deverá transportar apenas existência/volume de pendências e encaminhar o usuário para canal autenticado, sem CPF, UUID ou conteúdo cadastral no corpo da mensagem.

#### 9.9. Serviço de Divergências e metadados de consulta

A Fase 1 deve documentar exclusivamente as interfaces de divergência realmente materializadas na Solution: GET /api/v1/divergencias?limit= e POST /api/v1/divergencias/{divergenciaId}/desfecho. A tipologia automática é DIVERGENCIA_IDENTIDADE. O serviço informa a existência do indício e registra o desfecho; não cria canal paralelo de escrita cadastral ou factual.

Correções cadastrais e novas conferências documentais retornam pela ingestão de nova observação de Pessoa; correções de fatos usam ALTERACAO ou RETIFICACAO; conflitos de identidade permanecem no fluxo governado específico. Não existem na Fase 1 endpoints de divergencias-cadastrais, convergencia ou verificacoes, GET de detalhe nem filtros públicos por tipologia/situação.

GET /api/v1/pessoas/{uuid} e POST /api/v1/pessoas/consulta passam a expor metadados estáveis de interpretação da Gold - fontesDistintas, estadoConcordancia e atualizadoEm - no envelope metadados, fora do objeto dados governado pelo schema cadastral. Comparação cadastral assistida, tipologia óbito e notificações externas permanecem FORA_DE_ESCOPO da Fase 1 e são candidatas à Fase 2.

### 10. Camada 3 — Interoperabilidade por API

#### 10.1. Princípios

A Jornada.Api é a fronteira oficial entre os sistemas dos Gestores e a plataforma. Todo endpoint funcional exige código + chave de acesso válidos, TLS, correlation id, Policy Engine e auditoria. A credencial pode ser GESTOR, BENEFICIO ou SERVICO conforme a rota e os scopes concedidos. As rotas de Pessoa e a resolução CPF→UUID admitem credencial de Tipo quando ela tiver autorização explícita; isso não amplia os recursos do Tipo nem transfere à Jornada o controle dos usuários internos do sistema finalístico.

A API nunca expõe lista de candidatos probabilísticos de linkage. Também não transforma Possibilidade em direito, concessão ou encaminhamento automático.

#### 10.2. Resolução de identidade: CPF → UUID; dados auxiliares para qualidade/fallback

Endpoint:

POST /api/v1/identidade/resolver

A chamada exige uma credencial Jornada válida, vinculada ao Gestor responsável, com o scope jornada.identidade.resolve. A credencial pode ser GESTOR, BENEFICIO ou SERVICO; credenciais de Tipo permanecem limitadas ao próprio recurso e aos scopes concedidos, mas não a uma população histórica de Pessoas. CPF é enviado no corpo, não na URL, reduzindo exposição em proxies/logs. CPF válido resolve deterministicamente somente quando a associação corrente está ATIVA. Se o identificador estiver EM_CONFLITO, a resposta é CONFLITO / CPF_EM_CONFLITO_IDENTIDADE, sem UUID. Nome completo, data de nascimento e nome da mãe continuam sendo evidências de qualidade/consistência; divergência forte em CPF já conhecido bloqueia a associação antes da Gold factual.

Exemplo:

{
  "cpf": "11144477735"
}

Resposta normal por CPF: { "status": "RESOLVIDO", "pessoaUuid": "...", "metodo": "CPF_DETERMINISTICO" }. A resposta pública não contém score técnico, LLR, posterioridade, limiar superior/inferior, lista de candidatos ou versão de modelo. A interface semicega de busca autorizada é contrato separado.

Estados permitidos:

• RESOLVIDO — vínculo aceito internamente conforme modelo/política;

• NAO_RESOLVIDO — evidência insuficiente;

• CONFLITO — associação ambígua/incompatível.

Nenhum estado retorna UUIDs candidatos alternativos.

#### 10.2.1. Conflito e correção governada de identidade

POST /api/v1/identidade/conflitos/detalhe retorna, somente a credencial institucional GESTOR com scope jornada.identidade.conflitos.read, os núcleos observados associados a um CPF em conflito. A resposta é para workflow restrito de apuração e não pertence ao BI. POST /api/v1/identidade/correcoes exige scope jornada.identidade.corrigir e recebe agrupamentos explicitamente decididos pelo Gestor, grupo titular, ato_referencia e justificativa. A Jornada não escolhe automaticamente qual observação está correta. A aplicação da correção é serializável, auditável e preserva o estado anterior de cada vínculo.

Quando um CPF entra em EM_CONFLITO, a associação determinística anterior deixa de responder em /identidade/resolver e novas observações não podem reutilizá-la. A correção governada encerra o mapa conflitado, reassocia as observações aos UUIDs informados/criados, cria novo mapa ATIVO somente para o grupo declarado titular e atualiza somente os vínculos e a atribuição canônica dos fatos já materializados, sem reabrir lotes nem reescrever o sujeito declarado. Não existe CRUD administrativo genérico de UUID.

#### 10.3. Depósito de dados

Endpoint:

POST /api/v1/ingestao/entregas

O contrato detalhado está no item 6. O Gestor envia um único ZIP application/zip v2 contendo exatamente manifest.json, pessoas.jsonl e registros.jsonl. O arquivo factual pode estar vazio; quando possuir linhas, o manifesto deve declarar Natureza, codigoTipo e tipoVersao. A API gera entregaId e retorna 202 Accepted; não existem loteSeq/loteTotal nem número de versão do registro fornecido pelo cliente. O Processor conclui o pipeline posteriormente.

#### 10.4. APIs de retorno depois do UUID

Depois que o consumidor possui o PESSOA_UUID, a Jornada oferece três famílias semanticamente distintas de retorno: dados cadastrais da Pessoa por projeção autorizada; Registros factuais; e Possibilidades calculadas.

##### A consulta cadastral lê a Gold Pessoas e aplica duas camadas cumulativas: (1) o JSON Schema autorizado da credencial, que define a projeção municipal contratada; e (2) eventuais restrições versionadas declaradas pelo Gestor finalístico responsável pela informação. Na ausência de restrição ATIVA aplicável, prevalece a projeção municipal padrão. O núcleo de identificação não é ocultado de um GESTOR autorizado apenas porque ele ainda não produziu observação própria da Pessoa.

A consulta cadastral lê a Gold Pessoas e projeta somente os atributos permitidos pelo contrato selecionado pela credencial. O PESSOA_UUID retornado é o UUID canônico; quando o UUID solicitado foi absorvido por FUSAO_HISTORICA, o envelope preserva pessoaUuidSolicitado e redirecionadoPorFusao nos metadados. Os dados retornados obedecem ao JSON Schema autorizado e nunca constituem uma exposição irrestrita da Gold.

Endpoints:
GET  /api/v1/pessoas/{uuid}
POST /api/v1/pessoas/consulta

O GET consulta um UUID individual. O POST recebe um conjunto de UUIDs no corpo, por exemplo { "pessoaUuids": ["...", "..."] }, com limite operacional configurável, evitando listas extensas em URL e logs de proxy.

Existem três formas de autenticação para esses endpoints:
A) Código do Gestor + chave do Gestor → seleciona config/contracts/gestores/<GESTOR>/pessoa/vN/pessoa.schema.json;
B) Código do Tipo de Benefício + chave do Benefício → seleciona o pessoa.schema.json da versão autorizada daquele Tipo de Benefício;
C) Código do Tipo de Serviço + chave do Serviço → seleciona o pessoa.schema.json da versão autorizada daquele Tipo de Serviço.
A chave deve estar vinculada ao código apresentado. Credenciais de Benefício ou Serviço só podem usar o schema, os scopes, o recurso e o Gestor a que o respectivo Tipo pertence.

A resposta usa envelope estável e projeção variável. `dados` deve validar contra o schema selecionado; `metadados` descreve a interpretação corrente da Gold e fica fora do schema cadastral. Exemplo: { "pessoaUuid": "<uuid-canônico>", "dados": { ... }, "schemaRef": "...", "metadados": { "fontesDistintas": 3, "estadoConcordancia": "CORROBORADO", "atualizadoEm": "2026-08-30T12:00:00-03:00", "pessoaUuidSolicitado": "<uuid-solicitado>", "redirecionadoPorFusao": false } }. Campos existentes na Gold mas ausentes do schema ou suprimidos por regra de projeção não são retornados em `dados`. A ausência de um atributo na resposta não prova inexistência na origem/Gold. A API não retorna marcador RESTRITO, pois isso poderia revelar a própria existência de dado não projetável. Para consulta em lote, cada item preserva o UUID solicitado e o canônico correspondente.

##### 10.4.2. Registros — fatos já ocorridos

Registros são a projeção cronológica dos fatos Golden já ocorridos e vinculados ao UUID. Na Fase 1, gold.v_registros_jornada reúne Benefícios Concedidos e Serviços Prestados para formar o Histórico da Jornada, preservando origem, Tipo, temporalidade e Natureza.

Endpoints:

GET /api/v1/pessoas/{uuid}/registros
GET /api/v1/pessoas/{uuid}/beneficios-concedidos
GET /api/v1/pessoas/{uuid}/servicos-prestados

Filtros permitidos, conforme endpoint/política:

?codigo=AA01
?desde=2026-01-01
?ate=2026-08-28
?natureza=BENEFICIO|SERVICO

A resposta genérica de Registros conserva somente os campos comuns necessários ao Histórico da Jornada: Natureza, código/nome do Tipo, Gestor responsável, data de referência/ocorrência, situação e procedência. Os atributos específicos não são achatados nessa projeção. GET /beneficios-concedidos usa serving.v_beneficios_pessoa e retorna atributos próprios de Benefício; GET /servicos-prestados usa serving.v_servicos_pessoa e retorna atributos próprios de Serviço, como dataHoraServico e unidadeServico, conforme autorização.

O endpoint genérico /registros é derivado de gold.v_registros_jornada e constitui o Histórico da Jornada. Ele não obriga Benefícios Concedidos e Serviços Prestados a compartilharem a mesma estrutura física: gold.beneficio_concedido e gold.servico_prestado permanecem especializados e podem sustentar /beneficios-concedidos e /servicos-prestados com maior riqueza de campos.

##### 10.4.3. Possibilidades — compatibilidades calculadas

Possibilidades são resultados derivados pela plataforma a partir do perfil municipal disponível e de uma implementação versionada de critérios de compatibilidade. Elas não representam fato ocorrido, cadastro existente, concessão, direito adquirido nem decisão administrativa.

Endpoint:

GET /api/v1/pessoas/{uuid}/possibilidades

Filtros:

?natureza=BENEFICIO|SERVICO
?codigo=AA01

Resposta típica:

{
  "pessoaUuid": "...",
  "sujeitoAnaliseDoGestorResponsavel": true,
  "itens": [
    {
      "natureza": "BENEFICIO",
      "codigo": "AA01",
      "nome": "Auxílio Aluguel",
      "gestor": "SEHAB",
      "regraVersao": "AA01.POSS.v2",
      "avaliadoEm": "...",
      "validadeAte": "...",
      "motivo": "Critérios preliminares compatíveis com os dados disponíveis."
    }
  ]
}

A formulação visual/API obrigatória é equivalente a “possibilidade compatível com o perfil, sujeita à análise do Gestor responsável”.

#### 10.5. Avaliador de Possibilidades

QC e Possibilidade são mecanismos diferentes:

QC de um Registro existente
→ pergunta se um fato/concessão observado é coerente com as regras aplicáveis.

PossibilityEvaluator
→ pergunta se, diante do perfil atual, um Benefício/Serviço parece compatível para consideração futura.

Cada Tipo/versão que participar da função de possibilidades pode registrar uma implementação versionada compatível com IPossibilityEvaluator.

Resultado interno:

**COMPATIVEL
NAO_COMPATIVEL
NAO_AVALIAVEL**

Somente COMPATIVEL é exposto em serving.v_possibilidades_compativeis. NAO_AVALIAVEL não deve ser tratado como incompatibilidade.

A avaliação pode ser materializada pelo Processor quando houver alteração relevante da Gold ou da versão do avaliador. O GET deve preferencialmente servir a materialização vigente, evitando executar regras complexas de todos os Benefícios/Serviços dentro da requisição HTTP.

#### A decisão de acesso combina: código autenticado + chave de acesso validada + tipo da credencial (GESTOR | BENEFICIO | SERVICO) + Gestor responsável derivado do cadastro + scope + Natureza/Tipo solicitado + JSON Schema autorizado + propriedade do recurso quando a credencial for de Tipo + restrições versionadas da projeção + políticas setoriais/municipais + estado de resolução da Pessoa. Não integra essa decisão qualquer vínculo ou fato prévio da Pessoa com a credencial, nem motivo de consulta declarado pelo consumidor.

A Fase 1 não usa X-Jornada-Finalidade como cabeçalho obrigatório nem mantém catálogo/allowlist técnico de motivos de consulta. A Jornada não consegue verificar a veracidade de um motivo declarado pelo consumidor; a autorização é baseada em fatos objetivos da credencial, scopes e recurso. Base legal, finalidade e necessidade continuam obrigações institucionais/LGPD fora desse mecanismo técnico.

Cardinalidade é propriedade do endpoint. POST /api/v1/pessoas/consulta aceita de 1 a 1000 UUIDs por requisição; não existe controle especial PRIMEIRO_ATENDIMENTO. Proteção contra enumeração e abuso usa rate limiting por CredentialId autenticado e classe de endpoint, além de auditoria por chamada.

Qualquer credencial que possua o scope funcional pode localizar e consultar a Pessoa sem demonstrar relacionamento corrente de origem. Isso não amplia automaticamente a projeção: a credencial de Tipo continua confinada ao próprio recurso/schema e atributos transversais, Registros e Possibilidades passam pela projeção municipal contratada e por controle.restricao_projecao_jornada_versao.

Para reduzir enumeração por credenciais técnicas de Tipo, falhas de autorização ou de propriedade do recurso em consultas por UUID retornam recurso não localizado quando aplicável. A resolução CPF→UUID aceita credencial GESTOR, BENEFICIO ou SERVICO quando houver o scope jornada.identidade.resolve; não existe máscara de uma Pessoa existente por ausência de vínculo ou fato setorial prévio. Toda resolução continua auditada sem CPF do munícipe em logs/auditoria HTTP.

POST /api/v1/pessoas/consulta autoriza o conjunto de UUIDs atomicamente, respeita a cardinalidade do endpoint e registra todos os alvos em controle.api_evento_pessoa. Nenhuma credencial autorizada executa filtro por relacionamento prévio; a resposta continua não enumerável quanto a falhas de autorização/projeção.

10.6.1. Restrições versionadas de projeção. controle.restricao_projecao_jornada_versao registra somente exceções negativas ao compartilhamento padrão. Cada declaração identifica Gestor finalístico responsável, regra, versão, vigência, consumidor específico opcional, alvo (ATRIBUTO_PESSOA, REGISTRO ou POSSIBILIDADE), fundamento legal, justificativa e ato_referencia do processo/ato institucional. O alvo é validado antes de uma regra tornar-se ATIVA, evitando exceção inválida que falhe aberta por erro de código. O conteúdo de uma versão é imutável; mudança material exige nova versão. Ausência de regra ATIVA aplicável significa permitido pela projeção municipal padrão. A restrição pode reduzir o que é retornado, nunca criar campo, scope ou permissão inexistente. Na migração v3.46→v3.47, regras antes condicionadas a finalidade são preservadas como exceções negativas sem essa condição, evitando abertura silenciosa de acesso.

10.6.2. Identificação opcional do agente pela finalística. O sistema consumidor pode enviar X-Jornada-Agente-CPF. A Jornada não define como o Gestor autentica seus agentes, não exige gov.br/AD e não verifica a associação entre o usuário autenticado e o CPF informado; essa responsabilidade permanece integralmente com a Secretaria/Gestor. Quando presente, o CPF deve ser válido. A API remove o header antes de prosseguir, calcula HMAC-SHA-256 determinístico com chave secreta e versão da instalação e persiste apenas agente_cpf_hash BINARY(32) e agente_hash_versao em controle.api_evento. Chamadas automatizadas podem omitir o campo.

#### 10.7. Scopes mínimos sugeridos

jornada.identidade.resolve
jornada.ingestao.pessoas.write
jornada.ingestao.write
jornada.ingestao.status
jornada.pessoas.read
jornada.registros.read
jornada.possibilidades.read

Scopes são permissões internas associadas à credencial e não substituem Código + chave ou schema. O consumidor precisa simultaneamente de credencial válida, scope e contrato/recurso autorizado. O código apresentado nunca é aceito como parâmetro livre para ampliar escopo.

#### 10.8. Logs de API

Logs operacionais registram rota template, método, cliente, Gestor, correlation id, status HTTP, duração e volume, sem corpo integral, CPF do munícipe, nome, tokens ou segredos. A trilha restrita controle.api_evento registra também recurso lógico, UUID-alvo quando único e, quando a finalística o informar, somente o HMAC versionado do CPF do agente. Consultas em lote registram todos os UUIDs em controle.api_evento_pessoa. O CPF do agente em claro é removido do header antes de o restante do pipeline executar e não integra logs, BI ou mensagens de erro.

### 11. Camada 4 — Referência Territorial da Pessoa

Referência Territorial da Pessoa é o vínculo territorial temporal utilizado para análise e cobertura de política pública. Não é sinônimo de domicílio civil nem de endereço cadastral de residência e não recria a antiga entidade autônoma Território de Referência. Distrito é a unidade territorial; Subprefeitura é o órgão que administra um conjunto de Distritos. Tanto o vínculo da Pessoa quanto a hierarquia Distrito→Subprefeitura podem mudar no tempo e devem ser historicamente preservados.

Regra de visualização territorial. Toda visualização, agregação ou mapa da Jornada deve usar a Referência Territorial selecionada e seu snapshot de Distrito/Subprefeitura, nunca ENDERECO_RESIDENCIAL diretamente. Quando a referência selecionada possuir endereço postal, esse endereço é o Endereço de Referência daquela observação. Quando a referência for exclusivamente territorial, a visualização usa Distrito/Subprefeitura sem inventar CEP, logradouro ou número. O endereço cadastral de residência só influencia a visualização depois de materializado como referência DOMICILIAR de fallback.

A natureza da referência é domínio fechado: DOMICILIAR, ACOLHIMENTO_INSTITUCIONAL ou REFERENCIA_TERRITORIAL_DECLARADA. DOMICILIAR pode ser formada a partir de ENDERECO_RESIDENCIAL. ACOLHIMENTO_INSTITUCIONAL exige declaração explícita da fonte de que o endereço institucional é a referência da Pessoa naquele período. REFERENCIA_TERRITORIAL_DECLARADA admite Distrito/Subprefeitura declarados mesmo sem CEP, logradouro ou número.

Gestor declara REFERENCIA_TERRITORIAL
        ↓
natureza = DOMICILIAR | ACOLHIMENTO_INSTITUCIONAL | REFERENCIA_TERRITORIAL_DECLARADA
        ↓
situacaoGeografia explícita
        ├─ RESOLVIDA → Gestor fornece Distrito/Subprefeitura + referenciaMalha → preservar ORIGEM
        └─ FORA_MUNICIPIO | SEM_ENDERECO_APTO | NAO_RESOLVIDA_ORIGEM → geografia nula e motivo preservado
        ↓
Jornada valida e persiste; não existe chamada Pessoa-a-Pessoa à PRODAM na Fase 1

Regras:

• é proibida inferência silenciosa: a Jornada nunca transforma automaticamente unidade de serviço, local de atendimento, UBS, CRAS, Centro POP, equipamento, abrigo ou outro endereço institucional em referência territorial apenas porque é o único local disponível. A fonte autorizada pode declarar explicitamente uma referência institucional ou territorial; a plataforma apenas preserva/processa essa declaração;

• natureza_referencia_territorial e origem_geografia são dimensões independentes. A natureza é declarada pela fonte; na Fase 1 novas classificações Distrito/Subprefeitura são fornecidas pelo Gestor e persistidas como ORIGEM. ENRIQUECIMENTO_PRODAM pode existir apenas como valor histórico de upgrade e não é produzido pelo fluxo operacional;

• a classificação é snapshot da observação exata selecionada como Referência Territorial. O snapshot é persistido em silver.referencia_territorial_observacao como fonte geográfica única; a antiga persistência paralela de geografia residencial foi removida após migração idempotente. Mudança posterior de endereço, vínculo territorial ou hierarquia Distrito→Subprefeitura não reclassifica retroativamente fatos históricos. Referência territorial declarada diretamente pode existir sem endereço postal, evitando inventar logradouro para população em situação de rua ou outros casos sem domicílio convencional;

• ausência de territorialização resolvida não dispara chamada externa: o Gestor deve qualificar situacaoGeografia. NAO_RESOLVIDA_ORIGEM, SEM_ENDERECO_APTO e FORA_MUNICIPIO são mensurados explicitamente; a Jornada não tenta completar esses dados Pessoa-a-Pessoa;

• na materialização factual, a nomenclatura normativa é distrito_referencia_id, subprefeitura_referencia_id e geografia_referencia_observacao_id (ou equivalentes físicos), preservando no fato a referência exata usada naquele processamento. Até a migração física da Solution v3.27, os nomes residenciais existentes são legado de implementação e não autorizam reinterpretar local do fato/serviço como referência da Pessoa;

• versões observadas de Distrito/Subprefeitura são identidades de referência para preservar histórico; sua ordem de ingestão não é tratada como vigência jurídica oficial da malha;

• o BI deve permitir filtro/segmentação por Natureza da Referência Territorial e por Distrito/Subprefeitura, distinguindo população domiciliada, acolhida e com referência territorial declarada. Mapas e recortes territoriais consomem somente a geografia da referência selecionada; o endereço cadastral de residência não é dimensão direta de visualização. Não existe tabela/página autônoma de Território de Referência nem endpoint geográfico ou API da Jornada para descobrir Serviços por Distrito/Subprefeitura.

• o BI mantém filtros e agregações por Distrito/Subprefeitura a partir dos snapshots persistidos. Não existe, por consequência, necessidade de tabela/página autônoma de Território de Referência nem API de Serviços por território.

Regra de agregação mínima. Visualizações geográficas operam sobre dados agregados. Nenhuma camada de mapa exibe pessoa identificável. Recortes abaixo do mínimo definido pela governança são suprimidos.

#### 11.1. Sigilo de endereços de equipamentos de abrigo

A Jornada distingue ENDERECO_RESIDENCIAL de ENDERECO_CASA_ABRIGO_SIGILOSA. O segundo código representa exclusivamente o endereço do equipamento de casa-abrigo-sigilosa informado pelo próprio serviço responsável; não representa condição pessoal, marcador de risco ou classificação geral da Pessoa.

A origem é fail-closed: o Processor aceita ENDERECO_CASA_ABRIGO_SIGILOSA somente em Entrega de Natureza SERVICO vinculada a uma versão de Tipo com origina_endereco_casa_abrigo_sigilosa=1. Tipos cadastrais, benefícios e demais serviços não habilitados não podem declarar o atributo.

A projeção também é fail-closed entre Gestores: ENDERECO_CASA_ABRIGO_SIGILOSA nunca é retornado a Gestor consumidor diferente do Gestor responsável, ainda que não exista restrição manual em controle.restricao_projecao_jornada_versao. Os demais atributos, fatos e dados da Pessoa continuam submetidos às regras normais de compartilhamento municipal.

O atributo sigiloso não pode possuir naturezaReferenciaTerritorial, geografia ou situacaoGeografia e não participa da seleção de REFERENCIA_TERRITORIAL. A Jornada não infere casa-abrigo-sigilosa a partir de ACOLHIMENTO_INSTITUCIONAL, unidade de atendimento ou endereço institucional.

### 12. Camada 5 — Business Intelligence e serviços de consulta

O Power BI consome contratos estáveis e autorizados de Serving diretamente no banco relacional operacional adotado em cada ambiente: Microsoft SQL Server ou SQL Database in Microsoft Fabric. OneLake e SQL Analytics Endpoint não são componentes obrigatórios nem substitutos das views Serving transacionais. Modelos PBIP devem ser abertos, validados e salvos no Power BI Desktop homologado antes da publicação.

#### 12.1. Dimensões transversais

Sempre que semanticamente aplicável, os dashboards devem oferecer Gestor, tempo, Tipo de Benefício/Serviço, Subprefeitura e Distrito como filtro e/ou agregação.

Referência territorial é opcional no dado, mas sua ausência deve permanecer explícita como SEM_REFERENCIA_TERRITORIAL, sem alterar silenciosamente denominadores. Quando disponível, os dashboards devem permitir segmentação por NATUREZA_REFERENCIA_TERRITORIAL além de Distrito/Subprefeitura.

#### 12.2. Consulta individual nos sistemas finalísticos

Quando um sistema finalístico optar por apresentar informações da Jornada durante o atendimento, sua interface deve separar claramente:

1. Registros conhecidos — fatos já observados na Jornada: Benefícios Concedidos, Serviços Prestados e outras atividades vinculadas ao PESSOA_UUID;

2. Possibilidades compatíveis — oportunidades derivadas pelo PossibilityEvaluator, sempre acompanhadas da ressalva de análise pelo Gestor responsável e sem alterar automaticamente a base setorial.

A interface finalística nunca deve misturar uma possibilidade com um registro factual. A Jornada fornece os serviços separadamente; composição, experiência de atendimento e persistência do ato permanecem no sistema do Gestor.

Indisponibilidade das consultas da Jornada não deve impedir o registro do atendimento no sistema finalístico quando o procedimento setorial puder prosseguir. A publicação à Jornada pode ser retomada por outbox/retry idempotente após a recuperação do serviço.

#### 12.3. Formulação de elegibilidade

É vedado exibir “tem direito”, “benefício garantido”, “elegível definitivamente” ou equivalente com base apenas na Jornada. A formulação padrão é:

Benefícios e serviços compatíveis com o perfil disponível, sujeitos à análise do Gestor responsável.

#### 12.4. Projeção integrada de Registros

gold.v_registros_jornada une gold.beneficio_concedido e gold.servico_prestado por PESSOA_UUID e compõe o Histórico da Jornada. serving.v_registros_pessoa é a projeção comum; detalhes especializados vêm de serving.v_beneficios_concedidos_pessoa e serving.v_servicos_prestados_pessoa. Benefício Concedido preserva data_inicio_concessao, data_fim_concessao, data_evento_concessao, valor_concedido, quantidade e unidade_medida. Serviço preserva data_hora_servico, unidade_servico, situação e demais atributos contratados. regime_vigencia e a janela permitida pertencem à versão do Tipo, não ao payload do cidadão.

O campo natureza identifica BENEFICIO ou SERVICO. Novas naturezas somente entram por versão formal do contrato.

#### 12.5. Projeção de Possibilidades

serving.v_possibilidades_compativeis contém somente avaliações vigentes COMPATIVEL, incluindo UUID, Natureza, Tipo, Gestor, versão da implementação, data de avaliação, validade e motivo explicável.

Possibilidades expiradas ou calculadas com versão superada devem ser recalculadas ou retiradas da view vigente.

#### 12.6. Dashboards básicos obrigatórios

##### 12.6.1. Visão Geral

Resumo executivo de Gestores integrados, Pessoas alcançadas, Tipos de Benefício/Serviço, cobertura de identidade, saúde de ingestão, qualidade e alertas prioritários.

##### 12.6.2. Ingestão e Atualidade

Filtros: Gestor, Natureza, Tipo, período e status. Indicadores: Entregas recebidas/completas/incompletas/rejeitadas, lotes recebidos/total, idade do dado, tempo de processamento e cobertura geográfica dos dados processados.

#### 12.6.2.1. Qualidade dos Pacotes

Visão baseada em serving.v_bi_qualidade_envios, com filtros por Secretaria/Gestor, Sistema de Origem, Natureza/Tipo e período. Indicadores mínimos: Entregas recebidas/processadas/com falha, bytes, Pessoas e Registros recebidos, zero fatos no período, inclusões, novas versões, retransmissões idênticas e taxa de retransmissão. O objetivo é distinguir problema de contrato/processamento de simples redundância de envio e permitir comparar a qualidade operacional entre origens.

##### 12.6.3. API de Ingestão

Indicadores por Gestor/rota/período: requisições, 2xx/4xx/5xx, erros, latência média/p95, bytes recebidos, conflitos de idempotência, rate limiting de borda e pós-autenticação, recurso e última atividade. Volumes anômalos devem ser acompanhados por Gestor, credencial e classe de endpoint, com limiares operacionais calibrados em HML. serving.v_bi_api não expõe PESSOA_UUID, HMAC do agente, credencial_id nem api_evento_id; investigação individual utiliza a camada controle sob acesso restrito.

##### 12.6.4. Processamento

Fila de lotes RECEBIDO/PROCESSANDO, idade do mais antigo, duração por estágio, rejeições/quarentena e progresso da Entrega Lógica. O processamento inicial sequencial deve ser visível operacionalmente.

##### 12.6.5. Qualidade da Pessoa

Completude/validade dos quatro atributos, CPF ausente por motivo, cobertura de Referência Territorial, distribuição por Natureza da Referência, profiling e principais ocorrências por Gestor/Tipo/tempo/Subprefeitura/Distrito quando disponíveis.

##### 12.6.6. Linkage e Convergência

Cobertura de PESSOA_UUID, CPF determinístico, percentual de observações com CPF ausente, qualidade de blocking multipasse, recuperação por pass, pessoas e fontes elegíveis, Rank/Resolve V9 por identidade-base, vínculos aceitos, abstenções, conflitos, criação de identidade, falsas criações e resultados físicos quando houver gabarito. Reportar denominadores e coortes sem impor score ou limiar histórico fixo.

#### 12.6.6.1. Qualidade de Identidade por Origem

Qualidade por Gestor e fonte: volume de observações com CPF válido/ausente/inválido, taxa de consistência CPF→UUID, cobertura do blocking multipasse, alternativas recuperadas, ausência verdadeira de candidato versus limitação técnica, vínculos V9 aceitos e abstidos, diferença entre limiares superior/inferior, margem entre identidades, distribuição de razões de pendência/conflito e idade do estoque. Os painéis devem distinguir identidade-base, observação de origem e fato administrativo; medir falsos vínculos e falsas criações com unidades e denominadores explícitos, nunca consolidar por um score fixo de aceitação ou score = 1,0.

O Monitor e o BI exibem modelos, snapshots, estado de autorização e evidências recentes para observabilidade somente leitura. Eles não oferecem escolha clerical de candidatos, seleção manual de parâmetros ou botão de ativação. A aprovação é executada pela área de governança autenticada no fluxo de modelos, com ledger, dossiê e permissão próprios.

##### 12.6.7. Quality Control

Cobertura IMPLEMENTADO/NAO_IMPLEMENTADO, VALIDO/DIVERGENTE/NAO_VERIFICAVEL, regras mais divergentes, Tipo/versão, Gestor, tempo e geografia.

##### 12.6.7.1. Qualidade por Secretaria e Benefício Concedido

Visão especializada baseada em serving.v_bi_qualidade_beneficios_concedidos. Deve permitir comparação Gestor × Benefício e período, mostrando separadamente entrega completa, cobertura territorial, preenchimento conforme tipo_medida, coerência da vigência com regime_vigencia, concessão dentro da janela permitida, cobertura/resultado de QC, versionamento e retificação. Não se adota score sintético único de qualidade.

##### 12.6.7.2. Qualidade por Secretaria e Serviço Prestado

Visão especializada baseada em serving.v_bi_qualidade_servicos_prestados. Deve permitir comparação Secretaria/Gestor × Serviço e período. São comuns entrega, Referência Territorial/Natureza da Referência, QC, versionamento e retificação; unidade_servico e situacao são taxas de preenchimento e não devem ser classificadas automaticamente como erro quando opcionais no contrato do Tipo.

##### 12.6.8. Executivo de Benefícios Concedidos

Pessoas distintas, Gestores, Tipos, concessões vigentes/encerradas, sobreposição entre Benefícios/Gestores e alcance geográfico.

##### 12.6.9. Executivo de Serviços Prestados

Pessoas distintas com Serviços, Gestores, Tipos de Serviço, volume de Serviços, distribuição temporal/geográfica, situação e unidades de Serviço. A tela usa serving.v_bi_servicos_prestados e não reutiliza métricas monetárias próprias de Benefícios Concedidos.

##### 12.6.10. Atraso de Recebimento

Painel transversal de Benefícios e Serviços baseado em serving.v_bi_atrasos. Indicadores mínimos: Registros monitorados, Registros em atraso, percentual em atraso, percentual no prazo, atraso médio, p95 e atraso máximo. Filtros: Natureza, Gestor, Tipo, período de recebimento, Subprefeitura/Distrito quando disponíveis e situação do atraso. recebido_em é o instante oficial UTC gerado pelo servidor da API; data_recebimento_sla é sua data civil no fuso institucional de São Paulo.

A classificação é derivada da versão do Tipo. dias_para_recebimento = diferença em dias corridos entre o marco configurado e data_recebimento_sla; não se deve converter diretamente recebido_em UTC para DATE. dias_atraso = max(dias_para_recebimento - prazo_recebimento_dias, 0). Faixas recomendadas: NO_PRAZO, 1-3_DIAS, 4-7_DIAS, 8-30_DIAS e 31+_DIAS, mantendo SEM_MARCO e DATA_FUTURA visíveis separadamente.

Para evitar falsos alarmes, o dashboard interpreta datas de Benefício segundo o contrato versionado. data_fim_concessao ausente é coerente para PRAZO_INDETERMINADO e divergente para PRAZO_DETERMINADO; data_inicio_concessao fora da janela permitida é sinalizada. O SLA continua usando marco_atraso_codigo explícito, sem inferir um marco apenas pela presença de uma data.

##### 12.6.11. Monetário

Somente registros com valor_concedido. Exibe soma, média, mediana e distribuição por Gestor/Tipo/tempo/geografia quando disponível. A própria tela deve declarar que se trata de valor da concessão informado pelo Gestor, não de pagamento ou despesa orçamentária:

Soma dos valores declarados das concessões. Não representa pagamento ocorrido, despesa orçamentária ou desembolso.

valor_concedido nunca é somado a quantidade nem interpretado como valor pago/recebido.

##### 12.6.12. Registros

Quantidade de Registros e Pessoas com Registros, distribuição por Natureza, Tipo, Gestor, tempo e geografia quando disponível; inclui análise específica de Serviços Prestados e Benefícios Concedidos conhecidos.

##### 12.6.13. Possibilidades Compatíveis

Quantidade de Pessoas com Possibilidades, Tipos mais frequentemente compatíveis, distribuição por Gestor/Natureza/geografia quando disponível, idade/validade das avaliações e cobertura de avaliadores implementados. O painel deve declarar que possibilidade não é registro nem direito.

##### 12.6.14. Pendências de Identidade

Estoque de CPF ausente, PENDENTE_PROBABILISTICO, NAO_RESOLVIDO e CONFLITO, com prioridade, motivo, Gestor, idade da pendência, P50/P95 e faixas de envelhecimento. Não apresenta candidatos probabilísticos para seleção humana.

##### 12.6.15. Catálogo

Consulta somente leitura de Tipos, versões, vigências, contratos, tipo_medida, regime_vigencia, janela permitida de concessão, configuração de SLA de recebimento, QC e status de avaliadores de Possibilidades. A Fase 1 não exige CRUD administrativo; a fonte autoral é configuration-as-code/processo de governança.

#### 12.7. Fonte Power BI

O fonte do Power BI da Fase 1 fica em `bi/`. `bi/Jornada.pbip` aponta para `bi/Jornada.Report/` e `bi/Jornada.SemanticModel/`. O modelo consome os contratos serving.v_bi_* e utiliza database/Jornada_Seed_Dev.sql como massa sintética. Em DEV, servidor/banco apontam para SQL Server 2022 Developer local. Em HML/Produção, a conexão parametrizada aponta diretamente para o banco operacional selecionado — SQL Server institucional ou SQL Database in Microsoft Fabric — com credencial e permissões homologadas, sem obrigatoriedade de SQL Analytics Endpoint ou OneLake e sem alterar métricas, telas ou regras do modelo.

serving.v_bi_registro_versoes é view de apoio/auditoria para consulta técnica do histórico lógico e não integra, por padrão, o modelo semântico Power BI. Sua ausência do TMDL é intencional para evitar cardinalidade/volume sem necessidade de painel.

#### 12.8. Validação independente evolutiva

Permanece possível adicionar, fora do fluxo decisório oficial, módulo independente de IA/challenger para comparar linkage, regras declaradas, implementações QC/Possibilidade e resultados. A indisponibilidade desse módulo não pode interromper a Jornada.

### 13. Segurança

#### 13.1. Princípios

A Fase 1 adota autenticação máquina-a-máquina por código + chave de acesso, segregação por Gestor/Tipo autorizado, menor privilégio, TLS, auditoria, gestão externa de segredos e minimização de dados. O Código do Gestor e a chave do Gestor são obrigatórios nos endpoints funcionais padrão. A consulta cadastral de Pessoa admite também Código do Tipo de Benefício + chave específica do Benefício ou Código do Tipo de Serviço + chave específica do Serviço.

A identidade do Gestor deve ser derivada do cliente autenticado. Nenhuma rota de escrita deve confiar em código de Gestor informado livremente pelo chamador.

#### 13.2. Chaves, credenciais e certificados mínimos

| Ativo | Uso | Regra |
| --- | --- | --- |
| Código do Gestor | Identificação institucional do consumidor padrão da API | Valor público; deve mapear para controle.credencial_api e nunca autoriza sem a chave correspondente. |
| Chave de acesso do Gestor | Autenticação máquina-a-máquina dos endpoints do Gestor | Segredo fora de Git, payload, URL, log e banco funcional; rotação sem recompilação. |
| Código do Tipo de Benefício + chave do Benefício | Sobrecarga autorizada da consulta cadastral de Pessoa | Credencial própria do Benefício, vinculada ao Gestor responsável e ao pessoa.schema.json da versão autorizada. |
| Certificado TLS da API | HTTPS | Gerido pela infraestrutura de HML. |
| Credencial SQL da API | Recepção/consultas | Menor privilégio sobre controle/ingestão/bronze e views autorizadas. |
| Credencial SQL do Processor | Pipeline | Permissões apenas nos schemas necessários a processamento/materialização. |
| Credencial SQL do Parameters Worker | Frequências/linkage | Escrita limitada às estruturas de modelos/parâmetros e leitura das referências necessárias. |
| Territorialização de origem por Gestor \| Na Fase 1 não há credencial geográfica da Jornada; quando RESOLVIDA, a origem fornece Distrito/Subprefeitura e referenciaMalha. | CEP/endereço → Distrito/Subprefeitura | Secret externo; acesso somente pelo Processor. |
| Credencial de fonte setorial para Parameters Worker | Não aplicável | O Parameters Worker lê somente gold.pessoa; não possui credencial especial de qualquer Gestor. |
| Credencial Power BI/Gateway | Views serving.v_bi_* | Somente leitura. |
| Código do Tipo de Serviço + chave do Serviço | Sobrecarga autorizada da consulta cadastral de Pessoa | Credencial própria do Serviço, vinculada ao Gestor responsável e ao pessoa.schema.json da versão autorizada. |
| Credencial SQL do Linkage Runner | Execução de scores / runs | Leitura de Silver/Gold e modelo versionado; escrita em linkage_run/linkage_resultado; sem escrita direta em Gold e sem secret funcional. |
| Scope/recurso autorizado da credencial | Controle técnico das operações da Jornada | Scopes e, para credenciais de Tipo, código do próprio recurso/schema; não existe X-Jornada-Finalidade ou allowlist de motivos declarados. |
| Credencial do armazenamento Bronze da API/Processor | Leitura/gravação dos objetos ZIP imutáveis | Segredo/identidade fora do Git; mínimo privilégio na raiz/prefixo Bronze; sem URL pública; API grava e lê quando necessário, Processor lê; objeto_chave não é token de autorização. |

A implementação concreta da chave pode usar API key corporativa, HMAC, certificado ou outro mecanismo aprovado pela infraestrutura da PRODAM, desde que preserve a semântica normativa Código + chave, rotação, revogação e validação forte máquina-a-máquina. O material secreto permanece fora do repositório e do banco funcional.

#### 13.3. Scopes e segregação

Perfis mínimos de permissão são vinculados às credenciais e podem ser expressos por scopes internos. Código e chave identificam o contexto; scope e recurso determinam o que esse contexto pode fazer. Credenciais de Benefício e de Serviço não herdam automaticamente todas as permissões do Gestor responsável.

jornada.identidade.resolve
jornada.ingestao.pessoas.write
jornada.ingestao.write
jornada.ingestao.status
jornada.pessoas.read
jornada.registros.read
jornada.possibilidades.read

Scopes são associados ao cliente técnico e complementados pela fronteira de recurso e pelos contratos de dados autorizados.

#### 13.4. Gestão de secrets

Arquivos versionados armazenam somente referências, por exemplo:

{
  "sql": "secretRef:JORNADA_SQL_API",
  "gestorKeys": "secretRef:JORNADA_GESTOR_KEYS",
  "beneficioKeys": "secretRef:JORNADA_BENEFICIO_KEYS",
  "servicoKeys": "secretRef:JORNADA_SERVICO_KEYS"
}

Segredos reais de HML/Produção não podem existir em arquivos versionados do repositório, T-SQL, PBIP/TMDL/PBIR, documentação ou logs. Rotação deve ser possível sem recompilação.

As chaves de acesso dos Gestores, Benefícios e Serviços devem ser resolvidas pelo mecanismo corporativo de secrets ou por material de validação seguro; a aplicação nunca persiste a chave em claro. Deve existir rotação independente por credencial, bloqueio/revogação e trilha de auditoria da troca de versão da chave.

##### 13.4.1. Chaves locais para desenvolvimento e testes

A Solution não inclui gerador de chaves. Para Development, o pacote traz config/security/test-access-keys.json com credenciais sintéticas pré-geradas para os Gestores e Tipos de exemplo. Emissão e rotação de credenciais reais pertencem ao mecanismo corporativo de secrets/identidade, não à Jornada.Api.

Jornada.Api registra DevelopmentAccessContextResolver somente quando ASPNETCORE_ENVIRONMENT=Development. Fora desse ambiente, o resolver das chaves de teste não é registrado e a API permanece deny-by-default até a integração corporativa. Portanto, test-access-keys.json é conveniência local e não pode ser reutilizado em HML ou Produção.

As credenciais geradas preservam a mesma semântica normativa da API: Código do Gestor + chave, Código do Tipo de Benefício + chave e Código do Tipo de Serviço + chave. O utilitário não cria chaves produtivas, não grava secrets no banco funcional e não constitui mecanismo de autenticação homologado.

#### 13.5. Logs e dados pessoais

Logs operacionais não registram payload integral, CPF, nome, nome da mãe, token ou segredo. Para a API, registrar rota template e correlation id, evitando transformar observabilidade em réplica de dados pessoais.

#### 13.6. Segurança da API

Além de autenticação/autorização:

• limite de tamanho de corpo e de quantidade de objetos;

• rate limit bruto por IP na borda e limite por CredentialId autenticado/classe de endpoint;

• Idempotency-Key para escrita;

• validação de Content-Type;

• timeouts e limites de conexão;

• proteção contra enumeração abusiva de UUIDs e resolução;

• ausência de candidatos probabilísticos nas respostas;

• auditoria individualizada por credencial/Gestor/rota/recurso e UUID(s) alvo, sem depender de motivo declarado pelo consumidor.

#### 13.7. Política integrada de autenticação, auditoria e LGPD

Jornada.Access.Security centraliza autenticação e autorização no escopo da API e ResultadoApi; credenciais sintéticas com X-Jornada-Gestor e X-Jornada-Access-Key pertencem a DEV e não substituem o IdP individual institucional em HML/Produção. Scopes/policies devem distinguir ingestão, consulta de Pessoa, Monitor, governança de modelos, leitura de identidade e tarefas operacionais; Gestores não recebem implicitamente privilégio master ou dados sigilosos de outra Secretaria. A ausência de finalidade declarada por cabeçalho livre foi decisão arquitetural de 2026, mas base legal, finalidade institucional, responsabilidades, segredo e contratos de projeção precisam de deliberação formal competente, incluindo Núcleo Gestor do Programa Reencontro e gestores responsáveis; portaria anunciada não é norma publicada por antecipação.

Segredos e tokens nunca são gravados em arquivos de remessa, logs, HTML de relatório, pacotes, CI pública nem repositório. Telemetria deve registrar correlation_id, identidade da credencial/escopo, operação, resultado, duração, hash de conteúdo e classes de falha, omitindo nome, CPF, payload integral, conteúdo documental, cabeçalhos de autenticação e outros valores pessoais. Logs de login, acesso, visualização de casos, impressão/exportação e alterações de autorização demandam regras de retenção, consulta e auditoria de acesso documentadas. Criptografia em trânsito e no repouso, separação de secret store, segregação por Gestor, mínimo privilégio, controle de payload sigiloso e rotinas de descarte/retensão são requisitos HML/PROD ainda sujeitos a aceite e inventário técnico da PRODAM.

Consulta do cidadão por API deve seguir minimização, escopos e máscara por atributo; uma referência municipal compartilhada não significa disponibilizar nome civil, localização de abrigo, documento ou condição social sigilosa a todo consumidor. Qualquer espelhamento em BI/OneLake ou download de dados exige política própria; endpoint analítico read-only não herda automaticamente todas as proteções do SQL operacional. O audit trail governado de identidade identifica credencial/autoridade institucional efetiva e operacao_id transacional, não aceita CPF declarado livremente pela fonte como prova de autoria humana. Se a gravação do evento auditável falhar, a alteração de identidade também falha atomicamente.

### 14. Proteção de dados pessoais

#### 14.1. Papéis

Gestores finalísticos como controladoras dos respectivos dados; PRODAM-SP como operadora; encarregado municipal na supervisão.

#### 14.2. Ativos municipais de referência

A IDENTITY_MAP, a Gold Pessoas e os registros de evidência são ativos novos de caráter transversal e não pertencem isoladamente a uma único Gestor.

Devem ter definidos formalmente antes da produção:

• controladoria e papéis LGPD;

• finalidade e base legal;

• regime de acesso;

• política de retenção;

• responsabilidades sobre o ato de verificação presencial;

• critérios para promoção de atributos transversais a valor Golden.

A concentração desses ativos exige controles superiores aos de uma base setorial comum, mas não autoriza centralização indiscriminada de dados de política pública.

#### 14.3. Finalidade e necessidade

O compartilhamento entre Gestores não é automaticamente permitido por ser interno à PMSP. Finalidade, necessidade e base legal devem ser estabelecidas por tipo de compartilhamento, conforme os arts. 7º, 11 e 23 a 26 da LGPD, e registradas no ROT.

Merecem análise específica, por envolverem finalidade distinta da originária:

• uso de dado do CadÚnico como fonte de resolução de identidade;

• uso de informação de óbito, originada em vigilância epidemiológica, para verificação de regularidade de benefício.

#### 14.4. Retenção

A trilha ingestao.item_processado possui perfil de crescimento próprio: uma retransmissão idêntica é relevante para observabilidade, mas não cria nova versão Silver/Gold. implementa ingestao.item_processado_resumo, o procedimento ingestao.sp_consolidar_expurgar_item_processado e Jornada.Operations.Maintenance.Worker. A manutenção permanece desabilitada por padrão até a governança aprovar DetailRetentionDays; a consolidação preserva métricas históricas e a última confirmação RETRANSMITIDO por identidade de origem. HML deve medir crescimento, impacto de log/locks e necessidade real de particionamento antes de Produção.

Não se adota retenção indefinida. Retenção indefinida de dado pessoal é incompatível com os arts. 15 e 16 da LGPD.

| Elemento | Critério |
| --- | --- |
| Lotes na Camada Bronze | Prazo definido pela governança, findo o qual são eliminados |
| Gold Pessoas corrente e histórico | Enquanto perdurar a finalidade cadastral e conforme política específica de versionamento |
| Registros de evidência/verificação | Prazo definido pela finalidade, requisitos de auditoria e base legal |
| Estruturas setoriais derivadas | Enquanto perdurar a finalidade de compartilhamento |
| Eventos de divergência encerrados | Prazo definido, com preservação de registro agregado |
| Registros de acesso e auditoria | Cinco anos |
| Registros de óbito sem correspondência | Descartados ao final do processamento |
| Gold Registros | Enquanto perdurar a finalidade de compartilhamento dos fatos contratados, com temporalidade e retenção definidas por Natureza/Tipo |

#### 14.5. Minimização

Cada tipologia declara o conjunto mínimo de campos necessário à finalidade. Campos não declarados não são cedidos.

#### 14.5.1. CPF na Fase 1

Decisão da Fase 1: não se adota criptografia aplicacional por coluna, tokenização ou ofuscação do CPF nas camadas operacionais que necessitam do identificador para resolução determinística, reconciliação e qualidade. O CPF pode permanecer em claro exclusivamente nas tabelas operacionais restritas previstas pelo DDL e nunca deve ser publicado como coluna/medida no modelo semântico do Power BI, em logs HTTP, em auditoria de acesso ou em mensagens de erro. A infraestrutura deve manter TLS em trânsito e proteção de banco, disco e backup conforme padrão corporativo. A necessidade de criptografia de coluna/tokenização futura será reavaliada no RIPD e na arquitetura de Produção, sem transformar a Fase 1 em implementação prematura de criptografia que inviabilize indexação e resolução.

#### 14.6. Avaliação de impacto

Relatório de Impacto à Proteção de Dados Pessoais é pré-requisito de entrada em produção, dado o volume e a sensibilidade do tratamento.

O modelo SDLE, referência metodológica adotada, foi igualmente precedido de avaliação de impacto à privacidade.

### 15. Governança

#### 15.1. Instâncias

O Programa Reencontro coordena o projeto e sua articulação institucional, sem papel operacional na Jornada. A SGM/SEPE atua como área requisitante e na governança institucional/contratual; a PRODAM implementa e opera a plataforma; cada Gestor responde por seus dados, regras declaradas, contratos setoriais e decisões administrativas.

#### 15.2. Versionamento

São versionados e auditáveis: contratos cadastrais de Pessoa por Gestor; Tipos de Benefício/Serviço e versões; regime_vigencia/janela permitida de concessão; JSON Schemas; regras_texto; implementações de QC e Possibilidades; normalizações; estatísticas populacionais e modelo probabilístico de linkage; credenciais/autorizações sem material secreto; restrições negativas de projeção; contratos OpenAPI; modelo físico/Serving e fonte PBIP/TMDL/PBIR.

#### 15.3. Benefícios, Serviços, versões e regras declaradas

Cada Tipo possui código estável e versões temporais imutáveis. Uma versão ativa deve possuir contrato Pessoa + Registro válido e QC associado; o QC pode estar NAO_IMPLEMENTADO, mas esse estado precisa ser explícito.

Para um Tipo participar da função de Possibilidades, deve existir implementação versionada do avaliador correspondente. Avaliador ausente não autoriza inferência implícita.

O SLA de recebimento é parte da semântica da versão do Tipo. Versão monitorada declara prazo_recebimento_dias e marco_atraso_codigo compatível com o contrato JSON. Para Benefício, usar DATA_EVENTO_CONCESSAO, DATA_INICIO_CONCESSAO ou DATA_FIM_CONCESSAO conforme o fato que realmente dispara a obrigação; os nomes específicos impedem reaproveitamento futuro para pagamento/recebimento. Serviço usa DATA_HORA_SERVICO quando aplicável.

#### 15.4. Política de evolução aditiva

O modelo físico é preferencialmente aditivo. Campos históricos não são removidos/reutilizados com nova semântica. Mudança material de conceito gera novo campo/contrato/versão.

#### 15.5. Catálogo Municipal de Evidências

Mantém regras de comprovação presencial/documental para atributos transversais e correção cadastral, separado de QC e Possibilidades.

#### 15.6. Auditoria

Mudanças de versão, ativação de modelo de linkage, execução de QC, avaliação de Possibilidades, correção/fusão/separação de UUID por correção governada, correção por evidência e acessos individuais devem ser auditáveis. A correção de identidade preserva ato_referencia, justificativa, correlation_id, vínculo anterior, destino e itens append-only; FUSAO_HISTORICA registra também a sucessão UUID absorvido → UUID canônico e as transições/migrações de IDENTITY_MAP. Para acesso individual, a trilha mínima contém credencial, Gestor, rota, recurso, resultado, instante e UUID(s) solicitado/canônico quando aplicável; o agente é registrado por HMAC somente quando declarado pela finalística.

#### 15.7. Catálogo de Atributos Transversais

Define atributos municipais não exclusivos de um Gestor, sua temporalidade, evidências admissíveis, cardinalidade (SINGLE | MULTI), regra de chave de instância e regra de consolidação. TELEFONE_CONTATO usa TELEFONE_BR_CANONICO_V2 e EMAIL_CONTATO usa EMAIL_CANONICO_V2. Para EMAIL_CANONICO_V2, o envelope remove somente SPACE, TAB, CR, LF e NBSP nas extremidades; exige exatamente um @ com parte local e domínio não vazios; converte somente ASCII A–Z para a–z; não executa NFC/NFD e preserva os demais codepoints. Assim, SQL e C# produzem a mesma chave sem depender de collation.

#### 15.8. Configuration as Code

Na Fase 1, contratos e metadados autorais ficam na Solution/repositório versionado, e não dependem de tela CRUD:

config/contracts/gestores/<GESTOR>/pessoa/vN/...
config/contracts/registros/<CODIGO>/vN/...
config/catalog/atributos-transversais.json
config/security/test-access-keys.json       # somente Development
config/security/secrets.example.json

A tela de Catálogo, se implementada, é prioritariamente de consulta. Ativação de nova versão ocorre por processo técnico/governança controlado, com revisão de schema, regras, QC e, quando aplicável, avaliador de Possibilidades.

### 16. Operação

#### 16.1. Ambientes da Fase 1

A Fase 1 prevê um único ambiente corporativo central: Homologação (HML).

Desenvolvimento e testes usam recursos isolados, dados sintéticos e SQL Server Developer/LocalDB; nenhuma prova exige reset da instância persistente JornadaLocal, IBGE original, NODE/NAS ou HML/PROD. O ambiente corporativo de homologação tem implantação e segurança próprias.

#### 16.2. HML

HML deve disponibilizar:

• hospedagem contínua de Jornada.Api, Jornada.Processor.Worker e Jornada.Operations.Maintenance.Worker, além da execução controlada do Jornada.Linkage.Parameters.Worker e do Jornada.Linkage.Runner pelo scheduler corporativo homologado;

• banco relacional transacional único da HML, escolhendo Microsoft SQL Server institucional OU SQL Database in Microsoft Fabric, com DDL/manifesto, procedures, controles de integridade e testes aplicáveis ao ambiente escolhido; preservar contratos e implementação comuns à Solution validada;

• endpoint HTTPS e identidade máquina-a-máquina;

• capacidade/ACL do staging efêmero e do storage Bronze compartilhado; homologação dos códigos de Distrito/Subprefeitura e referenciaMalha produzidos por cada Gestor;

• conectividade normal dos Gestores participantes com Jornada.Api para cargas cadastrais e de fatos, sem canal setorial especial para geração de parâmetros;

• mecanismo corporativo de secrets/certificados;

• logs/métricas/health checks;

• Microsoft Power BI Desktop na versão homologada para abertura/validação/salvamento do PBIP e Power BI Service/Gateway conforme arquitetura corporativa;

• superfície de Serving e Power BI com segregação de acesso e prova de conformidade, consultando as views autorizadas do banco operacional escolhido; OneLake/SQL Analytics Endpoint não são dependências obrigatórias;

• CI/CD ou procedimento repetível de implantação.

#### 16.3. Estados e reprocessamento

Operação deve distinguir nova Entrega, retry transitório, reprocessamento interno e retificação do Gestor. Reprocessamento interno não duplica fatos. Retificação é nova chamada, nova Idempotency-Key e novo entrega_id gerado pela API.

#### 16.4. Observabilidade

Monitorar ao menos:

• taxa/latência/erros da API por rota/Gestor;

• tamanho e idade da fila de processamento;

• alertas estruturados do PipelineWatchdog: LINKAGE_RUN_STALE, LINKAGE_MODEL_GENERATION_STALE, PROCESSOR_LEASE_EXPIRED e PROCESSOR_BACKLOG_OLD; os alertas são somente observacionais, não concedem autorização de cancelamento ou recalibração. Estado obsoleto de carga inicial não é função vigente;

• duração por estágio;

• rejeições/quarentena;

• cobertura/erros do serviço geográfico municipal;

• versão e idade do modelo V9 ativo e da política de publicação; linkage_run_id, tipo, estado, duração, universo congelado, cobertura e candidatos por pass de blocking; taxas de aceitação/abstenção, falsa ausência de candidato, limiar superior/inferior e motivos de pendência por Gestor/sistema, sem expor PII nos painéis;

• cobertura de QC e Possibilidades;

• tempo de atualização das views de Serving e do Power BI; qualidade das Entregas por Gestor/Sistema/Tipo, incluindo taxa de retransmissão e zero fatos no período.

#### 16.5. Continuidade e degradação

Indisponibilidade do gerador de parâmetros não bloqueia registros com CPF válido nem o caminho determinístico do Processor. Registros sem CPF permanecem pendentes até uma execução do Jornada.Linkage.Runner. O Runner pode usar versão ATIVA ou versão validada explicitamente selecionada conforme procedimento; indisponibilidade momentânea do Parameters Worker não impede uso de uma versão já persistida. Indisponibilidade do Runner não bloqueia API, ingestão ou resolução por CPF. Indisponibilidade do Power BI não bloqueia APIs nem processamento.

#### 16.6. Scheduler corporativo e runbook

O scheduler corporativo executa recorrências operacionais definidas e permitidas para ingestão, manutenção, Runner ou monitoramento. A calibração do modelo não tem agenda semanal obrigatória nem é acionada automaticamente por ingestão, quantidade de linhas ou diagnóstico DT-23: exige solicitação explícita do operador. O app inicia com snapshot de modelo aprovado e não recalibra no startup.

Cada worker possui locks, leases, deadlines e checkpoints próprios; falha de agendamento não altera a integridade do lote. Watchdogs e monitores são observacionais. Ativar modelo, habilitar política inferior ou reavaliar RESOLVIDOS requer ato e processo próprios; incidentes e recuperação devem constar do runbook sem retreinamento silencioso.

#### 16.7. Console de desenvolvimento e workers independentes

O produto DEV inclui API, ResultadoApi e SQL independentes de três workers: Processor, Operations Maintenance e Bronze Maintenance. Na Console DEV, a supervisão contínua é um único toggle global OFF/ON, com estado observado no Docker/SQL, PID/heartbeat real e três controles por worker: RunOnce individual e Parar processo residente; o indicador reflete o backend, não apenas o último clique. No modo OFF, um RunOnce finito por vez pode executar sem religar loops; ao ativar ON durante RunOnce, o backend deve responder conflito, não matar silenciosamente o trabalho. Desconexão do navegador não cancela RunOnce, não libera mutex e não muda de OFF para ON. Desligar ON desarma restart antes de parar residentes, sem derrubar API/Resultado/SQL; retornar ON requer evidência de novos PIDs/heartbeats e workers independentes. O endpoint de mutação é restrito a perfil DEV/CI descartável e loopback, não aberto para cluster persistente.

O backend documenta GET/POST /api/workers/supervisor, POST /api/workers/{worker}/run-once, POST /api/workers/{worker}/stop e GET /api/activity para a sessão. Os comandos são audíveis por logs/IDs, mas o histórico na aba não prova atividade com Console fechada; fonte de telemetria durável, externo à sessão, deve ser comprovada. A confirmação cooperativa para cancelar RunOnce ativo (com ID/token de execução e prova de término antes de ON) e a trilha durável ainda constituem frentes distintas. A execução E2E documentada foi em sandbox GitHub JornadaE2E descartável, não em JornadaLocal/NODE do usuário. Jamais usar docker down -v, prune ou PID copiado da CI como método de recuperação em ambiente real; lease, fencing, heartbeat e replay de item devem sobreviver a reinício sem duplicar publicação.

#### 16.8. Scheduler corporativo, watchdog e operação do BI

A Solution mantém Jornada.Pipeline.Coordination como biblioteca sem executável coordenador. Agendamento, recorrência e ordem operacional passam a ser explicitamente responsabilidade do scheduler corporativo homologado pela PRODAM, com procedimento versionado em Solution/docs/Runbook_Operacao.md.

Jornada.Operations.Maintenance.Worker inclui PipelineWatchdogWorker como capacidade de observação, desligada por padrão. O watchdog alerta sobre runner/modelo potencialmente estagnado, lease de lote expirado, backlog, ausência de heartbeat e retenção indevida; não agenda calibração, não cancela RunOnce, não altera vínculo, não limpa lock nem encerra sessão. A operação corporativa deve usar consultas de observabilidade de acesso controlado, sem interpretar modo de carga inicial obsoleto como função vigente.

Os artefatos PBIP/PBIR/TMDL permanecem código-fonte do BI, porém a promoção passa a exigir explicitamente abertura, validação e salvamento em Microsoft Power BI Desktop na versão homologada. CI estrutural não substitui esse gate; publicação/refresh em Power BI Service/Gateway permanecem tarefas do ambiente corporativo.

### 17. Requisitos não funcionais

| ID | Requisito |
| --- | --- |
| RNF01 | Todo código C# do produto e de seus workers deve compilar na solution canônica Jornada.sln sobre .NET 10 LTS e SDK declarado no repositório; o compilador/versão exatos são fixados no manifesto da release. |
| RNF02 | Por ambiente, a autoridade transacional de Silver, Gold, identidade, contratos, logs e Serving é única: DEV/CI usa SQL Server 2022 Developer/LocalDB; HML/Produção pode usar Microsoft SQL Server institucional OU SQL Database in Microsoft Fabric, conforme escolha e homologação. Mesmos contratos, adapter, DDL, procedures e regras; sem dependência de OneLake ou SQL Analytics Endpoint. |
| RNF03 | A API deve suportar payloads de até 10.000 Pessoas e 10.000 Registros por lote, com limite físico de corpo configurável. |
| RNF04 | Escrita deve ser idempotente por cliente + Idempotency-Key. |
| RNF05 | PESSOA_UUID usa UNIQUEIDENTIFIER aleatório, sem derivação de CPF. |
| RNF06 | O Processor deve ser reiniciável sem duplicar materializações. |
| RNF07 | Falha de enriquecimento geográfico não bloqueia Pessoa/Registro; ausência é reprocessável. |
| RNF08 | Logs não podem conter payload integral nem identificadores civis desnecessários. |
| RNF09 | HML deve possuir health check/heartbeat para componentes residentes e telemetria/status de execução para o Jornada.Linkage.Runner run-once. |
| RNF10 | Views serving.v_bi_* são contratos estáveis com a equipe Power BI. |
| RNF11 | O PBIP deve abrir usando parâmetros de servidor/banco, sem credenciais versionadas. |
| RNF12 | A suíte ordinária de integração executa DDL/seed/migrations reais em SQL Server compatível e banco descartável. Compatibilidade funcional com SQL Database in Microsoft Fabric foi demonstrada na engenharia (58/58 Integration, linha v4.00); o banco escolhido para HML/Produção exige os testes e controles de homologação aplicáveis ao HEAD da implantação, sem exigir Analytics Endpoint. |
| RNF13 | Mudança incompatível de JSON Schema, API ou view Serving exige versionamento formal. |
| RNF14 | A consulta de Registros e a de Possibilidades devem permanecer semanticamente e fisicamente distinguíveis. |
| RNF15 | O Calibrador único V9 processa dados por agregações, amostragem/streaming e snapshots versionados sem carregar toda a Gold em memória; bootstrap inicial clean 30 mil, recalibração somente por solicitação do operador, sem agenda automática de treino. |
| RNF16 | Novo modelo V9 somente substitui ATIVO por autorização de governança vinculada ao dossiê, aprovação legítima, fingerprint e gate técnico; o algoritmo descobre os parâmetros sem escolha humana. |
| RNF17 | Autenticação e secrets devem ser substituíveis por configuração de HML sem recompilar aplicações. |
| RNF18 | A Solution não deve depender de componente histórico não incluído em Jornada.sln. |
| RNF19 | O projeto deve permitir paralelização das frentes por contratos definidos no primeiro mês. |
| RNF20 | A latência/SLA definitivo de Produção fica fora da Fase 1; HML deve medir p50/p95 e vazão para subsidiar dimensionamento. |
| RNF21 | Cada execução probabilística captura um único snapshot imutável de modelo, limiares, TF, blocking, Gold e código antes do primeiro score; resultados de versões diferentes não são fundidos no mesmo run. |
| RNF22 | Resultados probabilísticos são append-only; guardar decisão, identidade candidata/aceita, motivo, evidência, limites e proveniência suficientes para reprodução sem expor dados pessoais ou transformar os dois primeiros registros duplicados em margem entre identidades. |
| RNF23 | O Runner V9 é independente e pode rodar por comando ou scheduler operacional; o Calibrador não roda semanalmente por padrão e nova calibração depende de solicitação explícita. O Runner realiza blocking multipasse, Rank/Resolve e publicação guardada. |
| RNF24 | A visibilidade dos resultados de um linkage_run deve ser logicamente atômica: lotes podem ser persistidos em transações independentes, mas somente status PUBLICADO alimenta identidade.v_vinculo_corrente. |
| RNF25 | Nenhum passe de blocking ou conjunto de candidatos pode ser truncado silenciosamente. A ausência de candidato precisa de cobertura provada; ausência falsa e condição SEM_CANDIDATO contraditória impedem publicação e criação de UUID. |
| RNF26 | GENERATE_DRAFT do Parameters Worker e o Linkage Runner devem executar sob janela exclusiva do corpus integrado obtida pelo gate serial da Fase 1. A API/Bronze permanece disponível; o Processor termina o lote corrente e não inicia outro até a liberação. Não há requisito de SNAPSHOT isolation nem de ALLOW_SNAPSHOT_ISOLATION. |
| RNF27 | O Calibrador estima m/u, prior, TF e parâmetros derivados de snapshot e amostras representadas por grupos independentes, com relatório de suporte; a frequência nominal e sua proxy IBGE não podem ser confundidas com u condicionado pós-blocking. |
| RNF28 | Recebimento e jobs analíticos podem coexistir: commits da API continuam em Ingestão/Bronze, enquanto a materialização Silver/Gold/identidade é serializada. Parameters GENERATE_DRAFT e Runner declaram intenção exclusiva antes de aguardar o lote corrente do Processor drenar, impedindo starvation por novos lotes. Os comandos SQL críticos devem possuir timeout finito e a HML deve calibrar tempo de drenagem, duração dos jobs e backlog operacional. |
| RNF29 | Endpoints de identidade, Pessoa, Registros e Possibilidades devem autorizar pela credencial, scope e recurso antes da leitura funcional; nenhuma credencial autorizada depende de vínculo prévio da Pessoa. Cardinalidade é propriedade do endpoint e rate limits pós-autenticação usam CredentialId + classe de endpoint. |
| RNF30 | Todo acesso individual deve gerar trilha auditável com credencial/Gestor/rota/recurso e UUID-alvo; consultas em lote devem registrar todos os UUIDs do evento sem persistir CPF, nome, payload, access key ou finalidade declarada. |
| RNF31 | A aplicação não deve confiar diretamente em X-Forwarded-For. Forwarded Headers só pode ser habilitado com proxies explicitamente confiáveis por ambiente; sem lista de proxies, o middleware permanece desabilitado e o rate limit usa o endereço observado na conexão. |
| RNF32 | Os bytes do ZIP Bronze não devem ser persistidos em VARBINARY(MAX) no banco funcional. Devem residir em armazenamento externo durável por chave lógica content-addressed derivada do SHA-256, com escrita idempotente objeto-antes-da-linha e chave relativa independente da raiz física. |
| RNF33 | A Bronze externa deve falhar fechada quanto à integridade e ao ciclo de vida: objeto canônico existente é revalidado por SHA-256; falha temporária/corrupção na recepção retorna HTTP 503 com Retry-After; GC/expurgo físico só ocorre sob coordenação exclusiva por objeto após confirmação de zero referências e carência; todo objeto referenciado por backup SQL restaurável deve permanecer recuperável. Geradores homologados devem possuir perfil de ZIP determinístico para deduplicação de reenvios integrais inalterados. |

### 18. Plano de desenvolvimento, entregas paralelas e critérios de aceite

#### 18.1. Gate inicial de contratos

Para permitir trabalho realmente paralelo, até o final das primeiras semanas devem estar estabilizados, ao menos em versão v1:

• database/Jornada_Fase1.sql com schemas/tabelas/views contratuais;

• database/Jornada_Seed_Dev.sql com massa sintética determinística;

• JSON Schemas cadastrais de Pessoa dos primeiros Gestores e JSON Schemas dos primeiros Tipos de Benefício/Serviço;

• contrato OpenAPI e DTOs de resolução, ingestão ZIP v2 em envelope único, status, consulta cadastral por UUID, Registros e Possibilidades;

• interfaces IAccessContextResolver, IPolicyEngine, IContractResolver, IPersonProjectionService, QC e IPossibilityEvaluator;

• estados de Entrega/Lote/Processamento;

• views serving.v_bi_* e dicionário de métricas;

• parâmetros do PBIP para banco local/HML;

• modelo de Código do Gestor + chave, Código do Tipo de Benefício + chave e Código do Tipo de Serviço + chave, scopes/recursos, rotação e secrets;
• ownership de diretórios e contratos compartilhados para permitir paralelização sem sobreposição.

Depois desse gate, alterações incompatíveis exigem versionamento e comunicação formal entre as frentes.

#### 18.2. Frentes paralelas

O princípio de execução é explícito: BI não espera C# terminar; C# não espera BI; infraestrutura não espera a aplicação completa; banco não espera cargas reais. Com contratos estabilizados e ownership por diretório, até oito frentes técnicas podem trabalhar simultaneamente sem editar o módulo das demais, usando PR/revisão apenas para interfaces compartilhadas.

| Frente | Trabalho principal |
| --- | --- |
| Arquitetura / Contracts | Ownership de src/Jornada.Contracts/ e config/contracts/: DTOs, interfaces públicas, JSON Schemas, contratos de Pessoa/Benefício/Serviço e gates de versionamento. Coordena mudanças em fronteiras compartilhadas. |
| C# — API | Ownership de src/Jornada.Api/: autenticação/autorização, resolução CPF→UUID, endpoints de ingestão/consulta, projeção por schema, idempotência e telemetria HTTP. |
| C# — Processor | Ownership de src/Jornada.Processor.Worker/: Bronze→Silver→Gold/Serving, qualidade, geografia, resolução determinística por CPF, QC e Possibilidades. Não executa score probabilístico massivo. |
| C# / Dados — Linkage | Ownership de Calibrador V9 C# único, contratos FS/TF, bootstrap IBGE clean 30 mil, seleção automática de parâmetros/limiares, blocking multipasse, Rank/Resolve por identidade, Runner e provas Gold; treinamento apenas sob demanda do operador e publicação governada. |
| Banco / Dados | Ownership de database/: DDL, constraints, índices, seed, views Serving, performance, estratégias de escala, suporte aos application locks do gate serial e apoio aos testes de integração. |
| Power BI | Ownership de bi/: Jornada.pbip, TMDL, PBIR, medidas, páginas e documentação das métricas sobre serving.v_bi_*. |
| Infraestrutura / Segurança | Ownership da implantação do banco operacional em HML/Produção (Microsoft SQL Server institucional ou SQL Database in Microsoft Fabric), IdP/segredos/TLS, CI/CD, scheduler operacional sem recalibração automática, rede, recuperação e observabilidade. A camada analítica do Fabric não integra os requisitos obrigatórios. |
| Testes / Qualidade | Ownership de tests/: NUnit Unit/Integration, regressão de contratos, segurança, falhas, DDL/seed, linkage/modelos, critérios de aceite e evidências de homologação. |

Fronteiras compartilhadas. Jornada.sln, Directory.Build.props, DTOs/interfaces públicos, DDL contratual e views serving.v_bi_* não possuem edição cruzada informal: qualquer alteração passa por PR/revisão do owner da fronteira e comunicação às frentes consumidoras. Documentação e runbook são responsabilidade distribuída dos respectivos owners e não constituem uma nona frente técnica.

#### 18.3. Portas de aceite, homologação e implantação

| Gate | C# / Dados | Banco | BI | Infra/Security |
| --- | --- | --- | --- | --- |
| DEV integrado | API, Processor, Linkage, REF e testes de contrato | DDL, migration, clean/upgrade, fingerprint | Modelos semânticos e dados sintéticos | CI, segredos sintéticos, isolamento de instâncias |
| Qualidade V9 | Bootstrap único, FS/TF, blocking, Rank/Resolve | SQL físico em base descartável | Métricas de qualidade sem PII | Paridade independente, hashes e TEST fechado |
| Gold governada | Vínculos, fatos, catálogo documental e DT-10 | Procedure set-based, rollback e digest DT-15 | B-cubed, pendências e divergência | Autorização humana e OFF por padrão |
| Ensaio único | Pacotes ZIP, API, CPF tardio, múltiplas ondas | Carga, recuperação, consistência e custo | Validação visual Power BI Desktop | Performance, licença, auditoria e segurança |
| HML autorizada | Contrato real das Secretarias, Apoio executável | SQL operacional escolhido (SQL Server ou SQL Database in Fabric), segredos, capacidade e recuperação homologados | Serving e credenciais homologados | IdP corporativo, LGPD, finalidade/retensão |
| Piloto e PROD | Aceite institucional, reversibilidade, suporte | Migrações versionadas, plano de retorno | Publicação corporativa controlada | Gate institucional, observabilidade, incidentes |

#### 18.4. Critérios de aceite técnico da Fase 1

A HML deve demonstrar ponta a ponta:

• autenticação obrigatória por Código do Gestor + chave e resolução de identidade sem criação de UUID pela consulta;

• ingestão ZIP em endpoint único e envelope v2 uniforme: Pessoas sempre validadas pelo schema do Gestor e Registros, quando presentes, pelo schema do respectivo Benefício/Serviço;

• Processamento Bronze→Prata→Gold/Serving;

• tratamento de Referência Territorial com natureza classificada, regra anti-inferência e territorialização fornecida pelo Gestor, com situacaoGeografia explícita, referenciaMalha em RESOLVIDA e preservação histórica do snapshot;

• Formação de Gold Pessoas e Registros preservando fatos sem depender da resolução da identidade, com CPF válido determinístico e travas de consistência; bootstrap do Calibrador V9 com 30.000 identidades sintéticas clean e seleção automática de todos os parâmetros, sem mínimo obrigatório fixo de 5.000 pares e sem treino no startup.

• Runner V9 incremental/por demanda quando explicitamente executado, usando um único snapshot aprovado, blocking multipasse, Rank por identidade, Resolve, superior recall × FDR, inferior falsas criações × pendências, abstenção técnica e publicação transacional; nenhum registro ganha score artificial 1,0 e nenhum bloqueio é reduzido a nascimento exato.

• QC específico e seus estados de cobertura;

• consulta de Pessoa por UUID individual e por conjunto, com sobrecargas Gestor+chave, Benefício+chave e Serviço+chave e projeção estrita pelo schema correspondente;

• GET de Registros, Benefícios Concedidos e Serviços Prestados;

• GET de Possibilidades claramente separado dos fatos;

• PBIP ligado diretamente às views Serving autorizadas do banco operacional da HML (SQL Server institucional ou SQL Database in Microsoft Fabric), aberto, validado e salvo em Microsoft Power BI Desktop homologado, com dashboards prioritários incluindo Qualidade dos Pacotes e Qualidade de Identidade por Origem; não exigir OneLake ou SQL Analytics Endpoint;

• logs, métricas, auditoria e reprocessamento; watchdog observacional habilitado/calibrado em HML e scheduler corporativo configurado com evidência de cadência, retry, logs e não sobreposição de jobs.

#### 18.5. Regras da CI e da integração de alterações

O pipeline conserva gates de compilação .NET 10, unitários, integração SQL, schema, instalador, segurança, contratos, E2E, DT-10 físico e validação V9. Classificação de impacto pode dispensar probe de 30 mil somente quando a alteração for comprovadamente isolada; migrations, SQL, scorer, REF, dependências transitivas ou diffs ambíguos exigem prova ou falham fechado. Uma job marcada skipped não é evidência de execução; qualquer dependência alterada demanda nova verificação do HEAD exato. Ganho de desempenho de CI deve ser medido, nunca presumido.

Alterações de dependências e documentação podem avançar em frentes paralelas, sem duplicar alterações no workflow compartilhado. A integradora coordena owner e ordem de migração, rebase e merge condicionado a branch protection, teste no SHA exato, ausência de conflitos e evidência dos gates exigidos. Automação de CI e hooks podem antecipar execução e avisos, mas não aprovam modelos ou contornam validação física.

Qualquer merge deve registrar PR, SHA final, matriz de checks, eventuais falhas, testes skip legítimos e evidência física executada onde necessária. Não fundir simultaneamente duas PRs que alterem a mesma procedure e o mesmo manifesto sem definir ordem e equivalência clean/upgrade/flattened. Ao detectar concorrência, rebase/reconcile antes do merge; não editar migration antiga. Falha no check schema, testes unitários, probe necessário, compilação ou prova de conservação impede merge, mesmo que outros workflows estejam verdes. Testes sintéticos e merge não equivalem a validação HML, TEST final, autorização PRODAM ou release selada.

#### 18.6. Critérios de aceite positivos e negativos por componente

##### Ingestão e Apoio às Secretarias

Aceite positivo: seleção de arquivo e de pasta conforme perfil homologado; validação de CSV/JSONL; campos opcionais aceitos pelo contrato; um ZIP reprodutível; nenhum original modificado; relatório HTML/JSON; envio opt-in HTTPS com chave idempotente; recibo distinto de processamento; reenvio byte a byte. Aceite negativo: arquivo extra, campo não previsto, linha vazia indevida, Pessoa referenciada ausente, documento/schema com SHA errado, origem mutável, perfil alterado, output dentro da origem, HTTP redirect, token indevido e falha de rede sem perda do ZIP. Registrar hash, estado anterior/posterior, retorno esperado, log sem PII e prova de zero publicação sob erro. Exigir integração com uma instância descartável da API e contrato v2 real antes da homologação; não usar exemplo sintético como termo de aceite institucional.

##### Identidade e documentos

Aceite positivo: códigoPessoaOrigem presente/ausente nos contratos válidos; unicidade de initial_uuid por origem persistente; CPF válido cria/usa âncora sem reatribuição; CPF tardio altera semanticamente vínculo; dois RGs sob a mesma Pessoa; tipos documentais novos sem recompilar; evidências com múltiplas datas e campos; seleção por atributo/documento e precedência verdadeira. Aceite negativo: dois CPFs em uma âncora, fusão cruzada, divisão sem CPF herdando UUID antigo, data do atendimento mais recente vencendo documento próprio, evidência de tipo incapaz de comprovar atributo e Nome Social usado automaticamente como chave de identidade. Verificar invariantes e eventos numa mesma transação e a permanência de históricos após divisão/fusão.

##### Scorer V9 e estatística

Aceite positivo: scorer único V9 e Calibrador C# descobrem automaticamente níveis, m/u, TF, prior, blocking, limiares e margens; Pareto superior recall×FDR pela menor distância ao ideal, Pareto inferior falsas criações×pendências pelo joelho; 30 mil sintéticos clean com rastreabilidade e inferência independente de FDR por identidade-base. Negativos: só nome, nome comum, homônimos, dados ausentes, conflito de CPF, falsa ausência de candidato, score 1,0 artificial, seleção manual de peso/threshold, treino automático não solicitado e reutilização de TEST já observado.

##### Gold, DT-10 e continuidade

Aceite positivo: três ordens com corpus idêntico e bancos vazios, current e candidato, partições comparadas por origem, B-cubed convencional e coverage-aware, falsos vínculos/novas referências/pendências e variação por ordem; guards 51810/51835, SQL canonical+aditivo, rollback e digest DT-15 legítimo quando aplicável. Negativos: patch de harness levado ao banco operacional, flag OFF ultrapassada, aprovação antiga sem digest, candidatura sem snapshot, alteração pós-aprovação, motivo experimental, SEM_CANDIDATO com caracteres curinga, mistura de fit, corrida/onda repetida, falha em meio a lote e replay que duplique ledger. A #1199 só pode ser mergeada após matriz positiva e negativa no SHA exato; a #1210 após corrigir 74/73 e prova física literal.

##### Operação, segurança, CI e publicação

Aceite positivo: readiness SQL/IdP/secret store, scopes, trilha de acesso/alteração, segregação de Gestor, ON/OFF e RunOnce confiáveis, workers independentes com recuperação, contrato HTTP OpenAPI black-box, clean/install/upgrade equivalentes, schema e fingerprint coerentes, CI com status exigido e SHAs de artefatos. Negativos: DEV key em HML, monitor acionando ACTIVATE, dados de abrigo em projeção comum, relatório com CPF/token, job pesado omitido em diff de impacto incerto, skipped apresentado como teste verde, hash de candidato regravado por palpite e release sem gate institucional. Não modificar REF ATIVA, V8, dados reais ou Gold operacional durante esses testes.

#### 18.7. Harnesses de prova, fault injection e backup

A Solution passa a incluir massa sintética determinística e harness de escala para o Parameters Worker e Linkage Runner. Os perfis locais produzem evidência JSON de cardinalidade, duração e resultado do run; perfis maiores continuam sujeitos à capacidade da estação e a homologação de milhões de observações permanece obrigatória em HML representativa.

Os testes de coordenação ganham categoria FaultInjection e scripts próprios para injetar perda/KILL da sessão SQL que detém o gate. O critério esperado é fail-closed: LostToken deve cancelar o trabalho e o applock deve ser liberado quando a sessão termina. O mecanismo é exclusivo de Development/HML e não autoriza KILL automatizado em Produção.

O ensaio local de backup/restore passa a criar um objeto Bronze sintético controlado, executar BACKUP WITH CHECKSUM e RESTORE VERIFYONLY, restaurar o banco em base separada e validar o objeto restaurado com Jornada.Bronze.Verify. Esse ensaio comprova o procedimento da aplicação; RPO/RTO, storage compartilhado e restauração corporativa permanecem gates de HML/Infraestrutura.

O workflow CI inclui um harness-smoke sobre SQL Server 2022 Developer para aplicar DDL/seed/massa sintética, gerar/validar/ativar um modelo e executar MODEL_VALIDATION sem publicação. O runbook Solution/docs/Runbook_Testes_Tecnicos.md é a referência operacional desses ensaios.

### 19. Indicadores

#### 19.1. API e ingestão

Requisições por tipo de credencial/código autenticado/rota/período, taxa de sucesso/erro, 401/403, p50/p95, bytes, lotes/entregas, completude, idade da fila, rejeições, conflitos de idempotência, duração de processamento, taxa de retransmissão idêntica, novas versões e Entregas com zero fatos. Chaves nunca são dimensão de observabilidade.

#### 19.2. Pessoa, linkage e convergência

Completude do núcleo, CPF ausente/inválido por motivo, cobertura de CPF sem exposição do valor, nascimento em 01/01, status_cpf corrente, cobertura de Referência Territorial e sua distribuição por natureza, cobertura UUID, percentual resolvido deterministicamente por CPF, pendências probabilísticas, percentual resolvido probabilisticamente, NAO_RESOLVIDO, CONFLITO, SEM_CANDIDATO_NO_BLOCO_DATA_NASCIMENTO e sua taxa por Gestor/Sistema de Origem sobre elegíveis, distribuição de score por modelo_versao/linkage_run_id, T_LINKAGE, linkage runs por tipo/status/duração, snapshot/tamanhos de amostra e concordância por atributo. Scores de versões diferentes devem permanecer segmentados pela versão do modelo.

#### 19.3. Quality Control

Cobertura IMPLEMENTADO, resultados VALIDO/DIVERGENTE/NAO_VERIFICAVEL, regras divergentes, Gestor, Tipo/versão, tempo e Referência Territorial quando disponível.

#### 19.4. Alcance e Registros

Pessoas com registros, quantidade por Natureza, Gestor, Benefício, Serviço, tempo, Distrito/Subprefeitura e Natureza da Referência Territorial quando disponíveis; Tipos distintos por Pessoa e sobreposição entre Gestores.

#### 19.5. Possibilidades

Pessoas com possibilidade compatível, tipos/gestores mais frequentes, validade média, cobertura de avaliadores e distribuição por Referência Territorial quando disponível. Sempre separado de concessões/servicos existentes.

#### 19.6. Monetário

Soma dos valores declarados das concessões, média/mediana/faixas por Gestor/Benefício/tempo e Referência Territorial quando disponível. Nunca somar valor e quantidade nem denominar a medida como pagamento/despesa sem fonte financeira.

#### 19.7. Índice de Convergência Cadastral

Acompanha cobertura de máxima correspondência, concordância por atributo e evolução temporal por Gestor, com Referência Territorial quando disponível e cobertura territorial explícita.

#### 19.8. Atraso de recebimento

Indicadores: percentual de Registros no prazo/em atraso por Natureza, Gestor e Tipo; atraso médio/p95/máximo; distribuição por faixa; Registros SEM_MARCO e DATA_FUTURA; tendência temporal; Tipos sem SLA configurado.

### 20. Fora de escopo

• substituição dos sistemas transacionais dos Gestores;

• concessão, suspensão, cancelamento ou pagamento de Benefício;

• decisão administrativa baseada apenas em Possibilidade;

• execução financeira/unificação de meios de pagamento;

• emissão/custódia de documento civil;

• armazenamento obrigatório de cópias documentais;

• prontuário clínico ou cópia de base setorial para geração paralela de parâmetros fora da Gold;

• atendimento direto ao munícipe pela SGM/SEPE;

• tela CRUD administrativa obrigatória para contratos na Fase 1;

• implantação em Produção não é assumida por cronograma de Fase 1; depende de Ensaio, HML, riscos, segurança, autorização e decisão institucional específica.

• ambiente analítico fisicamente segregado e camada analítica adicional de desidentificação/pseudonimização. A Fase 1 utiliza Serving controlado, que pode conter PESSOA_UUID e portanto continua sendo dado pessoal pseudonimizado quando religável; a arquitetura deve permitir introduzir futuramente projeções analíticas segregadas sem remodelar as Golds.

• IA/challenger como dependência do fluxo oficial.

A conferência presencial de evidência cadastral permanece no escopo conforme itens 7–9, sem transformar a plataforma em emissora de documentos.

### 21. Inventário integral de dívidas técnicas (DT), obrigações e critérios de aceite

| # | Área de verificação transversal | Responsável pela evidência |
| --- | --- | --- |
| 1 | Base legal/controladoria e regime de acesso da IDENTITY_MAP, Gold, Registros e Possibilidades | Encarregado / Procuradoria |
| 2 | Catálogo Municipal de Evidências e procedimento de verificação presencial | Governança / Gestores / Procuradoria |
| 3 | Catálogo inicial de atributos transversais | Governança / Gestores |
| 4 | Política de transição para ausência de CPF | Governança / Gestores |
| 5 | Calibração/governança de limiares V9 superior/inferior e critérios por risco de uso | Governança / Dados / Gestores |
| 6 | Instrumento formal de compartilhamento | Procuradoria |
| 7 | Scopes e recursos por Gestor/Tipo | Gestores / Governança / Segurança |
| 8 | Política de retenção por camada/log/evidência | Governança / Encarregado |
| 9 | Primeiros Tipos de Benefício e Serviço + contratos JSON Schema | Gestores / SPE / PRODAM |
| 10 | Convenção final das interfaces QC e IPossibilityEvaluator | PRODAM / Arquitetura |
| 11 | Processo de aprovação de regras_texto, vigência e ativação de versões | Governança / Gestor |
| 12 | RIPD | Encarregado |
| 13 | RPO/RTO/SLA definitivos para eventual Produção | PRODAM / Governança |
| 14 | Catálogo técnico oficial de Distrito/Subprefeitura e política de referenciaMalha a serem fornecidos/validados pelos Gestores, com governança municipal. | PRODAM |
| 15 | Planejamento operacional da primeira ingestão cadastral de Pessoas por algum Gestor (contrato, volume, lotes, janela e replay), sem corpus privilegiado. | Gestores / PRODAM / SPE |
| 16 | Mecanismo corporativo de Código + chave, armazenamento/validação, rotação, revogação e rate limiting em HML. | Segurança / Infraestrutura / PRODAM |
| 17 | Versão do Power BI Desktop usada para validar PBIP/PBIR/TMDL em HML | BI / Infraestrutura |
| 18 | Desenho futuro do módulo de validação independente com IA | SPE / PRODAM / Governança |
| 19 | Schemas cadastrais iniciais por Gestor e política de projeção de Pessoa para credenciais de Gestor. | Gestores / Governança / PRODAM |
| 20 | Tipos de Benefício autorizados a consultar Pessoa e respectivos pessoa.schema.json + chaves de acesso. | Gestores / Governança / Segurança |
| 21 | SLA real de recebimento por Tipo/versão: monitorar_atraso, prazo_recebimento_dias e marco_atraso_codigo, validado com a semântica do contrato de cada Benefício/Serviço. | Gestores / Governança / PRODAM |
| 22 | Topologia de gateway/balanceador e lista de proxies confiáveis por ambiente para normalização segura de IP e calibração do rate limiting. | PRODAM / Segurança / Infraestrutura |
| 23 | Homologar quais Tipos reais de Serviço são casas-abrigo-sigilosas e podem ativar origina_endereco_casa_abrigo_sigilosa. Não existe taxonomia geral de proteção da Pessoa; a classificação pertence ao serviço de origem e ao endereço específico. | SMADS / Gestor responsável / PRODAM |
| 24 | Política para pendências persistentes de identidade: envelhecimento, busca ativa, critérios de encerramento e tratamento de registros que nunca recebem evidência suficiente. | Governança / Gestores / Encarregado |
| 25 | Homologação com os Gestores da territorialização fornecida na origem: códigos oficiais de Distrito/Subprefeitura, referenciaMalha, cobertura e motivos de ausência. Não existe enriquecimento online Pessoa-a-Pessoa na Fase 1. | Gestores / Governança |
| 26 | Avaliação de RNM/CRNM e outros identificadores/evidências necessários para população migrante, refugiada ou apátrida, sem alterar o CPF como rota determinística normal. | SMDHC / Governança / Procuradoria |
| 27 | Implementação física da Referência Territorial definida na v3.28: contratos/JSON Schema, domínio de natureza, snapshot Silver, seleção explícita/fallback DOMICILIAR, linhagem por referencia_territorial_observacao_id, nomenclatura *_referencia_id, SEM_REFERENCIA_TERRITORIAL e dimensões de BI. A territorialização v3.39 é fornecida pelo Gestor; não há integração geográfica online pendente na Fase 1. | CONCLUÍDA NA SOLUTION / HML COM GESTORES |
| 28 | Validar, com dados e ambiente autorizados, o blocking **multi-passe** descoberto e selecionado pelo Calibrador V9 (incluindo tratamento de divergências de data de nascimento quando selecionado), medindo recall, cobertura, ausência de candidatos, falsos negativos, custo e resultados por estrato/Gestor/Sistema de Origem. Os passes e seus parâmetros são versionados e sujeitos a ensaio de completude/recall; não há homologação prévia de blocking exato obrigatório por data de nascimento nem fallback silencioso. | Dados / Governança / Gestores |
| 29 | Integração com CBC/Conecta ou outras bases federais para verificação em lote — FORA_DE_ESCOPO da Fase 1; candidata à Fase 2. | Governança / Gestores / órgãos externos |

A matriz de verificação transversal abaixo representa frentes e requisitos de conferência de produto, não uma declaração de que todos os itens estejam abertos ou não entregues. A situação verificável de cada DT, PR e gate é discriminada em seguida.

O inventário a seguir diferencia dívida técnica aceita em DEV, pendência funcional, verificação de escopo, decisão revertida e dependência de autorização institucional. Uma conclusão histórica de componente não quita novas alterações do mesmo domínio nem a necessidade de regressão sobre o SHA atual.

#### 21.1. Matriz de alcance técnico e pendências por DT

| Código | Situação atual | Objeto principal |
| --- | --- | --- |
| DT-01 | ACEITA / conferência técnica | Tolerância congelada para confrontar scorer real com cálculo independente: diferença absoluta de LLR por par até 0,01 e decisão operacional FINAL EXATA, inclusive candidato, ordem e motivo. |
| DT-02 | ACEITA / .NET 10 | Solution única em net10. |
| DT-03 | ACEITA / contratos HTTP | Respostas OpenAPI v1 tipadas para sucesso/erro com regressão DTO/status e contrato de requisição; futuras novas operações precisam de schema, teste de compatibilidade de caixa-preta e versionamento, não aceitação por snapshot textual apenas. |
| DT-04 | ACEITA EM DEV / IdP pendente | Jornada. |
| DT-05 | PARCIAL / fechamento técnico aberto | Ledgers de mudança semântica append-only, replay de três ondas/CPF tardio, snapshots NAS/Parquet por run e manifesto imutável com SHA-256, pins de scorer/ruleset/projeção, candidate-state e vínculo SQL create-once; rehash e reprocessamento determinístico precisam ser comprovados no SHA. |
| DT-06 | ACEITA / evolução contínua | Baseline de migrations para instalação limpa e upgrade sem reescrever a história; CI parte de banco histórico isolado com sentinelas, backup CHECKSUM, RESTORE VERIFYONLY, instalação, schema e idempotência. |
| DT-07 | ACEITA / controle contínuo | Revisão de drift entre especificação, código, SQL, security, Gitleaks, manifestos, instalador, diagramas e workflow. |
| DT-08 | ACEITA / dependências C# | Código-fonte Linkage. |
| DT-09 | ACEITA EM DEV / promoção protegida | VALIDATE e ACTIVATE usam fonte única de parâmetros, mesma tolerância congelada, fingerprint de snapshot, status CONFORME e evidência mais recente, em transação SERIALIZABLE e assert SQL. |
| DT-10 | ACEITA NO LOTE; EXTENSÃO V9 ABERTA | Publicação set-based identidade. |
| DT-11 | ACEITA / orquestração | Runner único jornada-runner. |
| DT-12 | ACEITA / higiene | Limpeza de branches/artefatos com inventário, backup, correspondência exata entre SHA remoto e PR mesclada, preservação de tags, PRs abertas e evidências. |
| DT-13 | ACEITA / schema | Retirada do modo_carga_inicial obsoleto com migrações e regressão de queries/runtime. |
| DT-14 | ACEITA / procedimento | Runbook define conferência independente antes de promoção de modelo e em mudanças de comparadores/scorer, runtime ou limiares. |
| DT-15 | ACEITA EM DEV / gates externos | Dossiê ATIVO × RASCUNHO no mesmo corpus, ledgers append-only, SHA de dossiê, fingerprint do ATIVO-base e proteção transacional anti-TOCTOU; página master apartada do monitor. |
| DT-16 | REVERSÃO DECIDIDA | Separação em múltiplas solutions auxiliares foi revertida. |
| DT-17 | ACEITA EM DEV / regressão transversal | Matriz E2E-A/B/C comprovou identidade progressiva, homônimos, CPF tardio, conflitos, ledger, idempotência, migrações e segurança após modernização e limpeza. |
| DT-18 | ACEITA EM CI DEV | Processor, Operations Maintenance e Bronze Maintenance são workers independentes com restart por processo e SQL/API/Resultado não afetados por falha de um residente. |
| DT-19 | ACEITA EM CI DEV | Console apresenta indicador de estado automático e, por worker, Executar uma vez e Parar processo; o log da sessão registra comandos, mas não prova eventos fora da sessão. |
| DT-20 | PARCIAL EM DEV | Supervisão global OFF/ON integrada, RunOnce e parada individual com restart observado. |
| DT-21 | PARCIAL EM DEV | Testes E2E de OFF/ON, três workers, desconexão de navegador, parada por PID e lease/rollback avançaram. |
| DT-22 | ABERTA / POSTERGADA | Reavaliação extraordinária, governada e autorizada de RESOLVIDOS sem CPF quando novo modelo/evidência modifica candidatos. |
| DT-23 | ABERTA / coleta periódica ausente | Diagnóstico automático read-only de suficiência: avaliador puro, contagem por estrato, classes m/u, grupos independentes, política/snapshot SQL versionados e prévia no monitor; atualmente indeterminado sem verdade auditável. |

#### 21.2. Critérios completos, evidências e limitações por DT

##### DT-01 — ACEITA / conferência técnica

Tolerância congelada para confrontar scorer real com cálculo independente: diferença absoluta de LLR por par até 0,01 e decisão operacional FINAL EXATA, inclusive candidato, ordem e motivo. Evidências dirigidas de 9 cenários/11 pontuações e gates de regressão. Não é prova de risco populacional, qualidade estatística nem equivalência dos comparadores originais.

##### DT-02 — ACEITA / .NET 10

Solution única em net10.0, SDK 10.0.112 (ou revisão compatível aferida no HEAD), Microsoft.Data.SqlClient 7.1.1; migração acompanhada por testes de compilação, SQL, containers, E2E-B e conferência do scorer. Instalação Windows/versões efetivas continuam sujeitos a prova de pacote e ambiente; não supor .NET 8 produtivo.

##### DT-03 — ACEITA / contratos HTTP

Respostas OpenAPI v1 tipadas para sucesso/erro com regressão DTO/status e contrato de requisição; futuras novas operações precisam de schema, teste de compatibilidade de caixa-preta e versionamento, não aceitação por snapshot textual apenas.

##### DT-04 — ACEITA EM DEV / IdP pendente

Jornada.Access.Security centraliza X-Jornada-Access-Key, AuthenticationHandler, scopes, policies, 401/403 e auditoria para APIs. Em HML/Produção vale deny-by-default até identidade individual corporativa, secret store, autorização setorial e testes reais; aceitação DEV não homologa segurança institucional.

##### DT-05 — PARCIAL / fechamento técnico aberto

Ledgers de mudança semântica append-only, replay de três ondas/CPF tardio, snapshots NAS/Parquet por run e manifesto imutável com SHA-256, pins de scorer/ruleset/projeção, candidate-state e vínculo SQL create-once; rehash e reprocessamento determinístico precisam ser comprovados no SHA. Manutenção física de hash, deduplicação e GC opt-in isolado não autorizam política de retenção; falta consolidar custo e latência de snapshots/replay, recuperação/retensão governada e operação real.

##### DT-06 — ACEITA / evolução contínua

Baseline de migrations para instalação limpa e upgrade sem reescrever a história; CI parte de banco histórico isolado com sentinelas, backup CHECKSUM, RESTORE VERIFYONLY, instalação, schema e idempotência. Novas migrations devem preservar ordem, fingerprint físico e equivalência entre clean, upgrade e SQLCMD; contagem de migrations depende do HEAD, nunca de constante histórica.

##### DT-07 — ACEITA / controle contínuo

Revisão de drift entre especificação, código, SQL, security, Gitleaks, manifestos, instalador, diagramas e workflow. Entrega de auditoria documental não certifica governança real; divergências futuras são novas alterações e devem ser reconciliadas com o mesmo SHA de publicação.

##### DT-08 — ACEITA / dependências C#

Código-fonte Linkage.Core localizado fisicamente no projeto e links frágeis removidos. Construção de todos os projetos, empacotamento e não duplicação de regras permanecem testes exigíveis em futuras refatorações.

##### DT-09 — ACEITA EM DEV / promoção protegida

VALIDATE e ACTIVATE usam fonte única de parâmetros, mesma tolerância congelada, fingerprint de snapshot, status CONFORME e evidência mais recente, em transação SERIALIZABLE e assert SQL. Mudança de modelo ou dossiê invalida aprovação e falha fechada. A decisão institucional humana e validação representativa são gates separados.

##### DT-10 — ACEITA NO LOTE; EXTENSÃO V9 ABERTA

Publicação set-based identidade.sp_publicar_resolucao_progressiva_linkage_lote substituiu cursor, com equivalência escalar, idempotência, concorrência, rollback e plano. Extensão de limiar inferior V9 governado exige migration nova, política OFF, digest vinculado à aprovação ACTIVATE DT-15, cenário SQL positivo, negativos de adulteração/replay, rollback e revisão do loader REF. Guard SEM_CANDIDATO literal é frente independente e não pode perder invariantes da procedure.

##### DT-11 — ACEITA / orquestração

Runner único jornada-runner.py orquestra execução especializada por perfil e exige reset explícito. Isolamento estrito de JornadaE2E, JornadaSyntheticDev e JornadaLocal; artefatos, status e logs são vinculados ao run; não executar limpeza destrutiva como recuperação de falha.

##### DT-12 — ACEITA / higiene

Limpeza de branches/artefatos com inventário, backup, correspondência exata entre SHA remoto e PR mesclada, preservação de tags, PRs abertas e evidências. Não apagar referências por idade, sem aferição do destino e possibilidade de recuperação.

##### DT-13 — ACEITA / schema

Retirada do modo_carga_inicial obsoleto com migrações e regressão de queries/runtime. A matriz transversal de regressão utiliza DT-17; não ressuscitar campos/tabelas excluídas por desatenção ao histórico.

##### DT-14 — ACEITA / procedimento

Runbook define conferência independente antes de promoção de modelo e em mudanças de comparadores/scorer, runtime ou limiares. Etapa documental não dispensa os asserts de código DT-01/09 nem a avaliação de qualidade estatística dos vínculos.

##### DT-15 — ACEITA EM DEV / gates externos

Dossiê ATIVO × RASCUNHO no mesmo corpus, ledgers append-only, SHA de dossiê, fingerprint do ATIVO-base e proteção transacional anti-TOCTOU; página master apartada do monitor. Evidência sintética fica INCOMPLETO/SINTETICA_DEV e não pode ser promovida. HML/PROD requerem autenticador institucional, evidência representativa e decisão humana. Extensão de política V9 inferior precisa vincular digest da própria política, não só fingerprint do modelo.

##### DT-16 — REVERSÃO DECIDIDA

Separação em múltiplas solutions auxiliares foi revertida. Jornada.sln é a única solution suportada para build, dependências, testes e empacotamento; nenhuma exigência de migração para cinco solutions permanece.

##### DT-17 — ACEITA EM DEV / regressão transversal

Matriz E2E-A/B/C comprovou identidade progressiva, homônimos, CPF tardio, conflitos, ledger, idempotência, migrações e segurança após modernização e limpeza. Nova alteração de DT-10, runner ou CI exige regressão positiva/negativa no SHA exato, sem transportar aceitação do ensaio para mudança posterior.

##### DT-18 — ACEITA EM CI DEV

Processor, Operations Maintenance e Bronze Maintenance são workers independentes com restart por processo e SQL/API/Resultado não afetados por falha de um residente. Demonstrar health e PID reais, recuperação por lease/heartbeat e isolamento; não confundir restart com lote concluído.

##### DT-19 — ACEITA EM CI DEV

Console apresenta indicador de estado automático e, por worker, Executar uma vez e Parar processo; o log da sessão registra comandos, mas não prova eventos fora da sessão. Superfície é DEV/CI descartável, não console de operação corporativa.

##### DT-20 — PARCIAL EM DEV

Supervisão global OFF/ON integrada, RunOnce e parada individual com restart observado. Ação ON deve recusar RunOnce em andamento; cancelamento explicitamente autorizado com token/ID, confirmação de término, prevenção de órfãos e segurança de concorrência ainda precisam ser entregues.

##### DT-21 — PARCIAL EM DEV

Testes E2E de OFF/ON, três workers, desconexão de navegador, parada por PID e lease/rollback avançaram. Faltam cenários de cancelamento cooperativo confirmado e observabilidade durável fora da sessão, antes do aceite integral da supervisão.

##### DT-22 — ABERTA / POSTERGADA

Reavaliação extraordinária, governada e autorizada de RESOLVIDOS sem CPF quando novo modelo/evidência modifica candidatos. Não é varredura contínua, não é replay de resultado bruto histórico e não está implementada; o INCREMENTAL atual não cobre genericamente terceiros RESOLVIDOS afetados. Exigir dry-run, snapshots, universo, controles de fusão/separação, custo, retomada e decisão formal.

##### DT-23 — ABERTA / coleta periódica ausente

Diagnóstico automático read-only de suficiência: avaliador puro, contagem por estrato, classes m/u, grupos independentes, política/snapshot SQL versionados e prévia no monitor; atualmente indeterminado sem verdade auditável. Faltam coletor real independente, agendamento, cálculo de suporte pós-blocking, semáforo com justificativa e E2E. Nunca iniciar GENERATE_DRAFT, VALIDATE ou ACTIVATE automaticamente.

### 22. Frentes de desenvolvimento paralelo, responsabilidades e integração

O desenvolvimento avança em branches/PRs paralelas por superfície realmente independente, com uma frente integradora proprietária de migrations, manifestos, contratos globais, .github/workflows/, dependências centrais e baseline de instalação. Não criar cópias de trabalho que implementem simultaneamente a mesma procedure ou o mesmo contrato. Revisar prontidão pelo conjunto de gates no commit preciso, não pela ordem de abertura da PR. Encerrada uma frente, a equipe responsável assume a próxima pendência apta, sem suspender atividades independentes.

| Frente | Responsabilidade e dependência | Aceite exigido | Entregável verificável |
| --- | --- | --- | --- |
| Integração SQL / publicação | Proprietária exclusiva de migrations, procedimentos de publicação, manifestações e contratos comuns; coordena efeitos dos guards SEM_CANDIDATO e política inferior. | Fail-closed + gates SQL físicos | Conciliação entre procedure escalar/lote, migração e inventário; autorização digest DT-15; rollback e quatro cenários positivos. |
| Calibrador único V9 | Motor único C# integrado a snapshots REF, comparadores FS/TF, blocking e orçamento de memória. | Calibração automática e reprodutível | Parâmetros, prior, TF, pesos, limiares separados, diagnóstico por estrato, documentação de proxy IBGE e bootstrap clean. |
| Estatística FS/TF | Testes de independência, grupo de identidade-base e medidas de risco pós-Resolve. | Validação independente | FDR, recall, suporte, intervalos, matrizes de falsos vínculos e abstenções, sem tetos arbitrários de risco pré-impostos. |
| Gold incremental e coortes | Executor físico de Rank/Resolve, três ordens, falsa criação, fragmentação e coortes distintas. | 12+6 cenários reprodutíveis e TEST novo | Relatórios de gabarito e comparação por IDs estáveis; pior ordem; divergências e comportamento de replay. |
| Limiar superior/inferior | Responsável pelo contrato de dois Pareto independentes, integrado ao Calibrador único. | Superior recall×FDR; inferior falsas criações×pendências | Escolha superior por menor distância ideal normalizada; inferior por joelho; auditoria de finalistas, sem contrato de 4 objetivos. |
| CI e documentação | Integradora central possui workflows, filtros de impacto e checagens required. | Gates no HEAD exato | Documentação única, checks sem skip indevido, prove de melhora temporal e caminhos de deploy. |
| Dependências/runtime | Bibliotecas, vulnerabilidades, SDK e pacote único da Solution. | Build, security, migração, regressões | Atualizações testadas isoladamente com rebase, lockfiles e rollback. |
| Blocking e fronteira de confiança | Cobertura, ausência real, índices, candidade-state e permissões efetivas de escrita. | Sem falsas novas identidades | Atestado testável de limites, sem truncamento, prefixo literal SEM_CANDIDATO e SQL escritor segregado. |
| Apoio às Secretarias | Produto executável independente, integração por arquivo/pasta e contrato explícito. | Instalador + validação fail-closed | ZIP v2 canônico, JSON+HTML de erros, opt-in de envio e evidência de homologação com Secretarias. |
| Snapshots/continuidade | DT-05 replay NAS/Parquet; DT-22 extraordinária governada e postergada. | Fidelidade e recuperação | Hashes, integridade, custo/latência, retenção e replay/versionamento, sem varredura permanente de RESOLVIDOS. |
| Console/Workers | Supervisão DEV independente; DT-18 a DT-21. | Cancelamento e observabilidade durável | RunOnce sem órfão, ON/OFF e restart individual; logs persistentes por sessão e lease. |
| Institucional/Ensaio-HML | Governança e IdP, contratos reais, base legal, restrições de consumo e avaliação representativa. | Homologação institucional própria | Massa e rede de Secretarias, proteção de PII, desempenho, recuperação, segurança e ato de autorização de ambiente. |

#### 22.1. Contrato de independência, organização das PRs e ownership

Cada PR deve declarar arquivos e módulos, contrato público impactado, migration/schema/manifesto, consumidor das mudanças, dependências, plano de testes, evidência física, risco de rollback e documentação alterada. Branches distintas não bastam: duas alterações no mesmo protocolo ou pipeline só são independentes após revisão da interface e integração.

A integradora preserva a autoridade sobre YAMLs de Actions, CANDIDATE_INFO, migrações, DDL consolidado e superfícies de segurança compartilhadas. Mudanças de C# isoladas podem ser revistas paralelamente, mas não avançam ao merge quando dependem de um schema incompatível em master.

Gates de código no HEAD exato: compilação/unitários, integração SQL, schema inventory/consolidation, clean/upgrade, instalação, E2E, segurança, testes físicos DT-10, validação independente e probe físico V9 quando o impacto exigir. Job skipped não é evidência de execução. Classificador de impacto incerto falha fechado e não reaproveita aprovação antiga em novo commit.

Não promover modelo ou executar migração em Gold operativa durante uma pesquisa, e não tocar JornadaLocal, NODE/NAS, IBGE original, REF ativa, TEST histórico, HML ou PROD. Experimentos físicos usam bancos SQL descartáveis, coortes sintéticas e namespace/fingerprints predefinidos.

Prioridades paralelas: integridade SQL da publicação; calibração e segurança estatística V9; confronto de Gold incremental e limiares separados; integração da REF, documentação e CI; testes de blocking; apoio às Secretarias; supervisor DEV; retenção/replay; autorização institucional. Integrações dependentes de migration, scorer, contrato ou workflow têm proprietário único e merge condicionado a gates.

### 23. Evidência científica, gates de publicação e fluxo de promoção

O programa distingue observação simulada, execução SQL, avaliação independente e decisão real. A V9 não entra no dispatch produtivo sem snapshot completo congelado em ref, calibração reproduzível, scorer com paridade matemática, políticas de ranking/abstenção, testes de blocking completos e procedimento formal de aprovação. O parâmetro lower 0,05 pertence a um candidato congelado de estudo; não é valor definitivo hardcoded nem habilita criação operacional automática.

#### 23.1. Corpus, treino, avaliação cega e unidades de inferência

O bootstrap estatístico parte de 30.000 identidades sintéticas clean com gerador, seed, referência IBGE, hipóteses e hashes congelados. TRAIN e subcortes internos selecionam todos os parâmetros; VALIDATION e TEST independentes avaliam o candidato congelado sem influenciar retroativamente sua escolha. O fato de um parâmetro ter sido descoberto automaticamente não demonstra que seja seguro ou generalizável; resultados posteriores exibem magnitude, incerteza, desvio por grupo e limitações.

Não se impõe orçamento máximo percentual de falso vínculo ou falsa criação antes do treinamento e não se declara teto preestabelecido para aceitar a exploração. Métricas e procedimentos de estimativa são reprodutíveis e as decisões de governança consideram o resultado efetivamente observado, podendo aceitar, postergar ou rejeitar mediante fundamentação. O operador decide solicitar recalibração, o Calibrador descobre os parâmetros, e o operador/autoridade competente decide a ativação em governança; essas etapas não são fundidas.

A unidade de inferência distingue observações de origem, vínculos aceitos, identidades-base e fatos Gold. Pares dependentes de uma mesma identidade não contam como ensaios independentes. Publicar FDR de decisões aceitas, recall, falsas criações, pendências, cobertura, fragmentação, B-cubed e incerteza por estrato, na pior ordem e nas demais ordens, sem extrapolar sintético a dados reais.

Experimentos Gold usam três ordens determinísticas, corpus idêntico por cenário e novo banco LocalDB/SQL Server para cada execução. O comparador usa IDs estáveis do CSV/origem, não os identificadores SQL internos. Resultados físicos permanecem evidências de laboratório, e não ato de implantação.

#### 23.2. Passagens obrigatórias e decisão reversível

1. Congelar corpus, código, algoritmo, snapshot REF, estados de comparação, m/u, TF, prior, passes, limiares calculados pelo Calibrador, regras de abstenção, versão e hash do modelo. A inicialização da aplicação não treina nem promove.

2. Executar gates físicos sobre políticas e guardas SQL em banco descartável, com cenários positivos e negativos, cancelamento/rollback, concorrência, idempotência e bloqueio de ausência falsa de candidatos. A política inferior continua default OFF até ato governado e controles de ambiente.

3. Avaliar em VALIDATION e TEST independentes o candidato já congelado. A definição de origem, partições, gabarito, grupos independentes e métricas é protegida contra vazamento; os resultados de risco são analisados DEPOIS de observados, sem corte percentual arbitrário antecipado. Resultado inconclusivo não pode ser anunciado como certificação.

4. Preparar dossiê ATIVO × CANDIDATO, no mesmo corpus/verdade quando comparável, com divergências, resultados, incerteza e riscos. O operador integra o processo de governança e pode decidir/autorizar quando competente; VALIDATE e ACTIVATE registram aprovação específica, verificam fingerprints e impedem mudança após autorização.

5. Executar um Ensaio técnico-operacional com Secretaria(s) e massa apropriada; homologar contrato ZIP v2, origem, CPF tardio, alterações de documentos, pendências, fusion/split, API, BI, segurança, idempotência, backup e recuperação. Em HML, exigir IdP, autorização e controles institucionais próprios; somente depois considerar piloto controlado e Produção.

### 24. Governança institucional, pendências e risco residual da visão corrente

#### 24.1. Dependências jurídicas, institucionais e de acesso

Cabe às instâncias municipais competentes formalizar finalidade/base legal, controladores e operadores, compartilhamento e projeções autorizadas por Gestor, retenção e guarda, acessos com privilégio, autorização de tratamento sensível e resposta a incidentes. O Núcleo Gestor do Programa Reencontro e a coordenação técnica SGM/SEPE são encaminhamentos institucionais; competência anunciada em portaria somente vale depois de publicada. A Jornada não cria autorização jurídica por simples ausência de um cabeçalho livre de finalidade.

O IdP individual corporativo, a matriz institucional de scopes, segregação, segredos de produção, logs de visualização/impressão/exportação, criptografia e controles de qualidade são pré-condições para homologação. Manter qualidade de fatos e resolução de identidade como dimensões separadas; o compartilhamento padrão não expõe atributos de abrigo ou documento além do necessário e aprovado.

#### 24.2. Pendências executáveis não encerradas

As pendências funcionais e técnicas de desenvolvimento pertencem às seguintes áreas: (1) política de publicação inferior V9, guards SQL contra ausência falsa de candidato e matriz física de transações; (2) Calibrador C# único, seleção de limiar superior/inferior e snapshot REF íntegro; (3) inferência estatística e qualidade por estrato com dependência entre grupos; (4) auditoria independente de Gold, blocking e três ordens; (5) implantação do aplicativo instalável de Apoio às Secretarias; (6) cancelamento RunOnce e telemetria durável na Console DEV; (7) custo/latência e retenção do replay histórico DT-05; (8) diagnóstico periódico DT-23; (9) eventual DT-22 extraordinária postergada; (10) homologação institucional, IdP, legislação, acesso e Ensaio.

Cada pendência possui responsável técnico, dependências, evidência de teste e regra de aceite em seus respectivos capítulos. O quadro datado de PRs, commits, runs e estado de merge fica no capítulo 26, que não altera a norma quando a CI muda. O encerramento exige verificação de código, schema e teste no SHA exato, não declaração editorial.

#### 24.3. Matriz de risco e regra de não ativação

Risco científico: risco de falsos vínculos, duplicatas, injustiça por homônimo, nomes frequentes, atributos ausentes, TF incompatível e dependência estatística entre pares. Resposta: abstenção técnica quando necessário, maior evidência independente, estratificação e TEST cego; nenhuma adoção da melhor ordem isolada.

Risco de integridade: CPF compartilhado suspeito, mudança de UUID por fusão/divisão, falsa ausência de candidato, truncamento de blocking, corrida de SQL, falta de SHA ou aprovação alterada. Resposta: autoridade CPF, guardas transacionais fail-closed, snapshots e manifestos, ledger append-only, regressão com falha injetada e rollback.

Risco operacional: perda de worker, lease vencido, loop de reprocessamento, retenção inadequada, drift de schema, troca de ambiente e falha de credencial. Resposta: health/readiness real, fencing, retry limitado, separação de ações de DEV, contabilidade do Processing e autorização de retenção/GC.

Risco institucional: uso de dados reais sem finalidade formal, vazamento de dados sensíveis, concessão de privilégio de master, declaração indevida de homologação e publicação automática de modelo. Resposta: deny-by-default, gate jurídico e de segurança, auditoria de acesso, segregação por Gestor e aprovação humana expressa de cada etapa de promoção.

Esta especificação é uma visão técnica única e autossuficiente dos requisitos atuais, sem fingir conclusão de entregas ainda abertas. A publicação institucional exige rito competente. Código e testes no HEAD exato, evidências físicas e autorizações de ambiente são necessários para afirmar implantação. A gestão de pendências da implementação encontra-se no capítulo 26 e a triagem de decisão do produto no capítulo 27.

### 25. Glossário técnico unificado

• Gestor — órgão ou entidade municipal responsável pela gestão de cadastro, Benefício, Serviço ou política integrada.

• Tipo de Benefício — programa concreto identificado por código imutável de quatro caracteres alfanuméricos [A-Z0-9], por exemplo AA01; é uma linha de ref.tipo_registro com natureza=BENEFICIO.

• Tipo de Serviço — serviço concreto identificado por código imutável de quatro caracteres alfanuméricos [A-Z0-9], por exemplo CRA1; é uma linha de ref.tipo_registro com natureza=SERVICO.

• Serviço — fato administrativo da Natureza SERVICO, associado a PESSOA_UUID e a um Tipo de Serviço/versão. Possui atributos próprios definidos pelo contrato, como data/hora do Serviço, unidade do Serviço e situação. Não é sinônimo técnico de Atendimento.

• Versão de Tipo — vigência temporal com regras, schemas e implementações associadas.

• Jornada.Api — único ponto oficial de integração externa da Fase 1: autenticação por código+chave, resolução, ingestão cadastral de Pessoas, ingestão de fatos, status e consultas autorizadas.

• Processor.Worker — motor contínuo Bronze→Prata/Gold/Serving, incluindo validação da geografia declarada pela origem, qualidade, resolução determinística por CPF, QC e Possibilidades; registra PENDENTE_PROBABILISTICO quando aplicável, mas não executa score probabilístico massivo.

• Calibrador V9 C# — componente único que estima estados compostos, m/u, prior, TF, blocking, margens e limiares superior/inferior a partir de corpus congelado, sem mínimo fixo de pares nem treino agendado. A inicialização carrega somente snapshot aprovado; recalibração exige solicitação explícita do operador e ativação é ato posterior de governança.

• Linkage.Runner — executor independente do modelo V9 aprovado; recupera candidatos por blocking multipasse, Rank por identidade-base e Resolve por observação; conserva universo e versão do run, publica em transações governadas com idempotência, e não gera parâmetros. Modos e agendamento do Runner não constituem agenda de recalibração.

• Biblioteca de Contratos — contratos cadastrais de Pessoa versionados por Gestor em config/contracts/gestores/<GESTOR>/pessoa/vN e, para Tipos de Benefício/Serviço, pessoa.schema.json, beneficio.schema.json ou servico.schema.json organizados por Tipo/versão.

• JSON canônico — representação de intercâmbio pela API; objetos Pessoa/Registro são validados por JSON Schema. Para versionamento interno, a Jornada deriva uma forma canônica semântica: Unicode NFC, números equivalentes pelo valor, null opcional equivalente à ausência, exclusão de metadados técnicos declarados e ordenação somente de arrays definidos como conjuntos.

• Entrega — uma chamada ZIP externa no formato v2, sempre com manifest.json, pessoas.jsonl e registros.jsonl. O contexto Natureza+Tipo+versão é usado quando houver fatos ou quando se quiser declarar explicitamente zero fatos daquele Tipo no período; registros.jsonl vazio também permite carga exclusivamente cadastral.

• Lote interno — unidade técnica opcional de particionamento do Processor, identificada por lote_id/lote_seq/lote_total em ingestao.lote. Não é criada nem controlada pelo cliente e não integra o contrato HTTP.

• Idempotency-Key — chave fornecida pelo cliente para impedir duplicação de uma mesma operação de escrita.

• payload_sha256 — SHA-256 dos bytes do ZIP, persistido para auditoria/idempotência e também obrigatório no nome canônico do arquivo recebido.

• Registro — fato administrativo materializado independentemente de possuir identidade canônica resolvida. Estado de atribuição pode ser ATRIBUIDA, PENDENTE_IDENTIDADE ou CONFLITO_IDENTIDADE, com historicidade de alterações.

• Possibilidade — compatibilidade calculada para consideração futura; não é fato, direito ou decisão administrativa.

• PossibilityEvaluator — implementação versionada que avalia compatibilidade preliminar de perfil com Tipo/versão.

• QC IMPLEMENTADO — QC com verificações específicas.

• QC NAO_IMPLEMENTADO — componente contratual existente sem validações específicas; não conta como conformidade.

• Quality Control Plane — controles transversais de recepção, dados, linkage, QC, Gold e observabilidade.

• Profiling estatístico — distribuição, frequência, ausência, raridade e interação de atributos.

• Coerência intra-Gestor — concordância da mesma Pessoa entre observações do mesmo Gestor.

• Coerência inter-Gestores — concordância de observações de Gestores distintos associadas ao mesmo UUID.

• Bronze — ZIP recebido, imutável e auditável, armazenado externamente sob objeto_chave content-addressed por SHA-256; o SQL preserva hash, tamanho, content-type, chave lógica e lineage.

• Prata/Silver — observações normalizadas, qualidade, geografia e linkage.

• Ouro/Gold — camada canônica da Jornada: Gold Pessoas para cadastro transversal e Gold Registros para fatos Golden de Benefícios Concedidos e Serviços Prestados.

• Serving — projeções estáveis para APIs e BI.

• Gold Registros — fatos Golden de Benefícios Concedidos e Serviços Prestados em estruturas especializadas (gold.beneficio_concedido e gold.servico_prestado), unidos somente pelos campos comuns em gold.v_registros_jornada para formar o Histórico da Jornada.

• Pessoa — PESSOA_UUID, CPF, status do CPF, nome completo, data de nascimento, nome da mãe e metadados Golden de fontes_distintas, estado_concordancia e atualizado_em, formalizados em serving.v_pessoa. É compartilhado por padrão entre credenciais autorizadas e não se confunde com atributos transversais sujeitos a restrições de projeção.

• PESSOA_UUID — UNIQUEIDENTIFIER municipal aleatório, estável e sem semântica de negócio.

• IDENTITY_MAP — relação segregada entre identificadores e PESSOA_UUID, com estado ATIVO, EM_CONFLITO ou ENCERRADO e histórico append-only de transições. CPF EM_CONFLITO não resolve UUID até correção governada.

• Score técnico V9 — força estatística e evidência interna de decisão de uma versão de modelo; não é certeza civil, não aparece em CPF determinístico e não é critério universal score=1,0 de autorização.

• Fronteiras V9 — limiar superior aprendido no Pareto recall × FDR por menor distância normalizada ao ideal; limiar inferior aprendido no Pareto falsas criações × pendências pelo joelho normalizado. Ambos pertencem ao snapshot do Calibrador e não são definidos pelo operador.

• Baseline de fonte única — estado inicial sem corroboração independente mensurável.

• Concordância/Corroboração — grau e origem das evidências independentes que sustentam valores Golden.

• Data de referência — instante a que o Gestor declara que os dados se referem; não é competência financeira.

• Distrito/Subprefeitura — Distrito é unidade territorial e Subprefeitura é o órgão que administra vários Distritos. Na Referência Territorial, a dupla é persistida como snapshot histórico declarado pela origem/Gestor, acompanhada de referenciaMalha quando situacaoGeografia=RESOLVIDA. Local do fato/serviço é dimensão distinta e nunca pode ser escolhido automaticamente como referência da Pessoa.

• Referência Territorial da Pessoa — vínculo territorial temporal usado para análise de cobertura e política pública. Não é sinônimo de domicílio civil. Pode ter natureza DOMICILIAR, ACOLHIMENTO_INSTITUCIONAL ou REFERENCIA_TERRITORIAL_DECLARADA e pode, no último caso, existir apenas com Distrito/Subprefeitura.

• Endereço de Referência — endereço postal associado à Referência Territorial selecionada, quando houver. Pode coincidir com ENDERECO_RESIDENCIAL quando este origina fallback DOMICILIAR, ou corresponder ao endereço institucional explicitamente declarado pela fonte. Não é obrigatório em REFERENCIA_TERRITORIAL_DECLARADA e não é publicado como dimensão individual no BI; a visualização territorial usa o snapshot de Distrito/Subprefeitura derivado dessa referência.

• Origem da Geografia — na Fase 1, a classificação Distrito/Subprefeitura usada pelo pipeline é fornecida pela origem/Gestor e persistida como ORIGEM. Valores históricos ENRIQUECIMENTO_PRODAM de bases anteriores podem permanecer para auditoria, mas não são produzidos pelo fluxo operacional. A proveniência não determina a Natureza da Referência Territorial.

• SEM_REFERENCIA_TERRITORIAL — estado explícito em que não há referência territorial utilizável conhecida para a Pessoa no recorte analisado; não significa, por si só, ausência de domicílio nem autoriza inferência a partir de local de atendimento.

• PBIP — arquivo de entrada do Power BI Project. Na Fase 1 fica em `bi/Jornada.pbip`, ao lado de `Jornada.Report/` (PBIR) e `Jornada.SemanticModel/` (TMDL).

• Endereço de casa-abrigo-sigilosa — a Fase 1 não cria categoria geral de “Pessoa protegida”. O endereço sigiloso é um atributo transversal reservado, ENDERECO_CASA_ABRIGO_SIGILOSA, distinto de ENDERECO_RESIDENCIAL. Ele somente pode ser declarado pelo próprio Tipo de Serviço de casa-abrigo cuja versão esteja explicitamente habilitada no catálogo. A Jornada não infere essa condição a partir de acolhimento, unidade de serviço ou Gestor. O atributo não alimenta Referência Territorial e não é projetado a Gestor diferente do responsável.

• Credencial de Gestor — Código do Gestor + chave de acesso do Gestor; representa contexto institucional amplo, limitado por scopes, schemas autorizados e exceções negativas de projeção.

• Credencial de Benefício — Código do Tipo de Benefício + chave própria; vinculada ao Gestor responsável e ao pessoa.schema.json da versão autorizada. Quando recebe scope de Pessoa/Identidade, não depende de fato prévio da Pessoa; permanece limitada ao próprio recurso.

• Credencial de Serviço — Código do Tipo de Serviço + chave própria; vinculada ao Gestor responsável e ao pessoa.schema.json da versão autorizada. Quando recebe scope de Pessoa/Identidade, não depende de fato prévio da Pessoa; permanece limitada ao próprio recurso.

• Chaves sintéticas de Development — credenciais pré-geradas em config/security/test-access-keys.json para teste local. Não existe gerador de chaves na Solution; o arquivo só é carregado quando ASPNETCORE_ENVIRONMENT=Development.

• Projeção cadastral — subconjunto de atributos da Gold retornado por UUID e delimitado pelo JSON Schema associado à credencial.

• Configuration as Code — contratos/configurações autorais versionados em arquivos fonte, sem dependência de CRUD na Fase 1.

### 26. Estado de implementação e evidências — fotografia em 29/10/2026

Este capítulo é informativo e não altera os requisitos vigentes. Identificadores de PR, branch, run e SHA descrevem a fotografia verificável no momento da consolidação, podendo envelhecer sem revogar a arquitetura ou os critérios dos capítulos 1–25. Nada aqui transforma um draft ou uma CI sintética em implantação.

#### 26.1. Evidência registrada e limites da inferência

A base de referência examinada foi lucianox777/Jornada. Em 29/10/2026, a PR #1205 estava integrada ao commit b4fdc3f80310eeff9b48a4cf01429f485583e49a; as PRs #1199 (política inferior DT-10), #1210 (escape literal SEM_CANDIDATO), #1207 (SearchJoint exploratório), #1196 (auditoria de replay), #1211 (documentação CI) e #1206 (atualização de dependências) encontravam-se abertas. Status subsequente exige nova verificação no GitHub e não pode ser inferido deste documento.

A PR #1210 tinha evidência de CI falha no run jornada-ci #40911116954 por expectativa de migrations 73 versus 74 instaladas, além de necessidade de prova SQL física sem skip para guards. A #1199 dependia de completar/revalidar cenários positivos, negativos, digest e migração integrada; nenhuma das duas autoriza declarar V9 ativo. A #1207 permanecia draft: uma proposta de grade upper×lower não substitui o contrato final de dois problemas Pareto nem prova as seis ou doze execuções físicas.

Issue #1089: fechamento do Calibrador C# único e contratos REF; #999: dependência estatística e inferência FDR por identidade-base; #1209: cobertura de blocking e fronteira de confiança; #1200: ganhos mensurados do CI por impacto. As respectivas issues não são unidades autônomas de escopo normativo, e seus estados devem ser atualizados por revisão de implementação.

#### 26.2. Ensaio físico e uso correto dos números

Gold TRAIN V6: 12 corridas físicas com três ordens, baseline CURRENT e candidato KNEE, run #40156139352. Pendências CURRENT: 2.788 / 2.856 / 2.761; KNEE (lower 0,05 experimental): 26 / 28 / 24. Falsas criações CURRENT: 1 / 2 / 1; KNEE: 4 / 5 / 5. B-cubed com cobertura F1 CURRENT: 0,697587 / 0,690381 / 0,700641; KNEE: 0,996338 / 0,996230 / 0,996569. Resultados são laboratório sintético, não estimativas representativas da população.

Coorte externa sintética, run #40250618984, seed 20261025001: 30.804 observações elegíveis, 16.616 identidades verdadeiras, seis execuções. Ordem direta CURRENT/KNEE: pendências 11.122/163, falsos vínculos aceitos 20/13, falsas criações 5/31; reversa: 11.222/163, falsos vínculos aceitos 10/12, falsas criações 3/22; intercalada: 11.202/169, falsos vínculos aceitos 17/13, falsas criações 3/30. A reversa apresentou regressão de aceites falsos; não ocultar a pior ordem. Essa coorte NÃO é TEST final inédito para o candidato avaliado.

#### 26.3. Marcos e limitações de operação

DT-02 integra .NET 10; DT-10 tem publicação set-based tecnicamente aceita no lote preexistente, com extensão de política V9 não concluída. DT-15 possui dossiê e gates DEV, mas seu uso institucional depende de IdP e evidência própria. DT-05 requer fechamento técnico de custo/latência e política de retenção; DT-20/21 ainda demandam cancelamento confirmado e telemetria durável; DT-22 é postergada; DT-23 não opera monitoramento periódico plenamente homologado. Apoio às Secretarias possui protótipo com GUI/CLI, mas não foi homologado como instalador institucional autônomo.

#### 26.4. Verificação incremental em 30/10/2026 — decisões e execução técnica

Esta fotografia é posterior à consolidação de 29/10, não altera retrospectivamente os registros acima nem constitui certificação/ativação. Na `master`, as PRs #1205 (CI por impacto), #1210 (escape literal e proteção SEM_CANDIDATO), #1212 (cópia textual desta visão unificada com alternativa SQL Database in Microsoft Fabric) e #1211 (adendos pós-merge de CI, DT-17 e baseline) estão integradas. A #1211 conciliou os adendos da #1210 sem sobrescrever a documentação anterior; seus workflows no HEAD `7bcf91de6642e3db489abe343d0009282a0d049f` terminaram SUCCESS, com TRAIN físico 30k dispensado por diff estritamente documental (SKIPPED, não executado).

As PRs #1199 (política inferior governada), #1215 (integração V9/TF e paralelismo), #1207 (SearchJoint TRAIN upper × lower), #1196 (replay da coorte externa) e #1206 (Dependabot) ainda estão abertas nesta verificação. A #1199 já apresentou sucesso nos gates principais de CI, schema, installer e preflight físico, mas requer matriz governada positiva DT-15, adulteração e atomicidade sob autorização legítima, completude do blocking e integração do calibrador. A #1215 ainda depende dos resultados de sua CI completa e prova física na HEAD exata. Nenhuma aprovação em GitHub, CI, TRAIN sintético ou inclusão documental aciona política V9: a ativação operacional permanece subordinada a ato humano legítimo, dossiê, guardas e homologação no ambiente.

O workflow `.github/workflows/jornada-coding-agent.yml` é um **auxiliar de engenharia** sob demanda, não o Calibrador V9 nem um mecanismo de retificação de identidade. A proposta de correção de código gerada por modelo é **diagnóstico não aplicado**: não modifica arquivos-fonte, não faz commit/push e não altera automaticamente PR. `git apply --check` verifica apenas a aplicabilidade sintática do patch; não equivale a compilar, executar testes, revisar correção ou obter aprovação. Qualquer adoção exige revisão humana, PR e gates de CI no SHA exato. A alteração de segurança está proposta na #1218, ainda não certificada como integrada neste registro; o documento especializado é `Solution/docs/Agente_Correcao_Codigo_Diagnostico_20261030.md`. Nenhuma execução do agente constitui autorização DT-10, DT-15, V9 ou release.

### 27. Triagem integrada de decisões funcionais e responsabilidades do PO

Esta triagem não reabre regras técnicas já aprovadas. Marca o que exige comprovação de decisão formal do proprietário do produto (PO) ou autoridade institucional antes de ser tratado como compromisso operacional e separa requisito arquitetural decidido, detalhe técnico derivado e proposta funcional ainda não aprovada. A ausência de ata/ato no conjunto técnico examinado é indicada como PENDENTE DE CONFIRMAÇÃO, não como prova de rejeição. O fechamento é realizado pelo PO/autoridade competente com data, decisão, escopo, documento ou ato, e resultado de impacto no contrato técnico.

| Seção | Regra / questão de produto ou implantação | Classificação | Decisão a comprovar / consequência |
| --- | --- | --- | --- |
| 5 — Integração | Unidade de responsabilidade por Gestor/Tipo, mapeamento de atributos e envio opt-in no aplicativo de apoio. | Regra técnica vigente; validação setorial | PO deve confirmar a jornada de uso do instalador e participação de Secretarias em homologação; não presumir que protótipo é produto homologado. |
| 6 — Ingestão | ZIP v2, exatamente três arquivos, hash e fail-closed, coexistindo com schemas Pessoa/Tipo v1 ou vN. | Decidido tecnicamente | Confirmar apenas parâmetros funcionais de comunicação de erro, prazos e experiência por Secretaria. Versão do envelope não determina versão do contrato. |
| 8.1–8.12 — Gold | Precedência por evidência documental e data de expedição, nulidade de núcleo admitida, divergência e multivalorados. | Norma técnica vigente | PO confirma quaisquer consequências institucionais sobre uso cadastral; a regra de dados não deve se converter em decisão administrativa automática. |
| 8.15 — Registros | Materialização do fato independentemente da identidade e semântica ATRIBUIDA/PENDENTE_IDENTIDADE/CONFLITO_IDENTIDADE. | Regra de integridade vigente | PO confirma a apresentação de indicadores e experiência de consumo; nunca usar ausência de vínculo para apagar ocorrência válida. |
| 9 — Divergências | Retorno ao Gestor, fila de pendências e futuros recursos de tipologia/notificação. | Implementado em parte; expansão pendente | Decidir quais fluxos externos, prazos e notificações serão incluídos na Fase 1/Fase 2, com base legal e responsabilidade definidas. |
| 10 — APIs | Campos, acesso comum de Pessoa, consulta ad hoc e escala de uso individualizado. | Contratos técnicos vigentes; uso depende de autorização | Validar política de produtos/consumidores e exposição por Secretaria; não oferecer candidatos ou score probabilístico ao atendente. |
| 11 — Territorialização | Natureza da referência territorial, geografia fornecida pela origem e sigilo de casa-abrigo. | Regra técnica com implicação setorial | Confirmar processos de correção de geografia e responsabilidade dos Gestores; política de sigilo requer aprovação jurídica e de segurança. |
| 12 — BI | Dashboards de cobertura, qualidade, divergência, Pendência e Gold. | Contrato técnico e necessidades de produto | PO define priorização e o uso institucional das métricas; indicadores são auditáveis e não podem omitir denominador/cobertura. |
| 13–14 — Segurança/LGPD | Finalidade, acesso, retenção, IdP, consentimento/base legal e logs de impressão/consulta. | Dependência institucional | Exigir decisões de controlador/encarregado e aprovação de matriz de acesso/retensão; implementação DEV não equivale a autorização legal. |
| 15 — Governança | Decisão de ativação de modelos V9 e delegação de competência ao operador. | Decisão arquitetural + designação formal pendente | Operator pode fazer parte da governança, mas o ato de autorização deve ter competência formal, identidade individual, dossiê e auditabilidade. |
| 16–17 — Operação e RNF | Cronograma, SLA, qualidade de serviço, níveis de disponibilidade e suporte. | Dimensões técnicas; metas institucionais pendentes | PO/PRODAM definem SLAs, janelas de manutenção e capacidade na HML a partir de métricas e custos reais, sem inventar metas numéricas. |
| 19 — Indicadores | Índice de convergência, elegibilidade e interpretação institucional de volumes. | Métrica técnica; uso administrativo pendente | PO confirma objetivos, faixas de divulgação e transparência; FDR, cobertura e taxas de falso vínculo não se confundem com precisão administrativa. |
| 20 — Fora de escopo | Atendimento ao munícipe, decisões de benefício, biometria e extensão de serviços. | Escopo aprovado tecnicamente; expansão requer decisão | Qualquer deslocamento para Fase 1 exige ato e contrato explícitos; a Jornada não decide concessões. |
| 21 — DTs | Aceites DEV e mudanças de responsabilidade institucional. | Trilha técnica de implementação | PO só decide mudança de escopo funcional ou risco institucional; engenharia comprova conclusão por teste e artefato sem inventar homologação. |
| 22 — Paralelismo | PRs separadas, ownership de contratos e ordem de integração. | Governança de engenharia | PO acompanha prioridades de produto; CI, migrations e scorer têm proprietário integrador e gates técnicos obrigatórios. |
| 23 — Estatística | Calibrador escolhe parâmetros sem teto de risco antecipado; validação independente é examinada depois. | Decisão do PO incorporada | Operador solicita a recalibração; Calibrador seleciona; governança com operador autorizado decide ativação. Confirmar delegação formal no ambiente. |

#### 27.1. Registro de decisão a ser preenchido em governança

Para cada questão pendente, registrar: ID estável; seção; enunciado objetivo; impacto em cidadão, Gestor e dados; alternativa A/B quando houver; PO/autoridade competente; ato e data; decisão final; requisitos atualizados; versionamento do contrato; evidência de implementação; gate de implantação. É proibido converter uma hipótese de engenharia em política de compartilhamento ou elegibilidade sem ato competente.

#### 27.2. Alternativas de hospedagem do banco SQL e acesso ao Power BI

A Jornada possui um único banco relacional operacional por implantação. DEV/CI utiliza SQL Server 2022 Developer/LocalDB isolado; em HML/Produção a instituição pode selecionar Microsoft SQL Server institucional ou SQL Database in Microsoft Fabric. Trata-se de duas opções de hospedagem da mesma semântica relacional Microsoft SQL, com o mesmo adapter, contratos, DDL e procedures, não dois bancos ativos em paralelo nem dois motores de identidade. A Silver, Gold, identidade, locks, auditoria e Serving pertencem ao banco escolhido. Power BI acessa as views Serving autorizadas diretamente no destino selecionado. OneLake, Lakehouse e SQL Analytics Endpoint não são requisitos desse desenho.

A decisão sobre a alternativa de banco em HML/Produção deve constar do plano de implantação e sua homologação comprovar conectividade/autenticação, integridade de schema e migrations, transações/locks, concorrência, segregação de credenciais, autorização, desempenho, capacidade, recuperação e continuidade compatíveis com os requisitos do ambiente. A execução histórica de 58/58 testes Integration em SQL Database in Microsoft Fabric sustenta a compatibilidade funcional da linha de engenharia verificada em 03/09/2026, mas não equivale a homologação automática do build atual ou da operação produtiva. Não há dependência funcional de Lakehouse, OneLake ou SQL Analytics Endpoint.

#### 27.3. Correção do item 28 e governança do agente de código (30/10/2026)

**Retificação da linha 28 da matriz de dívidas técnicas (seção 21):** o blocking operacional candidato é **multi-passe, adaptativo e descoberto pelo Calibrador V9**. A data de nascimento pode compor um ou mais passes, mas não é pré-requisito fixo nem prova suficiente de recall. Homologação exige teste com dados autorizados, evidência de completude da busca, métricas por estrato e resultados por Gestor/Sistema de Origem; a escolha automática de parâmetros não substitui TEST independente nem decisão de ativação. Esta retificação corrige a instrução anterior de "homologar o blocking exato por data de nascimento"; não altera retroativamente decisões históricas.

**Agente de correção de código:** sua função é propor *patch* verificável para revisão, não implantar código. A proposta #1218 limita o workflow manual à frente V9, retira DT-10 (#1199), usa token de leitura sem credenciais persistidas e armazena apenas um arquivo `proposed-correction.patch` em artefato temporário. Sem modificação de fonte/teste no workspace, sem aplicação do patch, commit, push, merge ou promoção. A verificação `git apply --check` não comprova qualidade da mudança, CI ou compilação. A eventual aplicação depende de pessoa autorizada, revisão da evidência, PR independente, testes pertinentes no SHA exato, política de proteção de `master` e gates técnicos/institucionais. A guarda operacional e o estado de implementação devem ser auditados em [manual especializado](../Solution/docs/Agente_Correcao_Codigo_Diagnostico_20261030.md) e na PR #1218. O patch não é correção automática efetuada nem calibração V9. A PR #1199 mantém seu ciclo de governança próprio.

**Status:** DECIDIDO para a política de segurança e para a interpretação multi-passe; DOCUMENTADO nesta candidata Git; IMPLEMENTADO apenas nos commits de suas respectivas PRs após verificação, NÃO HOMOLOGADO, NÃO ATIVADO em HML/Produção. Conclusão e merge exigem checagem posterior dos SHAs e das Actions; o DOCX de origem permanece preservado.
