# Seletor V9 após Resolve — 19/10/2026

`V9PostResolveAdaptiveSelector` integra a saída por fonte do probe SQL físico.
`V9PhysicalCalibrationRunResult.PostResolveSelection` e o JSON da CLI
(`post_resolve_source_selection`) passam a expor a comparação reproduzível.
O relatório antigo de pares continua diagnóstico; ele não decide esta seleção.

A unidade é uma decisão final por observação fonte. O denominador do recall
inclui as fontes elegíveis anteriores ao blocking, inclusive as não recuperadas.
O seletor verifica as contagens, as razões recalculadas, o censo comum,
a unicidade das configurações e os limites válidos. TEST e TRAIN são rejeitados.
Agrupamento oracle nunca concorre. Agrupamentos desconhecidos falham fechados.

`BestDescriptiveConfiguration` compara somente configurações sem oracle que
atendem ao orçamento explícito de recall, FDR pontual, fanout P99 e suporte
por identidade. A ordenação é recall, FDR, conflitos, não resolvidos,
agrupamento, opção e margem, com desempates determinísticos. A ausência de
fanout da mesma opção impede qualquer indicação. O SHA vincula a política,
as medições e o fanout; ele não autentica a proveniência externa.

`SuggestedDiagnosticConfiguration` exige adicionalmente limite superior
condicional de FDR por identidade dentro do orçamento, correção pelo número
de comparações e independência dos clusters validada separadamente. Apenas
`BLIND_FS_COMPLETE_LINK_CANDIDATES` com margem de conflito positiva pode
gerar essa sugestão; RAW, tripla exata e margem zero permanecem descritivos.
Um mesmo OptionId não pode representar thresholds ou pesos TF distintos.
Um limite condicional inferior à FDR observada é rejeitado.
O probe atual declara `ClusterIidIndependentlyValidated=false`; portanto,
não produz sugestão, mesmo quando existe uma melhor configuração descritiva.
Os números sintéticos dos testes unitários verificam esse comportamento e
não constituem evidência estatística do corpus.

Nenhuma das saídas cria modelo operacional, ativa parâmetros ou autoriza
promoção. Mesmo uma sugestão diagnóstica exige paridade com o fluxo real,
proveniência auditável e TEST novo e lacrado para eventual aprovação.
Não foi executado experimento físico novo neste incremento.

## Isolamento das partições

A CLI valida a partição de cada identidade-base antes de excluir CPF ou TEST.
Uma identidade repetida entre quaisquer partições é rejeitada, inclusive quando
uma linha seria filtrada. Para TEST são lidos apenas identidade e partição;
seus atributos não são convertidos nem retornados ao probe. A avaliação por
fonte exige IDs de observação únicos, ausência de sobreposição TRAIN/VALIDATION
e o mesmo censo de VALIDATION congelado no frame. Se TRAIN for fornecido,
seu censo também deve coincidir; fontes apenas VALIDATION continuam permitidas.
O probe já rejeitava sobreposição antes de executar qualquer SQL.

O frame físico agora carrega os passes e o fingerprint de blocking reais e a
identidade da projeção `BlockingProjectionKeyProjector`, permitindo que a
exportação do snapshot verifique a mesma transformação em vez de aceitar
metadados fornecidos posteriormente sem correspondência ao fit.
