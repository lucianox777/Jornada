# GOV-OPS-V9-02 — mandato operacional A–L e fila de integração (07/11/2026)

**Fonte decisória:** Product Owner, mandato único nesta conversa em 07/11/2026 às 20h19 (America/Sao_Paulo). **Estado:** aprovado como política operacional; cada frente exige implementação, CI e evidência próprias. **Precedência:** supera orientações anteriores que exigiam 30k pré-merge ou permitiam PR substituta. Complementa [GOV-DOC-01](Politica_Registro_Decisoes_Discussoes.md) e [GOV-OPS-V9-01](Decisao_Vigilia_Horaria_V9_20261107.md). Esta página é documentação de decisão, não atestado de que as frentes terminaram.

## Contrato vinculante: desenvolvimento e merges

- **Vigília:** uma única rotina horária nesta conversa; ler ao vivo SHA, PRs e runs; retomar frentes, postar checkpoints curtos quando houver mudança, não inventar estado nem substituir registros oficiais. Desabilitar somente após A–L concluídas e master verde, ou por ordem do PO.
- **Desenvolvimento paralelo máximo** dentre dependências prontas. **Merge em fila única**, apenas uma PR por vez, HEAD exato e checks rápidos verdes. Após merge, aguardar `jornada-ci` e regressão técnica 30k física quando cumulativamente exigida, ambos no SHA do `push` da `master`, antes de mesclar a próxima; não apagar evidência ou fazer merge durante bloqueio.
- **Uma PR por frente**, sem abrir substituta; corrigir na mesma PR, um push por vez. Conflito deve ser resolvido por quem mescla depois, rebaseando a PR original na `master` atual e comprovando novo HEAD. Decisões GOV-DOC-01 devem acompanhar código/execução e sincronizar índices.
- **Não interromper desenvolvimento por “AGUARDA PO”**: registrar a questão, preservar o valor/comportamento atual, continuar outras frentes. Parar somente fila de merge se master vermelha após merge (reverter e investigar), falha vermelha que exige nova decisão científica/critério PO, ou conflito que muda comportamento do calibrador.
- Falha realmente transitória de infraestrutura: até duas reexecuções do **mesmo run**, sem apagar o fracasso. Não classificar compile/error lógico como infraestrutura. **Não fechar/reabrir** PR cuja decisão dependa do PO. Manter #1242 draft, sem merge.

## Frentes e dependências vinculantes

