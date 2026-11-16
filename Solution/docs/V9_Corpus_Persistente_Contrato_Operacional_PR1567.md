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

## Admissão independente das entradas de geração no consumidor — 15/11/2026

Commit `0836c4fc`: no workflow manual opt-in, o runner consumidor agora recalcula a configuração de geração usando o **checkout do SHA fixado**, o código do gerador e as referências IBGE/nascimentos presentes nesse checkout. Compara integralmente esse recibo esperado ao `config.json` recebido do produtor **antes** de admitir o corpus. Isso corrige o limite anterior em que o fingerprint era derivado apenas da configuração transportada pelo próprio produtor, sem âncora independente. O contrato permanece NONSCIENTIFIC e não comprova durabilidade entre execuções; o workflow manual ainda não foi executado fisicamente e a CI do novo HEAD está pendente. Sem alterar os geradores congelados, o TEST independente ou a política de autorização de 100k. A seleção de provedor/retention/ACL permanece pendente, portanto não declarar merge pronto.

## Contrato de configuração independente centralizado — 15/11/2026

Commits `0618696f`, `9a13d1e5` e `6ca4464b`: a API `verify_handoff` e a materialização TRAIN aceitam `expected_config_path` e rejeitam configuração divergente, ausente ou symlink antes de admitir arquivos. O workflow opt-in recalcula a receita de geração com fontes fixadas e passa `--expected-config` à API; a verificação deixa de depender de um trecho Python duplicado no YAML. Testes cobrem divergência, igualdade e symlink. Justificativa: centralizar a fronteira de confiança e reduzir risco de caminhos de execução sem verificação. Limites: o consumidor deve produzir a receita independente de fontes confiáveis; não comprova armazenamento durável, TRAIN real ou TEST; CI do novo HEAD ainda pendente. Sem custo ou execução científica.

## Regressões obrigatórias da proveniência do consumidor — 15/11/2026

Commits `e8b7c915` e `14af87b5`: o teste estático do workflow opt-in passa a exigir explicitamente `--expected-config`, código gerador e referência de nascimentos fixados; teste unitário garante que recibo independente ausente rejeite admissão e não crie destino. Justificativa: impedir regressão silenciosa da verificação independente recém-implementada. CI do novo HEAD pendente; persistência durável e TRAIN científico continuam bloqueados. Nenhuma autorização de custos ou nova execução científica inferida.

## Proteção de caminhos também no PREPARE handoff — 15/11/2026

Commits `318ac7c3` e `052eff0b`: `prepare.handoff` agora rejeita links simbólicos em todos os ancestrais do destino antes de criar o diretório, com teste regressivo para alias intermediário e ausência de efeitos no alvo real. Complementa a proteção já existente em `materialize_train_input`; não constitui garantia contra substituição concorrente de diretórios por agente com acesso de escrita. CI do novo HEAD pendente. Sem alteração de gerador congelado, TEST, custo ou decisão sobre backend durável.

## Fingerprint esperado ancorado na receita independente — 15/11/2026

Commit `82bf9d55`: no consumidor opt-in, `--expected-fingerprint` passa a ser derivado de `expected-config.json`, recalculado a partir de código e REF do checkout fixado, em vez de `config.json` transportado. A API já compara as duas configurações; a alteração elimina dependência redundante do valor não confiável no cálculo da expectativa. A CI do novo HEAD deve ser validada. Não modifica ciência, TEST, custos nem resolve armazenamento durável ou integração ao TRAIN real.

## Guarda de regressão do fingerprint independente — 15/11/2026

Commit `db813e6d`: teste estático do workflow passa a exigir que o fingerprint esperado derive explicitamente de `expected-config.json` e seja enviado a `--expected-fingerprint`. Evita regressão para a configuração recebida do produtor, preservando a âncora independente de proveniência. CI do novo HEAD pendente; sem alteração dos gates científicos, custos, armazenamento ou TEST.

## Validação dos ancestrais do armazenamento PREPARE — 15/11/2026

