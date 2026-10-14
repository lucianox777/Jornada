# V9 — incerteza do recall por identidade-base independente (diagnóstico)

## Por que recall pontual ≥95% ainda não certifica o blocking

Na execução sintética original seed42 sob `P AND (M OR D)`,
`UNION_EXPANDED` recuperou 19.447 dos 20.458 M de TRAIN no
**universo completo sem CPF**: 95,0582%. A seleção gulosa de 10
passes recuperou 19.436: 95,0044%, a apenas **um** M do limiar
discreto. VALIDATION confirmou a taxa pontual acima de 95%, porém
não garante que a taxa populacional ou outra semente sustentem
o limiar sob incerteza.

**Pares M da mesma identidade não são i.i.d.** Uma pessoa com três
observações em gestores diferentes pode gerar múltiplos pares
verdadeiros, com erros e probabilidades de recuperação correlacionados.
Intervalos binomiais ingênuos usando cada par como observação
independente subestimam essa dependência.

## Estimador incluído

O diagnóstico `cluster_bootstrap_global_recall` constrói, para
cada `base_person_id` com pelo menos um M intergestor sem CPF,
contagem `M_total` (denominador completo, inclusive registros
não elegíveis) e `M_capturado` (interseção de candidatos da união
com M verdadeiros). Reamostra **identidades-base** com reposição
(400 replicações, semente fixa 42 TRAIN / 43 VALIDATION),
calcula recall como razão de somas agregadas e publica percentis,
incluindo limite inferior unilateral de 95%.

Reamostramos somente identidades com pelo menos um M, pois
identidades sem M contribuem zero a ambas as somas da razão;
o estimando é, portanto, o recall **por par M**, com estrutura
de correlação capturada por clusters independentes de origem.
A métrica não se refere à proporção de pessoas com todas as
observações recuperadas. A reamostragem não deve incluir
observações de TEST e não ajusta seleção/TF/limiares com VALIDATION.

Para cada split, calcular separadamente os mesmos limites
para a união de 19 células, a enumeração exata (32 subconjuntos
com 14 fixas) e a seleção gulosa de redução de U congelada
em TRAIN. O campo
`independent_base_identity_recall_confidence.*.lower_one_sided_95`
é diagnóstico, **não** gate de promoção automática, e
`statistically_supported_global_95` significa apenas que
o limite empírico no cenário sintético supera 95%.
Massa-base sintética de 30 mil, poucas sementes e erros
provenientes de gerador não certificam cobertura municipal
de 11,45 milhões nem as caudas de homônimos reais.

## Proteções e extensões obrigatórias

- Sempre reportar **os dois denominadores**: M elegíveis P AND
  (M OR D) e todos os M sem CPF, com teto de elegibilidade.
- A meta 95% deve ser alcançada em TRAIN e VALIDATION sob
  recall global, mas TEST só poderá abrir no protocolo de
  certificação previamente congelado.
- Um limite inferior abaixo de 95% torna o resultado
  inconclusivo para a afirmação estatística forte.
  NÃO forçar redução no M sintético para aparentar progresso.
- Ainda faltam medição física/paridade no SQL Server, riscos
  U/fanout por gestor e chave, avaliação multissemente
  independentes, calibração FS `m/u` e TF por pessoas-base
  independentes, e FDR sobre vínculos **aceitos**, não candidatos.
- Nunca inserir PII, nomes, CPF, IDs ou amostras individuais no
  artifact: saídas da reamostragem são apenas totais,
  proporções e indicadores agregados.

Nenhuma alteração no vínculo V8, V9/DT-15 operacional,
Rank/Resolve, índices persistentes, HML/PROD, Gold, JornadaLocal,
NODE/NAS ou referências IBGE.
