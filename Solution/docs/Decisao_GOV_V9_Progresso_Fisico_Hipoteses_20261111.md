# GOV-V9-PROGRESS-324-20261111 — log mínimo por hipótese física

**Autoridade:** Product Owner, 11/11/2026: autorização expressa “Pode implementar”, após avaliação de linha por hipótese finalizada, tempo e previsão rudimentar de término.

**Contexto:** workflow `.github/workflows/v9-v3-four-name-100k.yml`, campanha 100k/324 TF4. A execução #43105369099 já iniciada não recebe mudanças retroativas. O GitHub Actions informa etapa ativa, mas não quantifica hipóteses completadas. O coordenador já possui `reportProgress` por lote; o resultado individual é construído após probe SQL compartilhado e replay físico individual, quando aplicável.

**Decisão e implementação proposta:** habilitar `V9_PHYSICAL_PROGRESS_LOG=1` somente no workflow TF4; emitir uma linha `[V9 PROGRESS]` para cada hipótese com contador atômico, total declarado, percentual, resultado (AVALIADA/REJEITADA), duração individual **após o SQL compartilhado**, minutos decorridos, ETA linear preliminar e margem contra os 305 minutos da etapa física. A estimativa considera ritmo global observado, não o tempo individual pós-SQL; é rudimentar e não garante conclusão. Até 7 conclusões, marcar `ETA_PRELIMINAR`. A margem negativa sinaliza `RISCO_TIMEOUT`. O log não deve expor identificadores pessoais, dados SQL, exceções, corpus ou segredos.

**Semântica e limites:** “AVALIADA” significa que houve resultado físico para a hipótese, inclusive se não elegível cientificamente; “REJEITADA” indica retorno controlado sem avaliação física. O contador acompanha hipóteses **finalizadas pelo coordenador**, não certificação científica, nem resultado final da auditoria. Duração individual pós-SQL **não é custo total imputável** por hipótese, pois SQL/fitting são compartilhados. A instrumentação não altera TF, M/U, FDR, seleção, replay, simultaneidade, hashes ou resultados persistidos. Sem cancelamento automático, checkpoints ou reinício.

**Riscos e mitigação:** paralelismo exige `Interlocked` e emissão serializada; operações de log devem ser pequenas. As conclusões em blocos podem produzir ETA instável; evitar interpretá-lo como previsão estatística. Não modificar limites de timeout. Validação necessária por build, testes de regressão e execução controlada antes de declarar a melhoria comprovada.

**Rastreabilidade:** branch `feat/v9-tf4-physical-progress-20261111`; PR vinculada a este documento. **Estado:** implementação proposta, aguardando CI/revisão e integração. **Precedência:** complemento operacional a GOV-V9-V3-TF4-324-20261111 e GOV-V9-MARGIN-REUSE-20261111, sem substituí-las. GOV-DOC-01.
