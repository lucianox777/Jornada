# DT-05 — snapshots incrementais de replay do Linkage no NAS

**Estado reconciliado em 28/09/2026:** PRs #520/#521/#522 já integrados; assinatura V1 e ledger append-only operacionais no Runner, com guardas 51940/51941, incluindo rejeição de run existente em PREPARANDO. O **aceite estreito** foi concluído no [PR #540](https://github.com/lucianox777/Jornada/pull/540): E2E HTTP/Processor/Runner com três ondas e CPF tardio real em `JornadaSyntheticDev`, [CI #36301124197](https://github.com/lucianox777/Jornada/actions/runs/36301124197). A [última regressão SQL disponível #36348920753](https://github.com/lucianox777/Jornada/actions/runs/36348920753) permanece verde. **DT-05 global ainda PARCIAL:** o vínculo SQL create-once do manifesto NAS imutável já foi integrado no PR #700; não habilitar replay histórico automático até congelar versões executáveis e o universo efetivo de candidatos/governança, concluir testes de reconstrução/concorrência/GC/recuperação e medir custo/latência. Os trechos cronológicos abaixo preservam o histórico da implementação, não bloqueiam retroativamente os PRs já mesclados.

## Decisão

Como desenho-alvo, referenciar os ZIPs Bronze já imutáveis e armazenar em **Parquet comprimido com ZSTD somente os deltas históricos não reconstituíveis**, endereçados pelo SHA-256 dos bytes. Cada execução do Linkage referencia um manifesto imutável com o conjunto exato de partições e versões dos insumos. Reutilizar partições não alteradas (copy-on-write lógico), sem duplicar um snapshot integral por execução. O manifesto não deve conter CPF, nomes nem atributos pessoais em texto aberto.

