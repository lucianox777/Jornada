# Requisitos de Negócio - Jornada do Cidadão - Fase 1

**Versão do documento:** 1.1  
**Data:** 03/09/2026  
**Base normativa:** Especificação Técnica Jornada v3.62  
**SolutionSchema:** SolutionSchema v3.68  
**Release de incorporação:** Solution Engenharia v3.98  
**Status:** BASELINE DE NEGÓCIO - ORGANIZADO NA HIERARQUIA RN -> RF/RNF -> RT

> Este documento consolida requisitos de negócio derivados da Especificação Técnica Jornada v3.62. Ele integra a família documental de Requisitos da Fase 1 e ocupa o nível mais alto da hierarquia RN -> RF/RNF -> RT. Não substitui a Base Normativa; em caso de divergência, prevalece a Base Normativa vigente e sua alteração formal.

## 1. Finalidade

Registrar, em linguagem de negócio e com rastreabilidade, os resultados que a Jornada do Cidadão - Fase 1 deve entregar à Prefeitura de São Paulo, aos Gestores finalísticos e às áreas de governança. O documento separa a necessidade de negócio da solução técnica usada para implementá-la.

## 2. Objetivos de negócio

- Criar referência municipal transversal de Pessoa sem substituir os cadastros setoriais.
- Consolidar Benefícios Concedidos e Serviços Prestados com origem, temporalidade e identidade preservadas.
- Melhorar interoperabilidade, qualidade cadastral, visão territorial e capacidade analítica das políticas sociais.
- Permitir compartilhamento municipal autorizado com controles objetivos, exceções negativas versionadas e auditoria.
- Tornar incerteza, cobertura de identidade e qualidade visíveis nos produtos analíticos e de consulta.
- Preservar competência administrativa dos Gestores: a Jornada referencia, integra e informa; não concede benefício nem altera automaticamente o sistema finalístico.

## 3. Partes interessadas e responsabilidades

| Parte | Responsabilidade de negócio |
|---|---|
| SGM/SEPE | Área requisitante; governança institucional e contratual da Jornada. |
| PRODAM-SP | Operadora de tecnologia; implementação e operação da plataforma. |
| Gestores finalísticos | Controladores dos respectivos dados; responsáveis por regras declaradas, qualidade, territorialização, contratos setoriais e decisões administrativas. |
| Áreas de governança, proteção de dados e auditoria | Supervisão de compartilhamento, LGPD, retenção, auditoria, controles e entrada em produção. |
| Sistemas finalísticos autorizados | Consumidores das APIs da Jornada para consulta e interoperabilidade; mantêm a experiência e a decisão no domínio setorial. |

## 4. Escopo de negócio da Fase 1

A Fase 1 cobre integração padronizada de Pessoas e fatos, identidade municipal, Gold Pessoas e Gold Registros, Referência Territorial, qualidade, APIs de consulta, Possibilidades compatíveis, BI, governança, auditoria e proteção de dados. A solução permanece sobrejacente aos sistemas dos Gestores.

## 5. Requisitos de Negócio

### RN-001 - Consolidar a Jornada sem substituir os sistemas finalísticos

**Requisito.** A Jornada deve funcionar como camada municipal de consolidação, identidade e interoperabilidade sobre os sistemas existentes, preservando os sistemas transacionais dos Gestores como sistemas de origem e decisão.

**Critério de aceitação de negócio.** A implantação não exige descontinuação nem substituição de sistema transacional do Gestor; alterações cadastrais e atos administrativos continuam registrados na origem.

**Rastreabilidade.** §§ 2.2, 4 e 5 da Especificação Técnica v3.62

### RN-002 - Padronizar a integração municipal de Pessoas e fatos

**Requisito.** Os Gestores devem enviar Pessoas, Benefícios Concedidos e Serviços Prestados por contrato padronizado, inclusive em períodos sem fatos ou com atualização exclusivamente cadastral.

**Critério de aceitação de negócio.** Uma Entrega pode conter zero registros factuais, desde que preserve o envelope e as Pessoas necessárias; os fatos enviados mantêm identidade estável na origem.