Commits `1f3dfb0c` e `8125ea65`: tanto `prepare` quanto `handoff` recusam armazenamento cujo caminho contenha symlink em qualquer ancestral; teste comprova recusa antes de executar gerador ou criar o armazenamento por alias. Justificativa: impedir redirecionamento acidental/malicioso de corpus e efeitos fora da raiz pretendida. Limite: não impede substituição concorrente de diretórios por processos com permissão de escrita. CI do novo HEAD pendente; provider durável e TRAIN científico permanecem bloqueios independentes.

## Publisher independente com mesma política de caminhos — 15/11/2026

Commits `dda72108` e `ed509212`: `v9-corpus-snapshot.publish` rejeita symlink em qualquer ancestral da raiz do armazenamento, mesmo quando chamado diretamente fora de `v9-corpus-prepare.py`; teste confirma que nenhum diretório de armazenamento é criado por alias. Contexto: impedir bypass da proteção PREPARE pela API de publicação isolada. Impacto: caminhos intencionalmente montados via symlink precisam ser substituídos por caminho canônico sem alias; não resolve corrida de diretórios nem storage durável. CI do novo HEAD pendente; sem mudança de ciência, TEST ou custos.

## Preflight explícito para montagem POSIX externa — 15/11/2026

Commits `914547bc` e `f232aa9e`: novo script `v9-corpus-external-store-preflight.py` exige caminho absoluto, montagem POSIX externa já provisionada, ausência de symlinks, permissões e declaração de evidências de pelo menos dois runs distintos vinculados ao repositório. Testes negativos recusam diretório local e declaração incompleta. **Limite importante:** a declaração é fornecida pelo operador, não constitui prova criptográfica nem verificação automática da durabilidade; `ismount` sozinho não distingue disco local montado de serviço externo. Portanto, o preflight é apenas um gate preparatório, **não** certificação de cross-run, nem integração ao workflow científico. Antes de promover, implementar verificação independente dos runs, identidade/autenticidade das evidências e provider real, além de aprovar retenção e custos. Nenhum fornecedor contratado e nenhuma campanha 100k disparada.

## Correção do shell do consumidor opt-in — 15/11/2026

Commit `73ca5934`: corrigidas quatro continuações de linha com barras invertidas duplicadas no passo `Verify exact config/manifest SHA and complete file hashes` de `.github/workflows/v9-corpus-prepare-opt-in.yml`. Com duas barras, o Bash não escapava a quebra de linha e podia executar opções `--generator-source`, `--ibge-names` e outras como comandos separados, interrompendo o consumidor antes da verificação. Agora cada continuação usa uma única barra. Esta correção não comprova execução física do workflow manual, não habilita TRAIN 100k nem resolve persistência durável; CI e ensaio operacional continuam necessários.

## Regressão do Bash no consumidor opt-in — 15/11/2026

Commit `e071cffd`: teste `test_v9_corpus_opt_in_shell_safety.py` verifica que as opções do recibo de entrada existem no job consumidor e que cada linha continuada termina em exatamente uma barra invertida, impedindo reintrodução da falha corrigida em `73ca5934`. É teste estático; não substitui execução física do workflow manual nem certifica persistência cross-run. CI do novo commit pendente.

## Negativa de raiz do filesystem no preflight — 15/11/2026

Commits `d2c446a4` e `4c17ea22`: o preflight de storage externo recusa explicitamente `/`, que satisfaz `os.path.ismount` em runners comuns, mas não é evidência de armazenamento externo. Teste negativo incluído. Esta proteção não certifica que outras montagens sejam remotas nem autentica as declarações do operador; a persistência cross-run continua não comprovada. Sem mudança científica e sem autorização de campanha 100k.

## Tipagem estrita da evidência cross-run — 15/11/2026

Commits `8ed3eb04` e `56dd7d8c`: IDs de execução na declaração de persistência exigem `int` exato positivo; `True` e `False` não são aceitos como inteiros por herança de tipo Python. Incluído teste negativo. Não equivale a verificação independente da autenticidade dos IDs ou da durabilidade do provedor; não libera merge, campanha 100k ou TEST. CI do novo HEAD deve ser conferida.

