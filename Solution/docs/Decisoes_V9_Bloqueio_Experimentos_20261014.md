# Jornada V9 — decisões de bloqueio, experimentação e evidências (14/10/2026)

**Natureza:** registro técnico de decisões vigentes para desenvolvimento/ensaio. Não constitui norma publicada, ativação de modelo, homologação ou certificação de produção. Na divergência com documentação normativa selada, respeitar a hierarquia do índice do acervo e abrir revisão explícita.

## 1. Objetivo e política de identidade

O linkage é responsável por escolher e executar os campos/passes de bloqueio; não transferir a decisão ao usuário da Console DEV. CPF, quando válido, é vínculo determinístico sujeito às invariantes um UUID com no máximo um CPF corrente e um CPF corrente para um único UUID. Documentos como RG podem ser múltiplos por pessoa: igualdade é evidência, não imposição de unicidade de RG. Calibração é **manual**; o diagnóstico de suficiência e a sinalização de oportunidade de recalibração podem ser automáticos, sem ativação automática de novos parâmetros.

## 2. Projeção canônica e isolamento de amostras

- Normalização e fonética devem ser produzidas pelo projetor C# canônico (`BlockingProjectionKeyProjector.Project`, `IdentityComparison.NormalizeText`, `MetaphoneBr.Encode`), versão `BLOCKING_PROJECTION_KEY_PROJECTOR_V4`. Não manter implementação fonética paralela em Python.
- Colunas canônicas: `observacao_id`, `base_person_id`, `particao`, `nome_normalizado`, `nome_fonetico`, `nome_mae_normalizado`, `nome_mae_fonetico`, `data_nascimento`.
- Partições TRAIN, VALIDATION e TEST são disjuntas por `base_person_id`; observações têm IDs únicos. Qualquer identidade presente em mais de uma partição invalida o ensaio (falha fechada).
- Frequências marginais e conjuntas devem contar **identidades distintas em TRAIN**, não observações repetidas, e nunca usar VALIDATION/TEST para aprender frequências. Manifests e hashes preservam a proveniência do snapshot congelado.
- Frequência de célula igual a zero significa **não observada em TRAIN**, não impossibilidade de ocorrência. O limite de fanout observado em TRAIN não é garantia de fanout físico em SQL: não descartar passes sem fallback e avaliação.

## 3. Desenho experimental A/B/C/D

Comparar quatro estratégias A, B, C e D com fonética desligada/ligada (A0/A1, B0/B1, C0/C1, D0/D1), cada uma sem/com E0: **16 braços**. E0 é passe demográfico exato de `nome_normalizado + nome_mae_normalizado + data_nascimento`; índice físico E0 não equivale à consulta nem prova efeito do passe. Comparação pareada exige corpus original idêntico, mesma elegibilidade M/U, partições congeladas e semente, incluindo recall, falsos candidatos, fanout, custo SQL, planos, CPU/memória, FDR e intervalos adequados a dependência por identidade.

**Distinção obrigatória:** `v9-abc-paired-blocking.py` fornece proxies diagnósticos de passes fixos. Seus braços D **não são** a estratégia adaptativa real. `v9-adaptive-frequency-planner.py` produz planejamento por frequência TRAIN, mas não executa recuperação física nem Rank/Resolve. Não publicar vencedor ou alegar equivalência de D a partir desses relatórios.

## 4. Execução e rastreabilidade

O corpus original congelado de aproximadamente 30 mil identidades foi publicado pelo workflow de origem (run `38593005691`, artefato `v9-physical-sql-active-candidate-wrong-link-30k`). O workflow `.github/workflows/v9-original-corpus-abcd-diagnostic.yml` prevê download, exportação canônica C#, congelamento e relatório dos 16 braços; a integração proposta na PR #1066 acrescenta relatório `adaptive-plans.json`. **O sucesso da execução sobre o corpus original e seus resultados não estavam confirmados ao redigir este registro.** Não confundir existência do workflow com evidência de execução bem-sucedida.

## 5. Evidência estatística e promoção

A regra proposta é minimizar falsos negativos sob teto de FDR `ε`, usando limite superior unilateral de incerteza, com TRAIN para estimar m/u/TF, VALIDATION para seleção e TEST independente para certificação. Valores de ε, α, estratos, n_min e gates dependem de política versionada; números ainda em discussão não devem ser tratados como homologados. Diagnóstico com variância ingênua de pares ou amostra insuficiente deve ser marcado **INCONCLUSIVO**. O problema de deterioração de ~18% segue distinto e não resolvido. Nenhum resultado sintético, proxy ou plano adaptativo isolado autoriza ativação em HML/PROD.

## 6. Estado e pendências verificáveis em 14/10/2026

- PR #1064: correções de testes canônicos, integrada.
- PR #1065: proteção de partições, integrada (merge `0d630b6`).
- PR #1066: ponte diagnóstica de planos adaptativos a partir de snapshot canônico com hashes e testes; **aberta, aguardando conclusão da CI principal** na última verificação. Não tratar como integrada.
- Próximas entregas: validar CI e merge #1066; confirmar artefatos reais do workflow original; implementar D adaptativo com recuperação física, cap/fallback e medição SQL; avaliar as 16 alternativas com métricas pareadas e suficiência estatística; documentar decisão de seleção antes de qualquer ativação.
- DEV não exige retrocompatibilidade automática; mudanças de contrato e de semântica ainda requerem testes e documentação.

**Critério de encerramento:** registrar IDs dos runs e hashes dos artefatos, evidência do braço D real (não proxy), resultados reproduzíveis e revisão dos gates; somente então propor promoção separada.