| Frente | PR | Objetivo e condição |
|---|---|---|
| A | [#1308](https://github.com/lucianox777/Jornada/pull/1308) | Segurança de exceções Program.cs/CLI — imediata |
| B | [#1310](https://github.com/lucianox777/Jornada/pull/1310) | Testemunha λ=0 ineligível à seleção — imediata |
| C | [#1304](https://github.com/lucianox777/Jornada/pull/1304) | Plano científico autocontido de U-mistura — imediata |
| D | [#1326](https://github.com/lucianox777/Jornada/pull/1326) | Paralelismo local determinístico, telemetria, separação P95 operacional — imediata |
| E | [#1336](https://github.com/lucianox777/Jornada/pull/1336) | Remover disparo manual **somente** do gate técnico 30k — imediata |
| F | [#1332](https://github.com/lucianox777/Jornada/pull/1332) | Neutralizar binários DOCX/PDF com QA e fonte — imediata |
| G | [#1334](https://github.com/lucianox777/Jornada/pull/1334) | Migração nova e forward-only `ENRIQUECIMENTO_DESENVOLVEDOR` sem quebrar selo SQL antigo — imediata |
| H | [#1328](https://github.com/lucianox777/Jornada/pull/1328) | Comentários em código/CI PRODAM → DESENVOLVEDOR — imediata |
| I | execução, sem PR artificial | Campanha U no **SHA da master**, após B e C mescladas e com autorização científica cabível |
| J | PR nova, somente quando pronta | Motor multiparâmetro, V2 como testemunha permanente, CANDIDATE/WITNESS e CP sobre candidatas — após B/C/D mescladas; rascunho anterior é permitido, sem PR substituta |
| K | [#1318](https://github.com/lucianox777/Jornada/pull/1318) | Especificação e índice únicos com **SHAs e runs reais**, merge após A–J; absorver #1335 |
| L | [#1333](https://github.com/lucianox777/Jornada/pull/1333) | Neutralização documental no mesmo arquivo de especificação, rebase após K mesclada |

**Fila sugerida, preservando prontidão:** A → B → C → D → E → H → G → F → I (execução) → J → K → L. I pode executar após B+C sem bloquear desenvolvimento paralelo das outras; **não abre merge de PR própria**. Dependências científicas e gates são mais fortes que a ordem sugerida.

## Parâmetros e ciência: decisões preservadas

1. O calibrador executa **sempre** o `plan.json` científico oficial versionado; V2, V3 e misturas são opções da mesma arquitetura, não SQL/planos paralelos inventados para obter aprovação. Comparações de campanhas anteriores permanecem analíticas, não alimentam seleção da campanha atual.
2. **V2 testemunha permanente:** a opção histórica V2 deve existir no plano oficial e ser auditada com seus parâmetros históricos íntegros; não inferir valores de campos cuja instrução original tenha ficado truncada no registro de chat. Não classificar a testemunha como nova candidata independente para multiplicidade/CP.
3. O limite de FDR é versionado e configurável no `plan.json`; CP é limite condicional sobre candidatas elegíveis, **não** certificação geral IID/FDR. Nunca adulterar TEST, Gold, hashes, 30k seed42 ou snapshots de prova.
4. Paralelismo local 1/0/N e tempos são propriedades operacionais: as métricas, os fits e a escolha científica devem independer de tempo/host; P95 pode vedar elegibilidade **operacional**, sem contaminar a proposta científica. Dados reais não são inseridos em Git; campanhas novas, holdout externo, TEST, Gold, HML, DT-15 e ativação permanecem sob seus controles separados.

## Triagem de PRs fechadas — não reabrir automaticamente

Consulta dos eventos/comentários GitHub de 07/11/2026, antes de qualquer nova frente. As quatro foram fechadas **sem merge** e possuíam comentários de triagem técnica pela conta `lucianox777`; isso não equivale a autorização para reabrir ou mesclar.

- [#1279](https://github.com/lucianox777/Jornada/pull/1279): lote Dependabot de sete upgrades Actions major/minor, PR defasada/conflitante e falhas de gates inclusive segurança; manter Actions pinadas por SHA. Segurança futura somente em PRs menores, com checks/contratos.
- [#1206](https://github.com/lucianox777/Jornada/pull/1206): upgrades NuGet mistos para 10.x com falhas dependency-lock, unit/e2e/security/Windows/cluster; só revisar mudanças com TFM, versões e lockfiles alinhados em PRs menores, sem mascarar warnings.
- [#1256](https://github.com/lucianox777/Jornada/pull/1256): armazenamento SQL durável do corpus sintético congelado foi arquivado fora do caminho crítico, sem decisão de retenção, permissões, backup/restauração e round-trip real; preservado corpus técnico descartável/imutável.
- [#1259](https://github.com/lucianox777/Jornada/pull/1259): sensibilidade TF com piso censurado arquivada; hipótese não é dado IBGE observado nem substitui TF0/TF oficial; eventual teste precisa massa difícil nova em LOCK e plano pareado pré-registrado.

[#1335](https://github.com/lucianox777/Jornada/pull/1335) era PR avulsa de vigília; seu conteúdo útil foi incorporado na #1318 em commit `c5524ca86b9bcbe8197a80b483875fa885e40cba`; fechada **sem merge**. Não reverter fechamentos, ocultar motivos ou criar PR substituta.

## Pendências sem paralisar o desenvolvimento (AGUARDA PO)

- Conferir o detalhamento integral dos campos históricos de **V2 testemunha permanente** na decisão original antes de declarar J concluída; o trecho fornecido neste mandato termina em “com TODOS o...”. Enquanto isso, manter o V2 histórico e seu fingerprint, sem inventar parâmetros ou eliminar opção.
- Autorização para campanhas novas com seed inédita e LOCK, TEST/Gold/DT-15, publicação/HML e migração institucional v3.71 continuam condicionadas às políticas anteriores; desenvolver código/testes sem ativar produção.
- QA de binários DOCX/PDF: não reescrever PDFs sem fonte, não alterar blobs históricos só para esconder menção.

## Evidência temporal separada da decisão

A execução pode avançar após esta versão do documento; a palavra *concluída* só vale com HEAD da PR, merge SHA, runs obrigatórios e SHA da master **registrados depois do fato**. A #1318 será reconciliada com a master e atualizada com os recibos de A–J antes do merge. Este arquivo não presume resultados futuros.
