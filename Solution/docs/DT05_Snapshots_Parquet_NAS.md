# DT-05 — snapshots incrementais de replay do Linkage no NAS

**Estado:** arquitetura aprovada como direção; implementação incremental em andamento. Não declarar DT-05 concluída até integrar Runner, publicação, SQL, testes e medição.

## Decisão

Armazenar no volume NAS arquivos **Parquet comprimidos com ZSTD**, imutáveis, endereçados pelo SHA-256 dos bytes. Cada execução do Linkage referencia um manifesto imutável com o conjunto exato de partições e versões dos insumos. Reutilizar partições não alteradas (copy-on-write lógico), sem duplicar um snapshot integral por execução. O manifesto não deve conter CPF, nomes nem atributos pessoais em texto aberto.

O NAS é armazenamento de replay, **não** banco de decisão nem backup único. Usar permissões de serviço mínimas, criptografia do volume, cópia independente conforme política de continuidade, verificação periódica de hash, controle de espaço e retenção governada (#379). Não versionar Parquets com dados pessoais no Git. O caminho raiz é configuração externa (por exemplo, `JORNADA_LINKAGE_SNAPSHOT_ROOT`); jamais fixar uma montagem NAS no código.

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

Montar volume em cada nó autorizado com caminho configurável e permissões restritas ao serviço. Exigir renomeação atômica dentro do mesmo mount, espaço livre monitorado e proteção contra alterações após publicação. Se NAS indisponível, suspender captura/publicação dependente de snapshot e emitir diagnóstico; não fazer fallback silencioso para disco efêmero. Backup externo e retenção dependem de decisão institucional (#379).

## Migração segura

Fase A: captura e replay dos insumos, com `linkage_resultado` atual preservado. Fase B: ledger semântico e seus testes. Fase C: migrar consumidores SQL/C#/BI e gate de completude antes de considerar redução de persistência bruta. Não eliminar resultados por run antecipadamente. Rollback da fase A: desabilitar captura por configuração e manter o contrato SQL atual; manifestos já publicados permanecem imutáveis.