## Ligação PREPARE → plano científico no probe opt-in — 15/11/2026

Commits `7944fa4e` e `5ea8e5a5`: após a validação independente do snapshot no runner consumidor, o workflow manual opt-in obtém o artefato histórico oficial por ID e SHA-256 fixos e passa o diretório materializado do PREPARE ao mesmo planejador `v9-scale-100k-technical-plan.py` e ao planejador V3 `v9-v3-four-name-100k-plan.py` usados no TRAIN. O download recebe somente o token Actions de leitura no passo consumidor. Isso é **prova de binding do plano, não execução das 324 hipóteses**; não modifica o workflow científico principal, não usa TEST, não demonstra armazenamento externo durável. Execução manual opt-in não disparada; aprovação CI do commit ainda pendente. O merge permanece bloqueado pelas evidências operacionais e integração física aprovadas.

## Regressão do binding do plano V3 — 15/11/2026

Commit `9f7c452c`: teste estático do workflow opt-in exige que o diretório materializado pelo verificador de handoff seja exatamente o `--corpus-root` do planejador técnico, seguido do planejador V3; exige SHA fixo do artefato histórico e token de leitura, e impede inclusão de calibração física ou preparação de TEST nesse job. A verificação estática não prova execução do probe manual, persistência durável nem TRAIN físico. A aprovação da CI deve ser verificada após o commit.

## Correção de falha CI do contrato de segurança — 15/11/2026

O job `corpus-contract` da execução `43784104143` falhou em `test_corpus_prepare_cannot_dispatch_scientific_train` porque o novo marcador de binding substituíra indevidamente o marcador exigido `TRANSFER_PROBE_ONLY`. Commit `5ca8cfb1` restaura explicitamente `V9_CORPUS_TRANSFER_PROBE_ONLY` preservando o novo `V9_CORPUS_PLAN_BINDING_PROBE_ONLY`. Impacto: preserva o controle de não execução científica; sem alteração dos dados, das 324 hipóteses ou do TEST. A aprovação CI do novo SHA segue necessária.

## Isolamento do armazenamento sintético na camada REF — 15/11/2026

Após aprovação do PO para priorizar a camada REF, commits `c71ef584` e `c725f761` introduzem `v9-corpus-ref-store-path.py` e teste de isolamento. O validador exige raiz REF e diretório de referências oficiais existentes, caminhos absolutos e sem ancestrais simbólicos, namespace sintético contido na raiz REF mas disjunto das referências oficiais; não cria nem altera dados oficiais. A validação é **apenas de topologia e segurança de caminho**: não comprova que a REF seja mount durável, nem provisiona ACL, retenção ou cross-run. Antes de ligar o TRAIN, o operador deve fornecer a localização real do volume REF compartilhado, comprovar durabilidade com duas execuções distintas e executar os preflights de mount e SHA. Nenhum custo ou campanha 100k foi autorizado por esta implementação. CI do novo HEAD pendente.

## Admissão conjunta REF + volume durável — 15/11/2026

Commits `c4943424`, `b7746eb0`, `dd1c3a45`: rejeição explícita de `/` como raiz REF; novo `v9-corpus-ref-store-admission.py` combina a prova de isolamento de namespace com o preflight de mount POSIX externo já existente e atestado pelo operador; teste negativo garante que um diretório REF local comum não seja aceito como volume durável, mesmo com atestado declaratório. **Não foi provisionado volume REF nem executada prova cross-run**; o atestado continua sujeito a auditoria independente. Sem execução do TRAIN científico ou TEST, sem autorização de gastos, e sem alteração das 324 hipóteses. A CI do HEAD ainda precisa ser confirmada antes de considerar este incremento validado.

## Planejamento de retenção fail-closed — 15/11/2026

