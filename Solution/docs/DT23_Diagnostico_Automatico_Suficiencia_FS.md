# DT-23 — Diagnóstico automático de suficiência amostral para recalibração FS

**Decisão confirmada:** 09/10/2026 · **Estado:** ABERTA / AVALIADOR PURO INTEGRADO; DIAGNÓSTICO OPERACIONAL NÃO IMPLEMENTADO · **Prioridade:** após estabilização do bootstrap congelado de referências da PR #883 · **Natureza:** diagnóstico read-only, sem execução de calibração.

> **Estado de implementação (10/10/2026):** o avaliador puro `SampleSufficiencyAssessment` e testes unitários foram integrados pela PR #900. Isso **não** implementa coleta automática, contadores persistidos, política versionada, agendamento, semáforo no Monitor nem alertas. A DT-23 permanece aberta; calibração e ativação continuam manuais/governadas. O filtro de estratos exigidos da PR #904 já foi incorporado. A PR #915 (integrada em 11/10/2026, squash `6d76242306fa8ab90abfa226146a942c8f082781`) acrescenta a verificação de diversidade de grupos independentes separadamente em `m` e `u` por estrato; seus testes e workflows aprovados não equivalem à implementação integral da DT-23.

## Critérios para encerrar a DT-23 (não satisfeitos pela PR #915)

A aprovação dos testes do avaliador puro não significa que exista diagnóstico automático em operação. Antes do encerramento, demonstrar em ambiente descartável: (1) coletor de evidências reais independentes com origem auditável, exclusão de pares repetidos e escopo de estratos explícito; (2) persistência e versionamento da política e dos contadores por estrato e classe m/u; (3) execução periódica do diagnóstico com estados e justificativas visíveis no Monitor; (4) testes de insuficiência, ausência de verdade independente, contradição, dados malformados e mudança de versão; (5) prova negativa de que o diagnóstico jamais invoca geração, validação ou ativação de modelo. Registrar artefatos e execução antes de marcar a dívida como concluída.

## Fronteira obrigatória

- **Automático:** acumular/consultar evidência real independente, calcular suficiência e emitir diagnóstico periódico com semáforo **VERDE / AMARELO / VERMELHO**, suportes e justificativas.
- **Manual:** iniciar a calibração FS por comando explícito de operador autorizado; `GENERATE_DRAFT`, `VALIDATE` e `ACTIVATE` não são disparados pelo diagnóstico. Ativação continua governada e auditável.
- **Inicialização:** carrega/verifica somente referências IBGE/demográficas congeladas em `ref`; **não** calibra, não recalcula projeções, não exige amostra suficiente para health/readiness.
- **Imutabilidade:** referências e calibrações históricas publicadas em `ref` não são sobrescritas. Um novo resultado é uma nova versão, após operação manual.

## Critérios técnicos de implementação

1. Definir elegibilidade da verdade independente por CPF e excluir da verdade rótulos derivados apenas do próprio linkage. Ocultar CPF das features ao avaliar o scorer.
2. Contabilizar `m` e `u` sobre o universo **efetivamente sobrevivente ao blocking**, incluindo deduplicação, suporte por passe/estrato, diversidade de pessoas/fontes e eventuais dependências entre evidências.
3. Parametrizar e versionar limites de suficiência, confiança e janela temporal. Os números exploratórios de 5.000/1.000 **não são gates homologados**.
4. Persistir ou expor snapshot de diagnóstico reproduzível: data de corte, versão da política, modelo ativo, contagens, estratos insuficientes, motivos, estado e proveniência; jamais dados pessoais em logs públicos.
5. Produzir semáforo operacional com justificativa objetiva e indicação de **oportunidade de recalibração manual**. VERDE significa evidência suficiente para iniciar avaliação, **não** autorização de promoção nem certificação automática de FDR.
6. Garantir execução idempotente, observabilidade, tratamento de indisponibilidade de fontes e testes de transição entre estados; falha de diagnóstico não deve derrubar API/Processor.

## Critérios de aceite

- Testes demonstram mudança do diagnóstico quando evidência independente elegível cresce, quando suporte por passe falta e quando a política versionada muda.
- Ausência de evidência retorna estado explícito de insuficiência/indeterminação, nunca falso VERDE.
- Testes provam que ciclos do Monitor, reinício da infraestrutura e subida de referências **não** invocam Calibrador nem `GENERATE_DRAFT`/`VALIDATE`/`ACTIVATE`.
- Exibição read-only no Monitor, com controle de acesso e evidência auditável; sem scheduler de calibração.
- Revisão do operador continua condição necessária para calibrar e promover.

## Relações

