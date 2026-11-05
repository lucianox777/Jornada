# V9 — TF congelado no Gold de diagnóstico: preflight de integração (05/11/2026)

**Escopo desta PR:** alteração mínima no construtor de variante Gold **experimental**, sem refit e sem publicação operacional. Não altera os modelos/snapshots históricos, não modifica os limites TF0 × ONE_MF das seeds 42–57, não aciona 30k e não desbloqueia DT-15.

## Estado comprovado pelo código

1. `V9FittedSnapshot.CreateGoldHarnessVariant()` anteriormente recusava quaisquer unidades TF ativas mesmo quando `V9_FITTED_SNAPSHOT_V3` já tinha suporte conjunto e todas as calibrações TF congeladas e validadas. A transformação de Gold altera somente `CandidateGrouping` e `GoldParentSnapshotSha256`, produzindo um **novo** SHA por conteúdo. Agora admite a mesma transformação sem apagar TF ativa.
2. `V9FittedSnapshot.FreezeAndValidate` continua verificando os parâmetros e a referência TF; a chamada recusa fit sem `JointSupport`, com schema incompatível, sem BLIND ou já convertido em GOLD. O contrato de publicação operacional não foi tocado.
3. O teste `Gold_diagnostic_variant_preserves_four_unit_tf_and_original_fit_bytes` constrói uma fixture V3 com TF positiva nas quatro unidades (prenome/sobrenome de pessoa/mãe), compara score/log-odds antes/depois, reconstrói o payload exatamente, verifica o SHA pai e rejeita V2 e conversão duplicada. **Esse teste é de contrato, não é ensaio físico Gold.**
4. `V9GoldRunnerVariantPreparation` e `V9GoldHarnessCommand` continuam exigindo vencedor e artefatos pré-selecionados, SHA de origem correto, escopo TRAIN/holdout autorizado e SQL LocalDB descartável. Nenhum vencedor/threshold/passe é escolhido por esta mudança.

## Separação obrigatória dos modelos já existentes

- O candidato Gold histórico `6d93690dccac2c92dc4d3113cbb9169dd19156c4f9b3d849ededfb4c54ae5f3f` (oito passes, TF0, superior 0,8671875) permanece **imutável**. Há evidência física das três ordens, incluindo falsos vínculos/criações e diferenças por ordem. **Não foi substituído.**
- Os snapshots TF0/ONE_MF da campanha 42–57 pertencem a **outro ensaio** (bloqueio de três passes e margem primária 0,5). Seus ganhos **não** provam que o Gold de oito passes passou a ter TF ativa nem autorizam misturar pesos de um modelo com os passes do outro.
- A migração `20261009_Ref_Calibracao_Inicial_Sintetica.sql` cria `ref.calibracao_inicial_versao` e publicação imutável, mas isso não prova a existência de **uma linha publicada no banco DEV/HML** nem estabelece vínculo de runtime operacional. Não há nesta PR consulta à instância SQL real.

## Próximos gates, sem recalibração automática

