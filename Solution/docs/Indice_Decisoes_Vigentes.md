# Índice de decisões vigentes — Jornada

**Atualização:** 29/09/2026 · **Natureza:** porta de entrada normativa da documentação, não atestado de implementação. Em caso de conflito, decisões explícitas posteriores do mantenedor prevalecem sobre ADRs e relatórios históricos; registrar emenda no documento canônico antes de implementar. O histórico permanece consultável pelo Git, sem reproduzir regras revogadas como instruções atuais.

## Leitura por finalidade

| Tema | Fonte corrente | Regra de precedência / estado |
|---|---|---|
| Prioridade e integração | [Plano](Plano_Desenvolvimento.md), [DP-01](DP-01_Desenvolvimento_Paralelo.md), [issue #614](https://github.com/lucianox777/Jornada/issues/614) | Integradora única para CI, pacotes, migrations/manifests e contratos transversais; DT-10 por último. Documentação exclusiva pode ir diretamente à master, sem esperar CI de código; filtros Actions ainda requerem implementação. |
| Blocking e seleção de registro | [Fluxos Mermaid](Fluxos_Blocking_Selecao_Registro.md), [decisão complementar](Decisao_Arquitetural_Blocking_Complementar_IBGE_20260927.md) | Nome completo, dinâmico e combinado são capacidades complementares, união deduplicada e único FS C#. Passes e índices iniciais deduzidos também das marginais IBGE; evolução posterior por ingestão Jornada. |
| Calibrador, `m/u` e bootstrap | [Especificação FS](Calibrador_FS_Specification.md), [ADR-002 emendada](../../Documentos/ADR/ADR-002-calibrador-fs-u-condicionado.md) | IBGE carregado uma vez: inicializa `u` nominal e orienta blocking inicial. `u` operacional converge para não-matches da união efetiva. `m` depende de pares positivos confiáveis, não das marginais IBGE. Dados sintéticos não são evidência de erro cadastral real. |
| Nulidade e contratos | [Fluxos, §5](Fluxos_Blocking_Selecao_Registro.md), [DP-01](DP-01_Desenvolvimento_Paralelo.md) | Todos os atributos de observação/identidade são logicamente anuláveis; contrato versionado por Secretaria decide admissão. Ausência não é conflito nem evidência de match. |
| Calibração e validação | [Decisões FS](Decisoes_Linkage_Calibracao_IBGE_20260926.md), [Conferência](Linkage_Implementation_Conference.md) | TRAIN estima, VALIDATION seleciona e TEST audita candidato congelado. DT-14 executada em mudança de scorer/comparador, runtime .NET ou threshold significativo, não em todo draft. Validação real #31 ainda obrigatória. |
| Bootstrap geográfico | [Escopo IBGE](Linkage_Bootstrap_U_Municipio_SP_20260926.md) | V1 nacional para mãe; V2 municipal da pessoa é candidata experimental. Sem fallback geográfico oculto. |
| Ensaio | [Plano](Plano_Desenvolvimento.md), [DP-01](DP-01_Desenvolvimento_Paralelo.md) | Preservar contratos sintéticos durante desenvolvimento; carregar contratos reais no Ensaio; não executar Ensaio integral antes da prontidão técnica nem resetar JornadaLocal. |

## Documentos históricos e incompatibilidades conhecidas

- ADRs preservam **rastreabilidade**, mas uma seção antiga não se sobrepõe à decisão posterior. A [ADR-002](../../Documentos/ADR/ADR-002-calibrador-fs-u-condicionado.md) foi emendada para revogar a dependência permanente de IBGE ativo. A ADR-007 registra o isolamento histórico do Splink; a seção de conferência externa em [Decisões FS](Decisoes_Linkage_Calibracao_IBGE_20260926.md) foi posteriormente rebaixada a diagnóstico opcional e **não** é gate do Ensaio.
- Qualquer documento que ainda instrua `GENERATE_DRAFT` a exigir IBGE ativo **em todas as execuções**, `GENERATE_DRAFT → CONFERÊNCIA → VALIDATE` como rito normal, três resolvedores concorrentes, CPF/nome/mãe/nascimento globalmente NOT NULL, ou contrato sintético removido antes do Ensaio está **desatualizado**; corrigir ou marcar como histórico antes de reutilizar.
- **Pendente de inventário exaustivo:** percorrer todas as ADRs, runbooks, planos e links internos; atribuir a cada um `VIGENTE`, `VIGENTE_COM_EMENDA`, `HISTÓRICO` ou `REVOGADO`, com substituto e data. Este índice não declara essa varredura concluída. Corrigir código e testes em tarefas separadas, sem presumir conformidade pelo texto.

## Publicação manual de regras e página de calibração (29/09/2026)

A [DT-15 consolidada](DT15_Governanca_Decisao_Modelo.md#6-consolidação-de-29092026--publicação-manual-e-atômica-do-conjunto-de-regras) é a fonte vigente: página master com marco inicial IBGE e histórico de calibrações Jornada; comparação pareada ATIVO × RASCUNHO; operador decide manualmente; três blocos lógicos do bundle de modelo (parâmetros probabilísticos/decisão, regras de blocking e contrato de execução/validação) sob manifesto/hash global e publicação atômica. **Não são necessariamente três arquivos físicos: o código atual persiste parâmetros e ruleset no SQL, enquanto normalização/comparadores e parte dos guardas pertencem ao runtime; cobertura global por hash ainda pendente**; não confundir com bundle de implantação ou ZIP de ingestão. Prévia DEV read-only não equivale à promoção implementada.
