# Ensaio com dados fornecidos pelas Secretarias — contrato de paridade com HML

**Estado:** diretriz de planejamento para a próxima etapa, ainda não declaração de prontidão operacional.  
**Sequência:** DEV → **Ensaio com Secretarias participantes** → HML → Produção. O Ensaio pode envolver uma ou várias Secretarias, com adesão e cargas em ondas, sem pressupor participação de todas.  
**Princípio:** Ensaio e HML diferem **somente pela massa de dados**. A mesma versão da aplicação, artefatos de implantação, DDL, contratos de integração, regras de identidade, autenticação/autorização, configuração funcional, observabilidade e procedimentos devem ser exercitados. Se algum componente não estiver disponível, a divergência deve ser registrada e resolvida; não se declara paridade fictícia.

## 1. Origem, preparação e responsabilidade pelos dados

Cada Secretaria participante fornece os dados de seu próprio sistema **dentro dos contratos de integração estabelecidos**. A responsabilidade por selecionar, preparar, anonimizar e/ou gerar dados sintéticos derivados de sua base é da Secretaria fornecedora, com sua governança e autorizações aplicáveis. A estratégia provável é gerar massas sintéticas a partir de características das bases reais, mas não se presume que esse método já esteja aprovado ou executado.

Antes da entrega, cada Secretaria deve declarar responsável, sistema de origem, contrato e versão, campos presentes/ausentes, volumes, período de referência, estratégia de anonimização ou síntese, limitações de representatividade, restrições de uso e contato para correção. A Jornada valida **conformidade contratual e segurança de recebimento**, mas não assume que dados recebidos são anônimos só porque foram rotulados como sintéticos. A classificação e a autorização de tratamento precisam estar formalizadas pelo fornecedor e pelos responsáveis competentes.

Não solicitar dados pessoais reais como alternativa informal quando uma massa falhar. Se uma massa não atender ao contrato, devolver diagnóstico técnico para correção na origem. Identificadores sintéticos e relacionamentos entre cargas devem permitir ensaiar CPF tardio, homônimos, conflitos, retransmissão, múltiplos Gestores e três ondas sem reidentificação de cidadãos reais.

## 2. Valor probatório e participação das Secretarias

O Ensaio é **fonte potencialmente forte de evidência técnica, operacional e estatística**, inclusive para a #31, na medida em que a qualidade dos dados recebidos e o desenho de avaliação sustentem as conclusões. Não há rebaixamento automático da evidência por ocorrer antes de HML ou por empregar dados sintéticos derivados de bases reais. Tampouco se presume representatividade: ela deve ser demonstrada no domínio efetivamente coberto.

A adesão pode ocorrer por uma ou várias Secretarias e por contratos distintos. Registrar para cada entrega a Secretaria, o contrato, a versão, a população de origem, o período, o volume, a cobertura de CPF e campos auxiliares, o processo de geração/anonimização, a fidelidade de distribuições e erros relevantes e a proveniência da verdade de referência. O relatório separa resultados por Secretaria, contrato, estrato e onda e só apresenta agregados quando composição, amostragem e comparabilidade justificarem.

Para que os resultados de linkage sejam evidência forte, avaliar preservação de homônimos, parentes, nomes brasileiros, nome da mãe, ausência e chegada tardia de CPF, padrões de erro, dependência entre campos, prevalência de pares verdadeiros, bloqueio, seleção/não resposta e incerteza. A verdade de referência deve ser independente da decisão avaliada e ter qualidade auditável. Quando a síntese reproduzir adequadamente as características relevantes das bases e oferecer verdade de referência confiável, seus resultados podem sustentar decisões de engenharia e avaliação do modelo no escopo demonstrado. Lacunas são qualificações da conclusão, não motivo para descartar toda a evidência.

A avaliação consolidada deve explicitar o universo coberto pelas Secretarias participantes e o que permanece sem cobertura; novas Secretarias ou novas ondas ampliam a evidência cumulativamente. O gate estatístico vigente da #31 continua aplicável: evidência forte produzida no Ensaio deve poder instruir sua aprovação, mediante os critérios e responsáveis previstos, sem exigir repetir mecanicamente o mesmo estudo em HML se apenas a massa mudar e a transportabilidade estiver fundamentada.

## 3. Matriz obrigatória de paridade

| Dimensão | Ensaio | HML | Regra |
|---|---|---|---|
| Binários, .NET, dependências, containers e DDL | Mesmos artefatos versionados | Mesmos artefatos | Divergência exige correção ou nova execução |
| Contratos ZIP/API/OpenAPI e regras de validação | Iguais | Iguais | Nenhum atalho para aceitar payload do Ensaio |
| Autenticação, autorização, segregação por Gestor e autoria individual | Mesma integração corporativa e policies | Mesma integração corporativa e policies | Credenciais/segredos distintos por ambiente; nenhuma autenticação DEV como substituto silencioso |
| SQL Server, Bronze, Processor, Parameters Worker, Runner e scheduler | Mesma topologia lógica e procedimentos | Mesma topologia lógica e procedimentos | Diferenças de capacidade física devem ser medidas e registradas; não presumir equivalência de desempenho |
| Modelo, guards, thresholds, conferência e promoção | Mesmas regras e gates | Mesmas regras e gates | Não contornar `UNFROZEN` ou #31 para obter resultado positivo |
| Auditoria, logs, métricas, backup/restore e recuperação | Mesmos controles | Mesmos controles | Evidências segregadas por ambiente |
| Dados | Fornecidos e preparados pelas Secretarias sob contrato; possivelmente sintéticos derivados das bases | Massa autorizada para homologação conforme governança aplicável | **Única diferença funcional planejada** |

