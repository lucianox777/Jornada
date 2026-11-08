> **GOV-V9-FINAL-20261108-B — PRECEDÊNCIA SUPERVENIENTE:** o [mandato de integração final e teste geral de 08/11/2026](Decisao_V9_Mandato_Integracao_Final_Teste_Geral_20261108.md) substitui TODOS os mandatos operacionais anteriores (inclusive GOV-OPS-V9-01/02 e o mandato preliminar de 04h05). Este documento preserva registros de estado do momento da sua redação; os SHAs/erros históricos não descrevem a master atual. Consulte o PAINEL V9 da PR #1318 (comentário 6725665397) antes de agir. A neutralização de binários ainda AGUARDA PO; não declarar V9 concluída.

> **Atualização 05/11/2026:** este é o protocolo manual **alternativo** de execução histórica. O gate técnico seed42 está autorizado a iniciar automaticamente em PR impactada, sob SHA exato, pin de referência imutável e SQL descartável. Não confundir com campanhas científicas com novas seeds/Gold/TEST, que permanecem manuais. Ver [decisão vigente](Decisao_V9_Gate_Tecnico_Auto_Campanhas_Cientificas_Manuais_20261105.md).

# V9 — gate manual do ensaio físico TF 30k (02/11/2026)

> **Escopo restrito do protocolo:** este documento exige 30.000 pessoas-base **neste ensaio histórico TF**, não em toda execução do Calibrador. A decisão de 05/11/2026 [DC-V9-CALIBRAÇÃO-CORPUS](DC-V9-CALIBRACAO-SEM-EXCLUSIVIDADE-30K-20261105.md) permite novos estudos não-30k com plano e população próprios, sem relaxar o contrato desta regressão nem seus gates de disparo manual.

**Estado:** PROCEDIMENTO PREPARATÓRIO, NÃO É AUTORIZAÇÃO. PR principal #1249; nenhum
merge, disparo 30k, leitura de TEST, publicação ou ativação é permitido por este documento.

**Deliberação de 02/11:** falsas criações de identidade permanecem **não medidas**
no estudo TF 30k; serão medidas no Gold com finalistas congelados, antes de qualquer
autorização operacional. A remoção de `TfCalibration`, `MotherTfCalibration` e
`NotSelected` foi **adiada para depois do 30k**, com migração de snapshots
históricos por contrato versionado; não altera os números deste estudo.

## Evidências obrigatórias antes da autorização

1. Congelar e registrar o HEAD exato da PR #1249 e da master. Invalidar a autorização
   se o SHA mudar (inclusive por rebase). Exigir CI de compilação, testes unitários,
   runner, segurança e integração aplicáveis; gate físico vermelho por ausência de
   execução deve permanecer **DEFERRED_NOT_CERTIFIED**, nunca ser dispensado.
2. Exigir manifesto C# do corpus IBGE-derived clean, seed 42, 30 mil **pessoas-base**
   e contrato cap4. Não confundir pessoas com número de observações em CSV.
   TRAIN e VALIDATION são disjuntos e congelados; TEST não participa.
3. Exigir SQL Server **JornadaHarness descartável**, em ambiente isolado, com IBGE
   publicado fixado por versão e hashes de publicação/materialização/bootstrap.
   Preservar JornadaLocal e qualquer banco operacional.
4. A grade declarada contém **45 configurações TF físicas**: TF0 + 40 outras elegíveis
   e quatro `TF_SHARED_PL_ML_*` somente diagnósticas, com os respectivos quatro
   controles `TF_TWO_PL_ML_*`. Seleção estatística/Pareto e eventual candidato
   à publicação usam apenas as **41 elegíveis**. É proibido usar diagnósticos como
   finalistas, exportar `.fitted.json` individual dos SHARED, ou congelá-los para ativação.