1. **Leitura apenas** do banco de referência autorizado: verificar se há registros `PUBLICADA` em `ref.calibracao_inicial_versao`, qual seu `codigo`, `algoritmo_versao`, `modelo_id`, `corpus_sha256`, `resultados_sha256`, e se o snapshot V3 completo correspondente existe e valida SHA. Não confundir `ref.frequencia_nome_versao` (fonte IBGE) com a calibração do algoritmo; não utilizar `ATIVA` do dia como substituição de pin.
2. **Identidade exata do modelo de teste:** confirmar `BlockingPasses` (quantidade/ordem/atributos), `UnitTfCalibrations` (lado, unidade, peso, estado), m/u das 81 células, prior, upper/lower, agrupamento e normalização. Preservar todo esse envelope. Se não houver vencedor completo com TF ampliada, **não fabricar modelo híbrido** a partir do Gold TF0 e dos snapshots ONE_MF.
3. **Ligação operacional separada:** `SqlProbabilisticIdentityLinkage.LoadModelByIdAsync` atualmente lê `identidade.modelo_linkage`, `identidade.parametro_linkage` e TF histórico; não carrega `V9FittedSnapshot` completo de `ref`. `LinkageModelPolicy.RequireCertifiedOperationalAlgorithm` bloqueia V9 deliberadamente. Exigir vínculo verificado, certificado e autorização DT-15 **antes de habilitar produção**.
4. **Ensaio incremental físico de integração:** somente quando o modelo V9 correto estiver fixado, exercitar o mesmo Runner com quatro ondas e três ordens, cada corrida em LocalDB isolada, com gabarito externo só para auditoria. Medir **recall global, FDR/FP, fusões, falsas criações, pendências, fragmentação, B³, idempotência, custo/P95 e pior ordem**. Comparar com metas da V9, **não** com V8. Os números anteriores das seis corridas Gold são históricos, não uma nova validação do TF.
5. **CI e governança:** executar testes unitários/integração do SHA exato e classificador de impacto. Se código de scorer exigir prova física 30k manual, manter gate pendente até autorização/disparo legítimo; jamais ativar 30k em PR automática. **Sem merge ou DT-15 sem autorização expressa do responsável.**

## Consulta SQL somente leitura proposta para conferência de referência

```sql
SELECT TOP (50)
  calibracao_versao_id, codigo, estado, algoritmo_versao,
  modelo_id, corpus_sha256, resultados_sha256, publicado_em
FROM ref.calibracao_inicial_versao
ORDER BY calibracao_versao_id DESC;
```

Esta consulta deve ser executada **somente** na base autorizada após verificar o nome do banco e o ambiente; não atualiza, publica ou ativa qualquer modelo. Para a identidade do snapshot exato, o consumidor ainda deverá validar conteúdo, origem e SHA, não apenas a coluna `estado`.

**Critério de conclusão desta PR:** permitir e testar a transformação experimental de V3 BLIND com TF ativa para GOLD, preservando integralmente o fit. **Não alegar que a V9 operacional está liberada ou que o TF ampliado passou no Gold físico.**


## Comando de auditoria REF implementado nesta PR

O worker expõe um comando **opt-in, somente leitura** para confrontar um modelo V9 portátil congelado (bytes e SHA externos) com a referência `ref.calibracao_inicial_versao` **de uma instância SQL autorizada**:

```bash
ConnectionStrings__Jornada='<conexao-SQL-com-permissoes-somente-leitura>' \
dotnet Jornada.Linkage.Parameters.Worker.dll \
  --v9-audit-ref-frozen-snapshot \
  /evidencias/<sha>.fitted.json <sha256-do-fit> <codigo-exato-em-ref>
```

O comando valida o SHA/canonicalização do fit antes de acessar SQL e consulta, sem `INSERT`, `UPDATE`, `EXEC` ou carga automática: estado `PUBLICADA`, SHA **SQL UTF-16** de `resultados_json`, identidade/algoritmo de `identidade.modelo_linkage` vinculado e igualdade de `snapshot_referencia` com o SHA do fit solicitado. Os hashes SQL do JSON de resultados e o SHA UTF-8 do fit são objetos **distintos** e não devem ser identificados como iguais por dedução.

A saída JSON não inclui nomes, observações, `resultados_json` nem parâmetros sensíveis e sempre marca `OperationallyCertified=false` / `ActivationAllowed=false`. Registros não publicados, sem vínculo, com hashes divergentes ou sem modelo V9 correspondente são **não conformes** (exit code 2, se o registro existe). A confirmação de metadados é apenas pré-requisito: não demonstra que todo o envelope V9 está instalado, nem permite retirar `RequireCertifiedOperationalAlgorithm`.

Nenhuma instância SQL do usuário foi acessada nesta PR: o comando e seus testes unitários estão disponíveis para a execução sob autorização e credenciais somente leitura. O próprio usuário/operador deverá fornecer a string de conexão apropriada, em ambiente seguro.