**Rastreabilidade.** §§ 2.1 e 7 da Especificação Técnica v3.62

### RN-003 - Preservar origem, temporalidade e linhagem

**Requisito.** Todo dado consolidado deve manter procedência suficiente para identificar Gestor, Tipo/versão, identidade no sistema de origem, período de referência e estado de processamento.

**Critério de aceitação de negócio.** Consultas e produtos analíticos conseguem rastrear o dado à origem e à versão contratual que o produziu.

**Rastreabilidade.** §§ 4, 6 e 15 da Especificação Técnica v3.62

### RN-004 - Constituir a Gold de Pessoas de forma evolutiva

**Requisito.** A primeira fonte integrada que individualize uma Pessoa deve poder constituir uma Gold válida, sem exigir uma base-verdade externa ou segunda fonte prévia.

**Critério de aceitação de negócio.** A primeira fonte individualizável resulta em Pessoa Golden utilizável como baseline; BASELINE_FONTE_UNICA não é tratado como staging, quarentena ou dado provisório.

**Rastreabilidade.** § 4.1, especialmente itens sobre constituição evolutiva da Gold

### RN-005 - Manter uma identificação municipal estável da Pessoa

**Requisito.** A Jornada deve manter um identificador técnico canônico e estável para a Pessoa, preservando histórico quando houver correções, separações ou fusões governadas.

**Critério de aceitação de negócio.** Correções de atributos não trocam o UUID; fusões/separações preservam rastreabilidade histórica e sucessão quando aplicável.

**Rastreabilidade.** § 4.1 e governança de identidade da Especificação Técnica v3.62

### RN-006 - Usar CPF como rota determinística com trava conservadora

**Requisito.** CPF válido é a rota normal de resolução determinística, mas não pode vincular automaticamente uma nova observação a uma identidade existente quando o núcleo cadastral indicar divergência forte.

**Critério de aceitação de negócio.** Em divergência forte, a observação permanece sem atribuição canônica e o conflito é explicitamente materializado para tratamento governado.

**Rastreabilidade.** § 4.1 e ajustes de integridade v3.42/v3.46

### RN-007 - Não descartar Pessoas admitidas sem CPF

**Requisito.** Quando o contrato permitir exceção sem CPF, a Pessoa deve permanecer processável em estado não resolvido/pendente, com possibilidade de resolução posterior conforme política aplicável.

**Critério de aceitação de negócio.** A ausência admitida de CPF não elimina o registro; o estado de identidade e a pendência de regularização ficam explícitos.

**Rastreabilidade.** §§ 4.1 e 7.13.5

### RN-008 - Separar núcleo de identidade de atributos transversais

**Requisito.** CPF, nome, data de nascimento e nome da mãe compõem o núcleo fixo de identidade; endereço, telefone, e-mail e demais atributos transversais seguem catálogo, cardinalidade e evidência próprios.

**Critério de aceitação de negócio.** O modelo e as consultas distinguem o núcleo fixo dos atributos transversais, permitindo múltiplos contatos quando o catálogo assim definir.

**Rastreabilidade.** § 4 da Especificação Técnica v3.62

### RN-009 - Promover valores Golden pela qualidade da evidência

**Requisito.** A precedência de valores transversais deve decorrer da qualidade, pertinência e temporalidade da evidência, e não da Secretaria/Gestor que a produziu.

**Critério de aceitação de negócio.** Evidência documental válida pode corrigir o valor Golden mesmo contra maioria de fontes; a divergência restante continua visível para convergência cadastral.

**Rastreabilidade.** § 4.1 e Catálogo Municipal de Evidências

### RN-010 - Permitir produção distribuída de evidência

**Requisito.** Agentes municipais habilitados dos Gestores participantes devem poder registrar verificação documental válida para os atributos efetivamente comprovados, segundo governança aplicável.

**Critério de aceitação de negócio.** O Gestor que produz a evidência não recebe precedência automática; o ato é auditável e submetido ao catálogo de evidências.

**Rastreabilidade.** § 4.1 e § 15.5

### RN-011 - Separar fatos administrativos da Gold cadastral

