# DT-05 — snapshots incrementais de replay do Linkage no NAS

**Estado:** fundação Parquet e ledger semântico V1 implementados na branch do PR #520; Runner invoca a procedure do ledger dentro da transação de publicação. **Não concluir nem mesclar** até aplicar a migration antes do deploy do Runner, integrar export consistente/manifesto ao SQL e validar regressão e replay ponta a ponta.

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

**Reutilizar o mount NAS existente da Bronze** em cada nó autorizado, com subdiretório e ACL próprios para snapshots. Validar que a raiz de snapshots não coincide com a raiz da Bronze nem com o prefixo dos ZIPs; o processo de expurgo Bronze não deve atravessá-la. Exigir renomeação atômica dentro do mesmo mount, espaço livre monitorado e proteção contra alterações após publicação. Se NAS indisponível, suspender captura/publicação dependente de snapshot e emitir diagnóstico; não fazer fallback silencioso para disco efêmero. Backup externo e retenção dependem de decisão institucional (#379).

## Migração segura

Fase A: captura e replay dos insumos, com `linkage_resultado` atual preservado. Fase B: ledger semântico V1 (`20260927_Linkage_Transicao_Semantica_DT05.sql`) e chamada transacional no Runner introduzidos; testes SQL de três ondas e revisão da assinatura ainda obrigatórios. Fase C: migrar consumidores SQL/C#/BI e gate de completude antes de considerar redução de persistência bruta. Não eliminar resultados por run antecipadamente. Rollback da fase A: desabilitar captura por configuração e manter o contrato SQL atual; manifestos já publicados permanecem imutáveis.

## Estado do PR #520 e dependências de implantação

A procedure `identidade.sp_registrar_transicoes_linkage_run` deve ser instalada **antes** da versão do Runner que a chama; ausência provoca rollback da publicação. O ledger é separado do ledger institucional `auditoria.decisao_identidade_evento`, que não deve ser duplicado. A assinatura V1 cobre campos discretos e versões disponíveis no resultado publicado; revisão da evidência relevante (incluindo score e universo) depende de definição e ensaio antes do aceite. A captura Parquet existente ainda opera por export NDJSON congelado: a integração automática com a janela exclusiva do corpus e a referência SQL ao manifesto estão pendentes. Não habilitar produção apenas com este PR.

## Revisão: snapshots referenciais à Bronze (proposta otimizada)

A Bronze já preserva ZIPs imutáveis content-addressed e o teste Bronze_Replay_Invariant demonstra reconstrução de projeções factuais, desde que contratos/configurações versionados e ledger canônico de identidade sejam preservados. **Não duplicar esses ZIPs em Parquet.** Para cada run, guardar manifesto com referências às entregas e seus SHA-256, versões de parser/normalização, modelo/scorer/ruleset, política de publicação, corte do universo e hash do estado de governança. O manifesto não inclui PII em texto aberto.

Persistir Parquet/ZSTD apenas para **estado complementar não reconstituível** da Bronze: projeções históricas afetadas por correções, mapeamento exato de observações/candidatos se não determinístico e evidências de governança relevantes. Reutilizar partições complementares inalteradas por hash. Não persistir novamente scores reproduzíveis nem cópias integrais de Silver/Gold.

**Proteção de retenção implementada, com aceite de concorrência ainda pendente:** o mantenedor Bronze e o worker de retenção consultam pins DT-05 sob o lock `Jornada.Bronze.Object.<sha256>`, impedindo expurgo de ZIP referenciado. O vínculo automático entre manifesto NAS e pins SQL, e os testes de concorrência/recuperação, continuam pendentes. Não confundir chave SQL preservada com bytes ainda disponíveis. Medir o custo marginal de retenção dos ZIPs, além dos bytes de Parquet; caso contrário, a economia será superestimada.

**Limite:** Bronze + parser histórico reconstrói projeções factuais, mas não por si só a decisão histórica: preservar universo efetivo de candidatos, versão executável do scorer e ledger de identidade/governança. O utilitário referencial Bronze dos PRs #520/#522 implementa manifesto offline com SHA-256 e proteção SQL de retenção; o utilitário Parquet permanece complementar/opcional, sem integração automática ao Runner. A revisão é uma alteração do desenho-alvo, não declaração de funcionalidade pronta.

## Decisão de custo e desempenho — replay histórico raro

Adotar snapshots referenciais à Bronze e aceitar replay histórico mais lento. Não construir cache permanente de projeções nem duplicar ZIPs Bronze em Parquet para acelerar operação excepcional. Manter apenas Parquets complementares para estado não reconstituível. O Linkage cotidiano continua sobre projeções operacionais; replay sob demanda pode reler/descomprimir ZIPs e reexecutar transformações históricas. A aceitação de latência não dispensa integridade, versões executáveis, universo de candidatos, ledger de identidade/governança e proteção contra expurgo dos ZIPs referenciados. Não habilitar modo referencial antes de validar retenção no GC Bronze. Medir bytes complementares e tempo de replay em DEV.

## Implementação referencial na branch — 26/09/2026

Incluídos `dt05_bronze_manifest.py` (manifesto create-only, sem copiar ZIP, verificação SHA-256 em streaming), `20260927_Linkage_Bronze_Pins_DT05.sql` (pins por run e procedure de fixação com o mesmo applock Bronze) e proteção nos dois caminhos de remoção física: GC de órfãos e retenção de Entregas. A retenção não seleciona entregas com pins; revalida sob lock antes de expirar. O GC verifica pins sob lock antes de remover objeto. **Aplicar a migration de pins antes de implantar ambos os workers alterados**; sem tabela a consulta falha e a remoção é bloqueada.

**Limitação ainda aberta após o PR #522:** o Runner cria pins SQL para todo o corpus Silver visível até o high watermark quando `LinkageReplay:CaptureBronzeSources=true`, mas não gera automaticamente o manifesto referencial imutável no NAS nem registra seu hash e cadeia no SQL. O utilitário offline exige que o orquestrador já tenha chamado `sp_fixar_bronze_para_linkage` para cada objeto; verificação física do ZIP não substitui prova transacional do pin. A implementação não deve ser habilitada como replay auditável até vincular o manifesto ao run SQL, congelar versões/estado histórico e concluir os testes SQL/GC de concorrência e três ondas. A decisão de aceitar replay lento elimina cache permanente, **não** esses gates.

## Otimização append-only implementada no utilitário

`dt05_bronze_manifest.py create --parent-manifest <manifesto-anterior>` aceita lista cumulativa de ZIPs, verifica integralmente a cadeia ancestral e grava no novo manifesto **somente** referências inéditas; o pai é fixado por caminho relativo ao NAS e SHA-256 do manifesto. `verify` verifica a cadeia e os hashes dos ZIPs; não há cópia dos bytes Bronze. Teste sintético cobre duas ondas, ausência de duplicação e adulteração do pai. **Importante:** esta otimização continua offline; a captura opt-in do Runner fixa pins SQL para seu corpus, mas ainda não publica/vincula a cadeia de manifestos no NAS. A retenção atual mantém pins até política explícita de liberação; não eliminar pins de pais enquanto existirem descendentes auditáveis.


## Implementação adicional: captura de proveniência SQL (PR pós-#521)

A migração `20260927_Linkage_Bronze_Captura_DT05.sql` introduz `identidade.sp_capturar_fontes_bronze_linkage` e `identidade.linkage_bronze_captura`. Com `LinkageReplay:CaptureBronzeSources=true`, o Runner invoca a procedure depois de materializar o universo e antes do score, sob o lease exclusivo do corpus. A captura inclui **todas** as observações Silver visíveis até o high watermark, não somente os itens reavaliados, pois o blocking consulta candidatos preexistentes. Agrupa por SHA-256 e fixa uma referência por objeto; falha fechada se a origem Bronze estiver ausente/indisponível. A ingestão append-only pode continuar: novos ZIPs sem projeção Silver não alteram o corte. A configuração permanece desligada por padrão enquanto não houver manifesto imutável vinculado ao SQL, congelamento das versões executáveis e teste de replay histórico completo. Esta etapa entrega captura/pins de proveniência, **não** DT-05 integral nem autorização de produção.


## Status reconciliado após merge do PR #522 (`de851c44`)

**Entrega confirmada:** Runner invoca a captura SQL de proveniência Bronze antes da pontuação, sob lease exclusivo do corpus, para **todas** as observações Silver visíveis até o high watermark (inclusive fontes de candidatos não reavaliados). A procedure deduplica por SHA-256, exige fonte disponível e usa `sp_fixar_bronze_para_linkage`; a proteção contra remoção física já existe nos dois workers. O ledger semântico V1 é registrado na transação de publicação e `linkage_resultado` permanece preservado. O utilitário NAS cria/verifica manifestos referenciais append-only offline. O CI do PR #522 concluiu com seis workflows verdes, mas esses workflows **não são** evidência do ensaio histórico completo. A opção `LinkageReplay:CaptureBronzeSources` permanece `false` por padrão.

**Marco A — critério estreito, prioridade imediata:** congelar campos e equivalência da assinatura semântica V1; executar ensaio SQL de três ondas e CPF tardio com referências novas, transição inicial/alteração/ausência de alteração, reexecução idempotente e preservação de resultado bruto. Documentar entradas, transições esperadas, evidência observada e decisão de aceite. Não condicionar esse aceite à construção de Parquet ou replay histórico amplo.

**Marco B — replay histórico completo, escopo ampliado e aceite independente:** vincular transacionalmente ao `linkage_run_id` o manifesto NAS imutável e seu SHA-256, capturar versões executáveis de parser/normalização/scorer/ruleset, universo efetivo de candidatos e estado de governança/intervenções; verificar cadeia e bytes Bronze, concorrência GC/retenção, falhas e recuperação, replay determinístico e custo/latência em DEV. Nenhum gate do Marco B está implicitamente aprovado pela captura SQL do PR #522. O cache permanente permanece fora do desenho-alvo, dado o replay raro.

**Regra documental:** atualizar a linha DT-05 em `Dividas_Tecnicas.md` e esta seção no mesmo PR de qualquer alteração de implementação/aceite DT-05. Registrar o commit e a evidência de CI, separando explicitamente implementado, testado e pendente; não chamar o marco B de concluído com base no marco A.