- [Especificação de evidência real e governança](Especificacao_Calibracao_Progressiva_Evidencia_Real_20260929.md)
- [Especificação FS corrente](Calibrador_FS_Specification.md)
- [Monitor Operacional](Monitor_Operacional.md)
- [DT-15 — governança](DT15_Governanca_Decisao_Modelo.md)
- [Decisões vigentes](Indice_Decisoes_Vigentes.md)

**Separação de escopo:** a PR #883 trata da carga congelada de referências e do vínculo/fingerprint; esta DT-23 é trabalho futuro independente. Não declarar a DT concluída com base apenas na documentação.

## Contrato negativo obrigatório para aceite

O diagnóstico automático de suficiência deve ser **estritamente observacional**: nenhuma transição `GENERATE_DRAFT`, `VALIDATE`, `ACTIVATE`, publicação de modelo ou alteração de limiar pode ser efeito colateral de uma consulta, coleta periódica ou evento de nova amostra. Ensaios negativos devem verificar esse comportamento inclusive quando a amostra passa de insuficiente para suficiente, em retries e após reinicialização. A suficiência é um convite auditável à decisão humana, não autorização para calibrar. A documentação deste contrato não representa implementação da DT-23.

### Matriz mínima de testes de fronteira

Testar com 0, `n_min-1`, `n_min` e `n_min+1` exemplos válidos por estrato; casos com duplicação da mesma pessoa, evidência contraditória e rótulos ausentes devem ser identificados e não inflar `n` efetivo. O resultado deve registrar versão da política, universo de candidatos, data de corte, fonte, contagens elegíveis e razões de não certificação. O sinal `SUFICIENTE` jamais altera modelo ativo, parâmetros ou jobs de calibração; a ativação depende de comando humano explícito. Este texto é critério de teste futuro, não prova de execução.

## Incremento integrado — PR #915 (11/10/2026)

O avaliador puro passa a aceitar `IndependentGroupKey` por evidência e um limite `minimumIndependentGroups` por estrato. A contagem de pares únicos (`m`/`u`) continua separada da diversidade de grupos: pares repetidos não aumentam o suporte, grupos ausentes ou divergentes no mesmo par impedem o estado `Sufficient`. O mínimo de grupos independentes é exigido **separadamente nas classes `m` e `u`, em cada estrato solicitado**; grupos abundantes de uma classe não compensam a insuficiência da outra. Para impedir certificação por muitos pares correlacionados, o avaliador exige em `m` pelo menos `max(minimumM, minimumIndependentGroups)` grupos distintos e em `u` pelo menos `max(minimumU, minimumIndependentGroups)` grupos distintos. As contagens de pares continuam disponíveis para auditoria, mas não substituem a diversidade de grupos. Esta é uma regra conservadora de suficiência, **não** uma estimativa estatística homologada de tamanho amostral efetivo; a derivação confiável de grupos e a política versionada ainda são pendências. Os testes de regressão cobrem distribuições assimétricas e fontes de evidência de enumeração única. Contradições de verdade independente seguem bloqueando a certificação e exigem investigação de integridade da fonte, **não revisão humana de pares**. Os testes exercitam a ausência e divergência de identificadores.

**Limite do incremento:** a configuração ainda não é política persistida/versionada; `IndependentGroupKey` precisa de derivação confiável de pessoa/fonte no pipeline real, sem tratar o identificador do par como grupo independente. Não há estimativa de tamanho amostral efetivo, intervalo de confiança para contradições nem integração com o Monitor. Portanto, não declarar DT-23 concluída ou amostra real certificada por estes testes.

**Responsabilidades de identidade:** canais apenas fornecem dados e proveniência. A resolução de identidade pertence ao Linkage, com abstenção quando as evidências são insuficientes. Não criar seleção manual de vínculos por usuários ou canais. O diagnóstico de suficiência não pode acionar calibração, que permanece por demanda explícita.

**Ajuste de integridade (10/10/2026):** duas chaves de grupo independente não vazias e distintas associadas ao mesmo par tornam o diagnóstico `Indeterminate`. A ausência de chave continua `Insufficient`. Nenhuma calibração é acionada.

**Endurecimento de integridade — PR #915:** a duplicação do mesmo par com uma chave de grupo presente e outra ausente também é divergência de proveniência e retorna `Indeterminate`, mesmo quando os limiares numéricos são atendidos. A decisão é fail-closed: não se completa automaticamente a chave ausente com o valor da outra ocorrência. Teste unitário de regressão cobre o caso. A CI foi validada antes do merge da PR #915; isso não altera a regra de calibração exclusivamente manual.

**Integridade de chaves de pares (11/10/2026):** a avaliação materializa uma única vez a evidência independente dos estratos solicitados. Evidência independente sem `PairKey` válido (inclusive somente espaços) não pode ser descartada silenciosamente: o diagnóstico retorna `Indeterminate`, mesmo se outros pares alcançarem os mínimos. Estratos não solicitados não contaminam o diagnóstico. Testes de regressão específicos foram integrados na PR #915. Isto não conclui a coleta automática, a política versionada, a integração com Monitor nem autoriza calibração automática.