Paridade não significa copiar credenciais, identificadores de recursos, endereços ou dados entre ambientes: são valores ambientais distintos que devem satisfazer os mesmos controles e procedimentos. Se HML usar dados diferentes, as conclusões estatísticas e de volumetria precisam ser reavaliadas na nova massa.

## 4. Roteiro do Ensaio

1. **Acordos e inventário:** confirmar contratos vigentes, uma ou várias Secretarias participantes, responsáveis, autorizações, cronogramas e catálogo de cargas; admitir adesões e ondas posteriores. Não pressupor aprovação de documento institucional pendente.
2. **Prontidão técnica comum:** executar build, testes, implantação, migração DDL, integração de identidade corporativa, readiness HTTP, scheduler, Bronze e observabilidade. Defeitos encontrados aqui também bloqueiam a alegação de prontidão para HML.
3. **Recebimento controlado:** validar schema, versão, ZIP, integridade, segurança, volumes, campos obrigatórios/opcionais, classificação e proveniência; rejeitar payloads incompatíveis sem correção silenciosa.
4. **Cargas em ondas:** executar pelo menos três ondas com melhoria posterior de atributos e CPF tardio; medir retransmissão, idempotência, reavaliação de pendentes, conflitos, homônimos, mudança de decisão e crescimento de `linkage_resultado`.
5. **Produto consumível:** testar operações e **payloads** da API/OpenAPI com consumidores das Secretarias; conferir autorização, respostas negativas, paginação/lotes, rastreabilidade, top-5 quando implementado e resolução governada quando disponível.
6. **Operação:** medir throughput, P95/P99, backlog, locks, publicação, restore SQL/Bronze, falhas controladas e recuperação com infraestrutura representativa.
7. **Conferência e avaliação:** separar conferência determinística de implementação e avaliação do modelo. Tratar os dados do Ensaio como evidência estatística potencialmente forte, inclusive para a #31, segundo fidelidade, cobertura, amostragem, verdade de referência e incerteza; publicar resultados por Secretaria/estrato/onda e agregados justificados. Modelo RASCUNHO não vira VALIDADO/ATIVO enquanto os gates vigentes não forem atendidos.
8. **Saída:** publicar relatório por contrato/Secretaria/onda, evidências técnicas e estatísticas, conclusões proporcionais à qualidade e cobertura dos dados, não conformidades, limitações dos dados, inventário de diferenças Ensaio × HML e plano de correção. HML reutiliza a mesma versão e os mesmos testes, trocando a massa.

## 5. Critérios para transição Ensaio → HML

- Contratos e cenários essenciais executados de ponta a ponta; divergências da API documentadas e corrigidas.
- Mesma imagem/binários, schema, policies e configuração funcional reproduzidos, com hashes/versões registrados.
- Autenticação corporativa real integrada ou impedimento formalmente declarado; não usar o resolver sintético de Development para declarar equivalência.
- Falhas críticas de segurança, isolamento, identidade determinística e integridade de dados corrigidas; nenhuma ativação probabilística sem gates.
- Resultados, limitações de síntese/anonimização, volumetria e evidências acessíveis aos responsáveis autorizados.
- Diferenças de infraestrutura e de dados catalogadas; se houver diferenças funcionais, **não** afirmar que somente os dados mudaram.
- Evidências estatísticas produzidas no Ensaio podem fundamentar a #31 se satisfizerem os requisitos de representatividade, verdade de referência, avaliação independente e incerteza no escopo avaliado. A aprovação formal continua dependente dos gates aplicáveis; não exigir nova coleta apenas por mudança de nome da etapa.
- As pendências institucionais ainda aplicáveis continuam visíveis. A conclusão do Ensaio não constitui automaticamente liberação de HML estrita ou autorização de Produção.

## 6. Impacto no backlog

Prioridade do núcleo antes do Ensaio: motor único de linkage C# reutilizando a infraestrutura existente, identidade progressiva com reprocessamento por mudanças **e dependências do lado candidato**, persistência apenas de transições semânticas e testes de três ondas. A referência IBGE permanece como bootstrap; CIDACS-RL é benchmark metodológico. Não pressupor qualidade diferente entre registros com e sem CPF. Em paralelo, corrigir os bloqueios indispensáveis de runtime, contratos de API, autenticação e tolerância de conferência. Busca de semelhantes para o balcão é opcional e posterior ao núcleo; BI não compete com essas prioridades. Detalhes em `Nucleo_Linkage_Identidade_Progressiva.md`. Medir publicação set-based no Ensaio e HML antes de substituição definitiva. Retenção/expurgo requer decisão formal e deve impedir descarte prematuro de evidências.

**Nota de segurança:** dados sintéticos derivados de bases reais podem reter combinações identificáveis ou reproduzir registros raros. O fornecimento deve incluir avaliação e aprovação da Secretaria; a Jornada aplica controles de minimização, acesso, segregação e retenção adequados à classificação recebida.
