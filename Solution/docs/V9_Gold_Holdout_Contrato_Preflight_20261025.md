# V9 Gold — contrato de pré-verificação de holdout independente (etapa 1)

**25/10/2026 — implementação parcial, NÃO experimento físico nem validação independente.**

Esta etapa introduz `V9GoldIndependentHoldoutContract.Prepare` e testes unitários fail-closed. O método aceita o candidato congelado pela PR #1190, manifesto declarado da nova coorte e SHA calculado do CSV, e devolve plano imutável de seis corridas (CURRENT e KNEE, cada um nas três ordens fixas). Não executa SQL nem abre o TEST. Não define limiar operacional: 0,05 entra exclusivamente como valor já selecionado em TRAIN V6.

O pré-voo rejeita escopo TRAIN_SELECTION, TEST ou VALIDATION histórica, seed diferente da proposta ou explicitamente histórica, coorte fora de 30 mil pessoas clean, CSV inconsistente com manifesto ou igual ao SHA TRAIN, fit diferente do congelado, refit, certificação/ativação e conexão diferente da LocalDB descartável prevista. Um namespace de identidade deriva da seed e do fingerprint da coorte; identificadores locais P000... iguais entre seeds não constituem prova de reutilização.

**Limitação crítica:** manifesto é declaração e SHA é prova de integridade, não prova da origem estatística. Ainda falta criar gerador/manifesto verificável de coorte nova, vincular hash real dos bytes CSV à seed e à versão do gerador, adaptar o Runner físico para escopo `INDEPENDENT_HOLDOUT` sem reaproveitar `TRAIN_SELECTION`, implementar seis execuções isoladas, métricas e auditoria por grupos independentes. Não executar `--v9-gold-harness` com CSV novo: esse comando corretamente exige SHA TRAIN original. Não forjar envelope.

A issue #1191 acompanha a implementação integral, inclusive perícia de falsas criações/falsos pares, evidência física e gates. Esta etapa não altera migration DT-10 canônica, ref, Gold operacional, HML/PROD, TEST ou DT-15.