5. Única união de blocking SQL predeclarada, três margens de conflito: **135**
   medições de regra×opção×margem (12 SHARED, 123 selecionáveis). Com 3.726
   comparações históricas declaradas: **3.861 comparações de orçamento físico**,
   **3.849 comparações na correção estatística de seleção**. Preservar esta
   multiplicidade no plano, não redefini-la após observar VALIDATION.
6. A mesma união SQL materializada precisa ser atestada por digest SHA-256 das
   arestas TRAIN+VALIDATION com IDs do CSV (não IDs SQL gerados ou SHA de
   nome de ruleset apenas). O relatório C# dos quatro SHARED deve ser pareado
   por ruleset, margem, agrupamento e digest de candidatos, com contagens reais
   de pares que compartilham sobrenome. FDR, recall, falsos aceites e pendências
   devem constar; **falsas criações de identidade são métrica distinta** e
   não podem ser inventadas a partir de falsos aceites.
7. Sem EndToEndRepetitions medidas, não declarar P95 nem sugestão elegível;
   não autorizar modelo operacional. TEST independente e DT-15 continuam
   em gates próprios, posteriores ao estudo TRAIN/VALIDATION.

## Workflow manual TF 45 braços — somente após autorização explícita

`.github/workflows/v9-physical-seed42-probe.yml` executará o **Calibrador
C# `--v9-adaptive-study`**, não o histórico `--v9-calibrate`. Nenhum
resultado do caminho antigo pode substituir a prova da grade atual.

O `workflow_dispatch` expõe `mode=preflight|execute_30k`,
`expected_sha` e `pinned_ibge_json` (JSON com versão, dois digests
de referência/materialização e bootstrap sintético). O modo padrão
`preflight` testa a conferência de SHA com o checkout e a HEAD remota
da branch, sem carregar nem gerar corpus; não constitui execução nem
certificação. Somente `execute_30k` passa pelo gate manual e exige
a referência IBGE fixada; o job físico continua inacessível por evento
`pull_request` e guarda o plano/relatório pelo SHA exato.

O runner inicializa **JornadaHarness** em SQL Server efêmero, aplica
o schema e publica a referência IBGE do repositório. O plano continua
obrigado a comprovar digests e bootstrap fixados **na mesma versão do SQL**:
discrepância falha fechado, sem fallback para a referência ATIVA.
O corpus é gerado com `--people 30000` pessoas-base, seed 42 e
perfil clean; o leitor C# exige exatamente 30.000 **IDs de identidade-base
distintos** considerando TRAIN/VALIDATION/TEST, com o conteúdo de TEST
excluído do estudo. O runner revalida o SHA remoto antes da calibração.

O comando físico preparado na Action é:

```text
python3 scripts/v9-adaptive-30k-study.py prepare-tf \
    <30k/observacoes.csv> <novo-plano-tf.json> --pinned-ibge <referencia-fixada.json>
dotnet run --no-build -c Release --project src/Jornada.Linkage.Parameters.Worker -- \
    --v9-adaptive-study <novo-plano-tf.json> <30k/observacoes.csv> <novo-estudo-tf>
```

O gate pós-execução exige 45 opções (41 selecionáveis), 135 medições
físicas, 12 linhas pareadas, digest real dos pares SQL, e ausência de
falsas criações alegadamente medidas. A Action preserva os artefatos
de proveniência, geração, plano, relatório pareado e fits elegíveis,
**sem publicar as observações/PII ou o gabarito TEST**.

O status legível do gate físico em PR permanece
`DEFERRED_NOT_CERTIFIED` até a execução física autorizada do SHA exato;
o verde de compilação/segurança/integração **não** é dispensa desse gate.
O `timeout-minutes: 360` é teto de proteção, não previsão de duração.

## Regra de parada

Antes de qualquer comando que comece a executar o corpus 30k, interromper
e solicitar autorização explícita ao responsável pelo projeto. Nenhum
scheduler, merge, política de CI, aprovação de teste ou PR substitui isso.