Commits `68b6bc5e` e `2227afe5`: criado `v9-corpus-retention-plan.py` como **planejador somente leitura**, com teste de exatamente um corpus `CURRENT` aprovado e validado por configuração, fingerprints únicos e reconhecimento de leitores ativos e dependências protegidas. Todas as versões recebem `RETAIN`: **não há exclusão automática** porque ausência de dependências científicas e capacidade de replay não foram comprovadas. O mecanismo implementa validação inicial da política aprovada, mas **não** promoção atômica, leases duráveis, rollback ou coleta segura; não declarar política integralmente entregue. CI do novo HEAD pendente. Não toca no TEST ou nas 324 hipóteses.

## Promoção atômica do índice REF — 15/11/2026

Commits `c0caf828` e `bdadc0b1`: `v9-corpus-ref-promote.py` altera **somente o índice** de retenção sob lock POSIX, com gravação em arquivo temporário, `fsync`, `os.replace` e sincronização do diretório. Exige aprovação explícita e entrada-alvo já marcada validada/aprovada, preserva o corpus anterior como `PREVIOUS` e não remove bytes. Testes verificam promoção, idempotência, ausência de alteração sem aprovação e rejeição de alvo inválido. **Limite importante:** o script não prova a existência nem a integridade dos arquivos do corpus apontado; deve ser chamado somente após verificação independente dos snapshots, admissão do volume REF durável e ausência de leitores não coordenados. Não está ligado ao workflow físico; rollback e coleta segura seguem pendentes. CI do novo HEAD pendente. Nenhum TEST foi acessado.

## Verificação física obrigatória antes da promoção — 15/11/2026

Commits `068120bf`, `869ae454`, `e22940aa`: o promotor do índice REF agora exige `--store` e `--generation-config` e verifica o fingerprint calculado e todos os hashes do snapshot físico **sob o lock do índice**, antes de marcar a versão `CURRENT`. Testes de promoção usam snapshots reais de fixture, não apenas declarações no índice. A verificação física local **não comprova durabilidade cross-run, isolamento de volume ou ausência de concorrência com escritores externos**; integração com preflight REF e com o workflow TRAIN continua pendente. Não houve merge nem execução científica.

## Revisão de automação do PO — 15/11/2026

Commits `1bd4dd07`, `eab3ab69`, `654d2c20`, `cc6ec1a0`: decisão aprovada e indexada; retirada a exigência de `--approved` por promoção rotineira do índice REF. O alvo continua obrigado a estar **previamente validado e aprovado no índice** e a passar verificação física SHA-256. **Não foi habilitada exclusão automática:** falta prova independente de ausência de leitores ativos e dependências de reprodutibilidade, além da durabilidade real do backend REF. A automação end-to-end por alteração de parâmetros e CI do HEAD seguem pendentes; nenhum corpus foi excluído.

## Correção da CI e bloqueio técnico de promoção produtiva — 15/11/2026

Run `43785788817` identificou duas chamadas obsoletas `approved=True` nos testes após revisão da interface. Commit `9ba60ef9` removeu esses argumentos e forneceu os parâmetros de verificação física. Commits `463af1a9` e `95a144dd` exigem `--environment experimental|production` na CLI do promotor e **rejeitam `production` antes de qualquer operação**, com teste negativo. A ferramenta é exclusivamente de ciclo interno experimental; autorização manual de produção continua externa a ela, conforme decisão do PO/DT-15. Este bloqueio não implementa pipeline produtivo de aprovação nem prova REF cross-run. Nova CI pendente.

## CI — correção de chamada residual (16/11/2026)

O run `43785880946` falhou em **um** teste (`test_unvalidated_target_rejected_without_mutation`), ainda chamando `promote(..., approved=True)` após remoção deliberada desse argumento. Commit `1acc458c` corrige a última chamada residual e passa `store` e `generation` para a validação física; demais 51 testes desse job passaram. A CI do novo HEAD precisa confirmar a correção. Não altera produção, TEST ou os critérios científicos.

## Testes negativos de promoção REF — 16/11/2026

Commit `7dcefd7a`: inicializa o armazenamento de snapshots no teste de rejeição de alvo não validado (evita `NameError` de variável `store` não definida após migração da API) e adiciona teste de adulteração de `observacoes.csv`, que exige rejeição por SHA e índice inalterado. A CI do novo HEAD é necessária para atestar os resultados. A PR continua DRAFT; não há backend REF durável cross-run comprovado nem autorização de promoção produtiva.

