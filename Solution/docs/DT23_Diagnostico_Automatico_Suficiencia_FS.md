# DT-23 — Diagnóstico automático de suficiência amostral para recalibração FS

**Decisão confirmada:** 09/10/2026 · **Estado:** ABERTA / NÃO IMPLEMENTADA · **Prioridade:** após estabilização do bootstrap congelado de referências da PR #883 · **Natureza:** diagnóstico read-only, sem execução de calibração.

> **Estado de implementação (10/10/2026):** o avaliador puro `SampleSufficiencyAssessment` e testes unitários foram integrados pela PR #900. Isso **não** implementa coleta automática, contadores persistidos, política versionada, agendamento, semáforo no Monitor nem alertas. A DT-23 permanece aberta; calibração e ativação continuam manuais/governadas. O filtro de estratos exigidos da PR #904 já foi incorporado. A PR #915 (ainda draft) acrescenta a verificação de diversidade de grupos independentes separadamente em `m` e `u` por estrato; seus testes e workflows aprovados não equivalem à implementação integral da DT-23.

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

## Incremento em desenvolvimento — PR #915 (10/10/2026)

O avaliador puro passa a aceitar `IndependentGroupKey` por evidência e um limite `minimumIndependentGroups` por estrato. A contagem de pares únicos (`m`/`u`) continua separada da diversidade de grupos: pares repetidos não aumentam o suporte, grupos ausentes ou divergentes no mesmo par impedem o estado `Sufficient`. O mínimo de grupos independentes é exigido **separadamente nas classes `m` e `u`, em cada estrato solicitado**; grupos abundantes de uma classe não compensam a insuficiência da outra. Os testes de regressão cobrem distribuições assimétricas e fontes de evidência de enumeração única. Contradições de verdade independente seguem bloqueando a certificação e exigem investigação de integridade da fonte, **não revisão humana de pares**. Os testes exercitam a ausência e divergência de identificadores.

**Limite do incremento:** a configuração ainda não é política persistida/versionada; `IndependentGroupKey` precisa de derivação confiável de pessoa/fonte no pipeline real, sem tratar o identificador do par como grupo independente. Não há estimativa de tamanho amostral efetivo, intervalo de confiança para contradições nem integração com o Monitor. Portanto, não declarar DT-23 concluída ou amostra real certificada por estes testes.

**Responsabilidades de identidade:** canais apenas fornecem dados e proveniência. A resolução de identidade pertence ao Linkage, com abstenção quando as evidências são insuficientes. Não criar seleção manual de vínculos por usuários ou canais. O diagnóstico de suficiência não pode acionar calibração, que permanece por demanda explícita.
