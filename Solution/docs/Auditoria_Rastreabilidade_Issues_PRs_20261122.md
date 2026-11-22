# Auditoria retrospectiva de rastreabilidade — issues e PRs (22/11/2026)

**Estado:** inventário inicial verificável; não é atestado de exaustividade nem autorização de fechamento em lote. **Governança:** GOV-DOC-01. **Repositório:** lucianox777/Jornada.

## Motivação, método e limites

Após a decisão do PO de consolidar ou encerrar issues antigas, verificou-se que alterar o escopo da #408 diretamente ocultava a distinção entre requisito histórico, PR integrada e decisão superveniente. Reconstrução baseada em leitura das issues atuais, busca de PRs por número de issue, corpo das PRs e conferência de estado de PRs selecionadas. **Busca textual não prova relação causal:** menções incidentais, comentários de testes ou nomes coincidentes são classificados como *indícios*, não como implementação. A API disponível nesta sessão não expôs o histórico de eventos de edição de cada issue; portanto, **não se afirma reconstrução integral das versões antigas da descrição**. O corpo original da #408 foi preservado sob aviso de substituição na própria issue. Não foram fechadas PRs ou issues.

## Matriz de vínculos verificados

| Issue | Escopo original / decisão posterior | PRs com vínculo direto identificado | Estado conferido | Encaminhamento e ressalva |
|---|---|---|---|---|
| #392 | Motivos de ausência de CPF; PO aprovou encerramento | #427 (contrato Pessoa v5, compartilhada com #393/#402) | #427 integrada | Verificar gate contratual/3.71 antes de fechar; PR compartilhada não é exclusiva |
| #393 | Natureza tipada territorial; PO aprovou encerramento | #427 | integrada | Mesmo gate 3.71 |
| #402 | CNH e RG parcial; PO aprovou encerramento | #425, #427 | ambas integradas | Mesmo gate 3.71 |
| #401 | Fingerprint causal da divergência; PO aprovou encerramento | #722, #724 | ambas integradas | Verificar execução do runtime e testes |
| #411 | Coordenar trem estrutural 3.71; PO quer fechar e deixar #414 aberta | #425, #427, #722, #723, #724; #490 cita #411 em implementação de métricas | cinco PRs estruturais integradas; #490 integrada | Coordenadora transversal: não fechar sem validar rebind, instalador, manifest e dependência #414. #723 contém fatia histórica #408 |
| #414 | Beneficiário x responsável; PO determina manter aberta | #722 (estrutura), #995/#996/#997/#1351 (menções relacionadas, requerem inspeção funcional individual) | #722/#995/#996/#997/#1351 integradas | Implementação parcial não comprova aceitação funcional |
| #408 | Original: POSSIVEL + apresentação/atos humanos; decisão superveniente: **não permitir confirmação/rejeição/autorização humana de vínculo; corrigir na origem e reavaliar nova carga** | #723 (declara implementar fatia estrutural #408); #410 documenta POSSIVEL como planejado | #723 e #410 integradas | **Issue foi reescrita em 22/11 e mantida aberta como auditoria técnica; requer correção da rastreabilidade:** restaurar escopo histórico como registro e separar auditoria de remoção em issue própria antes de fechar. A implementação #723 permanece no histórico, não se desfaz por fechar PR |
| #424 | Medir rescoring/storage; PO aprovou absorção na otimização | #488, #489, #490 | três integradas | Transferir critérios de medição ao destino explícito (#418 é candidata, verificar aceite), referenciar PRs e só então fechar |
| #409 | Métricas reais de CPF, qualidade e regularização; PO aprovou absorção no Monitor | Nenhuma PR de implementação inequivocamente atribuível na busca textual; #1357 trata documentação de rótulos reais | #1357 integrada | Identificar issue Monitor de destino e transferir denominadores/critério de dados reais; não declarar execução de medições |
| #404 | Invariantes, diagramas, documentação; PO aprovou absorção na governança documental | #410, #527 | ambas integradas | Conferir entregáveis residuais e destino antes de fechar |
| #405 | Hardening segredos; PO pediu auditoria residual | #412, #422, #457, #458, #459, #462–#487 (série de segurança; verificar cada relação no inventário específico) | #422, #458, #465, #486 integradas; demais não conferidas individualmente nesta rodada | Documentação registra triagem de histórico, literais e credenciais remanescentes; **não** fechar em lote |
| #416 | Calibrador sintético DEV; PO aprovou fechamento condicionado | #417, #426, #428–#433, #436–#440 e correções #441–#447 | #426, #429, #432, #433, #437, #438, #440 integradas; demais não conferidas nesta rodada | Provar critérios completos; não confundir ensaio DEV com TRAIN científico |
| #434 | Regras brasileiras de nomes; PO aprovou fechamento condicionado | #435, #436 | ambas integradas | Confirmar cobertura e testes |
| #413 | Classificar sentinelas sem destruir original; PO manteve aberta | Nenhuma PR direta comprovada nesta rodada | — | Sem mudança de escopo ou encerramento |
| #31, #79, #449 | Pendências preservadas pelo PO | Fora da amostra de PRs desta rodada | — | Continuam abertas |

