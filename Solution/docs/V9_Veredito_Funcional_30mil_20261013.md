# Prioridade P0 — o FS nominal V9 funciona em 30 mil sintéticos?

**Escopo 13/10/2026.** O termo "venom" foi interpretado como **V9** pelo contexto do projeto. Esta avaliação remove a comparação V8×V9 como dependência; a V8 é apenas replay histórico. DT-15 continua tratando de ATIVO×PROPOSTO quando houver substituição operacional, independentemente da versão.

## Experimento que a CI executa

O comando `generate --people 30000 --seed 42 --population-profile demographic-primary --v9-functional-probe true` roda o **scorer Fellegi–Sunter C# V9 verdadeiro**, não uma implementação paralela, sobre o corpus sintético versionado. Gera o artefato agregado `v9-functional-probe.json` com os campos `Validation`, `Test`, `SyntheticProxyVerdict` e `OperationalConclusion`. O job de CI verifica que foram usadas efetivamente 30.000 pessoas e o fingerprint/seed do mesmo manifesto.

- TRAIN estima as 81 probabilidades nominais conjuntas M/U e os estados de nascimento; os grupos são separados por pessoa-base. Células sem observação M/U são informadas, e suavização técnica α=0,5 permite um **experimento** sem declarar que ganhou suporte observacional.
- Blocking **proxy** por primeira inicial. M: primeiro par entre gestores de mesma pessoa, com perda por blocking explicitamente contabilizada como FN; U: pares de bases distintas adjacentes no bucket. **Não** são os passes SQL reais `VERSIONED_ALIAS`.
- O prior usa a proporção M/U **da amostra artificial TRAIN**, não a prevalência operacional. O ajuste TF foi desabilitado **explicitamente neste experimento**; não se alega calibração completa de peso TF.
- VALIDATION congela um threshold com orçamento exploratório de FPR em pares U proxy de 1%; TEST não influencia a escolha. TEST relata TP, FP, FN, TN, recall, FPR, precisão e FDR da **amostra pareada**, não da população.
- Critério exploratório declarado antecipadamente: `PASS_ON_SAMPLED_SYNTHETIC_PROXY` só com TP>0, recall TEST≥80% e FPR TEST≤1%; caso contrário `FAIL_ON_SAMPLED_SYNTHETIC_PROXY`. Este critério **não substitui orçamento aprovado de DT-15**. Mesmo em PASS, `OperationalConclusion` permanece `NOT_PROVEN_ON_REAL_SQL_BLOCKING_OR_MUNICIPAL_POPULATION`.

**Conclusão a extrair:** o artefato da execução CI indica objetivamente **se a implementação V9 passou ou não passou no teste sintético proxy**, com contagens. Isso **não é suficiente** para afirmar que o algoritmo completo, incluindo blocking SQL real e TF calibrada, funciona na Jornada. O gate CI valida execução, integridade e relatório, **não exige PASS** e nunca ajusta TEST até passar.

Para responder sobre o **algoritmo operacional completo**, a próxima prova indispensável é repetir o ensaio com união de passes SQL real na massa sintética, TF selecionado em VALIDATION, orçamento por estrato, abstenção e TEST congelado. Não extrapolar precisão/FDR de pares proxy para pares elegíveis do SQL, nem converter 59/81 células sem suporte bilateral de TRAIN em evidência por suavização.

Nenhuma execução deste ensaio publica/ativa V9, altera V8, DT-15, banco, referências IBGE ou JornadaLocal.

## Rodada seguinte — réplica por sementes independentes (PR #996)

A avaliação isolada da seed 42 já deu **3.414 TP, 39 FP, 67 FN e 5.833 TN**
em TEST no *proxy por inicial*, recall 98,08% e FPR 0,664%.
A reprodutibilidade num único conjunto não basta para atestar robustez da
calibração. Por isso, a CI executa **mais duas massas independentes de 30.000
pessoas cada**, seeds fixadas **43** e **44**, com o **mesmo código de geração,
fonte IBGE, nascimento e contrato FS V9**, mantendo a execução original 42.

O validador `scripts/v9-synthetic-multiseed-gate.py` confere, de forma
independente do gerador, (a) a integridade criptográfica dos três manifestos
e arquivos de dados; (b) unicidade de seed, fingerprint e verdade sintética;
(c) impossibilidade de vazamento de TRAIN/VALIDATION/TEST e aritmética de
TP/FP/FN/TN/recall/FPR/precisão/FDR; (d) critérios de PASS/FAIL originais
por seed, sem converter FAIL em PASS; e (e) indicadores de faixa
de recall/FPR em três réplicas. O arquivo agregado
`v9-synthetic-multiseed-summary.json` informa `passedProxySeeds`,
`failedProxySeeds`, `crossSeedProxyVerdict`, limites e resultados por seed.

**Importante:** a seed 42 é original e 43/44 são **novas execuções**, mas
cada seed recalibra seu próprio TRAIN e seleciona o limiar em sua própria
VALIDATION. **Não** é validação de transportabilidade de um modelo
congelado entre amostras, nem corrige o *proxy U* não aleatório. Um PASS
em todas as réplicas comprova apenas repetibilidade funcional desse recorte;
não comprova FDR operacional, suficiência das 81 células ou equivalência
com passes SQL `VERSIONED_ALIAS`. Nenhuma execução publica/ativa V9.

**Política de falha:** se qualquer réplica não alcançar os limiares
exploratórios, o resumo marcará `AT_LEAST_ONE_PROXY_REPLICATE_FAILED`
mesmo com CI verde — a CI valida a **honestidade do relatório**, não
mascara fracassos de desempenho. O julgamento final dependerá
de análise humana dos próprios números, nunca de um PASS artificial.

**P0 subsequente:** reutilizar exatamente esse gabarito por pessoa-base no
*blocking* SQL real e avaliar o FS V9 com TF calibrada e política explícita
de abstenção por célula sem suporte bilateral. Nenhum comparativo V8×V9
é requisito dessa validação.
