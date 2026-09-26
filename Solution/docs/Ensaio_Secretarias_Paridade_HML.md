# Ensaio com dados fornecidos pelas Secretarias — contrato de paridade com HML

**Estado:** diretriz de planejamento para a próxima etapa, ainda não declaração de prontidão operacional.  
**Sequência:** DEV → **Ensaio intersecretarial** → HML → Produção.  
**Princípio:** Ensaio e HML diferem **somente pela massa de dados**. A mesma versão da aplicação, artefatos de implantação, DDL, contratos de integração, regras de identidade, autenticação/autorização, configuração funcional, observabilidade e procedimentos devem ser exercitados. Se algum componente não estiver disponível, a divergência deve ser registrada e resolvida; não se declara paridade fictícia.

## 1. Origem, preparação e responsabilidade pelos dados

Cada Secretaria fornece os dados de seu próprio sistema **dentro dos contratos de integração estabelecidos**. A responsabilidade por selecionar, preparar, anonimizar e/ou gerar dados sintéticos derivados de sua base é da Secretaria fornecedora, com sua governança e autorizações aplicáveis. A estratégia provável é gerar massas sintéticas a partir de características das bases reais, mas não se presume que esse método já esteja aprovado ou executado.

Antes da entrega, cada Secretaria deve declarar responsável, sistema de origem, contrato e versão, campos presentes/ausentes, volumes, período de referência, estratégia de anonimização ou síntese, limitações de representatividade, restrições de uso e contato para correção. A Jornada valida **conformidade contratual e segurança de recebimento**, mas não assume que dados recebidos são anônimos só porque foram rotulados como sintéticos. A classificação e a autorização de tratamento precisam estar formalizadas pelo fornecedor e pelos responsáveis competentes.

Não solicitar dados pessoais reais como alternativa informal quando uma massa falhar. Se uma massa não atender ao contrato, devolver diagnóstico técnico para correção na origem. Identificadores sintéticos e relacionamentos entre cargas devem permitir ensaiar CPF tardio, homônimos, conflitos, retransmissão, múltiplos Gestores e três ondas sem reidentificação de cidadãos reais.

## 2. Matriz obrigatória de paridade

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

## 3. Roteiro do Ensaio

1. **Acordos e inventário:** confirmar contratos vigentes, Secretarias participantes, responsáveis, autorizações e catálogo de cargas. Não pressupor aprovação de documento institucional pendente.
2. **Prontidão técnica comum:** executar build, testes, implantação, migração DDL, integração de identidade corporativa, readiness HTTP, scheduler, Bronze e observabilidade. Defeitos encontrados aqui também bloqueiam a alegação de prontidão para HML.
3. **Recebimento controlado:** validar schema, versão, ZIP, integridade, segurança, volumes, campos obrigatórios/opcionais, classificação e proveniência; rejeitar payloads incompatíveis sem correção silenciosa.
4. **Cargas em ondas:** executar pelo menos três ondas com melhoria posterior de atributos e CPF tardio; medir retransmissão, idempotência, reavaliação de pendentes, conflitos, homônimos, mudança de decisão e crescimento de `linkage_resultado`.
5. **Produto consumível:** testar operações e **payloads** da API/OpenAPI com consumidores das Secretarias; conferir autorização, respostas negativas, paginação/lotes, rastreabilidade, top-5 quando implementado e resolução governada quando disponível.
6. **Operação:** medir throughput, P95/P99, backlog, locks, publicação, restore SQL/Bronze, falhas controladas e recuperação com infraestrutura representativa.
7. **Conferência e avaliação:** separar conferência determinística de implementação, avaliação de desempenho do modelo na massa sintética e validação estatística em população representativa. Modelo RASCUNHO não vira VALIDADO/ATIVO enquanto os gates vigentes não forem atendidos.
8. **Saída:** publicar relatório por contrato/Secretaria, evidências, não conformidades, limitações dos dados, inventário de diferenças Ensaio × HML e plano de correção. HML reutiliza a mesma versão e os mesmos testes, trocando a massa.

## 4. Critérios para transição Ensaio → HML

- Contratos e cenários essenciais executados de ponta a ponta; divergências da API documentadas e corrigidas.
- Mesma imagem/binários, schema, policies e configuração funcional reproduzidos, com hashes/versões registrados.
- Autenticação corporativa real integrada ou impedimento formalmente declarado; não usar o resolver sintético de Development para declarar equivalência.
- Falhas críticas de segurança, isolamento, identidade determinística e integridade de dados corrigidas; nenhuma ativação probabilística sem gates.
- Resultados, limitações de síntese/anonimização, volumetria e evidências acessíveis aos responsáveis autorizados.
- Diferenças de infraestrutura e de dados catalogadas; se houver diferenças funcionais, **não** afirmar que somente os dados mudaram.
- As pendências institucionais e estatísticas que impedem homologação/Produção continuam visíveis. A conclusão do Ensaio não constitui automaticamente aprovação da #31, liberação de HML estrita ou autorização de Produção.

## 5. Impacto no backlog

Priorizar antes do Ensaio: OpenAPI tipado e testes de payload; atualização do runtime suportado; autenticação corporativa e autoria individual; tolerância da conferência tecnicamente justificada; gravação por mudança; contratos de cargas; instalação reproduzível; roteiro de três ondas. Medir publicação set-based no Ensaio e HML antes de substituição definitiva. Retenção/expurgo requer decisão formal e deve impedir descarte prematuro de evidências.

**Nota de segurança:** dados sintéticos derivados de bases reais podem reter combinações identificáveis ou reproduzir registros raros. O fornecimento deve incluir avaliação e aprovação da Secretaria; a Jornada aplica controles de minimização, acesso, segregação e retenção adequados à classificação recebida.
