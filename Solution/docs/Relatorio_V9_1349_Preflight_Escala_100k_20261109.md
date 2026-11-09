# V9 — preflight para escala real do motor multiparâmetro em 100k sintéticos

**09/11/2026 · #1349 · Estado: NÃO COBERTO / PREPARAÇÃO TÉCNICA, SEM RUN FÍSICO.** Consulta GitHub da master em sequência aos merges #1366, #1368 e #1370. Esta análise não é comprovação de escala e não aprova alteração estatística.

## Distinção das provas
- [Run 42590459089](https://github.com/lucianox777/Jornada/actions/runs/42590459089): SQL técnico 100k **motor operacional V8**, 25k pendências; PASS de V8, não V9.
- [Run 42337308236](https://github.com/lucianox777/Jornada/actions/runs/42337308236): V9 160/160 em **30k**, pico RSS global ~5,80 GiB; não extrapolar para 100k.
- HTTP 1k/10k: provas técnicas independentes; não substituem calibrador multiparâmetro.

## Bloqueio do protocolo histórico 30k, não limite universal do motor
Os componentes científicos existentes congelam explicitamente a proveniência **30k seed42**, não apenas um parâmetro de CLI:
1. `Solution/scripts/v9-tf-pin-source.py` exige `gabarito.people=30000`, contagem de 30k identidades, seed42, três partições e hashes.
2. `Solution/scripts/v9-tf-option-grid.py` exige `PinnedIbgeBootstrap.People=30000`; `V9TfPinnedIbgePreflightCli.cs` exige `CanonicalBootstrapPeople` (30k).
3. `Solution/scripts/v9-mu-historical-plan.py` rejeita pin que não seja 30k/seed42; `v9-multiparameter-official-plan.py` depende do freezer histórico e constrói 24 hipóteses, 16 testemunhas/diagnósticos e 8 elegíveis CP.
4. A CLI C# genérica suporta população variável por `ObservationCapDeclaration`, manifesto e hash. O pin histórico `PinnedIbgeBootstrap.People=30000` identifica a origem da **referência IBGE/TF** e pode ser preservado ao calibrar um corpus independente de 100k: não é um teto de registros. O novo corpus exige seus próprios hashes/fingerprint, sem reutilizar `plan.json` histórico nem alterar referência ou regressão 30k.

## Próxima implementação controlada (ainda não realizada)
- Criar caminho **diagnóstico V9 100k** estritamente separado do modo histórico 30k, preservando imutáveis os parsers/planos/fingerprints 30k, e explicitando novo manifesto/seed técnica não reservada, `PinnedIbgeBootstrap`, revisão SQL/IBGE e o novo hash de estudo.
- Importar do plano oficial somente valores científicos já aprovados para V2 WITNESS/ANCHOR, V3, misturas, TF, M/U, blocking e limites; manter neutrals e `plan.json` único aplicável. **Não** copiar digests 30k como se fossem de 100k.
- Escrever testes offline positivos/negativos: origem 100k consistente, hash de plano, rejeição de pin/CSV incompatível, ausência de TEST/Gold, verificação SHA exato, paridade entre estratos V2/V3 e não seleção de testemunhas.
- Estimar memória/CPU/tempo antes do runner: 30k exigiu 5,8 GiB; isso não é estimativa validada de 100k. Runner e timeout precisam ser suficientes e declarados.
- Reaproveitar workflow somente se executar de fato motor V9; caso contrário PR técnica separada com Actions pinadas por SHA, manual, validação na branch e prova definitiva pós-merge em master.
- Medir TP/FP/FN, recall, FDR observado, P95 de latência de resolução tipado, duração, RAM, recursos, fingerprints, SHA, comparabilidade V2 no mesmo frame e limites de inferência; classificar PASS/FAIL/NÃO COBERTO.

## Gating e dependências
A Frente C permanece sem autorização de disparo até #1302 formalizada, preflight #1253 e privacidade aplicáveis, plano completo/assinado, ambiente SQL descartável, recursos e gates. Não executar TEST cego, seed reservada, Gold, seleção operacional ou ativação. O conector de escrita disponível não expõe `workflow_dispatch`, portanto nenhum 100k V9 foi iniciado.

Rastreabilidade: [#1349 comentário técnico de 09/11](https://github.com/lucianox777/Jornada/issues/1349#issuecomment-6739623411), [mandato](Decisao_GOV_V9_Mandato_Unico_20261109.md), GOV-DOC-01.


## Retificação interpretativa 09/11/2026

A ratificação do PO confirma: **30k não é teto universal de registros**. O bloqueio mencionado acima é o impedimento de usar **bytes e fingerprints da campanha histórica** como se fossem evidência de uma população diferente, não proibição de calibrar populações maiores. [Decisão vigente e ratificada](DC-V9-CALIBRACAO-SEM-EXCLUSIVIDADE-30K-20261105.md). A via técnica V9 100k foi integrada pela PR #1372, mas o teste físico permanece PENDENTE por #1253 e recursos. Não declarar aprovação científica/ativação.
## Adenda 09/11 — incompatibilidade de plano da conta pessoal com larger runner

A correção de capacidade da PR #1374 usou etiqueta `ubuntu-24.04-16core`, mas a conta proprietária do repositório é pessoal (`owner.type=User`), enquanto a documentação GitHub restringe *larger runners* a **organizações Team/Enterprise**. Não confundir perfil técnico de 64GB com possibilidade de agendamento nesta conta; nenhuma execução 100k aconteceu. Foi preparada rota **manual, PO-only, em runner autohospedado e efêmero Linux x64 com etiqueta customizada**, sem registrar ou disparar a VM; essa alternativa requer aceitação consciente dos riscos de segurança associados a runner autohospedado em repositório público, além de #1253 e infraestrutura. [Protocolo detalhado](Decisao_V9_Escala_100k_Multiparametro_Tecnica_20261109.md); [GitHub Docs](https://docs.github.com/en/actions/how-tos/manage-runners/larger-runners/manage-larger-runners). GOV-DOC-01.

## Retificação PO — testar otimização no runner padrão, sem VM própria (09/11/2026)

O PO estabeleceu que a rota preferencial da prova 100k deve usar o **próprio GitHub Actions padrão**, sem máquina física/VM do PO. Requisitos históricos de 24GiB/40GiB eram conservadores e incompatíveis com hardware padrão; não se tratavam de teto do motor. O novo fluxo mede limites de capacidade disponíveis no runner, restringe somente a memória de SQL descartável e executa primeiro um perfil **sem SQL, gerando 100k sintéticos e validando digests**, em push de arquivos específicos na master. Esse perfil só mede preparação de dados; **a prova física V9 24/24 em 100k ainda não foi realizada** e depende de segurança #1253/dispatch manual. [Decisão GOV-DOC-01](Decisao_V9_GitHub_Standard_100k_Otimizacao_20261109.md). A rota self-hosted da PR #1378 foi superada como padrão, não usada.

## Prova concreta do gerador no runner padrão — sem SQL (09/11/2026)

No SHA master `b4902dbe0f0ee815eafd4aa48fa216be23c9c382`, [run 42664056609](https://github.com/lucianox777/Jornada/actions/runs/42664056609) **SUCCESS** na geração sintética de 100 mil identidades no GitHub Actions padrão (`ubuntu-24.04`), com corpus/hash validado; artifact **13694813389**, pico RSS do **gerador isolado** ~491 MiB, arquivos ~27,23 MiB. Medição de RAM e disco totais do job e digests detalhados no [relatório oficial](Relatorio_V9_GitHub_Standard_100k_Perfil_Gerador_20261109.md). **Não foram executados o SQL nem os 24 fits V9**; a evidência favorável à opção sem VM própria não comprova viabilidade do Calibrador 100k completo. Não ultrapassar bloqueio #1253 nem fechar #1349. GOV-DOC-01.