**Integridade entre estratos (11/10/2026):** `PairKey` representa um par globalmente identificavel no universo de candidatos, nao uma chave local a cada estrato. Se a mesma chave normalizada ocorrer em dois estratos obrigatorios distintos, a avaliacao retorna `Indeterminate` e explicita o conflito; uma unica observacao independente nao pode certificar duas particoes. Pares diferentes em estratos diferentes continuam elegiveis. Os testes cobrem ambos os caminhos. Esta regra nao substitui a coleta real nem a politica versionada, e nunca aciona calibracao.

**Integridade de entrada do coletor (11/10/2026):** registros de evidência nulos na sequência de entrada impedem certificação (`Indeterminate`), inclusive quando outros pares satisfazem os mínimos. O avaliador materializa a sequência uma única vez e não divulga identificadores dos pares no motivo. O teste unitário correspondente foi acrescentado à branch de guardrails; o incremento foi integrado pela PR #915, mas a operação automática permanece pendente. Não há gatilho de calibração automática.

## Registro de reconciliação — 11/10/2026

- **Integrado:** PR #915 (squash `6d76242306fa8ab90abfa226146a942c8f082781`) — guardrails do avaliador puro e testes. Não representa coleta operacional.
- **Não implementado:** fonte de verdade independente auditável, extração automática de pares pós-blocking, persistência dos contadores por estrato, política versionada, ciclo periódico e painel de diagnóstico no Monitor.
- **Aceite obrigatório:** uma execução reprodutível em SQL descartável com dados sintéticos que demonstre estados, motivos e versão da política; evidência negativa de nenhuma chamada a `GENERATE_DRAFT`, `VALIDATE` ou `ACTIVATE`.
- **Governança:** a recalibração e a ativação de modelos permanecem exclusivamente manuais. A DT-23 continua aberta até a implementação e validação operacional.

## Implementação incremental em revisão — PR #920 (11/10/2026)

A PR #920 introduz, **ainda em draft**, as tabelas `ref.dt23_politica_suficiencia` (versão única, JSON de configuração e timestamp) e `ref.dt23_diagnostico_suficiencia` (referência à política, data de corte, estado, contadores, justificativas e proveniência JSON). O smoke SQL associado verifica escrita e leitura em transação com rollback; não instala scheduler, triggers nem comandos de calibração. O estado `INDETERMINADO` permite representar falta de verdade independente sem emitir falso sinal verde.

**Importante:** enquanto a PR #920 não estiver integrada, essas tabelas **não existem como funcionalidade garantida na master**. Mesmo após eventual merge, a existência de tabelas e smoke não prova coleta real independente, política homologada, deduplicação pós-blocking, execução periódica, integração read-only no Monitor ou proteção operacional contra disparos de calibração. A DT-23 permanece **ABERTA**. O teste transacional precisa ser executado em SQL descartável na CI antes de promover a PR.

### Projeção observacional acrescentada à PR #920

A migração também expõe `ref.vw_dt23_ultimo_diagnostico`, com **uma linha por política que já tenha diagnóstico persistido**. A seleção prioriza `data_corte` (mais recente), seguida por `registrado_em` e desempate determinístico por `diagnostico_id`, preserva a versão da política e retorna `INDETERMINADO` quando esse foi o estado efetivamente registrado. Política sem diagnóstico não recebe VERDE sintético. O smoke SQL exercita inserção de duas observações e seleção da mais recente, com rollback. **Esta visão não constitui endpoint do Monitor, coletor, agendamento nem gatilho de calibração**; esses itens continuam pendentes.

### Testes negativos incrementais (11/10/2026)

O arquivo `database/tests/20261011_DT23_Diagnostico_Politica_Versionada_Negative.sql` verifica a ausência de triggers nas tabelas observacionais, referências a comandos de calibração na definição da visão e certificação indevida de política sem diagnóstico. Utiliza apenas política sintética em transação revertida por rollback. **A existência do arquivo não demonstra que o teste esteja incluído no job SQL da CI**: verificar explicitamente a invocação e o resultado antes de considerar esse aceite cumprido. O teste não substitui coletor independente, avaliação periódica, idempotência operacional nem integração read-only no Monitor.

**Regressão de idempotência (11/10/2026):** o teste negativo da PR #920 tenta inserir dois diagnósticos com a mesma combinação `(politica_id, data_corte)` e estados divergentes; exige rejeição por unicidade SQL (erros 2601/2627), com rollback de todos os registros sintéticos. A restrição evita persistência duplicada, mas o produtor ainda deve comparar snapshots e definir a semântica de reprocessamento. A execução deste teste no pipeline SQL descartável ainda deve ser comprovada; não confundir arquivo versionado com gate executado.
