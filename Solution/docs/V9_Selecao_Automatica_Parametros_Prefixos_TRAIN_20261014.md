# V9 — o próprio sistema escolhe parâmetros de blocking por evidência TRAIN

A decisão não é fixar **3 letras** do prenome ou **1 letra** da mãe.
Esses são apenas valores de um **espaço de busca previamente registrado**.
O fluxo experimental executa todas as 34 hipóteses de prefixo,
inicial de mãe e nascimento via junções **SQL SQLite indexadas reais**,
com gabarito sintético por identidade-base e splits protegidos.

## Algoritmo e integração ao plano de referência

1. Importa a escolha `greedy_pruned.selected_cells` do relatório
   do mesmo corpus congelado, obtida **exclusivamente de TRAIN**.
   Recalcula as arestas de seus passes em SQL, sem usar dados
   de VALIDATION para reconstruir a seleção original.
   Esse plano existente é a âncora, não o isolado P3+DOB.
2. Para **cada hipótese** nova, calcula união deduplicada com a
   mesma âncora, M/U e fanout incluindo observações sem candidatos.
   Publica **M incremental e U incremental contra a união
   já selecionada** e recall sobre **todos** os M sem CPF
   (não somente os elegíveis).
3. O próprio algoritmo constrói a **fronteira de Pareto em TRAIN**:
   descarta alternativas cujo ganho M é dominado por outra
   que gera menos (ou igual) U. Não usa VALIDATION nem TEST na
   construção e não impõe a priori que três letras sejam melhores.
4. Um orçamento **explícito, versionado e vinculado por SHA-256**
   é opcional para escolher automaticamente a melhor célula
   adicional admissível, segundo M recuperados, U total e
   limites P95/P99, com alvo pontual global ≥95%.
   **Sem orçamento aprovado, não escolhe uma regra para ativação**:
   publica opções e diagnósticos. Se nenhuma configuração couber,
   reporta `NO_TRAIN_FEASIBLE_ADD_ON`, não aumenta o cap.
5. Registra o mesmo candidato congelado e a fronteira em VALIDATION
   **sem ajustar escolhas**. Esta comparação é no corpus
   sintético, não valida limite inferior estatístico, FS/FDR
   ou custo SQL Server.

A primeira versão procura **uma única célula adicional** sobre
o plano congelado. É uma prova controlada de otimização automática
de parâmetros, **não** uma garantia de ótimo global de subconjuntos
nem de que esse seja o método final de seleção. A próxima etapa
é estender a busca para combinações complementares e separar
estratos mãe+nascimento, mãe sem DOB e DOB sem mãe, sempre
computando U marginal depois de deduplicar contra toda união.

As frequências TRAIN observadas no snapshot original são independentes
por identidade-base. Distribuições marginais IBGE não são
probabilidades conjuntas; nem a frequência de SOBRENOME em qualquer
posição equivale a frequência de último sobrenome.

## Critérios de segurança e integração

- Nome da pessoa obrigatório e **mãe OU nascimento**; pessoa e
  nascimento válidos não exigem mãe. CPF válido segue caminho
  determinístico fora dessas junções.
- TEST jamais fornece seleção, agregados ou parâmetros;
  preserva-se o checksum SHA-256 e as partições por identidade-base.
- O usuário pode definir um **orçamento** estatístico/operacional,
  mas o sistema deve aprender a **configuração dentro do orçamento**,
  não exigir que ele escolha manualmente o número de letras.
- Orçamento observado não autoriza extrapolação simples
  para 11,45 milhões, não garante 1 s, nem baixo FDR.
  É necessário validar índices e planos de execução no SQL Server,
  inclusive caudas de chave e fanout por gestor.
- Sobrenome e fonética da projeção podem ser C# canônicos;
  a divisão do prenome em primeira palavra e o corte P1–P4
  permanecem **transformação exploratória Python** até provar
  paridade com `BrazilianNameComponents`.
- Resultado sempre `may_promote_policy=false`. Nenhuma ativação
  da V9/DT-15, nem alteração de m/u/TF/prior/FS, V8, Gold,
  IBGE original, HML/PROD, JornadaLocal ou NODE/NAS.
- FDR exige medir as decisões de vínculo após scorer calibrado;
  U não é FDR. A calibração segue manual quando solicitada
  por governança, ainda que o diagnóstico de suficiência
  seja automático.

## Camadas interativa, lote e tempo

A execução em camadas pode utilizar **orçamentos distintos**
quando houver decisão governada, e aprender parâmetros por
camada e estrato sob os mesmos conjuntos. A fila de lote deve
considerar registros **sem vínculo confiável**, inclusive quando
o blocking produziu candidatos falsos, sem presumir que
`NAO_RESOLVIDO sem candidato` representa todos os falsos negativos.
A camada temporal pode receber CPF tardio e novas observações,
mas não fornece recall certo: medir recuperação acumulada
e latência sob horizonte temporal. Essas camadas ainda são
**arquitetura proposta**, não executadas nem ativadas nesta PR.