**Requisito.** Benefícios Concedidos e Serviços Prestados não devem ser tratados como atributos cadastrais da Pessoa; devem compor histórico factual próprio.

**Critério de aceitação de negócio.** A Gold Pessoas contém referência cadastral; a Gold Registros contém fatos de Benefícios Concedidos e Serviços Prestados com origem e temporalidade preservadas.

**Rastreabilidade.** § 4 da Especificação Técnica v3.62

### RN-012 - Preservar fato válido mesmo sem atribuição canônica

**Requisito.** Um fato finalístico válido deve permanecer disponível na Gold Registros mesmo quando a identidade canônica da Pessoa estiver pendente ou em conflito.

**Critério de aceitação de negócio.** O fato preserva sujeito/identificador declarado pela origem e pode ter PESSOA_UUID ausente sem ser descartado.

**Rastreabilidade.** § 4 da Especificação Técnica v3.62

### RN-013 - Distinguir Possibilidade de fato, direito e decisão

**Requisito.** Possibilidades calculadas devem ser tratadas como compatibilidades informativas para consideração futura, nunca como concessão, fato ocorrido, direito adquirido ou decisão administrativa.

**Critério de aceitação de negócio.** A API e o BI exibem Possibilidades separadamente dos Registros e com ressalva de análise pelo Gestor responsável.

**Rastreabilidade.** §§ 10.4.3, 10.5 e 12.6.13

### RN-014 - Versionar regras que alteram significado de negócio

**Requisito.** Tipos de Benefício/Serviço, contratos, regras, SLA, QC e avaliadores de Possibilidades devem ser versionados quando houver mudança material de significado.

**Critério de aceitação de negócio.** Uma mudança material gera nova versão imutável e auditável; não reutiliza campo ou versão histórica com nova semântica.

**Rastreabilidade.** §§ 15.2 a 15.4

### RN-015 - Manter Referência Territorial separada de endereço civil

**Requisito.** A análise territorial deve usar uma Referência Territorial própria, que não se confunde com ENDERECO_RESIDENCIAL nem com endereço de correspondência.

**Critério de aceitação de negócio.** Distrito/Subprefeitura usados em análise derivam da Referência Territorial selecionada e mantêm natureza, situação geográfica e linhagem.

**Rastreabilidade.** §§ 2.1, 4 e 8 da Especificação Técnica v3.62

### RN-016 - Responsabilizar a origem pela territorialização

**Requisito.** O Gestor/origem deve declarar a situação geográfica da Pessoa e, quando resolvida, informar Distrito, Subprefeitura e referência de malha exigidos pelo contrato.

**Critério de aceitação de negócio.** A plataforma aceita explicitamente estados não resolvidos e não depende de chamada geográfica online Pessoa-a-Pessoa para completar a carga.

**Rastreabilidade.** Ajuste operacional v3.38 e § 2.1

### RN-017 - Aplicar precedência territorial explícita

**Requisito.** Quando houver múltiplas referências territoriais candidatas, a seleção deve obedecer à regra: referência explícita, depois qualidade da evidência, depois recência.

**Critério de aceitação de negócio.** O resultado territorial é determinístico e auditável segundo essa ordem de precedência.

**Rastreabilidade.** Ajuste territorial v3.33 e regras de Referência Territorial

### RN-018 - Disponibilizar consulta da Pessoa aos sistemas autorizados

**Requisito.** Sistemas finalísticos autorizados devem poder consultar a identificação municipal da Pessoa e projeções permitidas por API, sem acesso direto às tabelas físicas da Gold.

**Critério de aceitação de negócio.** A consulta ocorre por contrato e autorização; não requer acesso SQL às tabelas Golden.

**Rastreabilidade.** §§ 2.1, 4 e 10

### RN-019 - Adotar compartilhamento municipal padrão da Pessoa autorizada

**Requisito.** O núcleo público da Pessoa deve ser compartilhável entre credenciais municipais autorizadas com scopes aplicáveis, sem exigir relação prévia da origem com a Pessoa.

