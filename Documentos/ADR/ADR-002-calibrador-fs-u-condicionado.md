# ADR-002 — Calibrador Fellegi–Sunter e u condicionado ao blocking

- **Status:** Aceita
- **Data:** 2026-09-20
- **Escopo:** linkage probabilístico, Calibrador, blocking, referência nominal

## Contexto

A proposta anterior previa um estágio DF nominal antes do Fellegi–Sunter. A implementação mostrou que esse estágio não participava do runtime e não resolvia os limites observados: homônimos totais permanecem indistinguíveis; abreviação pertence ao comparador; estados m sem suporte dependem de pares rotulados.

Também foi identificado que u nominal incondicional — inclusive quando derivado do IBGE — não representa necessariamente os não-matches produzidos pelo blocking. Passes nominais pré-selecionam concordância e elevam a chance de coincidência entre candidatos.

## Decisão

1. O resolvedor probabilístico operacional é um único FS.
2. O estágio DF autônomo é retirado da arquitetura corrente.
3. A matemática de term frequency pode ser preservada como componente reutilizável dentro do FS, mas não cria uma segunda decisão nem contorna guards.
4. O alvo de u é o universo efetivo de candidatos do blocking.
5. Como o scorer atual é pass-agnostic após a união, o u operacional é estimado sobre a união deduplicada dos passes.
6. O Calibrador mede suporte nominal por passe e exige suficiência por passe antes de abandonar o bootstrap.
7. **Atualizado em 29/09/2026:** o IBGE é carregado **uma única vez**, antes da primeira Entrega, com integridade, versão e hash auditáveis. O bootstrap inicial fornece parâmetros nominais de `u` **e insumos para dedução de índices/projeções e passes iniciais de blocking**. Persistir derivados e proveniência. Gerações posteriores usam dados ingeridos da Jornada, sem exigir referência IBGE ativa, recarregar snapshot ou reinterpretar modelos anteriores. Com evidência empírica insuficiente, conservar os parâmetros iniciais persistidos, sem declarar convergência. A regra anterior de falhar todo `GENERATE_DRAFT` sem referência IBGE ativa está **revogada**; falha por ausência do primeiro bootstrap permanece.
8. A troca de bootstrap para estimativa empírica ocorre por critérios explícitos de suficiência, não por calendário. `m` **não** é deduzido das marginais IBGE: requer pares positivos confiáveis, preferencialmente de Gestores distintos, com CPF usado apenas como gabarito oculto do score. Um `m` sintético depende de hipóteses de erro explícitas e não equivale a medição cadastral real. Todos os atributos da observação/identidade podem ser NULL; a exigência de admissão é definida pelo contrato versionado de cada Secretaria, nunca por NOT NULL global. A ausência desabilita somente passes dependentes e é neutra no score quando a política vigente assim determina.
9. TRAIN/VALIDATION/TEST permanecem separados; TEST nunca retroalimenta seleção.
10. Threshold e margem são produtos da calibração; não entradas fixas operacionais.
11. Evidência demográfica exata não é presumida única. Homônimo total deve permanecer não resolvido/conflitado até existir evidência independente suficiente.

## Consequências

- remove-se código experimental DF/Splink desconectado;
- reduz-se uma segunda fronteira de calibração sem ganho operacional comprovado;
- u nominal passa a ter proveniência explícita de bootstrap ou blocking condicionado;
- o bootstrap único também orienta índices/passes iniciais, sem alegar que marginais IBGE medem a distribuição conjunta pessoa+mãe+nascimento;
- todos os atributos são logicamente anuláveis; contratos por Secretaria definem requisitos de entrada;
- suporte por passe vira evidência persistida, sem persistir PII adicional;
- TF continua disponível para futura integração ao FS, sujeita a calibração própria;
- a ativação real continua bloqueada pela validação representativa da issue #31.


## Emenda de interoperabilidade de auditoria — 2026-09-20

A retirada do estágio DF/Splink não impede uma superfície read-only de intercâmbio. `Jornada.Linkage.Evaluation --export-calibration` pode exportar modelo `ATIVO` ou `VALIDADO`, parâmetros, ruleset, proveniência e vetores matemáticos para auditoria externa, desde que:

- o exportador não participe da decisão operacional nem escreva modelo;
- o artefato declare que o u da Jornada é condicionado ao blocking;
- estados sem mapeamento 1:1 sejam declarados, não colapsados silenciosamente;
- nenhuma alegação de qualidade estatística seja derivada do round-trip de formato;
- a ativação probabilística continue sujeita à issue #31.

## Esclarecimento posterior — ADR-007 (26/09/2026)

O uso de bootstrap IBGE antes de suporte suficiente do u condicionado é **transição metodológica declarada**, não licença para fallback geográfico por nome. A [decisão de escopo por atributo](../../Solution/docs/Decisoes_Linkage_Calibracao_IBGE_20260926.md) fixa mãe no nacional V1 e pessoa municipal SP apenas em V2 candidata, sem preencher lacunas de SP por UF/Brasil. A [ADR-007](ADR-007-conferencia-externa-splink-sem-python-operacional.md) apenas isola o runner de diagnóstico: sua comparação nominal V1 de u aleatório entre pessoas distintas não valida nem substitui o u condicionado à união deduplicada dos candidatos desta ADR.

## Precedência e registro histórico (29/09/2026)

Os itens 7 e 8 acima substituem expressamente a exigência histórica de snapshot ativo em cada `GENERATE_DRAFT`. O histórico de commits preserva a formulação antiga, que **não é normativa**. Para decisões correntes, consultar [índice canônico](../../Solution/docs/Indice_Decisoes_Vigentes.md), [fluxos detalhados](../../Solution/docs/Fluxos_Blocking_Selecao_Registro.md) e [Calibrador](../../Solution/docs/Calibrador_FS_Specification.md). A mudança documental não prova adequação do Worker, SQL ou contratos; verificar implementação e gates antes de declarar entregue.
