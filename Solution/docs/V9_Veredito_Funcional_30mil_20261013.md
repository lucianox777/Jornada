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
