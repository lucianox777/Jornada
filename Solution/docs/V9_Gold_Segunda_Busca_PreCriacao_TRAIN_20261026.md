# V9 Gold — segunda busca antes de criar identidade (TRAIN, proposta executável)

**Data da decisão técnica: 26/10/2026. Estado: IMPLEMENTAÇÃO EXPERIMENTAL, NÃO VALIDADA FISICAMENTE NESTA PR.** O calibrador V9 C# continua único. Não existe novo fit, mudança de TF, M/U, prior, upper, limiar inferior, procedure canônica DT-10, modelo ativo, TEST ou acesso ao Gold operacional.

## Evidência que motivou a experiência

Na issue [#1191](https://github.com/lucianox777/Jornada/issues/1191), run externo [#40250618984](https://github.com/lucianox777/Jornada/actions/runs/40250618984), 30.804 observações elegíveis e 16.616 pessoas da seed nova 20261025001, o KNEE 0,05 reduziu pendências em **10.959/11.059/11.033** nas três ordens, mas gerou **26/19/27** falsas criações a mais que CURRENT. Entre as **31/22/30** falsas criações KNEE, **20/11/14** tinham pessoa verdadeira já representada e ausente dos candidatos físicos de blocking. Isso motiva a segunda consulta, **não comprova** que os casos desaparecerão.

Não reabrir a seed externa para escolher passes ou políticas: ela já foi inspecionada. A massa TEST histórica continua fechada. A união exploratória de 19 células da camada de lote é uma hipótese de recuperação, não automaticamente um novo blocking produtivo. Em estudos prévios, podia aumentar fortemente os U retornados.

## Desenho implementado: opção opt-in no harness isolado

Somente a entrada `--v9-gold-harness` em banco físico **JornadaHarness** de instância `(localdb)\JornadaV9Gold...` exclusiva e vazia aceita o argumento adicional `SECOND_SEARCH=EXPANDED_19`. Sua guarda exige **LOWER_NONMATCH=0.05**, `TRAIN_SELECTION`, sem captura para o calibrador e sem `INDEPENDENT_HOLDOUT`. A rota produtiva `ProbabilisticLinkageBatchRunner`, o SQL da DT-10 e o scorer FS permanecem intactos. O modo padrão sem argumento mantém o comportamento anterior.

Fluxo por observação, **antes** da publicação física da onda:

1. Executar normalmente a união completa de passes **do fit V9 fixado**, Rank/Resolve e aplicação hipotética do joelho 0,05.
2. Somente quando o resultado efetivamente **proporia criar identidade** (`SEM_CANDIDATO_*` ou `LIMIAR_INFERIOR_GOLD_TRAIN_EXPERIMENTAL`), consultar uma segunda união parametrizada dos **19 passes exploratórios + passes do fit**, usando **as mesmas chaves projetadas fisicamente** e índices `identidade.blocking_chave`. Não há TOP, fallback de projeção, CPF, labels ou refit; preservar candidatos originais é condição obrigatória.
3. Se não houver **nenhum candidato adicional**, conservar a decisão original, inclusive criação proposta. Se recuperar referências adicionais, **reexecutar o mesmo Rank/Resolve congelado** sobre a união completa.
4. Se o Resolve ampliado aceitar pelo upper e margem originais, vincular; se gerar conflito, manter conflito; se continuar não resolvido, **abster** (`BUSCA_AMPLIADA_CANDIDATOS_SEM_VINCULO_SEGURO`) para não produzir duplicata sem evidência suficiente. Não usar simples presença de candidato como match.
5. Falhar fechado em divergência de catálogo/fingerprint/projeção, perda de candidato primário, duplicata de UUID na união ou mais de **5.000 candidatos** na segunda consulta; nunca truncar para forçar publicação.

A regra de abstenção é **deliberadamente conservadora e ainda experimental**. Uma nova referência recuperada pode ser homônima; por isso a segunda busca pode aumentar pendências mesmo sem reduzir falsos vínculos. Não inferir sucesso a partir de presença de candidatos. O ganho precisa ser medido após publicação nas quatro ondas e reavaliação final.

### Catálogo e fronteiras de integridade

O arquivo `src/Jornada.Linkage.Runner/V9GoldPreCreationSecondSearch.cs` declara os 19 `PassId/Fields` do catálogo `data/calibration/v9/mass-support-20261020/plan.json`, sem elegibilidade por verdade/gabarito. O plano só usa passes com todos os campos disponíveis, preserva a ordem e as cláusulas do fit, valida colisão de ID e aplica o mesmo filtro de schema/fingerprint, normalização, semântica temporal e limite de parâmetros do query builder atual. A projeção materializada é verificada antes das ondas. Todas as estatísticas `secondSearch` são agregadas e não contêm UUIDs, nomes, datas de nascimento ou source IDs.

Os 19 passes **não são um contrato de runtime operacional** aprovado. O experimento não autoriza afirmar paridade do SQLite com SQL Server sem resultado físico.

## A/B físico TRAIN pareado

`scripts/v9-gold-knee-second-search-train-localdb.ps1` recebe Runner Release, schema, envelope de seleção TRAIN, validação condicional do finalista, snapshot Gold V6, CSV TRAIN original, candidato congelado e diretório de saída novo. Confere os hashes originais `28bbb9f4...` (CSV) e `6d93690d...` (Gold fit), KNEE=0,05, e recusa certificados/TEST. Faz **seis execuções novas**:

| Ordem fixa | Controle | Intervenção |
|---|---|---|
| G0→G1→G2→G3 | KNEE 0,05 | KNEE 0,05 + segunda busca |
| G3→G2→G1→G0 | KNEE 0,05 | KNEE 0,05 + segunda busca |
| G1→G3→G0→G2 | KNEE 0,05 | KNEE 0,05 + segunda busca |

Cada corrida cria **nova instância SQL LocalDB e schema** sem limpar bases anteriores e publica as quatro ondas reais e reavaliação final. O relatório `second-search-paired-train.json` computa, por ordem, **delta de pendências, criações falsas, vínculos falsos**, número de ampliações, abstenções e duração; os relatórios Gold existentes mantêm B-cubed, pares falsos, fragmentação e motivo de pendência. Não selecionar apenas a ordem mais favorável. A publicação do relatório agregado não equivale à certificação.

**Critério de interpretação registrado ANTES da medição:** preservar a linha KNEE isolada como referência; investigar todos os aumentos de vínculos falsos e custo extremo (fanout, latência, timeout). Preferir a segunda busca somente se reduzir duplicatas no pior cenário sem crescimento inaceitável de pendências, fusões falsas ou custo. Se causar falha física ou degradação, registrar a falha e não mudar os números para obter aprovação. Uma escolha feita nesses seis TRAIN exige **nova coorte independente ainda não utilizada** para avaliação posterior. A seed 20261025001 da #1191 **não está disponível** para essa seleção.

## Gates para consolidar DT-10 e release v5.00

- Unit tests/CI Release warnings-as-errors e SQL isolado, com comparação de candidatos primários versus segunda união e medição de fanout.
- Seis corridas TRAIN completas, hashes iguais dentro de cada par, nenhum TEST/VALIDATION aberto e nenhuma alteração de fit/TF/upper.
- Revisão dos piores indicadores por ordem, custos SQL e motivos de abstenção; escolha final explícita ANTES de nova avaliação externa.
- Somente então PR **distinta** para migration canônica DT-10 com rollback, decisão formal de política `SEM_CANDIDATO`, observabilidade e gates. TEST final lacrado requer autorização própria; V9/DT-15 não podem ser ativados por esta PR.

**Situação no momento da implementação:** código, testes e runner de medição adicionados em branch de experimento. **Não há novas seis medições TRAIN, nem comprovação de melhora, nem autorização de merge da DT-10.**
