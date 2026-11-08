> **PRECEDÊNCIA 08/11/2026 (PO) — D2 cancelado, P95 e LOCK mantidos:** este documento registra o mandato operacional **histórico** de 07/11. Não interpretar “separação P95 operacional” como autorização para tirar P95 da elegibilidade da seleção. O PO **cancelou o passo 3/D2** (sem gate de comparação automática, sem `LatencyAffectsSelection`, sem ressalva de tempo) e determinou **preservar o P95 na seleção e o LOCK científico pré-registrado**, retirando-os da fila de dívida técnica. Fonte superveniente e vinculante: [decisão vigente de 08/11](Decisao_V9_Decisoes_Vigentes_P95_LOCK_Cancelamento_D2_20261108.md). Preservar registros e provas históricos sem editar ou reinterpretar medições.

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
| A1 + A2 | [#1308](https://github.com/lucianox777/Jornada/pull/1308) + [#1338](https://github.com/lucianox777/Jornada/pull/1338) | A1 mesclada só para dois CLIs; **A NÃO concluída**. A2 cobre TODAS as bordas Program.cs/host, deve entrar após gates master pós-C e antes de D. |
| B | [#1310](https://github.com/lucianox777/Jornada/pull/1310) | Testemunha λ=0 ineligível à seleção — imediata |
| C | [#1304](https://github.com/lucianox777/Jornada/pull/1304) | Plano científico autocontido de U-mistura — imediata |
| D | [#1326](https://github.com/lucianox777/Jornada/pull/1326) | Paralelismo local determinístico, telemetria, separação P95 operacional — imediata |
| E | [#1336](https://github.com/lucianox777/Jornada/pull/1336) | Remover disparo manual **somente** do gate técnico 30k — imediata |
| F | [#1332](https://github.com/lucianox777/Jornada/pull/1332) | Neutralizar binários DOCX/PDF com QA e fonte — imediata |
| G | [#1334](https://github.com/lucianox777/Jornada/pull/1334) | Migração nova e forward-only `ENRIQUECIMENTO_DESENVOLVEDOR` sem quebrar selo SQL antigo — imediata |
| H | [#1328](https://github.com/lucianox777/Jornada/pull/1328) | Comentários em código/CI PRODAM → DESENVOLVEDOR — imediata |
| I | execução, sem PR artificial | Campanha U no **SHA da master**, após B e C mescladas e com autorização científica cabível |
| J | [#1337](https://github.com/lucianox777/Jornada/pull/1337) (**draft**) | Motor multiparâmetro, V2 como testemunha permanente, CANDIDATE/WITNESS e CP sobre candidatas — após B/C/D mescladas; rascunho anterior é permitido, sem PR substituta |
| K | [#1318](https://github.com/lucianox777/Jornada/pull/1318) | Especificação e índice únicos com **SHAs e runs reais**, merge após A–J; absorver #1335 |
| L | [#1333](https://github.com/lucianox777/Jornada/pull/1333) | Neutralização documental no mesmo arquivo de especificação, rebase após K mesclada |

**Fila sugerida, preservando prontidão:** A → B → C → D → E → H → G → F → I (execução) → J → K → L. I pode executar após B+C sem bloquear desenvolvimento paralelo das outras; **não abre merge de PR própria**. Dependências científicas e gates são mais fortes que a ordem sugerida.

## Parâmetros e ciência: decisões preservadas

1. O calibrador executa **sempre** o `plan.json` científico oficial versionado; V2, V3 e misturas são opções da mesma arquitetura, não SQL/planos paralelos inventados para obter aprovação. Comparações de campanhas anteriores permanecem analíticas, não alimentam seleção da campanha atual.
2. **V2 WITNESS + ANCHOR permanente — texto integral expressamente retificado pelo PO:** "o plan.json oficial contém sempre a opção V2 com TODOS os parâmetros históricos exatos, explícitos; ela é WITNESS e ANCHOR (não concorre na seleção, não entra na multiplicidade do CP) e roda no mesmo recorte das demais opções." Não reduzir o requisito a DEFAULT sem todos os parâmetros históricos, não rodar V2 em SQL/recorte paralelo. O erro de truncamento da conversa anterior está **RESOLVIDO**; J precisa demonstrar cada parâmetro, o mesmo frame físico e CP somente candidatas.
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

- A frase integral do PO sobre **V2 WITNESS + ANCHOR** foi fornecida; não se aguarda novo esclarecimento textual. Resta **executar** a comparação exaustiva dos parâmetros V2 históricos exatos com o plan.json oficial e comprovar a exclusão da multiplicidade CP, sem deixar faltar campo ou inventar valores.
- Autorização para campanhas novas com seed inédita e LOCK, TEST/Gold/DT-15, publicação/HML e migração institucional v3.71 continuam condicionadas às políticas anteriores; desenvolver código/testes sem ativar produção.
- QA de binários DOCX/PDF: não reescrever PDFs sem fonte, não alterar blobs históricos só para esconder menção.

## Evidência temporal separada da decisão

A execução pode avançar após esta versão do documento; a palavra *concluída* só vale com HEAD da PR, merge SHA, runs obrigatórios e SHA da master **registrados depois do fato**. A #1318 será reconciliada com a master e atualizada com os recibos de A–J antes do merge. Este arquivo não presume resultados futuros.

## Checkpoint adicional — J aberta; G recebeu prova SQL real (07/11)

- Frente **J**: PR original nova [#1337](https://github.com/lucianox777/Jornada/pull/1337), rascunho HEAD `bab38e4d6bcefdd86cbd4e87690e66f50706f4d6`, institui `CANDIDATE/WITNESS` como campo opcional, não muda planos legados e impede seleção V2 quando expressamente WITNESS. **Não é motor finalizado**: falta contrato CP só candidatas, plano oficial íntegro com V2 e ensaio físico; só merge depois de B/C/D integradas. Registro de decisão próprio na #1337, a reconciliar nesta frente K no fim.
- Frente **G**: [#1334](https://github.com/lucianox777/Jornada/pull/1334), HEAD `720dafec65007bb43c288446c2fdb9f94e325583`, workflow SQL real descartável `42427310276` concluiu **SUCCESS** (baseline 3.70, seed sintética, migração 3.71, replay e CHECKs confiáveis). O run anterior `42427131049` falhou **antes do SQL** por permissão de criação de pasta dentro do container; corrigido na mesma PR. Um sucesso em SQL efêmero não é autorização de migração institucional, e a PR permanece em rascunho. 

## Errata obrigatória — relatório de 21h05 (07/11/2026)

**Critério de relatório:** nunca declarar uma frente CONCLUÍDA quando qualquer requisito aprovado não estiver implementado e testado; listar expressamente os itens faltantes e separar decisão, implementação, teste rápido, prova física, merge e validação pós-merge. A #1308 foi **MERGED**, porém a frente A estava **INCOMPLETA** e por isso a correção A2 tem PR **#1338** única, proposta depois de master pós-C e antes de D. Protege os comandos individuais `Program.cs` e o fallback do host, além de controlar logs internos de host; o conjunto de canários unitários da A2 e a verificação de logs públicos não substituem prova E2E de todas as falhas internas. Documento temático [SEG-V9-A2](Decisao_V9_Seguranca_A2_Bordas_Program_20261107.md) na PR #1338; **não considerar validado antes dos gates**, ainda que a PR esteja aberta.

**V2 histórica:** o texto integral determina simultaneamente `WITNESS` e `ANCHOR`, todos os parâmetros exatos explícitos, execução física no mesmo recorte e exclusão da seleção e da multiplicidade CP. Em J/#1337 a implementação de marcador `Anchor` e validação do método `DEFAULT` é apenas um começo; parâmetros históricos completos, cálculo CP seletivo e prova física ainda faltam. A referência anterior a prompt truncado foi substituída por esta decisão integral.

**Gates pós-C efetivamente conferidos no SHA `d6e576c13d10804848ccfc127cd658d1acf56828`:** `jornada-ci` run **42427871183 SUCCESS**; `v9-physical-seed42-probe` run **42427871123 SUCCESS** (não é FDR certificada nem TEST/Gold). Isso libera a **análise de prontidão da A2**, não seu merge sem checks próprios. Após A2, exigir novamente CI completa e físico 30k no novo SHA antes de D. **Fila segue única.**

## Checkpoint de execução pós-errata — 07/11/2026 (não é atestado final A–L)

- **A2 #1338 MERGED** na master pelo merge SHA `31a6c43c9d55b654509356d2c26462068039293d`; HEAD integrada `1ec090ba5ffaf3a1d5af9bdec6493c45ae26c20f`, os **sete checks de PR** verdes, inclusive canários de 17 processos reais e validação independente. A master pós-A2 teve 30k físico técnico seed42 run **42430487545 SUCCESS**; `jornada-ci` run **42430487707** estava **em execução** no instante desta checagem. **Não declarar frente A CONCLUÍDA antes de confirmação de jornada-ci pós-merge e revisão de cobertura restante do host**.
- **D #1326** reconciliada na mesma PR original sobre master A2 (sem empilhamento), HEAD `7045eee43df66c608df02c3fc2c29ca253cb7ab2`. Teste de 1.000 identidades sintéticas seed42, em SQL Server descartável, mesmo corpus/mesmo plano, paralelismo local **1/0/8**, run **42430959610 SUCCESS**; SHA científico idêntico `ec25d15ee8fc498171648d2fbcdbc83cbf807a285b20e6ac697c2126b77093d7`, métricas TP/FP/FN, pendências, fitted hashes, pares SQL e proposta científica iguais no auditor. Windows executou somente unidade de digest, não SQL físico Windows. **PENDENTES:** teste físico entre SOs/ambientes, `independent-validation` completo no HEAD da PR e master CI+30k antes de merge. **D permanece RASCUNHO.**
- **J #1337** mesma PR rascunho, head rebaseada sobre master A2 `891c910ac9b121fef5cb770e394d6f6532d4686e`. Código **propõe** `WITNESS+ANCHOR` V2, λ=0 físico mas inelegível, CP com limite superior conservador **somente sobre candidatas** (orçamento físico total menos testemunhas iniciais predeclaradas). Checks novos, verificação exaustiva dos parâmetros V2 históricos **literalmente no plano oficial**, paridade física e integração após D ainda **PENDENTES**. Nada de FDR certificada, TEST/Gold ou ativação.
- **K #1318** somente documentação em PR existente, agora sincronizando a decisão, precedência e índices. A atualização não substitui evidência nem autoriza merge K antes de A–J. Próxima etapa de integração condicionada à master pós-A2 `jornada-ci` verde e prova suficiente da frente D.

## Checkpoint pós-merge D — 08/11/2026 — prevenção de vigília repetitiva

**Ocorrência:** houve repetição de consultas aos mesmos checks sem efetuar o merge que já estava autorizado pelos gates. O PO sinalizou “Parece em loop”. A correção operacional é fazer **uma leitura consistente dos SHAs e gates**, efetuar a ação disponível imediatamente e **não repetir polling de resultados inalterados dentro da mesma interação**. Se o próximo gate está efetivamente em curso, registrar pendência objetiva e encerrar a rodada de trabalho, deixando o próximo ciclo da vigília consultar novos resultados. Isso não modifica a fila nem autoriza pular verificações.

**Ação real:** a PR original **D/#1326 foi MERGED**, com HEAD conferido `daf4d561ed12f1775caccf7c265764d005d67e67`, dez checks SUCCESS, incluindo comparação de SQL físico em runners Ubuntu 22.04/24.04 e Windows .NET, após master A2 verde. Merge `33ba4a201b293aaa1053a9c6d52f1d9e40cd481b`. Nesse SHA da master, a CI completa `jornada-ci` run **42456779221** e o ensaio físico técnico 30k/seed42 run **42456779210** estavam **EM EXECUÇÃO** na primeira consulta; **não** iniciar merge E/H/G/F antes de ambas concluírem SUCCESS no SHA exato. A existência da execução não é sinônimo de prova final nem conclusão integral da frente D.

**Pendências explícitas:** D mesclada, mas aguarda CI+30k físicos pós-merge e limites de extrapolação (teste SQL em duas distribuições Ubuntu, não SQL no Windows). E/#1336 e J/#1337 permanecem abertas sem merge, J ainda exige prova física e plano V2 histórico exato. Não marcar D nem J “CONCLUÍDA” automaticamente. As demais frentes seguem em paralelo na mesma PR de origem, sem novas substituições.