**Reutilizar o NAS já empregado pela camada Bronze**, configurado por `BronzeStorage:RootPath` e provider `FileSystem`. Reservar o prefixo `linkage-snapshots/v1/` **fora** do prefixo de objetos ZIP `sha256/ab/cd/<hash>.zip`; o worker de retenção Bronze não pode varrer esse prefixo. O NAS é armazenamento de replay, **não** banco de decisão nem backup único. Usar permissões de serviço mínimas, criptografia do volume, cópia independente conforme política de continuidade, verificação periódica de hash, controle de espaço e retenção governada (#379). Não versionar Parquets com dados pessoais no Git. A raiz padrão é `BronzeStorage:RootPath/linkage-snapshots/v1` no **mesmo volume já montado**. Aceitar `JORNADA_LINKAGE_SNAPSHOT_ROOT` somente como override explícito quando a implantação exigir. Não duplicar configuração de mount, não colocar Parquet no namespace de ZIP Bronze nem conceder ao worker de expurgo Bronze permissão de exclusão sobre snapshots.

## Fronteira transacional

1. Congelar o universo efetivamente visível à execução: observações, referências candidatas, dados de comparação, vínculos protegidos relevantes, regras de blocking, scorer, parâmetros e política de publicação. Um timestamp isolado **não** prova o universo observado nem preserva correções retroativas.
2. Materializar partições Parquet e manifesto em área temporária no NAS. Validar hashes e contagens; publicar arquivos imutáveis e o manifesto por renomeação atômica **no mesmo filesystem**. A referência ao manifesto só pode ser persistida no SQL após a publicação física.
3. Vincular no SQL o identificador da execução ao hash e caminho lógico do manifesto, com estado de captura e reconciliação. Falha no NAS deve impedir a promoção de uma execução que dependa de replay, sem produzir decisão operacional parcialmente publicada. Objetos órfãos podem ser coletados apenas após janela de segurança.
4. Preservar `identidade.linkage_resultado` por observação/run e os gates atuais até a migração de seus consumidores. Separar transições semânticas operacionais em ledger append-only; assinatura versionada deve incluir decisão publicada, status/motivo, alternativas/conflitos, evidência relevante e versão efetiva do scorer/ruleset. Mudança numérica irrelevante do score não deve criar transição.
5. `MODEL_VALIDATION` pode produzir evidência de execução, mas não transição operacional. `initial_uuid` é linhagem, não feature do score. Vínculos protegidos e intervenção de governança não podem ser sobrescritos pelo replay.

## Contrato mínimo do manifesto

`schema_version`, `run_id`, `captured_at_utc`, `source_watermark`, `scorer_version`, `ruleset_version`, `model_version`, `publication_policy_version`, `snapshot_schema_version`, `partitions[]` (caminho relativo, SHA-256, bytes, linhas), `row_count`, `partition_set_sha256`. O `run_id` identifica o manifesto; os arquivos de dados podem ser compartilhados entre execuções. Não usar data de modificação de arquivo como identidade.

## Replay e verificação

Verificar todos os SHA-256, contagens e versões antes de executar o scorer histórico. Restaurar o **mesmo conjunto de candidatos** e as mesmas regras/versões, não apenas os dados com vigência temporal equivalente. Conferir resultado bruto, decisão publicada e ledger separadamente. Registrar incompatibilidade do runtime ou insumo ausente como falha de replay, jamais recalcular silenciosamente com versão atual.

## Medição e aceite

Medir em `JornadaSyntheticDev` snapshot inicial, bytes de partições alteradas por execução, bytes físicos únicos, tempo de captura e replay, número de observações reavaliadas e novas transições semânticas. Ensaio obrigatório de três ondas: reprocessamento idêntico, referência nova afetando origem inalterada, CPF tardio, homônimos, conflito, replay idempotente, concorrência e rollback. Meta de planejamento de 50 GB/ano é **hipótese**, não limite nem medição; validar 50–150 GB/ano com dados representativos. Não declarar ganho de recall sem #31.

## Implantação NAS

**Reutilizar o mount NAS existente da Bronze** em cada nó autorizado, com subdiretório e ACL próprios para snapshots. Reutilizar a raiz/mount durável da Bronze, mas manter snapshots sob prefixo lógico dedicado (`linkage-snapshots/v1/`), separado do prefixo content-addressed dos ZIPs (`sha256/`); o processo de expurgo Bronze não deve atravessar o prefixo de snapshots. Exigir renomeação atômica dentro do mesmo mount, espaço livre monitorado e proteção contra alterações após publicação. Se NAS indisponível, suspender captura/publicação dependente de snapshot e emitir diagnóstico; não fazer fallback silencioso para disco efêmero. Backup externo e retenção dependem de decisão institucional (#379).

## Migração segura

Fase A: captura e replay dos insumos, com `linkage_resultado` atual preservado. Fase B: ledger semântico V1 (`20260927_Linkage_Transicao_Semantica_DT05.sql`) e chamada transacional no Runner introduzidos; testes SQL de três ondas e revisão da assinatura ainda obrigatórios. Fase C: migrar consumidores SQL/C#/BI e gate de completude antes de considerar redução de persistência bruta. Não eliminar resultados por run antecipadamente. Rollback da fase A: desabilitar captura por configuração e manter o contrato SQL atual; manifestos já publicados permanecem imutáveis.

## Histórico do PR #520 e dependências de implantação (PRs iniciais já integrados)

A procedure `identidade.sp_registrar_transicoes_linkage_run` deve ser instalada **antes** da versão do Runner que a chama; ausência provoca rollback da publicação. O ledger é separado do ledger institucional `auditoria.decisao_identidade_evento`, que não deve ser duplicado. A assinatura V1 cobre campos discretos e versões disponíveis no resultado publicado; revisão da evidência relevante (incluindo score e universo) depende de definição e ensaio antes do aceite. **Registro histórico da fase inicial:** a captura Parquet operava por export NDJSON congelado e, naquele corte, a integração automática e a referência SQL ao manifesto ainda estavam pendentes. O desenho corrente reutiliza Bronze referencial; o PR #700 integrou o binding SQL e a PR #702 candidata integra a orquestração automática antes do score. Isso ainda não autoriza produção.

## Revisão: snapshots referenciais à Bronze (proposta otimizada)

A Bronze já preserva ZIPs imutáveis content-addressed e o teste Bronze_Replay_Invariant demonstra reconstrução de projeções factuais, desde que contratos/configurações versionados e ledger canônico de identidade sejam preservados. **Não duplicar esses ZIPs em Parquet.** Para cada run, guardar manifesto com referências às entregas e seus SHA-256, versões de parser/normalização, modelo/scorer/ruleset, política de publicação, corte do universo e hash do estado de governança. O manifesto não inclui PII em texto aberto.

Persistir Parquet/ZSTD apenas para **estado complementar não reconstituível** da Bronze: projeções históricas afetadas por correções, mapeamento exato de observações/candidatos se não determinístico e evidências de governança relevantes. Reutilizar partições complementares inalteradas por hash. Não persistir novamente scores reproduzíveis nem cópias integrais de Silver/Gold.

**Proteção de retenção implementada, com aceite de concorrência ainda pendente:** o mantenedor Bronze e o worker de retenção consultam pins DT-05 sob o lock `Jornada.Bronze.Object.<sha256>`, impedindo expurgo de ZIP referenciado. O vínculo SQL create-once foi integrado no PR #700 e a PR #702 candidata automatiza publicação/verificação + binding antes do score; os testes de concorrência/recuperação continuam pendentes. Não confundir chave SQL preservada com bytes ainda disponíveis. Medir o custo marginal de retenção dos ZIPs, além dos bytes de Parquet; caso contrário, a economia será superestimada.

**Limite:** Bronze + parser histórico reconstrói projeções factuais, mas não por si só a decisão histórica: preservar universo efetivo de candidatos, versão executável do scorer e ledger de identidade/governança. O utilitário referencial Bronze dos PRs #520/#522 permanece como ferramenta offline de criação/verificação. A PR #702 candidata adiciona ao Runner a publicação/verificação automática do manifesto referencial antes do score; o utilitário Parquet permanece complementar/opcional. A revisão é uma alteração do desenho-alvo, não declaração de funcionalidade pronta.

## Decisão de custo e desempenho — replay histórico raro

Adotar snapshots referenciais à Bronze e aceitar replay histórico mais lento. Não construir cache permanente de projeções nem duplicar ZIPs Bronze em Parquet para acelerar operação excepcional. Manter apenas Parquets complementares para estado não reconstituível. O Linkage cotidiano continua sobre projeções operacionais; replay sob demanda pode reler/descomprimir ZIPs e reexecutar transformações históricas. A aceitação de latência não dispensa integridade, versões executáveis, universo de candidatos, ledger de identidade/governança e proteção contra expurgo dos ZIPs referenciados. Não habilitar modo referencial antes de validar retenção no GC Bronze. Medir bytes complementares e tempo de replay em DEV.

## Implementação referencial, já integrada — 26/09/2026

Incluídos `dt05_bronze_manifest.py` (manifesto create-only, sem copiar ZIP, verificação SHA-256 em streaming), `20260927_Linkage_Bronze_Pins_DT05.sql` (pins por run e procedure de fixação com o mesmo applock Bronze) e proteção nos dois caminhos de remoção física: GC de órfãos e retenção de Entregas. A retenção não seleciona entregas com pins; revalida sob lock antes de expirar. O GC verifica pins sob lock antes de remover objeto. **Aplicar a migration de pins antes de implantar ambos os workers alterados**; sem tabela a consulta falha e a remoção é bloqueada.

**Limitação histórica após o PR #522:** naquele corte, o Runner criava pins SQL para todo o corpus Silver visível até o high watermark quando `LinkageReplay:CaptureBronzeSources=true`, mas ainda não gerava automaticamente o manifesto referencial imutável nem registrava seu binding no SQL. O PR #700 integrou o binding create-once e a PR #702 candidata adiciona a publicação/verificação automática antes do score. O utilitário offline exige que o orquestrador já tenha chamado `sp_fixar_bronze_para_linkage` para cada objeto; verificação física do ZIP não substitui prova transacional do pin. O vínculo imutável do manifesto ao run SQL foi integrado no PR #700. A implementação ainda não deve ser habilitada como replay auditável até congelar as versões executáveis e o estado histórico necessários à reconstrução, concluir os ensaios de concorrência GC/retenção e recuperação e comprovar o replay histórico determinístico; o E2E de três ondas do Marco A permanece evidência estreita, não aceite do Marco B. A decisão de aceitar replay lento elimina cache permanente, **não** esses gates.

## Otimização append-only implementada no utilitário

`dt05_bronze_manifest.py create --parent-manifest <manifesto-anterior>` aceita lista cumulativa de ZIPs, verifica integralmente a cadeia ancestral e grava no novo manifesto **somente** referências inéditas; o pai é fixado por caminho relativo ao NAS e SHA-256 do manifesto. `verify` verifica a cadeia e os hashes dos ZIPs; não há cópia dos bytes Bronze. Teste sintético cobre duas ondas, ausência de duplicação e adulteração do pai. **Importante:** a cadeia incremental com `parent-manifest` continua sendo capacidade do utilitário offline. A PR #702 candidata publica e vincula automaticamente um manifesto por run, mas não implementa ainda a cadeia incremental de pais no Runner. A retenção atual mantém pins até política explícita de liberação; não eliminar pins de pais enquanto existirem descendentes auditáveis.


## Implementação adicional: captura de proveniência SQL (PR pós-#521)

A migração `20260927_Linkage_Bronze_Captura_DT05.sql` introduz `identidade.sp_capturar_fontes_bronze_linkage` e `identidade.linkage_bronze_captura`. Com `LinkageReplay:CaptureBronzeSources=true`, o Runner invoca a procedure depois de materializar o universo e antes do score, sob o lease exclusivo do corpus. A captura inclui **todas** as observações Silver visíveis até o high watermark, não somente os itens reavaliados, pois o blocking consulta candidatos preexistentes. Agrupa por SHA-256 e fixa uma referência por objeto; falha fechada se a origem Bronze estiver ausente/indisponível. A ingestão append-only pode continuar: novos ZIPs sem projeção Silver não alteram o corte. A configuração permanece desligada por padrão: o binding SQL já existe no PR #700 e a PR #702 candidata automatiza o manifesto, mas ainda faltam congelamento executável completo e teste de replay histórico completo. Esta etapa entrega captura/pins de proveniência, **não** DT-05 integral nem autorização de produção.


## Incremento Marco B — vínculo SQL do manifesto (02/10/2026)

A migration `20261002_Linkage_Replay_Manifest_Binding_DT05.sql` introduz o registro create-once `identidade.linkage_replay_manifesto` e a procedure `identidade.sp_registrar_manifesto_replay_linkage`. O registro deve ocorrer em transação explícita **somente depois** de o manifesto ter sido publicado/verificado no NAS. A procedure exige captura Bronze prévia, igualdade entre a contagem capturada e os pins do run, caminho sob `linkage-snapshots/v1/manifests/`, SHA-256 válidos e versões históricas exatas; um run não pode substituir seu manifesto já registrado. Este incremento fecha apenas o vínculo SQL imutável do Marco B. Na PR #702 candidata, a geração/verificação automática pelo Runner passa a existir; congelamento completo de runtime/governança, concorrência GC/retenção, recuperação, replay determinístico e medição DEV continuam pendentes; `LinkageReplay:CaptureBronzeSources` permanece desligado por padrão.

## Status reconciliado após merge do PR #522 (`de851c44`)

**Atualização em 28/09:** o Marco A definido abaixo **já foi concluído** pelo E2E do PR #540; suas exigências abaixo são o contrato histórico, não tarefas ainda por realizar. O Marco B permanecia pendente integralmente naquele corte histórico; desde 02/10/2026, o PR #700 entregou o vínculo SQL imutável create-once descrito acima, e os demais critérios do Marco B continuam pendentes. A regressão PREPARANDO encontra-se no teste SQL atual `Dt05PublicationGuardsSqlServerTests` e foi exercitada pela CI do PR #568.

**Entrega confirmada:** Runner invoca a captura SQL de proveniência Bronze antes da pontuação, sob lease exclusivo do corpus, para **todas** as observações Silver visíveis até o high watermark (inclusive fontes de candidatos não reavaliados). A procedure deduplica por SHA-256, exige fonte disponível e usa `sp_fixar_bronze_para_linkage`; a proteção contra remoção física já existe nos dois workers. O ledger semântico V1 é registrado na transação de publicação e `linkage_resultado` permanece preservado. O utilitário NAS cria/verifica manifestos referenciais append-only offline. O CI do PR #522 concluiu com seis workflows verdes, mas esses workflows **não são** evidência do ensaio histórico completo. A opção `LinkageReplay:CaptureBronzeSources` permanece `false` por padrão.

**Marco A — critério estreito, CONCLUÍDO e mantido como contrato de regressão:** congelar campos e equivalência da assinatura semântica V1; executar ensaio SQL de três ondas e CPF tardio com referências novas, transição inicial/alteração/ausência de alteração, reexecução idempotente e preservação de resultado bruto. Documentar entradas, transições esperadas, evidência observada e decisão de aceite. Não condicionar esse aceite à construção de Parquet ou replay histórico amplo.

**Marco B — replay histórico completo, escopo ampliado e aceite independente:** o subcritério de vínculo transacional create-once do manifesto NAS imutável e seu SHA-256 ao `linkage_run_id` foi integrado no PR #700. Permanecem como critérios do aceite completo: capturar versões executáveis suficientes de parser/normalização/scorer/ruleset, universo efetivo de candidatos e estado de governança/intervenções; verificar cadeia e bytes Bronze; ensaiar concorrência GC/retenção, falhas e recuperação; comprovar replay determinístico e custo/latência em DEV. A captura SQL do PR #522 e o binding do PR #700 aprovam somente seus respectivos subcritérios; não implicam aceite do Marco B completo. O cache permanente permanece fora do desenho-alvo, dado o replay raro.

## Incremento Marco B — orquestração automática antes do score (branch de implementação)

O Runner passa a impor, quando `LinkageReplay:CaptureBronzeSources=true`, a ordem **materializar universo → capturar pins → construir/verificar manifesto → registrar binding SQL em transação → score**. O manifesto referencial é publicado create-only sob `linkage-snapshots/v1/manifests/`, reabre e recalcula o SHA-256 de cada ZIP Bronze pinado, calcula separadamente `bronze_set_sha256` e usa JSON canônico compatível com o utilitário Python. O `input_snapshot_id` V1 identifica deterministicamente o universo lógico pelo high-watermark e IDs ordenados de `linkage_run_item`; não é confundido com o conjunto físico Bronze. O binding usa a procedure do PR #700 em transação SQL curta somente após a publicação física; qualquer falha impede o início do score.

Este incremento permanece **opt-in e desligado por padrão**. Ele não conclui o Marco B: parser/normalização e estado completo de governança/candidatos ainda precisam de congelamento executável suficiente para replay histórico, além dos ensaios de concorrência GC/retenção, recuperação, reconstrução determinística e custo/latência DEV. Modelos sem ruleset versionado falham fechado no modo de captura, em vez de receber versão sintética.

**Regra documental:** atualizar a linha DT-05 em `Dividas_Tecnicas.md` e esta seção no mesmo PR de qualquer alteração de implementação/aceite DT-05. Registrar o commit e a evidência de CI, separando explicitamente implementado, testado e pendente; não chamar o marco B de concluído com base no marco A.


## Ensaio SQL do critério estreito — três ondas (PR de regressão pós-#523)

O teste `Dt05SemanticThreeWavesSqlServerTests` cria três runs isolados em transação SERIALIZABLE e executa duas vezes por run a procedure real de ledger. Compara a cadeia de assinaturas: primeira onda `INICIAL`, segunda onda com evidência idêntica sem novo evento, terceira onda com marcador `CPF_TARDIO_EVIDENCIA_CONFIRMADA` e `ALTERACAO_SEMANTICA`; confere o SHA anterior e preservação dos três `linkage_resultado` brutos. **Limitação expressa:** a terceira onda simula a mudança de evidência de CPF no resultado de publicação; ainda não cria uma nova observação Silver pela ingestão real, não roda o scorer nem prova CPF tardio ponta a ponta. Não confundir teste do ledger com aceite final do critério estreito. A assinatura V1 deve ser congelada em contrato normativo antes de declarar o marco A encerrado. Este PR não ativa `LinkageReplay:CaptureBronzeSources`.


## Contrato normativo da assinatura V1

O contrato exato dos 12 campos, ordem, conversão SQL, sentinela de nulos, exclusões e idempotência está em [DT05_Assinatura_Semantica_V1.md](DT05_Assinatura_Semantica_V1.md). Um teste unitário verifica a correspondência da implementação SQL com esse contrato para evitar mudanças silenciosas. O PR #524 passou a regressão SQL de três ondas, que isoladamente não provava ingestão de CPF tardio real; **essa prova adicional foi produzida no E2E do PR #540**. A assinatura V1 não inclui CPF bruto: a chegada de CPF só gera transição se alterar algum campo de publicação assinado. O replay histórico completo permanece um aceite independente.


## Regressão adicional — alteração exclusivamente numérica

O teste SQL de ondas foi ampliado com uma quarta execução: após a transição semântica da terceira onda, somente `score_melhor` muda, preservando todos os 12 campos da assinatura V1. A expectativa é **nenhuma transição adicional**, mesmo após retry duplo, com os quatro resultados brutos preservados. Esse teste não comprova ingestão real de CPF nem replay histórico; seu resultado depende da execução do CI do PR correspondente.


## Regressão de guardas da publicação — PR posterior ao #528

O teste de integração `Dt05PublicationGuardsSqlServerTests` exercita a procedure real sem transação (erro SQL `51940`) e com run inexistente dentro de transação `SERIALIZABLE` (erro `51941`). Não gera resultados operacionais; a transação de teste é revertida. A regressão exige SQL Server e `JORNADA_TEST_SQL_CONNECTION`; sua execução no CI deve ser verificada antes do merge. Esta guarda não substitui o ensaio Bronze → Silver → Runner de CPF tardio nem o replay histórico do Marco B.


## Guarda adicional — run existente em PREPARANDO

A regressão de publicação passa a criar, em transação isolada, um run real em `PREPARANDO` e exigir erro `51941` da procedure de ledger, além dos cenários sem transação e run inexistente já integrados no PR #529. Isso distingue a validação de estado de uma mera validação de existência. O teste depende de SQL Server configurado no CI, passou na última regressão e não substitui o ensaio de CPF tardio real (já comprovado no PR #540) nem o replay NAS (ainda pendente).


## Gate operacional incremental — Runner real

`scripts/dt05-runner-e2e.ps1` é um smoke gate independente para um banco DEV isolado já preparado com modelo ATIVO, carga inicial desativada, Silver e referências disponíveis. Requer `ConnectionString`, `SqlcmdServer`, `SqlcmdDatabase`, `SqlcmdUser`, `SQLCMDPASSWORD` no ambiente e `sqlcmd` no PATH. Executa `Jornada.Linkage.Runner --mode ON_DEMAND --publish true`, identifica o run por marcador único e exige `PUBLICADO` e resultados brutos. Não redefine nem apaga o banco; **não é** ensaio de CPF tardio, ledger de múltiplas ondas ou replay NAS. CI sem SQL Server não comprova sua execução.


## Integração opcional do Runner ao E2E de ingestão

`pwsh ./scripts/local-e2e.ps1 -VerifyLinkageRunner` mantém o banco isolado padrão `JornadaE2E`, executa duas entregas reais via API/Processor e depois chama o Runner real com marcador único. Exige modelo ATIVO e carga inicial desativada, verifica run `PUBLICADO` e ao menos um `linkage_resultado` bruto. Recusa `-AllowSharedDatabaseReset` combinado com o novo gate. Não há alteração da fixture para CPF tardio neste PR: o ensaio de múltiplas ondas com nova observação Silver, mudança de CPF e verificação de ledger continua pendente. O estágio opt-in só está comprovado quando efetivamente executado com SQL Server, não apenas quando o CI padrão passa.


**Correção de segurança e evidência do gate local (pós-#535):** `-VerifyLinkageRunner` combinado com `-AllowSharedDatabaseReset` agora falha no preflight, antes do reset. A evidência `.local/e2e/evidence.json` inclui `linkageRunner` (marcador do run, contagem de runs PUBLICADO e de resultados brutos) quando o estágio é solicitado; caso contrário, `null`. A execução opt-in com SQL Server e o ensaio de CPF tardio real tiveram comprovação no PR #540; os testes de replay histórico NAS seguem pendentes de comprovação.


### Incremento Marco B — orquestração do manifesto pelo Runner (02/10/2026)

O incremento em `feat/dt05-runner-replay-manifest` fecha a ordem fail-closed anterior ao score, ainda sob `LinkageReplay:CaptureBronzeSources=false` por padrão:

`materializar universo → capturar pins → construir/verificar manifesto → registrar binding SQL em transação → score`.

O `input_snapshot_id` passa a identificar o universo lógico congelado por algoritmo versionado `dt05-input-v1`: SHA-256 sobre o high-watermark e a sequência ordenada de `pessoa_observacao_id` de `linkage_run_item`. Ele não substitui `bronze_set_sha256`, que identifica separadamente o conjunto físico de fontes. O Runner reabre cada ZIP pinado, recalcula SHA-256 e tamanho antes de aceitar o objeto, gera JSON canônico com chaves ordenadas recursivamente (compatível com o utilitário Python), publica o manifesto create-only em `linkage-snapshots/v1/manifests/` e somente então chama `sp_registrar_manifesto_replay_linkage` em transação SQL curta. Modelo legado sem `RuleSetVersion` falha fechado nesse modo.

Este incremento **não declara replay histórico completo**. Permanecem fora do aceite: congelamento das versões executáveis adicionais de parser/normalização e do estado de candidatos/governança necessário à reconstrução, concorrência de retenção/GC e recuperação, reconstrução histórica determinística e medição de custo/latência. A flag continua desligada por padrão até esses gates.


## Incremento Marco B — contratos executáveis no manifesto v2 (02/10/2026)

Após a integração da PR #702, o Runner já publica/verifica e vincula o manifesto antes do score quando a captura DT-05 opt-in está habilitada. Este incremento evolui novos manifestos para `schema_version=2` e congela adicionalmente identidades executáveis já canônicas no runtime: `IdentityComparison.NormalizationVersion`, `PersonResolutionContractCatalog.CatalogVersion` e o par `PersonResolutionProjectionContract.SchemaVersion/FingerprintSha256` efetivamente vinculado ao modelo. O SQL preserva leitura dos bindings v1 legados e exige os quatro campos adicionais para novos bindings v2.

O Runner falha fechado antes do score se o modelo não trouxer identidade exata da projeção ou se ela não for suportada pelo runtime atual. A flag `LinkageReplay:CaptureBronzeSources` continua desligada por padrão. Esta fatia reduz a lacuna de congelamento executável, mas **não conclui o Marco B**: ainda permanecem o estado histórico efetivo de candidatos/governança/intervenções, ensaios de concorrência GC/retenção e recuperação, replay histórico determinístico e medição de custo/latência em DEV.


## Incremento Marco B — identidade do estado candidato/governança (02/10/2026)

Este incremento captura, create-once por run e antes do manifesto/score, uma identidade auditável do estado operacional observado: contagem e SHA-256 determinístico dos UUIDs de `gold.pessoa` em `estado_identidade='REFERENCIA'`, mais o high-watermark de `auditoria.decisao_identidade_evento`. A captura ocorre em transação `Serializable` e o registro rejeita UPDATE/DELETE. Ela permite detectar que o universo de referências ou o ledger humano mudou entre execução e futura reconstrução.

**Limite deliberado:** esta etapa ainda não materializa os atributos/candidate sets por observação nem faz o scorer consumir uma cópia histórica congelada durante todos os batches. Portanto reduz a lacuna de governança, mas não fecha o critério de universo efetivo de candidatos nem autoriza replay histórico. Permanecem necessários o consumo determinístico do estado congelado, concorrência GC/retenção/recuperação, replay histórico e medição de custo/latência.


## Incremento Marco B — manifesto v3 vincula identidade de candidatos/governança (02/10/2026)

Após a captura create-once integrada na PR #704, novos manifestos passam a usar `schema_version=3` e carregam, sem PII em texto aberto, `candidatos_referencia`, `candidatos_sha256` e `governanca_evento_high_watermark`. O Runner lê esses valores do registro imutável `identidade.linkage_replay_estado_governanca` depois da captura e antes da publicação física do manifesto. O binding SQL v3 aceita o manifesto somente quando os três valores coincidem exatamente com a captura create-once do mesmo run; v1/v2 permanecem legíveis para histórico.

**Limite deliberado:** o hash/count/high-watermark no manifesto tornam a divergência detectável e vinculam a evidência ao artefato imutável, mas não materializam atributos/candidate sets históricos nem alteram o candidate loader, que ainda consulta a Gold/projeção operacional viva. Portanto este incremento não fecha o universo efetivo de candidatos nem autoriza replay histórico; a próxima fatia deve congelar/consumir o estado não reconstituível necessário, seguida pelos ensaios de retenção/GC/recuperação, reconstrução determinística e medição DEV.


## Incremento Marco B — contrato Parquet do estado candidato (02/10/2026)

O utilitário `tools/dt05_parquet_snapshot.py` evolui o formato para `schema_version=2` e passa a distinguir `snapshot_kind=input-universe` de `snapshot_kind=candidate-state`. Para `candidate-state`, a chave imutável é `candidate_uuid` e cada linha contém exatamente os atributos que o candidate loader operacional lê hoje: `candidate_uuid`, `nome_completo`, `data_nascimento`, `nome_mae` e `estado_identidade=REFERENCIA`. As partições continuam Parquet/ZSTD content-addressed e reutilizáveis por hash lógico; o manifesto contém somente hashes, contagens, caminhos e versões, sem nomes ou outros atributos pessoais em texto aberto. O verificador preserva leitura dos manifestos v1 históricos e valida o contrato kind/key em v2.

**Limite deliberado desta fatia:** ela congela o **formato físico canônico** para o estado candidato não reconstituível, mas o Runner ainda não exporta automaticamente a Gold para esse formato nem o `BlockingProjectionCandidateLoader` consome essas partições. Portanto o Marco B continua parcial. A próxima fatia deve integrar captura/publicação create-only antes do score, vincular o hash do candidate-state ao manifesto SQL e então introduzir leitura histórica fail-closed; somente depois cabem os ensaios de replay determinístico, retenção/GC/recuperação e custo/latência DEV.


## Incremento Marco B — captura automática do candidate-state pelo Runner (02/10/2026)

Com o contrato físico v2 já congelado, o Runner passa a capturar automaticamente o `candidate-state` quando `LinkageReplay:CaptureBronzeSources=true`, depois da captura create-once de governança e antes da publicação do manifesto de replay e de qualquer score. A exportação lê `gold.pessoa` em `estado_identidade='REFERENCIA'` ordenada por UUID sob transação `Serializable`, recalcula a mesma identidade SHA-256 da captura SQL e recusa a publicação se contagem ou hash divergirem. Assim, uma mudança entre o selo de governança e a exportação física falha fechada.

As partições são escritas nativamente pelo Runner em Parquet com ZSTD, publicadas create-only e endereçadas pelo SHA-256 dos bytes sob `linkage-snapshots/v1/objects/`. O manifesto específico de `candidate-state` contém somente versões, hashes, contagens e caminhos; `nome_completo`, `data_nascimento` e `nome_mae` permanecem exclusivamente dentro das partições Parquet no volume protegido. A dependência `Parquet.Net 6.1.0` é fixa e seu grafo deve ser produzido/validado pelo gate `dependency-lock`; lockfiles não são fabricados manualmente.

**Limite deliberado:** esta fatia materializa o estado histórico físico antes do score, mas ainda não vincula `partition_set_sha256`/manifesto de candidate-state ao binding SQL v3 nem altera `BlockingProjectionCandidateLoader` para consumir o snapshot em replay. O Marco B permanece parcial até esses dois incrementos e os ensaios de reconstrução determinística, retenção/GC/recuperação e custo/latência DEV.


## Incremento Marco B — consumo histórico fail-closed do candidate-state (03/10/2026)

O modo `REPLAY` propaga obrigatoriamente `--replay-source-run-id` até o contrato do batch. Antes de materializar/pontuar o novo run, o Runner lê o binding v3 create-once do run de origem, verifica SHA-256 do manifesto candidate-state, partition-set, bytes/linhas/SHA físico e lógico dos Parquets e a identidade do conjunto de candidatos. Somente após essa verificação instala os candidatos congelados no scorer.

Nesse modo, o candidate loader **não consulta `gold.pessoa` e não possui fallback para o estado corrente**. Para modelos legados, o filtro de nascimento é aplicado sobre o candidate-state congelado preservando os passes históricos suportados. Para modelos com ruleset dinâmico, o replay falha fechado enquanto a projeção de blocking congelada não estiver conectada ao consumo histórico; isso evita reconstruir candidate UUIDs usando a projeção SQL corrente.

Este incremento fecha o consumo histórico do estado candidato legado, mas não declara o Marco B completo. Restam conectar a projeção de blocking histórica para rulesets dinâmicos, comprovar replay determinístico ponta a ponta e executar ensaios de concorrência GC/retenção, recuperação e custo/latência DEV.


## Incremento Marco B — projeção de blocking histórica consumível (03/10/2026)

O manifesto de replay evolui para **schema v4** e passa a vincular create-once também o caminho lógico, SHA-256 do manifesto e `partition_set_sha256` da projeção de blocking congelada. O Runner verifica esse binding contra o objeto físico no NAS antes de qualquer score histórico.

Para rulesets dinâmicos, o replay deixa de consultar `identidade.blocking_chave` corrente: os passes são executados em memória sobre a projeção histórica verificada, preservando `INTERSECT` entre cláusulas de um passe, `UNION` entre passes e semântica temporal das features. Os UUIDs resultantes são resolvidos exclusivamente contra o candidate-state histórico já verificado. Ausência, adulteração ou incompatibilidade do binding v4 falha fechado.

Com isso, tanto candidate-state quanto candidate generation dinâmica deixam de depender do estado Gold/projeção corrente durante REPLAY. Permanecem como fechamento do Marco B a prova determinística ponta a ponta, ensaios de GC/retenção/recuperação e medição de custo/latência DEV.