**Critério de aceitação de negócio.** Credencial autorizada localiza/consulta a Pessoa segundo seu scope e recurso; ausência de relação prévia não bloqueia a existência da Pessoa na projeção.

**Rastreabilidade.** § 4 e § 10.6

### RN-020 - Permitir restrições setoriais como exceções negativas

**Requisito.** O Gestor responsável deve poder declarar restrições versionadas que reduzam atributos transversais, Registros ou Possibilidades retornados a consumidores determinados, sem criar permissões inexistentes.

**Critério de aceitação de negócio.** Na ausência de restrição ativa aplicável, vale a projeção municipal padrão; uma restrição só reduz o retorno e é versionada, justificada e auditável.

**Rastreabilidade.** § 10.6.1

### RN-021 - Proteger informação territorial especialmente sigilosa

**Requisito.** Informações classificadas como endereço de casa-abrigo sigilosa devem permanecer fail-closed entre Gestores, independentemente da existência de restrição manual adicional.

**Critério de aceitação de negócio.** Consumidor de outro Gestor não recebe esse atributo mesmo se não houver regra manual de projeção.

**Rastreabilidade.** Regra de projeção sigilosa consolidada na Especificação Técnica v3.62

### RN-022 - Manter decisão e escrita cadastral no Gestor finalístico

**Requisito.** A Jornada não deve alterar automaticamente a base transacional do Gestor nem transformar Gold/Possibilidade em decisão administrativa.

**Critério de aceitação de negócio.** Correção cadastral continua na origem; a Gold é referência de dados e a decisão finalística permanece sob competência do Gestor.

**Rastreabilidade.** §§ 2.2, 4 e 5

### RN-023 - Estabelecer controles de qualidade antes do consumo

**Requisito.** Controles de qualidade devem existir antes da formação operacional da Gold, dos indicadores de política pública e das APIs de consumo finalístico.

**Critério de aceitação de negócio.** Nenhum produto de política pública entra em produção sem informações mínimas de origem, atualidade, cobertura de resolução e qualidade do conjunto que o sustenta.

**Rastreabilidade.** § 6 - Quality Control Plane

### RN-024 - Evidenciar cobertura e incerteza nos indicadores

**Requisito.** Produtos analíticos devem informar cobertura de resolução de identidade e, quando usarem linkage probabilístico, versão do modelo, distribuição de scores e cobertura pertinente.

**Critério de aceitação de negócio.** O consumidor consegue distinguir vínculos determinísticos, pendentes e probabilísticos e interpretar o indicador sem ocultar incerteza.

**Rastreabilidade.** §§ 4.1 e 6

### RN-025 - Oferecer BI municipal sobre Pessoas, fatos, qualidade e território

**Requisito.** A Jornada deve disponibilizar visão analítica para políticas sociais, cobrindo Pessoas, Benefícios Concedidos, Serviços Prestados, qualidade, territorialização, atraso e possibilidades, conforme disponibilidade dos dados.

**Critério de aceitação de negócio.** O Power BI consome camada Serving estável; dimensões Gestor e tempo são expostas quando aplicáveis e território vem acompanhado de cobertura.

**Rastreabilidade.** § 2.1, § 12 e anexos de BI

### RN-026 - Manter consultas individualizadas fora do BI

**Requisito.** Consultas individualizadas para atendimento devem ocorrer nos sistemas finalísticos por APIs autorizadas, e não por um portal individual central da Jornada ou por identificadores individuais no modelo analítico padrão.

**Critério de aceitação de negócio.** O BI padrão permanece orientado a análise e minimização; o atendimento individual acontece na aplicação do Gestor.

**Rastreabilidade.** § 12.2 e regras de minimização

### RN-027 - Medir tempestividade de recebimento por Tipo e versão

**Requisito.** Cada Tipo/versão sujeito a SLA deve poder declarar prazo e marco factual de recebimento, preservando a semântica do programa/serviço.

**Critério de aceitação de negócio.** O indicador separa no prazo, atraso e casos não avaliáveis e usa a data civil de São Paulo para calendário institucional.

**Rastreabilidade.** § 15.3 e Anexo de SLA/Atraso v1.22

