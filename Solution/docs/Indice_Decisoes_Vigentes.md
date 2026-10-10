> **Decisão de 09/10/2026 — DT-23:** [diagnóstico de suficiência](DT23_Diagnostico_Automatico_Suficiencia_FS.md) deve ser automático, periódico e read-only; **calibração FS manual por demanda**, **ativação governada**. O diagnóstico ainda não está implementado. A subida do sistema verifica/carrega referências congeladas, mas não calibra.

> **Nota editorial — 09/10/2026:** o índice de decisões de
> 29/09 permanece válido como referência de precedência para
> identidade/linkage, mas **não** é fotografia das implementações
> de Console/CI em outubro. Para essas superfícies ver
> [Manual integrado](Manual_Sistema_Consolidado_20261009.md),
> [Estado atual](Estado_Atual_Projeto.md) e
> [Console DEV](Console_DEV_Supervisao_Atual.md).
> Nenhuma implementação altera uma decisão normativa por si só.
>
# Índice de decisões vigentes — Jornada

**Atualização:** 29/09/2026. **Fonte de precedência para a candidata v5.00:** [Decisões canônicas de identidade e linkage](Decisoes_Canonicas_Identidade_Linkage_20260929.md). A decisão nova prevalece sobre texto histórico contraditório, mas não torna o código automaticamente conforme, não reescreve a última release formal nem substitui aprovação institucional. Consulte o [catálogo de vigência de 29/09](Catalogo_Vigencia_Documental_20260929.md) como snapshot datado; para estado corrente, confronte [Estado atual](Estado_Atual_Projeto.md) com `master`, CI e issues.