## PRs e riscos transversais

- **#723** foi integrada em 03/10/2026 e declara explicitamente que só persiste a apresentação POSSIVEL, sem ativar POSSIVEL operacionalmente. **Não** confundir registro de apresentação/ato com autorização operacional comprovada.
- O runtime atual contém `Solution/src/Jornada.Api/IdentityCorrectionService.cs` chamando `identidade.sp_aplicar_correcao_identidade`, e `Solution/docs/Identity_Decision_Ledger.md` descreve confirmação institucional. É preciso auditar se são caminhos operacionais incompatíveis com a decisão superveniente do PO; não afirmar sem teste que estejam ativos em produção.
- **#410** integrada em 21/09/2026 diz que POSSIVEL era estado planejado na época. **#723** integrada depois criou estruturas, não comprovando ativação.
- **#488/#489/#490** demonstram implementação parcial da #424. Absorção administrativa não apaga essas entregas.
- Busca por "#401", "#404", "#405" ou "#409" também retorna PRs com referências incidentais; **não** classificar todas como dependentes nem encerrar por correspondência textual.
- **PR #1844** é documental e está aberta. Não confundir aprovação do PO com merge, nem PR aberta com requisito abandonado.

## Regras de reconciliação antes de qualquer fechamento

1. Recuperar timeline de edições de título/corpo da issue via API de eventos/auditoria do GitHub ou export verificável; registrar datas, autor, diff e texto anterior. **Não recuperado nesta rodada**.
2. Para cada PR aberta encontrada, inspecionar corpo, arquivos alterados, commits, checks e vínculos múltiplos. Distinguir: integrada, fechada sem merge, aberta necessária, aberta parcialmente útil e aberta obsoleta.
3. PR integrada é evidência histórica imutável; **não tentar fechar/desfazer PR integrada**. PR aberta só deve ser fechada após provar que todo seu escopo é obsoleto e não atende outra issue.
4. Para issue substituída, manter registro histórico e abrir issue sucessora para a nova obrigação, com links bidirecionais, antes de encerrar a antiga. Prioridade: #408.
5. Para issue absorvida, identificar issue destino, copiar critérios e referências de PRs, registrar precedência e só depois encerrar a origem. Prioridades: #424, #409, #404.
6. Para issues estruturais #392/#393/#401/#402/#411, validar instalação/rebind 3.71, testes e dependência #414 antes do fechamento; o aceite do PO é necessário, mas não substitui evidência.
7. Integrar documentação e índice GOV-DOC-01 antes de considerar encerramento concluído. Preservar links, datas e justificativas.

