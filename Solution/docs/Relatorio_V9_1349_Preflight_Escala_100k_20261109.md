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
