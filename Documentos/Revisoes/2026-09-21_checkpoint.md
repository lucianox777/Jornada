# Checkpoint técnico — 2026-09-21

**Natureza:** checkpoint preparatório do primeiro trem pós-RC  
**Baseline revisado:** `master@08ca8fc0effbbeba6f8cdb8ba6a671329eb01b0a`  
**Branch de documentação:** `docs/post-rc-governance-404`  
**Decisão:** `ATUALIZACAO_DOCUMENTAL`

Este checkpoint não altera o SHA candidato da RC e não autoriza merge da branch antes do corte de `v5.00-rc.1`.

## 1. Invariantes e arquitetura

A revisão identificou como propriedades já implementadas e merecedoras de fonte canônica:

- fato válido não bloqueado por identidade pendente;
- CPF como âncora permanente;
- `initial_uuid` como proveniência, não evidência;
- NIS/RG secundários, sem poder determinístico;
- blocking separado da decisão;
- score bruto separado da publicação operacional;
- ledgers append-only;
- deny-by-default fora de Development sem identidade corporativa.

As fontes criadas nesta branch são `Solution/docs/Invariantes_Arquitetura.md`, `Conformidade_Invariantes.md` e `Mapa_Modulos_Produto_Andaime.md`.

## 2. Diagramas

Os quatro diagramas de sequência antigos foram reexpressos na visão corrente e acrescentados os fluxos de ledger e fila governada em `Solution/docs/Sequencias_Identidade_Linkage.md`.

O manifesto `Solution/docs/Diagramas_Rastreabilidade.json` liga cada visão às âncoras técnicas. O gate de frescor permanece deliberadamente manual/informativo nesta etapa; nenhum workflow foi modificado.

A lacuna da fila após desfecho foi mantida explícita e vinculada à #401; a documentação não afirma que o fingerprint causal já exista.

## 3. Higiene documental conferida

### Sigla institucional

A forma canônica é **SGM/SEPE**. `InstitutionalAcronymRegressionTests.cs` trata `SGM/SPE` como forma legada e reprova sua presença nos artefatos institucionais vigentes. Portanto não há correção nova a aplicar neste checkpoint.

### Versões de anexos

`Documentos/README.md` já distingue:
- modelo físico v1.40 como visão corrente da candidata 3.70;
- DER v1.39 como histórico;
- pendências v1.47 como snapshot histórico, não backlog corrente;
- requisitos v1.1 como baseline consolidado e v1.0 como histórico.

A lacuna “Base Normativa v3.64 declarada × Especificação materializada v3.62” permanece explicitamente documentada e não deve ser resolvida fabricando arquivo v3.64.

### Matriz de rastreabilidade

`Documentos/Requisitos/05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.1.md` já é a matriz institucional consolidada. O novo `Solution/docs/Conformidade_Invariantes.md` é complementar: liga invariantes de arquitetura a provas técnicas e não substitui RN/RF/RNF/RT.

### LEIA-ME

`LEIA-ME.txt` já identifica o estado candidato, a ordem de leitura, a separação fato × identidade e os gates externos. Não há necessidade de um segundo arquivo de entrada concorrente.

### IBGE

`Solution/data/reference/ibge-nomes-2022/raw-parquet/PROVENANCE.md` já registra:
- fonte original: IBGE — Censo Demográfico 2022 — Nomes no Brasil;
- data de referência 01/08/2022;
- origem técnica da compilação;
- atribuição dos dados originais ao IBGE;
- licença da estruturação da compilação;
- `source-files.sha256` para fixar os bytes capturados.

Não foi encontrada lacuna que justifique duplicar a atribuição nesta branch.

## 4. C# × T-SQL

Foi criada a regra de alocação e o inventário dos guards críticos em `Solution/docs/Decisoes_Contingentes_Alocacao_CSharp_TSQL.md`.

A revisão confirmou a linha:
- C# calcula/orquestra regras ricas, parsing, comparadores, scorer e integrações;
- T-SQL protege invariantes transacionais, unicidade, append-only, idempotência e guards de promoção;
- defesa em profundidade não deve resultar em duas implementações semânticas concorrentes.

## 5. Issues externas preservadas

Continuam abertas sem aprovação implícita:
- #31 — validação estatística com dado real;
- #93 — volumetria HML/decisão institucional;
- #378 — autenticação PRODAM e separação ambiental;
- #379 — decisões de política;
- #401 — não renascimento da divergência sem mudança causal;
- #408 — zona cinzenta/`POSSIVEL`.

## 6. Resultado

Não foi identificado defeito funcional que justifique mover `master` antes da RC. O próximo passo de release continua sendo o preflight manual `rc_evidence_preflight=true` no SHA `08ca8fc0...`, seguido do corte da tag se o preflight permanecer verde.