## Decisões e precedência

**PO, 22/11/2026:** #424 → otimização; #409 → Monitor; #404 → governança documental; #413 e #414 continuam abertas; #392/#393/#401/#402/#411 podem encerrar após verificação; #408 proíbe decisão humana direta e exige reavaliação por nova carga; #405 exige inspeção residual. A decisão de #408 **substitui** as regras anteriores de confirmação humana como requisito funcional, mas **não apaga** a PR #723 ou os registros auditáveis anteriores.

**Estado de execução:** inventário inicial registrado nesta branch; ainda faltam timeline de edição, mapeamento exaustivo das PRs abertas, identificação dos destinos e integração do registro. Não há autorização para fechamento indiscriminado de PRs.

## Triagem das 41 PRs abertas — 22/11/2026

**Método:** busca das 41 PRs abertas e leitura individual de metadados e descrições de todas as 41. Classificação **provisória**: não foram auditados diffs completos, checks, reviews ou sobreposição byte a byte. `mergeable=true` não implica checks aprovados; `mergeable=false` não prova obsolescência. Nenhuma PR foi fechada ou integrada nesta triagem.

### Grupo A — candidatas à revisão de integração (11)
- **#1844, #1843, #1842:** documentação GOV-DOC-01 recente; reconciliar índice compartilhado, gates e decisões antes de integrar.
- **#1804:** decisão de retrocompatibilidade; verificar precedência contra #1620 e demais PRs de recuperação.
- **#1708:** correção de testes DT-05; verificar necessidade atual e checks.
- **#1641:** seed TEST inédita; conferir isolamento, evidência e gates.
- **#1564:** catálogo IBGE/SINASC; ainda faltam extrações, não confundir com calibração pronta.
- **#1553:** decisão documental sobre timeout; conferir se já está representada em master.
- **#1429:** correção do controle TF0; verificar atualidade e testes.
- **#1420:** monitor manual de Actions; conferir custo operacional e necessidade.
- **#1815:** reconciliação de dívida TF0/ONE_MF; requer resolver conflitos e redundância com #1810/#1807.

### Grupo B — bloqueadas por reconciliação/possível substituição (17)
**#1813, #1811, #1810, #1809, #1807, #1806, #1805, #1776, #1772, #1720, #1710, #1633, #1620, #1613, #1604, #1468, #1459.**

Todas têm `mergeable=false` no metadado consultado; muitas registram decisões GOV-DOC-01 em cadeia, com recuperações e versões sucessoras explícitas. **Não fechar automaticamente**: comparar conteúdo original e sucessor, preservar decisão ainda não integrada e rebasear/absorver quando necessário. Exemplos: #1710 → #1807 → #1810 → #1815; #1633 → #1805 → #1809 → #1813; #1620 → #1804; #1806 → #1811. O estado de conflitos pode mudar.

### Grupo C — rascunhos ou entregas técnicas ainda não prontas (13)
**#1841, #1836, #1770, #1768, #1767, #1758, #1751, #1576, #1567, #1441, #1392, #1356, #1352.**

Todas foram identificadas como `draft=true`; algumas também têm conflitos. Necessitam validação de escopo, pré-condições, implementação e autorização antes de integrar ou encerrar. **#1567** informa autorização condicional a gates, mas continua draft; **#1441** declara integração incompleta; **#1352** é apenas fase exploratória.

### Reconciliação e regra de fechamento

- **Total consultado:** 41 = 11 + 17 + 13. **PRs efetivamente encerradas:** 0. **PRs efetivamente integradas:** 0.
- **Nenhuma das 41 pode ser marcada comprovadamente obsoleta só com título, descrição ou `mergeable`.** Grupo B é fila de investigação de duplicidade, não autorização de fechamento.
- Verificar diffs e arquivos alterados de pares sucessores, estado dos checks e commits integrados na master. Documentar equivalência de conteúdo e preservação da GOV-DOC-01 antes de fechar PR substituída.
- Ordem sugerida: (1) cadeia #1710/#1807/#1810/#1815; (2) #1633/#1805/#1809/#1813; (3) #1806/#1811; (4) #1620/#1804; (5) #1842/#1843/#1844, reconciliando índice; (6) PRs técnicas.