### RN-028 - Suportar carga inicial sem degradar governança de identidade

**Requisito.** A implantação deve permitir carga inicial em volume, tratando a primeira fonte individualizada como Gold baseline e controlando a execução de linkage probabilístico durante a formação do corpus.

**Critério de aceitação de negócio.** A primeira carga não fica indefinidamente em staging; Pessoas individualizadas formam Gold válida, enquanto estados pendentes permanecem explicitamente tratados.

**Rastreabilidade.** § 4.1 e ajustes operacionais de carga inicial v3.38

### RN-029 - Garantir rastreabilidade e auditoria de atos sensíveis

**Requisito.** Acessos individuais, correções de identidade, fusões/separações, mudanças de versão, execução de QC e avaliação de Possibilidades devem possuir trilha auditável.

**Critério de aceitação de negócio.** A trilha contém os elementos mínimos definidos para o ato, sem registrar dados pessoais desnecessários ou segredos em log.

**Rastreabilidade.** §§ 13.5, 13.6 e 15.6

### RN-030 - Aplicar minimização e proteção de dados pessoais

**Requisito.** Cada tipologia deve declarar somente os campos necessários à finalidade e o ambiente analítico/logs não deve expor CPF ou outros dados individuais além do necessário.

**Critério de aceitação de negócio.** Campos não declarados não são cedidos; CPF não aparece no modelo semântico padrão do Power BI, logs HTTP, auditoria de acesso ou mensagens de erro.

**Rastreabilidade.** §§ 14.5 e 14.5.1

### RN-031 - Definir governança de retenção antes da produção

**Requisito.** A Jornada não deve adotar retenção indefinida de dados pessoais e deve ativar expurgo/consolidação somente após decisão formal de governança.

**Critério de aceitação de negócio.** Políticas de retenção e seus parâmetros são aprovados antes de produção e preservam métricas/auditoria necessárias.

**Rastreabilidade.** § 14.4

### RN-032 - Exigir avaliação de impacto antes da produção

**Requisito.** A entrada em produção deve ser condicionada à avaliação de impacto à proteção de dados pessoais e à definição formal dos papéis e ativos transversais.

**Critério de aceitação de negócio.** RIPD e decisões sobre controladoria, base legal, acesso, retenção e responsabilidade por evidência estão concluídos antes de produção.

**Rastreabilidade.** §§ 14.2 e 14.6

### RN-033 - Formalizar responsabilidades institucionais

**Requisito.** SGM/SEPE, PRODAM e Gestores finalísticos devem operar com responsabilidades distintas e explícitas, sem deslocar para a Jornada decisões administrativas dos órgãos responsáveis.

**Critério de aceitação de negócio.** Papéis de requisitante/governança, operador técnico e controlador/dono do dado estão formalizados e refletidos nos processos operacionais.

**Rastreabilidade.** §§ 3 e 15.1

### RN-034 - Manter interlocução técnica e qualidade por Gestor

**Requisito.** Cada Gestor deve manter interlocutor técnico designado para integração, qualidade cadastral e cumprimento do cronograma de implantação.

**Critério de aceitação de negócio.** Há responsável identificado por origem e acompanhamento de indicadores de qualidade e integração.

**Rastreabilidade.** § 3.4

### RN-035 - Permitir expansão a novos Gestores e Tipos

**Requisito.** A arquitetura e o modelo de negócio devem suportar inclusão de novos Gestores, Tipos de Benefício e Tipos de Serviço por configuração/versionamento, sem redesenho estrutural da Jornada.

**Critério de aceitação de negócio.** Nova origem ou novo Tipo é incorporável por contratos, catálogos, regras e autorização versionados, preservando compatibilidade histórica.

**Rastreabilidade.** §§ 3.3, 7, 15.2 e 15.4

### RN-036 - Manter biometria como evolução e não como núcleo cadastral

**Requisito.** Biometria e identificação facial podem ser incorporadas evolutivamente como mecanismos de verificação/autenticação, sem substituir CPF, nome, data de nascimento e nome da mãe nem se tornarem atributos civis do núcleo Golden.