## CI verde e contexto experimental explícito — 16/11/2026

No SHA `ffa0e4e9`, os checks `v9-corpus-persistence-contract`, `v9-experimental-fast-pr` e `V9 corpus cross-runner transport PR probe (NONSCIENTIFIC)` concluíram com **SUCCESS**; `jornada-pr-required-fast` ainda aparecia `pending` na consulta. Commits `991920d7` e `90ea0977` removem o default implícito `experimental` da API Python de promoção e exigem contexto experimental expresso também nos testes; ausência de contexto e `production` falham fechados. A CI deve ser reavaliada no HEAD. A prova cross-run de armazenamento REF persistente e integração TRAIN real continuam pendentes, portanto sem merge ou ativação produtiva.

## CI 43786123043 — ambiente experimental nos testes (16/11/2026)

O contrato de persistência falhou no SHA `9bddfb1a` por **três chamadas de teste** que não informavam `environment="experimental"` após o bloqueio fail-closed da API. Commit `d7b3de76` passa o contexto explícito nos casos de promoção, idempotência, alvo não validado e snapshot adulterado, mantendo testes negativos separados para ausência de ambiente e `production`. Não foi relaxado o bloqueio produtivo. A CI do HEAD deve confirmar. A integração REF durável cross-run e PREPARE→TRAIN permanece pendente; PR DRAFT sem merge.

## CI 43786215815 — duas chamadas negativas remanescentes (16/11/2026)

A CI de persistência ainda falhou em dois testes negativos porque as chamadas de alvo não validado e snapshot adulterado omitiam `environment="experimental"`. O commit `f0970aec` altera explicitamente ambas, preservando os testes que rejeitam ambiente omitido ou `production`. A correção foi verificada pela leitura do arquivo na branch; confirmação da nova CI ainda pendente. Sem alteração de critérios de produção, REF durável ou merge.

## Auditoria de observações entre execuções — 16/11/2026

No SHA `44e26ba8`, todos os quatro workflows da PR concluíram com **SUCCESS**, inclusive `jornada-pr-required-fast`. Commits `0c36404b` e `762e4ae3` adicionam `v9-corpus-cross-run-proof.py` e testes negativos para comparar observações de duas execuções distintas (run IDs, tentativas únicas, fingerprint, hashes de arquivos e identidade declarada do armazenamento). A saída é explicitamente `TWO_OBSERVATIONS_CONSISTENT_NOT_DURABILITY_CERTIFIED`: igualdade de recibos **não comprova** que o armazenamento REF é realmente compartilhado; exige atestado independente de montagem e execução real em runners distintos. A CI do novo HEAD está pendente. Sem autorização de calibração 100k, sem alteração produtiva e sem merge até comprovar REF persistente e PREPARE→TRAIN científico.

## Evidência física REF — observador (16/11/2026)

Após sucesso dos quatro workflows no HEAD `22b8d4f5`, os commits `84257e7b` e `6364374e` estendem a auditoria: o modo `--corpus --config --repository --run-id --run-attempt --store-identity` produz observação diretamente de arquivos físicos verificados com `snapshot.verify`, não de hashes fornecidos pelo operador; rejeita caminho de corpus com symlink, configuração inválida, fingerprint não hexadecimal e metadados incompletos. A identidade do mount ainda é declaração do operador; recibos não equivalem a prova de persistência ou atestado independente. Não houve execução 100k, implantação em produção ou autorização de merge. Nova CI pendente.

## Falha de sintaxe no observador REF — CI 43786788804

A CI de contrato de persistência falhou com `SyntaxError` no arquivo `v9-corpus-cross-run-proof.py`, pois uma atualização introduziu sequências literais `\\n` na linha de validação do repositório. O commit `47b4cadf` substituiu os escapes literais por quebras de linha reais. A CI subsequente deve validar o reparo. Não há evidência nova de durabilidade REF ou integração PREPARE→TRAIN e o merge permanece bloqueado pelos critérios científicos e revisão final do PO.