| Domínio | Fonte de decisão | Detalhamento; estado |
|---|---|---|
| **Reavaliação de RESOLVIDOS / Trilha 4** | [DT-22 — decisão de escopo de 09/10](DT22_Reavaliacao_Governada_Resolvidos.md) | **Trilha 4 contínua encerrada como frente**. Replay extraordinário governado de RESOLVIDOS sem CPF **ABERTO/POSTERGADO**, não confundir com `REPLAY` histórico nem considerar implementado. Limitação atual do `INCREMENTAL` para terceiros RESOLVIDOS indiretamente afetados permanece documentada. Preservar [regras canônicas CPF/UUID](Decisoes_Canonicas_Identidade_Linkage_20260929.md), sem alterar publicação normativa. |
| UUID inicial/canônico, CPF, fusão e divisão | [DC-ID-01/02](Decisoes_Canonicas_Identidade_Linkage_20260929.md#dc-id-01--dois-uuids-sem-atribuição-artificial) | [Arquitetura](Arquitetura_Identidade_Linkage.md), [âncora](CPF_Ancora_Processor_V1.md), [composição](Identidade_Composicao_PreAplicacao.md). **Nova regra de separação sem CPF ainda não codificada.** |
| V8 único, sem veto demográfico fixo | [DC-LK-01/02](Decisoes_Canonicas_Identidade_Linkage_20260929.md#dc-lk-01--somente-v8-como-contrato-executável-futuro) | [Política V8 histórica](Linkage_Ausencia_Neutra_V8.md), [conferência](Linkage_Implementation_Conference.md). **Legados e guard ainda presentes no runtime.** |
| Nome social, blocking, score e ausência | [DC-LK-03](Decisoes_Canonicas_Identidade_Linkage_20260929.md#dc-lk-03--núcleo-nominal-e-nome-social) | [FS](Calibrador_FS_Specification.md), [blocking](Decisao_Arquitetural_Blocking_Complementar_IBGE_20260927.md). **Projeções físicas existem; hipótese estatística do score não implementada/validada.** |
| Calibrador, IBGE e promoção | [DC-LK-01/03; DC-OP-01](Decisoes_Canonicas_Identidade_Linkage_20260929.md) | IBGE bootstrap inicial uma vez, com proveniência; `m/u` calibrados no universo condicionado. [DT-15](DT15_Governanca_Decisao_Modelo.md) e [runbook](Runbook_Operacao.md) distinguem fluxo pretendido e gate hoje executável. |
| Produto e requisitos candidatos | [Diretrizes](Diretrizes_Identidade_Progressiva_Apoio_Decisao.md), [Especificação v5.00 candidata](../../Documentos/Especificacao_Tecnica_Jornada_v5.00_Candidata.md) | A candidata ainda requer reconciliação editorial, técnica e publicação formal; [v3.62 publicada](../../Documentos/README.md) não é reescrita retroativamente. |
| Corpus sintético — nascimento diário | [DC-SYN-01-E1](DC-SYN-01-E1_Referencia_Diaria_Nascimento.md) | Snapshot local imutável derivado somente do SIDRA 9514; conversão de coorte determinística; 100+ → 100–105 uniforme por convenção declarada; pós-01/08/2022 extrapola a taxa diária da coorte zero até corte explícito; SINASC não integra a E1; validação real #31 permanece separada. |
| Console DEV — workers separados e recuperação | [DT-18](DT18_Servicos_Independentes_Console_DEV.md) | Decisão de isolar processo/supervisor por worker, sem cascata de reinício do NODE. **Ainda não implantada.** |
| Console DEV — ações mínimas e log existente | [DT-19](DT19_Console_Acoes_Workers.md) | **Dois botões verticais** por worker: **Executar uma vez** e **Parar processo** (ON+PID real). Estado **automático** no cartão e detalhes no **Log da sessão já existente**; sem botão Status. Backend consulta liveness real. **Ainda não implantada.** |
| Console DEV — supervisor global de modo | [DT-20](DT20_Supervisao_Opt_In_Workers.md) | **OFF inicial:** os 3 RunOnce disponíveis. **ON:** interrompe RunOnce ativos e inicia os 3 contínuos; Parar processo mata PID, **supervisor reinicia automaticamente apenas aquele processo**, e **worker recupera o próprio trabalho**. OFF volta a RunOnce. **Ainda não implantada.** |
| Console DEV — prova de resiliência | [DT-21](DT21_Testes_Resiliencia_Workers.md) | **RunOnce e testes preservados**; matriz CI OFF→ON→OFF, **dois botões verticais**, status automático + log, kill individual, restart externo e recuperação sem duplicação. **Ainda não executada.** |
| Plano, paralelismo e gates | [Plano](Plano_Desenvolvimento.md), [DP-01](DP-01_Desenvolvimento_Paralelo.md) | CI, migrations e contratos transversais permanecem sob integração única. DT-10, DT-02 e a sequência E2E-A/DT-17A → E2E-B/DT-17B → E2E-C/DT-17C foram concluídas tecnicamente em DEV até 01/10/2026; a retomada deve seguir as pendências ativas do Plano/Estado atual, sem reabrir esses marcos por sequência documental antiga. A issue #31 continua impedindo inferir validação estatística representativa ou ativação real. |

## Precedência, divergência e preservação

1. **Decisões canônicas datadas** governam alterações da candidata de identidade/linkage. Se um subdocumento estiver desatualizado, prevalece o texto canônico **somente como decisão futura**, não como declaração de código implantado.
2. Documentos especializados descrevem desenho ou implementação, indicando quando ainda falta adequação. Estado de execução deve ser comprovado no **HEAD, testes/Actions e issues**, não deduzido do título `vigente`.
3. ADRs, contratos de versões anteriores, comparações V6/V7, evidências e snapshots permanecem em **arquivo histórico lógico** no catálogo; preservar bytes, datas e referências. Não devem instruir novas PRs quando contradizem a decisão de 29/09.
4. `RELEASE_INFO.txt` e a Especificação Técnica v3.62 publicada são imutáveis como fatos da release; a candidata v5.00 ainda não é publicação institucional.

**Correções necessárias antes de integrar frentes paralelas:** rejeitar exigência de IBGE ativo para todo `GENERATE_DRAFT` após o primeiro bootstrap; remover guard exato fixo ao implementar V8; não preservar V6/V7 como alternativas operacionais novas; separar UUID inicial de canônico e implementar regra de dois novos UUIDs em divisão não ancorada; tratar ausência de nome social como **hipótese mensurável** e não sinal fixo. Confrontar especialmente PRs #602, #610, #611, #615, #617 com esta fonte e com a suíte de contratos.

## Publicação manual de regras e página de calibração

A [DT-15](DT15_Governanca_Decisao_Modelo.md) especifica comparação pareada ATIVO × RASCUNHO, revisão humana explícita e publicação atômica do bundle **lógico** (parâmetros/política, blocking, execução/validação), sem fingir que já existem três artefatos físicos ou manifesto completo implementado. Conferência de implementação não substitui a avaliação estatística da issue #31.


> **Norma vigente (09/10/2026) — FS, Splink, TF, IBGE e Calibrador:** consultar [DC-LK-TF](Decisoes_Canonicas_Identidade_Linkage_20260929.md#dc-lk-tf--norma-vigente-de-frequência-nominal-fs-e-calibrador-09102026). O peso TF zero é neutro (peso 1 aplica ajuste integral); os pesos e m/u devem ser estimados pelo Calibrador a partir do bootstrap sintético IBGE e, progressivamente, de evidência histórica real. Primeiro nome e último sobrenome significativo de pessoa e mãe devem participar do FS sem dupla contagem. V8 é referência histórica, não segunda implementação operacional. Em caso de divergência, prevalece a decisão canônica; este documento não certifica implementação concluída.
