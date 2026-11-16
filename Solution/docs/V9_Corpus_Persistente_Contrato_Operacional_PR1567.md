# V9 — Contrato operacional do corpus persistente (PR #1567)

**Estado:** implementação parcial; PR #1567 em **DRAFT, aberta e sem merge**, sincronizada à master após #1571. Contratos Python e transporte físico de **apenas 2 registros sintéticos** aprovados; persistência durável, gerador familiar e PREPARE→TRAIN real continuam pendentes. Registro de engenharia vinculado à decisão GOV-DOC-01 já documentada nesta branch. A aprovação de arquitetura pelo PO não autoriza executar a campanha científica nem mesclar.

## Fluxo implementado nesta branch

- `v9-corpus-input-receipt.py`: captura hashes do código-fonte do gerador, IBGE nomes e referência diária de nascimento; aceita referência familiar somente se explicitamente fornecida. Não supõe que o catálogo REF familiar contém valores estatísticos.
- `v9-corpus-snapshot.py`: identidade SHA-256 da configuração de geração, hash dos quatro arquivos de saída reais e manifesto; recusa alteração silenciosa; publicação com lock POSIX e rename.
- `v9-corpus-prepare.py`: lock do ciclo lookup/generate/publish; rejeita cache existente corrompido; handoff para diretório independente com nova validação.
- `.github/workflows/v9-corpus-prepare-opt-in.yml`: PREPARE manual na branch de trabalho, com cache GitHub Actions, artefato por `run_id` e job **`train-handoff-probe` em runner separado**, validando SHA, configuração e conteúdo. **Não é o TRAIN de 324 hipóteses nem prova de retenção durável.**
- `v9-corpus-handoff-verify.py`: admite somente corpus/manifesto/recibo de PREPARE íntegros em outro runner, rejeita corrupção, configuração trocada e paths simbólicos; não executa calibração.
- `.github/workflows/v9-corpus-persistence-contract.yml`: testes focados de corpus e guard de workflows na PR (não altera os testes históricos do gerador seed42/43/44).

## Limites e bloqueadores

1. O cache do GitHub Actions pode expirar ou ser removido; não constitui armazenamento durável garantido. A arquitetura aprovada exige política de retenção, ACL, auditoria, imutabilidade e recuperação cross-runner antes de ativar o fluxo principal.
2. O artefato do PREPARE é limitado à execução e retenção do Actions; ainda não há consumidor seguro no TRAIN de 324 hipóteses.
3. O PREPARE manual usa `expected_sha` para a branch de desenvolvimento. O fluxo científico existente mantém exigência de `master`; **não alterar essa admissão para permitir execução científica em branch**.
4. O modelo familiar exige valores quantitativos oficiais congelados e rastreáveis (não apenas catálogo de fontes). Não atribuir probabilidades inventadas a mães, irmãos ou gêmeos.
5. Perfil clean histórico: taxa de corrupção materna de **10%**, congelada com a fonte do gerador para seed42/43/44; **13,4% é hipótese ilustrativa e NÃO executável** no fluxo atual. O recibo pode registrar uma taxa experimental, mas o PREPARE manual recusa **qualquer override** antes de gerar. Uma variante de gerador com hash/versão próprios, testes científicos e autorização específica é requisito para habilitá-la.
6. O contrato de transferência por outro runner foi codificado e protegido, mas **a execução física do PREPARE manual e do download inter-runner ainda exige CI com recibo**. Persistência cross-run, performance, concorrência e armazenagem segura precisam de evidência específica. A mera existência de testes não prova execução.
7. A integração final exige evidências de preservação das 324 hipóteses, 81 células, TF, m/u, FDR/CP/P95, TRAIN/VALIDATION/TEST e regras de acesso.

## Critérios para declarar PR pronta (sem mesclar)