## Auditoria comparativa das quatro cadeias de recuperação documental

**Escopo desta rodada:** inspecionados diffs e metadados das 12 PRs #1710, #1807, #1810, #1815, #1633, #1805, #1809, #1813, #1806, #1811, #1620, #1804. Todas continuam **abertas**, `mergeable=false` no retorno consultado. Todas são documentais; isso **não** significa que sejam redundantes em relação à master.

| Cadeia | Comparação material | Resultado seguro |
|---|---|---|
| #1710 → #1807 → #1810 → #1815 | Quatro diffs adicionam o mesmo registro de decisão TF0 × ONE_MF e mexem em três arquivos; as versões posteriores mudam texto de estado/índice e contexto de integração. | **Forte candidata a consolidação**, mas não há equivalência literal de todos os hunks; conferir versão canônica contra master e preservar backlog antes de fechar qualquer antecessora. |
| #1633 → #1805 → #1809 → #1813 | Quatro diffs registram decisão de referência estatística comum; a primeira versão contém 19 linhas adicionadas e uma removida, as posteriores 24 linhas adicionadas, com reconciliações de índice distintas. | **Forte candidata a consolidação**, sem equivalência integral comprovada; conferir decisão e diferenças semânticas contra master. |
| #1806 → #1811 | Ambas adicionam duas linhas de índice com a mesma abertura de decisão GOV-V9-CALIBRADOR-MERGES-AUTORIZADOS-20261121; contextos de base/índice diferem. | **Candidata prioritária a absorção**, mas ainda sem prova de que o registro foi integrado em master ou de que o diff completo é equivalente. |
| #1620 → #1804 | Ambas adicionam documento de compatibilidade retroativa do bundle e entrada de índice; textos de estado/contexto diferem. | **Candidata a consolidação**; preservar precedência funcional e comprovar registro canônico antes de encerrar. |

**Proibição de encerramento prematuro:** uma PR substituída por outra **ainda aberta e conflitante** não tem substituição integrada comprovada. Encerrar todas as antecessoras antes de consolidar e integrar a sucessora colocaria em risco GOV-DOC-01. Não há prova de PR obsoleta pronta para fechamento automático nesta rodada.

**Próximo gate verificável:** consultar arquivos canônicos em `master`, comparar por seção e significado (não apenas linhas), escolher um único diff por decisão, resolver conflitos do índice, passar CI e integrar; só então registrar comentário de substituição e encerrar PRs redundantes. Qualquer fechamento deve preservar links cruzados e justificativa.

## Verificação direta da master — quatro cadeias (22/11/2026)

**Evidência nova e superveniente à triagem:** consulta direta aos arquivos `ref=master` mostrou que **as quatro decisões já possuem registro na master e no índice vigente**. Portanto, a expressão anterior “integração documental ainda pendente” descreve a situação das **PRs antigas abertas**, não a ausência dessas decisões na master. O inventário anterior deve ser lido à luz desta verificação.

