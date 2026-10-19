# Revisão de independência, multiplicidade e suficiência — 19/10/2026

Esta revisão deriva do código de `V9IdentityClusterFdr`, do avaliador por fonte
e dos relatórios históricos versionados. Não abre TEST, não mede nova qualidade
física e não atesta independência. O probe deve continuar emitindo
`ClusterIidIndependentlyValidated=false` enquanto não houver evidência externa
verificável. Ampliar o corpus ou obter zero FP não resolve essa condição.

## Família de comparações

Para uma família definida usando somente TRAIN, declarar antes de VALIDATION
`M = número de rulesets × opções completas threshold/TF × margens` quando cada
combinação for efetivamente avaliada exclusivamente com agrupamento FS blind.
Se famílias têm tamanhos diferentes, usar a soma dos produtos de cada família.
Cada bound recebe o mesmo M global. Batches, caches, exclusões de fanout,
configurações rejeitadas e tabelas finais não reiniciam nem reduzem M.

Uma configuração examinada em VALIDATION e descartada ainda conta. Novas opções
concebidas após inspecionar VALIDATION não se tornam automaticamente cobertas
apenas por aumentar M: a família depende dos próprios outcomes usados na
inferência. Exigem protocolo estatístico apropriado ou nova validação lacrada.
Braços oracle/raw/exact legados só podem ser excluídos da nova família se seus
resultados não orientarem nenhuma decisão desse estudo. Resultados históricos
reutilizados orientam desenvolvimento, mas não constituem nova validação cega.

## Atestado externo necessário

Independência das pessoas sorteadas não implica independência dos seus erros
finais: uma observação participa de candidatos, agrupamento e competição de
outras identidades. Complete-link impede determinadas fusões transitivas, mas
não remove essa dependência estatística. Um teste de correlação sem rejeição,
seed nova, nome de perfil `independent`, bootstrap por base ou booleano manual
não prova as hipóteses de IID usadas pelo bound.

Antes de permitir um atestado positivo, o contrato externo precisa vincular:

1. Estudo/protocolo imutável, revisão independente identificada, metodologia,
   população-alvo e esquema de amostragem anteriores à inspeção de VALIDATION.
2. Manifestos e hashes TRAIN/VALIDATION, família completa de configurações,
   scorer/Resolve, grouping, projeção, SQL, referências TF e cenário de aplicação.
3. Justificativa de independência da **unidade final de erro**, incluindo todo
   o caminho de influência entre candidatos/identidades e alterações de Gold.
4. Verdade completa obtida independentemente, cap de aceites imposto pelo
   desenho, inclusão das unidades com zero aceites, alpha e M globais.
5. Contribuições por unidade suficientes para recomputar CP e denominador,
   contagens reconciliadas, hashes dos artefatos e escopo/limites da aprovação.
6. Verificador que derive o estado aceito de artefato autorizado e íntegro.
   Hash autoemitido ou campo `--iid=true` não substitui revisão/autorização.

Esse verificador não está implementado pelo booleano do DTO atual. Até existir,
nenhuma entrada CLI deve habilitar o flag. A ponte Pareto valida o limite
numérico e sua consistência; não reconstrói uma prova IID ausente do DTO.

Um desenho possível a estudar é amostrar painéis completos independentes ou
consultas independentes contra uma referência fixa, sem atualizações cruzadas.
Isso altera unidade/cap/estimando e precisa de protocolo e estimador coerentes;
não basta agrupar componentes conexos observados a posteriori e chamá-los IID.
Os resultados sintéticos permaneceriam relativos à população do gerador,
mesmo depois de demonstrada independência dentro desse desenho.

## Déficit no cenário histórico de zero FP

O [relatório de 18/10](V9_Incremental_Blind_FS_Candidate_Groups_20261018.md)
registra 5.556 identidades VALIDATION, 7.505 aceites, zero FP e cap16.
Com alpha=0,05, M comparações e média normalizada
`q = 7505/(5556×16)`, o bound implementado simplifica, para zero FP, a:

```text
L = log(2M/alpha)
U(n) = min(1, (1-exp(-L/n)) / (q-sqrt(L/(2n))))
```

Denominador não positivo significa bound indefinido. A tabela calcula o menor
n inteiro com U(n)≤1%, **supondo** zero FP futuro e a mesma média de aceites.
Não é garantia de poder, qualidade futura ou independência.

| M | UCB condicional com n=5.556 | n necessário no cenário | Déficit | Pessoas-base com a mesma fração retida de VALIDATION |
| ---: | ---: | ---: | ---: | ---: |
| 1 | 1,00254% | 5.569 | 13 | 30.071 |
| 36 | 2,22302% | 10.978 | 5.422 | 59.277 |
| 512 | 3,27341% | 14.986 | 9.430 | 80.918 |
| 4.096 | 4,18729% | 18.125 | 12.569 | 97.868 |
| 65.536 | 5,53960% | 22.310 | 16.754 | 120.465 |
| 1.000.000 | 7,03161% | 26.424 | 20.868 | 142.679 |

A última coluna extrapola `5556/30000`; partições e filtros são aleatórios,
portanto não garante a quantidade retida. Pior aceitação, FP positivos,
estratos raros e unidades de dependência maiores podem elevar muito o custo.
O cap16 foi preservado; não se ajusta cap a posteriori para aprovar o ensaio.
O cenário M=1 também ilustra o mínimo condicional para um modelo já congelado
avaliado uma única vez em TEST, com protocolo/alpha próprios e sem retuning.

Reproduzir sem ler corpus ou TEST:

```powershell
python scripts/v9-cluster-zero-error-sample-plan.py
python scripts/v9-cluster-zero-error-sample-plan.py --comparisons 65536
```

O script usa a expressão fechada de zero erros e verifica a fronteira inteira
n/n−1. Não implementa outro calibrador, scorer ou seletor. Ele não cobre FP>0;
os bounds medidos continuam sendo calculados pelo código C# existente.

## Disponibilidade de TEST e recursos

O gerador particiona por identidade aproximadamente 60/20/20. O corpus
histórico clean seed42 tem 30.000 pessoas-base e 58.148 observações; isso não
significa 30.000 identidades independentes em VALIDATION. Seus TEST e os das
seeds43/44 já foram consumidos como holdout histórico. Nenhum novo TEST lacrado
foi comprovado pelos artefatos desta revisão; não contabilizá-los como reserva
de certificação.

Um novo corpus deve ter seed/protocolo e manifestos registrados, TRAIN e
VALIDATION segregados e TEST com atributos/outcomes inacessíveis à seleção.
Abrir TEST somente após congelamento elegível. Se a amostra não suportar o
gate, declarar inconclusivo; ampliar após observar resultados requer protocolo
de extensão/sequenciamento ou nova amostra com orçamento estatístico próprio.

A busca grande aumenta M apenas logaritmicamente na fórmula, mas o custo de
SQL/scoring cresce com dados e configurações. O relatório integral de todas as
relações Pareto pode ser quadrático. Não truncar famílias ou escolher vencedores
por batch. Reusar resultados matematicamente idênticos é permitido: por exemplo,
o CP depende de n, identidades com erro, alpha e M, podendo ser cacheado por essa
tupla sem mudar resultados. Zero FP já tem cálculo fechado O(1). Nenhum benefício
de GPU foi medido; este estudo não autoriza alegar aceleração por GPU.