- [ ] Fonte REF familiar com números oficiais, período, município/UF, hashes, licenças e justificativa.
- [ ] Variante de gerador experimental isolada para taxa materna configurável, sem alterar os hashes congelados de seed42/43/44; no estágio atual override é explicitamente rejeitado antes da geração.
- [ ] Modelagem familiar estatisticamente rastreável e testes determinísticos.
- [ ] Persistência durável entre execuções e runners, com retenção e recuperação testadas.
- [ ] PREPARE integrado ao TRAIN principal com handoff fail-closed e controle de custo. **Existe apenas PROBE PREPARE → consumidor diferente com verificação SHA, nunca o TRAIN científico.**
- [ ] Testes unitários, CI e segurança aprovados com links de execução no HEAD atualizado. Hashes de código do gerador histórico mantidos; suíte científica independente é protegida.
- [ ] Revisão de diff, documentação GOV-DOC-01 e índice sincronizados.
- [ ] Revisão final do PO para autorizar merge **em momento posterior**.

**Instrução permanente do PO para esta atividade:** manter PR #1567 aberta, sem merge na master. A branch foi atualizada para conter todas as mudanças da master até #1571; isso **não** implica aprovação de integração da PR.

## Prova CI independente, sem executar 100k — 15/11/2026

| Gate | Execução | Resultado | Escopo real |
|---|---|---|---|
| [PR #1567](https://github.com/lucianox777/Jornada/pull/1567) | DRAFT, não mesclar | aberta | Só protótipo de corpus |
| [PR-only #43759160834](https://github.com/lucianox777/Jornada/actions/runs/43759160834) | `probe-prepare` + `probe-consumer` | **2/2 SUCCESS** | Dois runners separados; **duas linhas públicas sintéticas**; arquivo/recibo completos; **NÃO TRAIN** |
| [Contrato #43759160878](https://github.com/lucianox777/Jornada/actions/runs/43759160878) | Python corpus 14/14 + segurança workflow 3/3 | **SUCCESS** | Corrupção e metadados rejeitados; sem ciência |
| Fonte congelada seed42/43/44 | Código C# do gerador | preservado | Alteração anterior revertida em vez de mudar os locks |

O job consumidor em runner separado registrou exatamente `V9_CORPUS_CROSS_RUNNER_HANDOFF_PASS` com `status=TRANSFER_VERIFIED_NONSCIENTIFIC` e `scientific_test_used=false`. O contrato usa `v9-corpus-crossrunner-pr-smoke.yml`, fixture de duas linhas (`tests/scripts/v9_corpus_crossrunner_fixture.py`) e `v9-corpus-handoff-verify.py`, sem certificados TEST, sem banco real, sem pontuação de pares e sem 324 hipóteses. Não extrapolar o smoke de duas linhas para escala/durabilidade 100k.

**Bloqueios ainda abertos:** armazenamento durável independente do cache evictável, publicação imutável de corpus entre execuções, verdadeiros parâmetros familiares congelados em REF, variante experimental do gerador para degradação materna `13,4%` sem mexer no gerador histórico, integração no workflow físico TRAIN com handoff completo, controle de acesso/retentividade, medição P95 e aceite posterior do PO. O fluxo `workflow_dispatch` criado somente na branch draft não equivale a workflow manual registrado na master. O TEST científico 100k permanece lacrado e NÃO utilizado.

## Correção de concorrência e regressão CI — 15/11/2026

**Estado: IMPLEMENTADO na branch, validação do HEAD mais recente PENDENTE.** O handoff agora mantém `flock(LOCK_SH)` em `.prepare.lock` durante a verificação e cópia dos quatro arquivos e manifesto. Falha fechada quando o lock está ausente ou é symlink. O PREPARE utiliza `LOCK_EX` no mesmo lock para lookup/generate/publish. Isso é uma proteção local em volume POSIX compartilhado, **não** prova de armazenamento durável entre runs ou imutabilidade contra escritores externos não cooperativos.

Rastreabilidade: commits `04234e839d0f8645f13fadd7deadb5d52d95cfc4` (handoff), `bb70758a62a19bf859061d881dcde7b1338d6332` (teste de lock ausente) e `fb3a9ba1948f6b082ff672181739e2c33e61210c` (fixture de publicação direta). A [CI de contrato #43778938540](https://github.com/lucianox777/Jornada/actions/runs/43778938540) **FALHOU** no SHA `bb70758` porque seis casos do fixture chamavam `snapshot.publish` diretamente, sem criar o lock que o PREPARE normalmente cria. A correção do fixture adiciona explicitamente esse lock; **não afirmar PASS até o novo SHA passar pela CI**. O [smoke cross-runner #43778938541](https://github.com/lucianox777/Jornada/actions/runs/43778938541) no SHA anterior passou, mas não substitui o contrato reprovado.

Precedência: complementa o contrato operacional da PR #1567, sem modificar a decisão canônica GOV-V9-CORPUS-PERSISTENTE-REF-FAMILIAR-20261115, nem autorizar merge, TRAIN científico ou TEST 100k. Próximo gate: CI no HEAD corrigido; depois durabilidade real com ACL, retenção, recuperação e provas entre execuções.

## Verificação de regressão e negativos adicionais — 15/11/2026

No HEAD `6f97aa928758557abc665938c692cc0b452dd99c`, os quatro workflows de PR terminaram **SUCCESS**: [contrato #43779051691](https://github.com/lucianox777/Jornada/actions/runs/43779051691), [cross-runner #43779051798](https://github.com/lucianox777/Jornada/actions/runs/43779051798), [fast experimental #43779051701](https://github.com/lucianox777/Jornada/actions/runs/43779051701) e [required-fast #43779051694](https://github.com/lucianox777/Jornada/actions/runs/43779051694). Essa evidência resolve a regressão do fixture no SHA citado, **não** valida commits posteriores.

O commit `e79df6bcbc6a221e06dd86c5d1a3dd0edd1e21e9` acrescenta dois cenários negativos: lock de preparação simbólico rejeitado no handoff e corpus reutilizado corrompido rejeitado sem executar gerador novamente. **Testes adicionados, CI deste novo HEAD ainda não confirmada.** Sem mudança de decisão do PO, sem ativação de armazenamento durável, TRAIN ou TEST científico.

## Ampliação do contrato de admissão — 15/11/2026

Commit `a61b1bd51fc65d609a0b31b85a32e6158faac0f7`: testes de consumidor para aceitar recibo `REUSED` íntegro, rejeitar estados inesperados e recusar arquivo de corpus substituído por symlink. São **testes adicionados**, não evidência de validação até a CI correspondente passar. Complementam os negativos de lock e corrupção de `e79df6bcbc6a221e06dd86c5d1a3dd0edd1e21e9`. Não alteram a arquitetura de persistência nem os gates científicos.

## Recusa de store simbólico no consumidor — 15/11/2026

No handoff, a verificação anterior rejeitava symlink do diretório de corpus final e do lock, mas não rejeitava explicitamente um **symlink no diretório raiz do store**. O commit `b4a79a9d8e35c05c9f074c8d47f995888c7e1d56` acrescenta recusa antecipada para store ausente ou simbólico; o commit `d9f25b0af1933171cacd98b8244c7d9c303489e1` introduz regressão negativa específica. **Implementado na branch, CI do novo HEAD pendente**. Impacto: reduz aceitação acidental de caminhos indiretos no consumidor; não substitui ACL/imutabilidade de um backend durável. Precedência: complementa o contrato da PR #1567, sem decisão nova sobre provedor, retenção ou ativação científica.

## Contrato de não sobrescrita do snapshot — 15/11/2026

Commit `deefe4a5a9cb3b2c63be1afb6a926322cc1ba8d9`: teste de regressão para a semântica existente de `publish`: quando o corpus identificado pela mesma configuração já existe e é íntegro, uma segunda fonte com bytes diferentes **não substitui** os arquivos publicados; o recibo e os bytes originais permanecem. Este é um teste de imutabilidade **lógica sob a API cooperativa**, não prova de armazenamento WORM, ACL ou proteção contra alterações externas. CI do HEAD com este teste ainda pendente. A escolha de provedor e política de retenção continuam pendentes de aprovação/implementação; TEST 100k lacrado e PR DRAFT.

## Reutilização entre processos — contrato local, não cross-run — 15/11/2026

Commit `b355be587322fbd3c618c9cd20aaa5bc299a6d80`: teste inicia um **processo Python independente** sobre store POSIX previamente publicado, sem comando de gerador, e exige estado `REUSED`, ausência de geração e handoff íntegro. Isso exercita o caminho de reutilização entre processos, mas **não** comprova persistência entre runs de GitHub Actions, runners ou máquinas; o teste compartilha o mesmo diretório temporário local. CI do novo HEAD pendente. Próximo gate físico exige backend durável governado, ACL, retenção, imutabilidade, recuperação, duas execuções independentes com cache miss/hit e teste de corrupção. O cache Actions continua evictável, sem garantia de retenção. Sem alteração do TEST científico, sem ativação TRAIN, sem autorização de merge.

## Gate de seleção e prova de armazenamento durável — especificação de aceite (não ativada)

**Estado: critérios propostos de verificação do requisito já aprovado; provedor, credenciais, retenção e custos continuam SEM decisão específica do PO.** O volume POSIX dos scripts é uma interface local, não uma escolha de serviço. GitHub Actions cache e artifact de 7 dias não satisfazem isoladamente o requisito de durabilidade. Não marcar este gate como aprovado por sucesso de testes locais, cross-process ou cross-job.

| Gate | Evidência física exigida | Estado |
|---|---|---|
| Backend e segurança | Identificação explícita de provedor/volume, responsável, ACL de leitura/escrita por papel, criptografia, trilha de auditoria e credenciais efêmeras; sem secrets em logs | PENDENTE |
| Retenção e recuperação | Política de retenção e expurgo aprovada, procedimento de recuperação documentado e restauração comprovada com SHA idêntico | PENDENTE |
| Miss entre runs | Run A em runner novo, store inicialmente vazio, gera fixture pequena não científica, publica atomicamente e guarda recibo/ID imutável | PENDENTE |
| Hit entre runs | Run B independente de A, runner novo e ID de execução distinto, consome a mesma versão sem invocar gerador; comprova hash, tamanho e fingerprint | PENDENTE |
| Corrupção e identidade | Run C separado com fixture propositalmente adulterada ou referência inválida: rejeita antes do consumo; não sobrescreve versão íntegra nem faz fallback silencioso | PENDENTE |
| Concorrência | Dois produtores competindo pela mesma chave no backend real: somente uma versão íntegra visível, sem parcial e com lock/lease compatível com o backend | PENDENTE |
| Métricas | Bytes, duração PREPARE, transferência, custo de armazenamento/egresso, tempo de restauração e impacto no orçamento do TRAIN, sem executar 100k sem autorização | PENDENTE |
| Integração científica | PREPARE→TRAIN físico com 324 hipóteses/81 células e segregação TRAIN/VALIDATION/TEST comprovadas; TEST independente permanece selado até autorização separada | BLOQUEADO |

**Sequência de aprovação:** (1) PO decide provedor e política operacional; (2) implementar adaptador adequado, sem tratar `flock` POSIX como lease de object storage; (3) executar provas físicas de baixo custo com fixture pública; (4) revisar resultados e custos; (5) somente depois avaliar ativação do TRAIN e autorização científica independente. Nenhuma etapa autoriza mesclar a PR #1567 sem aprovação expressa posterior. Esta matriz complementa a decisão `GOV-V9-CORPUS-PERSISTENTE-REF-FAMILIAR-20261115` e não altera sua precedência.

## Endurecimento do inode de lock — 15/11/2026

O controle anterior verificava `.is_symlink()` antes de `open()`, permitindo uma janela TOCTOU entre a checagem do caminho e sua abertura. Commits `54e9ad4d` e `09f23e84`: a abertura do lock de PREPARE/handoff agora usa `os.open` com `O_NOFOLLOW`, `O_CLOEXEC` e `O_NONBLOCK`, verifica `fstat` para exigir inode regular e fecha o descritor em caso de recusa. O commit `5e7b9803` adiciona teste negativo com FIFO para comprovar recusa sem bloqueio indefinido. **CI deste novo HEAD pendente**. Limitação: `flock` e `O_NOFOLLOW` pressupõem Linux/POSIX; não representam lease distribuído em object storage e não eliminam alterações externas sem ACL. Esta implementação reforça o contrato existente, sem seleção de provedor nem nova decisão aprovada pelo PO.

## Proteção equivalente no lock de publicação — 15/11/2026

Após o endurecimento do lock PREPARE, a publicação atômica ainda usava `open(lock_path, "a+b")` após verificação de symlink separada. O commit `0bd7feb709d3c1411d63362fe16ca7f9f33fc049` aplica `os.open(O_NOFOLLOW | O_CLOEXEC | O_NONBLOCK)` e `fstat` para exigir inode regular também em `.publish.lock`; `f881aedd923c77dc7d0be6d93cbfabbf4f415813` adiciona teste de FIFO que deve falhar antes da exposição do snapshot. Objetivo: eliminar a mesma janela de troca do componente final do lock e evitar bloqueio em FIFO. **CI do HEAD com estes commits pendente**. Não constitui lease distribuído, durabilidade física, nem autorização para TRAIN/TEST ou merge. Complementa GOV-V9-CORPUS-PERSISTENTE-REF-FAMILIAR-20261115 sem decisão nova do PO.

## Teste complementar de symlink no lock de publicação — 15/11/2026

Commit `6fdb7a9bbc15c46e2e859254afe2cfc9e69c866b`: cenário negativo cria `.publish.lock` como symlink para arquivo externo e exige falha fechada, preservação dos bytes externos e ausência de snapshot publicado. Complementa o teste de FIFO do commit `f881aedd`. Evidência da CI **anterior**, no commit `f62847f3`: workflow de contrato [#43780348371](https://github.com/lucianox777/Jornada/actions/runs/43780348371) **SUCCESS**. A CI do commit novo ainda não está confirmada. Sem escolha de backend, sem mudança de decisão do PO e sem uso do TEST científico.

## Diretriz posterior do Product Owner — prosseguir até a mesclagem (15/11/2026)

O Product Owner solicitou expressamente **“Pode continuar até mesclagem.”** Essa instrução substitui a proibição anterior de realizar o merge **quanto à autorização para concluir o trabalho e mesclar**, mas **não declara atendidos os critérios de aceite** nem autoriza presumir provedor de armazenamento, custo, política de retenção, liberação do TEST lacrado ou campanha científica 100k. A PR continua DRAFT até conclusão técnica e verificação dos gates documentados; não efetuar merge de implementação incompleta sob pretexto desta autorização. Contexto: PR #1567 já dispõe de contrato local, prova não científica entre runners e testes de integridade, porém não dispõe de durabilidade entre runs nem integração real ao TRAIN. Impacto: a equipe pode conduzir a PR até merge após resolver e evidenciar os bloqueios, sem solicitar nova autorização genérica de merge; decisões operacionais novas, como backend e retenção, permanecem sujeitas à governança. Rastreabilidade: conversa do PO de 15/11/2026, PR #1567 e decisão GOV-V9-CORPUS-PERSISTENTE-REF-FAMILIAR-20261115. Estado: **AUTORIZAÇÃO DE OBJETIVO REGISTRADA; CRITÉRIOS TÉCNICOS PENDENTES**.

## Reiteração da autorização para concluir até merge — 15/11/2026

O PO reiterou **“Pode continuar até mesclagem.”** após ser apresentada a proposta de escolher o armazenamento durável mais adequado com prioridade a baixo custo e integração GitHub. A reiteração autoriza prosseguir com a avaliação e implementação técnica visando o merge; **não especifica** provedor, orçamento, credenciais, prazo de retenção nem aprova execução científica 100k ou abertura do TEST lacrado. Decisões concretas de infraestrutura deverão ser documentadas antes de sua ativação e comprovadas em execução independente. Estado da CI observado no HEAD `c7f434eb1a0840774d1968b43d5a3a5f39d5a1da`: contrato [#43780466895](https://github.com/lucianox777/Jornada/actions/runs/43780466895), cross-runner [#43780466903](https://github.com/lucianox777/Jornada/actions/runs/43780466903) e fast experimental [#43780466982](https://github.com/lucianox777/Jornada/actions/runs/43780466982) SUCCESS; required-fast [#43780466965](https://github.com/lucianox777/Jornada/actions/runs/43780466965) ainda `pending` na consulta. A autorização não substitui as evidências de durabilidade e integração TRAIN exigidas na decisão original.

## Regressão de concorrência entre publicadores locais — 15/11/2026

Commit `b6a23d1cd0b4ba2ac901e41af83a53e671fbc0c5`: teste não científico com 12 publicações concorrentes em 6 threads para a mesma identidade de configuração, exigindo um único diretório final, recibos equivalentes e validação integral do snapshot. A prova cobre a serialização cooperativa no mesmo filesystem POSIX e **não** comprova concorrência distribuída entre runners ou durabilidade entre execuções GitHub Actions. CI deste commit ainda pendente de verificação. Mantidos os gates de backend durável, integração real TRAIN, autorização de infraestrutura e TEST lacrado.

## Allowlist física de entradas no snapshot — 15/11/2026

Commits `07d5601e` e `0050027f`: além de exigir o conjunto exato de arquivos no manifesto, `verify` passa a rejeitar entradas físicas adicionais no diretório do snapshot (arquivos ou subdiretórios). Testes negativos cobrem rejeição em `verify` e na reutilização por `publish`, sem regeneração silenciosa. Isso fortalece a fronteira de integridade, **não** demonstra durabilidade entre runs nem integra TRAIN. CI dos novos commits precisa ser observada. Sem alteração de decisões do PO, gerador congelado ou TEST lacrado.

## Preparação do diretório de entrada TRAIN — 15/11/2026

Commit `9001e6905d177322c7e86c4b444ed374b37b3ced`: o verificador `v9-corpus-handoff-verify.py` passou a oferecer `--train-destination <diretorio-ainda-inexistente>`. A rotina `materialize_train_input` valida recibo, fingerprint e SHA de todos os arquivos ANTES de copiar, copia apenas a allowlist para staging temporário, valida novamente o snapshot e expõe a entrada por rename. Rejeita destino já existente, inclusive symlink, sem sobrescrever dados. **É um primitivo de admissão, não a integração do workflow TRAIN**: o workflow científico continua gerando o corpus seed42, sem consumir esta opção. O backend durável, a prova física independente, testes CI da nova rotina e a ligação ao TRAIN seguem pendentes; não executar 100k nem mesclar antes dos gates. A autorização de merge do PO está registrada, mas condicionada à satisfação dos critérios técnicos.

## Cobertura da admissão TRAIN e prova opt-in — 15/11/2026

Commits `d983a4b0` e `2011b04f`: três testes unitários exercitam materialização íntegra para destino novo, rejeição de payload adulterado sem publicar destino e rejeição de symlink no destino. O job manual `train-handoff-probe` passa a invocar `--train-destination` para comprovar cópia validada em runner consumidor, mas continua **NONSCIENTIFIC**, não invoca TRAIN, não comprova retenção entre runs e não deve ser confundido com execução física já realizada (workflow manual não disparado). A CI deste HEAD ainda precisa ser observada. Nenhum novo custo, alteração do gerador congelado ou TEST independente.

## Vinculação opcional de fingerprint na admissão — 15/11/2026

Commits `a9a57bbf`, `9ebd9fd6`, `255aa371` e `12f861d2`: `materialize_train_input` aceita `expected_fingerprint` opcional e rejeita divergência antes de copiar; teste cobre rejeição e sucesso. O job manual consumidor calcula fingerprint da configuração recebida e o passa ao verificador; esse cálculo **não é uma âncora externa independente**, portanto não substitui a vinculação futura à receita esperada pelo TRAIN principal. A variável derivada no PREPARE não atravessa automaticamente jobs; o último commit corrige esse escopo. CI atualizada ainda não validada; sem backend durável, sem execução científica e sem merge.

## Endurecimento de ancestral simbólico — 15/11/2026

Commits `0a7cdf9c` e `8e9a2b7b`: a admissão do diretório TRAIN rejeita agora symlinks em **qualquer ancestral** do destino, além do destino e do pai imediato. Teste regressivo cria um alias simbólico intermediário e exige falha sem criação de diretório sob o alvo real. Esta defesa de caminhos não é prova de isolamento contra adversários concorrentes com controle do filesystem; uso produtivo exige diretórios/ACL controlados. CI do HEAD pendente. Sem alteração da ciência ou dos requisitos de armazenamento durável.
