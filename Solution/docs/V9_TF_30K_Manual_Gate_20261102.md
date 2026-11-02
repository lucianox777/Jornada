# V9 — gate manual do ensaio físico TF 30k (02/11/2026)

**Estado:** PROCEDIMENTO PREPARATÓRIO, NÃO É AUTORIZAÇÃO. PR principal #1249; nenhum
merge, disparo 30k, leitura de TEST, publicação ou ativação é permitido por este documento.

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

## Comando planejado — EXCLUSIVAMENTE após autorização explícita

Estando no diretório `Solution`, com `ConnectionStrings__Jornada` apontando
para `JornadaHarness` descartável e corpus/manifesto versionados:
 
```text
python scripts/v9-adaptive-30k-study.py prepare-tf \
    <corpus/observacoes.csv> <novo-diretorio/plano-tf.json> \
    --pinned-ibge <referencia-ibge-fixada.json>
dotnet build -c Release src/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.csproj
dotnet run --no-build -c Release --project src/Jornada.Linkage.Parameters.Worker -- \
    --v9-adaptive-study <novo-diretorio/plano-tf.json> \
    <corpus/observacoes.csv> <novo-diretorio/estudo-tf>
```

Saídas: `plan.json`, `study.json`, `tf-shared-paired-diagnostics.json` e
fits individuais somente das opções elegíveis; reter manifesto, hashes
de artefatos e SHA de GitHub do disparo. Verificar a contagem/fingerprint
de TODOS os quatro pares e falhar fechado em discrepâncias.

**Bloqueio de integração:** o workflow atual
`.github/workflows/v9-physical-seed42-probe.yml` executa
`--v9-calibrate` (grade aninhada histórica), **não** o comando
`--v9-adaptive-study` de 45 braços C#. Não utilizar o sucesso desse
workflow como evidência física da grade TF até existir acionamento
manual equivalente, auditado e preso ao SHA exato. A janela do workflow
atual é `timeout-minutes: 360`, limite de segurança, **não estimativa**
de duração da grade TF.

## Regra de parada

Antes de qualquer comando que comece a executar o corpus 30k, interromper
e solicitar autorização explícita ao responsável pelo projeto. Nenhum
scheduler, merge, política de CI, aprovação de teste ou PR substitui isso.