| Cadeia de PRs abertas | Prova encontrada na master | Interpretação |
|---|---|---|
| #1710 → #1807 → #1810 → #1815 | `Solution/docs/Decisao_GOV_V9_TF0_ONE_MF_Divida_Tecnica_20261118.md`, entrada em `Dividas_Tecnicas.md`, entrada GOV-V9-TF0-ONE-MF-DT-20261118 no índice | Decisão documental e dívida registradas; **quitação da dívida não comprovada**. |
| #1633 → #1805 → #1809 → #1813 | `Solution/docs/Decisao_GOV_V9_TEST_DATABASE_Referencia_Comum_20261117.md` e entrada GOV-V9-TEST-DATABASE-REFERENCIA-COMUM no índice | Decisão registrada; **implementação do adaptador ainda pendente**, segundo o documento. |
| #1806 → #1811 | Entrada GOV-V9-CALIBRADOR-MERGES-AUTORIZADOS-20261121 no `Indice_Decisoes_Vigentes.md` | Autorização condicional documentada; não autoriza executar TRAIN/TEST nem dispensar gates. |
| #1620 → #1804 | `Solution/docs/Decisao_GOV_V9_Compatibilidade_Retroativa_Bundle_Calibrador_Producao_20261117.md` e entrada GOV-V9-BUNDLE-COMPAT-RETROATIVA-20261117 no índice | Decisão registrada; implementação e validação da retrocompatibilidade **não atestadas**. |

**Conclusão atualizada:** as **12 PRs das quatro cadeias são candidatas fortes a encerramento administrativo por conteúdo documental já registrado**, desde que a comparação final de hunks confirme inexistência de material adicional não preservado na master. **Não são 12 encerramentos aprovados automaticamente**; verificar especialmente diferenças de redação, referências e índice. Se o conteúdo de alguma PR contiver acréscimo válido ausente da master, incorporar primeiro por PR consolidada, com checks. As 12 continuam abertas; nenhuma foi fechada nesta sessão.

**Rastreabilidade:** os arquivos e identificadores acima são fontes canônicas já existentes em master. Este registro de auditoria está somente na PR #1844 até integração.

## Comparação linha a linha dos diffs adicionados contra arquivos canônicos da master

**Método:** extraídos todos os trechos `+` (exceto cabeçalhos `+++`) dos diffs das 12 PRs e comparadas suas linhas não vazias por ocorrência literal no conjunto dos cinco arquivos canônicos de `master`: três documentos decisórios, `Dividas_Tecnicas.md` e `Indice_Decisoes_Vigentes.md`. Comparação de linhas **não é prova de equivalência do patch**: não verifica deleções, contexto, ordem, duplicatas, metadados, checks ou efeitos de merge.

| PR | Linhas adicionadas não vazias | Linhas sem correspondência literal na master | Observação |
|---|---:|---:|---|
| #1710 | 26 | 4 | Estado antigo “registro em revisão”, título de dívida e redações anteriores do backlog/índice; requer inspeção semântica. |
| #1807 | 25 | **0** | Todas as linhas adicionadas aparecem na master. |
| #1810 | 25 | **0** | Idem. |
| #1815 | 25 | **0** | Idem. |
| #1633 | 13 | 1 | Entrada antiga de índice, incluindo referência conjunta a três coortes; verificar preservação semântica no índice vigente. |
| #1805 | 15 | 1 | Entrada de índice de versão anterior; documento decisório presente. |
| #1809 | 15 | 1 | Idem. |
| #1813 | 15 | 1 | Idem. |
| #1806 | 1 | **0** | Linha substantiva de autorização já na master. |
| #1811 | 1 | **0** | Idem. |
| #1620 | 16 | 2 | Estado antigo “registro em PR” e entrada de índice antiga. |
| #1804 | 16 | **0** | Todas as linhas adicionadas aparecem na master. |

**Síntese:** 6 PRs com **0 linhas adicionadas não vazias ausentes** (#1807, #1810, #1815, #1806, #1811, #1804); outras 6 com diferenças predominantemente editoriais/de índice (#1710, #1633, #1805, #1809, #1813, #1620). **Correção do total:** são seis, não oito, com coincidência literal total. A decisão da master é a fonte canônica. Antes de encerrar, validar se o texto adicional da #1633 sobre coortes está preservado no documento de isolamento, se nenhum patch remove conteúdo e se as PRs não contêm compromissos pendentes. **Não fechar sem esta última validação.**