**Critério de aceitação de negócio.** A adoção futura depende de regras de proteção de dados, segurança e atos normativos específicos; ausência de biometria não inviabiliza a Fase 1.

**Rastreabilidade.** § 3.4

## 6. Regras de negócio consolidadas

| Regra | Tema | Definição |
|---|---|---|
| RN-REG-01 | Gold de primeira fonte | A primeira fonte individualizável estabelece baseline válido; corroboração só existe com outra fonte independente comparável. |
| RN-REG-02 | Fato x atribuição | Fato válido e atribuição canônica da Pessoa são dimensões distintas; conflito de identidade não apaga o fato finalístico. |
| RN-REG-03 | Possibilidade | COMPATIVEL significa apenas compatibilidade calculada; nunca elegibilidade automática, direito ou concessão. |
| RN-REG-04 | Territorialidade | Referência Territorial é vínculo analítico temporal e não sinônimo de domicílio civil. |
| RN-REG-05 | Compartilhamento | Autorização técnica usa credencial, scope e recurso; restrições setoriais são exceções negativas de projeção. |
| RN-REG-06 | Gold não vinculante | A Gold é referência municipal de dados e não impõe atualização automática aos sistemas finalísticos. |
| RN-REG-07 | Evolução aditiva | Mudança material de conceito exige nova versão/campo/contrato; histórico não é reutilizado com nova semântica. |
| RN-REG-08 | Retenção | Retenção de dado pessoal exige política aprovada; não há retenção indefinida por padrão. |

## 7. Fora de escopo

- Substituição dos sistemas transacionais dos Gestores.
- Concessão, suspensão, cancelamento ou pagamento de benefício pela Jornada.
- Atendimento direto ao munícipe pela SGM/SEPE ou por aplicação central da Jornada.
- Unificação de meios de pagamento.
- Edição direta do cadastro transacional dos Gestores pela Jornada.
- Transformação de Possibilidade em concessão, direito ou decisão automática.
- Uso de biometria/identificação facial como substituto do núcleo cadastral na Fase 1.

## 8. Critérios de aceite de negócio da Fase 1

1. Gestores conseguem integrar Pessoas e fatos por contratos versionados, preservando origem e temporalidade.
2. Primeira fonte individualizável constitui Gold baseline sem exigir segunda fonte.
3. Conflitos e pendências de identidade ficam explícitos e não geram atribuição canônica indevida.
4. Fatos válidos permanecem preservados mesmo quando a identidade canônica estiver pendente.
5. Consultas e produtos analíticos distinguem Pessoa, Registros, Possibilidades, qualidade, cobertura e Referência Territorial segundo as regras de negócio.
6. Compartilhamento, restrições setoriais, auditoria, retenção e proteção de dados estão governados antes de Produção.

Os critérios técnicos de build, testes, desempenho, segurança e operação pertencem aos documentos RNF/RT e à Matriz de Rastreabilidade.

## 9. Rastreabilidade e controle de mudança

Este baseline foi derivado da Especificação Técnica Jornada v3.62 e dos anexos normativos vigentes no pacote. A versão 1.1 reorganiza o documento na família de Requisitos da Fase 1 e é incorporada pela Solution Engenharia v3.98 sem alterar Base Normativa v3.62 nem SolutionSchema v3.68. A rastreabilidade completa encontra-se em `Matriz_Rastreabilidade_Requisitos_Jornada_v1.0`.

Mudança material em requisito de negócio deve gerar nova versão deste documento e, quando afetar regra normativa, contrato, dado ou comportamento da plataforma, deve ser refletida por alteração formal da especificação correspondente.

## 10. Histórico de versões

| Versão | Data | Alteração |
|---|---|---|
| 1.0 | 03/09/2026 | Baseline inicial consolidado a partir da Especificação Técnica Jornada v3.62; incorporado na Solution Engenharia v3.96. |
| 1.1 | 03/09/2026 | Reorganização documental na hierarquia RN -> RF/RNF -> RT; critérios de aceite técnicos foram remetidos aos documentos próprios; sem mudança semântica dos 36 RN. |
