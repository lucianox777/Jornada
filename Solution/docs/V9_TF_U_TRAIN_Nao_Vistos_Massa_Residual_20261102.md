> **RETIFICADO — decisão 19 (02/11/2026).** O desenho de massa residual
> `V9_TRAIN_U_UNSEEN_IBGE_CONDITIONAL_V1` abaixo documenta a hipótese
> histórica de #1254, **revogada para pontuação TF**, por cancelar a
> diferenciação raro/comum entre nomes não vistos e poder invertê-la
> entre nomes observados no TRAIN. O bloco abaixo serve somente para
> auditoria do incidente; **não** é a especificação vigente do scorer.
>
> **Regra candidata implementada (ainda NÃO certificada):**
> `V9_TRAIN_NONMATCH_NOMINAL_AGREEMENT_U_REF_V1`.
> Para cada lado (pessoa/mãe) e unidade (prenome/último sobrenome), o
> Calibrador conta, exclusivamente nos pares SQL candidatos não pares
> de TRAIN, o total `N_U` e o total `A_U` de concordâncias do token
> nominal. O contrato condiciona explicitamente ao blocking do frame;
> a probabilidade única `U_ref=(A_U+α)/(N_U+2α)` usa o α já calibrado
> e uma suavização binária simétrica. Contar o evento diretamente
> impede omitir acordos por prioridade dos níveis conjuntos 9×9.
> O ajuste é `peso × ln(U_ref/max(freq_esquerda,freq_direita,piso))`;
> assimétrico usa `min(0,ajuste)`. As 81 células m/u não mudam.
>
> Termos publicados no IBGE e ausentes em TRAIN reutilizam o **mesmo
> U_ref**; ausentes do IBGE, suporte nominal TRAIN insuficiente, política
> ou origem incompatível seguem **fail-closed**. A distribuição residual
> por termo fica **diagnóstica/legada** e é rejeitada nos snapshots TF
> positivos novos. Proveniência, versão e lado/unidade integram o fit e
> snapshot portável. `TF0` continua separado, sem exigência de U_ref.
> Regressões rápidas e CI no SHA exato ainda são necessárias; não
> inferir certificação física, resultados FDR, autorização DT-15,
> ativação ou permissão para campanha 30k.
>
> **Governança de 01/11/2026:** não realizar merge nem executar
> `execute_30k` sem autorização humana específica, mesmo se os
> testes rápidos passarem. A orientação antiga de merge/preflight no
> final deste documento está **subordinada** a esta regra.

---

# V9 TF — massa residual U TRAIN para nomes não vistos (02/11/2026)

**Incidente:** execução manual física `41539120255` no SHA `52120da3951a913159ef04ccd48226ad303d6c9c` falhou antes das 135 medições: `V9TrainSqlNominalUCalibration.Preview` recusou um valor elegível em VALIDATION não visto nos pares U TRAIN. O pin de referência IBGE, os 30 mil registros-base e o gate de SHA passaram. **Não há resultado TF/TF0 novo aprovado**.

## Regra estatística congelada

Dado `n` pares U de TRAIN em que a mesma unidade nominal foi selecionada, `k` valores distintos e a suavização `α`, as frequências observadas são `U(v)=(n_v+α)/(n+α(k+1))`. A **única massa total** reservada para todo o domínio não observado é `r=α/(n+α(k+1))`.

Para cada valor `v` **não visto em U TRAIN mas publicado na marginal IBGE fixada**, distribuir `U(v) = r × q(v) / Σ_{w não visto em U TRAIN, w publicado no IBGE} q(w)`. `q` é a frequência publicada para a **mesma unidade e lado**. Os valores vistos conservam seu U TRAIN; nenhum caso troca TF positivo por BF=1 ou peso zero. Esta regra faz a massa total atribuída a todos os valores publicados não vistos ser exatamente `r` (salvo arredondamento decimal), ao contrário de atribuir `r` inteira a cada novo nome. É uma **hipótese de modelagem baseada no IBGE**, não uma medição direta de U nem certificação FDR. A proxy de sobrenome continua `ANY-POSITION`, não uma frequência real de última posição.

O fallback só é permitido quando a unidade nominal foi selecionada, há U TRAIN global suficiente (`n≥2`), a frequência para o valor está publicada no IBGE e o domínio residual positivo está presente. Ausência da fonte IBGE, divergência de referência, suporte TRAIN insuficiente, U fora de faixa e pesos/proveniência inválidos seguem **fail-closed**. TF0 continua neutro e não precisa ajustar essa massa.

## Equivalência e trilha

O mesmo cálculo versionado `V9_TRAIN_U_UNSEEN_IBGE_CONDITIONAL_V1` é compartilhado pelo Calibrador VALIDATION/Resolve e o snapshot `CreateTf`; o snapshot leva `UnseenAllocation` (versão, massa U e massa IBGE residual) sob SHA do próprio artefato. Snapshots legados sem política não ganham fallback implícito. O resumo físico `UnseenTfUByOptionAndUnit` contabiliza pares **candidatos VALIDATION avaliados**, por opção e lado/unidade (e.g. `Person/FirstName`), não eventos operacionais nem pessoas distintas. Nenhum conteúdo nominal, CSV ou PII entra nessas contagens.

**Aceitação:** paridade exata do score Calibrador/snapshot, normalização com três ou mais nomes não vistos, faltas de IBGE ainda rejeitadas, políticas adulteradas rejeitadas, status TF0 preservado e testes/CI no SHA exato. Não há dispensa da prova física V9 (30k manual), do holdout independente, nem autorização operacional DT-15. Após merge, **novo preflight e somente depois novo execute_30k com autorização humana específica**.
