# Fase 4 V9 — preparação do harness 1k contrafactual (v6)

**Estado:** INICIADA EM DRAFT; planejamento executável, **zero células medidas**, sem publicação. Não é nova decisão de política nem aprovação r2. Autoridade: plano v6 e esclarecimento PO de 25/11/2026, GOV-DOC-01, #1926/#1931/#1851.

## Baseline vinculada

PR #1938 integrada por squash em d657ededf9aafe4d01e153960d98d7ced966d3e6. HEAD físico 058f185f6ac6e3f3f9fc519b0c10a3274f11efda, run 45620370970, artefato 15026250198, SHA-256 155de4c59181146e565f6d3c20c2ac2a17138863a830542b1ebd04854487b94c. Vencedor #38 congelado, 1.000 bases, 2.349 versões, 1.957 origens, 315 CPF, TP563/FP0/FN1842, 1.528 versões não resolvidas; contraste read-only pós-FULL 0 recuperados. E2E_NUCLEO_PASS=true no escopo único; E2E_INTEGRAL_PASS=false. [Relatório](https://github.com/lucianox777/Jornada/pull/1938#issuecomment-7163513870).

## Planejamento inicial

`scripts/v9-phase4-1k-plan.py` confere bytes do artefato exato, recibos e população antes de produzir um plano de 24 células: evidência b1/b2/resumo × prior registros/pessoas × r1/r2 × blocking amplo/seguro. Uma única célula é baseline oficial. Proposta técnica de desenho de rodadas (não política): comparar FULL depois das duas ondas com uma rodada após cada onda, preservando a mesma sequência de entregas. Cada célula e trajetória tem namespace independente; snapshots por rodada são distintos, só admitem predecessor na mesma trajetória. O planejador não cria SQL nem executa scoring. Medições, motor e verificador de recibos reais ainda pendentes.

A massa privada da Fase 3 foi descartada. Re-materializar o mesmo corpus sintético pelo gerador já aprovado, seed 355, perfil demographic-primary/clean, 4 gestores, duas ondas, 0 atraso/revelação CPF, correção nominal/materna/nascimento = 1, data 2026-11-22T00:00:00Z. Pseudônimos/chaves/ZIPs novos por tentativa, portanto **não** afirmar igualdade byte a byte entre tentativas. Gerar uma vez por tentativa, validar proveniência e censo antes de todas as células; fixar o mesmo hash de ordem de pacotes, truth fora de ZIP/scorer e fingerprint compartilhado. O baseline oficial precisa reproduzir a métrica de Fase 3 antes de interpretar contrastes. Reutilizar o vencedor congelado, sem refit. O binding de fixture é opcional apenas para planejamento, nunca para execução.

## Próximo incremento

Definir r2 apenas como hipótese não publicante conforme #1926, antes de medir: múltiplas referências, vínculos protegidos, abstenção, origem persistente. Não inventar nova política na ausência de especificação. Implementar estados isolados e leitor/motor de contrafactual sem writes nas tabelas oficiais; comparar b1/b2/resumo, prior por registros/pessoas (sem alterar kernel ou forçar diferença se prior fixo), r1/r2, blocking amplo/seguro. Métricas e recursos listados no plano; todo resultado por célula/rodada com gabarito separado, denominadores, tempo/memória/tempdb/SNAPSHOT/índice. Nenhum resultado inferido dos rótulos do planejador. Sem TRAIN/TEST científico, INCREMENTAL, 10k/100k, HML/PROD ou ativação; relatório 1k e plano 10k precedem autorização específica. Não integrar esta fase enquanto CI master não concluir verde.
