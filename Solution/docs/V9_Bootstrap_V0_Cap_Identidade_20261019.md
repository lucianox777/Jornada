# V9 — candidato experimental v0 e cap populacional

O candidato solicitado (`clean`, threshold 0,95, margem 0,5, TF 0) foi preservado
sem treinamento adicional. Os bytes vieram do artefato 12227823619 da execução
[SQL física](https://github.com/lucianox777/Jornada/actions/runs/39338667251),
HEAD `b334f7d2fa99783df3c3d698a5a36055165fbffc`.

- Snapshot: [`613f9141…6953d.fitted.json`](../data/calibration/v9/bootstrap-v0/613f914114073d4a9100e848a01a56321354ae9a7885a77462941ae86936953d.fitted.json).
- SHA-256 integral: `613f914114073d4a9100e848a01a56321354ae9a7885a77462941ae86936953d`.
- [Recibo e métricas originais](../data/calibration/v9/bootstrap-v0/experimental-reference.json).
- Reprodução create-only: `python scripts/v9-freeze-experimental-bootstrap.py <zip-verificado> <diretorio-novo>`.

O leitor C# recarregou o snapshot canônico, com 81 células m/u conjuntas e os
mesmos parâmetros e passes. Nenhum treino, TEST ou escrita SQL foi necessário.
O artefato é uma referência experimental escolhida explicitamente pelo usuário.
Não constitui saída elegível do seletor, envelope certificado ou aprovação DT-15.

## O cap correto do gerador base é 4

`SyntheticObservationMultiplicityContract` explicita o suporte já usado pelo
gerador: contagens 1/2/3/4, pesos 40/34/18/8. A sequência do RNG e os CSVs não
mudam. A média de observações não fundamenta o cap. A materialização recusa
gerações com mais de quatro observações por identidade antes de escrever arquivos.
O manifesto passa a declarar versão, suporte, pesos, máximo e escopo.

O plano pode fixar `ObservationCapDeclaration`, incluindo SHA do manifesto e CSV.
A CLI verifica esses bytes e o output declarado no manifesto antes de conectar ao
SQL. A leitura confere o cap em TRAIN/VALIDATION incluindo observações com CPF,
antes de filtrá-las. TEST continua excluído, inclusive seus atributos e resultados.
O coordenador também recusa frames experimentais acima do cap declarado.

O cap vale para o corpus base desse gerador. Não se estende a ondas de ingestão,
concatenação de massas, importações arbitrárias ou à população real. O limite de
16 membros de um grupo candidato cego continua sendo outro parâmetro. Não se
altera esse parâmetro do snapshot histórico ao corrigir o cap de fontes.

## Resultado e sensibilidade, sem nova certificação

No VALIDATION histórico: 7.505 TP, 0 FP, recall 95,7148% (7.505/7.841), 176
conflitos e 2.615 não resolvidos entre 10.296 observações; 5.556 identidades-base.
Zero FP observado não demonstra risco populacional nulo.

Aplicando a mesma fórmula C# CP/Hoeffding, alpha 0,05 dividido entre numerador e
denominador e todas as opções, aos totais já medidos:

| Comparações | Cap 16 | Cap 4 |
|---|---:|---:|
| 1 | 1,0025% | 0,2078% |
| 36 | 2,2230% | 0,4191% |
| 2.493 | 3,9615% | 0,6774% |

São limites **condicionados à hipótese IID** e a uma população com esse cap,
recalculados retrospectivamente. O valor 0,6774% não é aprovação estatística.
O protocolo novo `prepare-cap4` fixa o cap antes do ensaio, mantém os 1.233 pontos
e contabiliza conservadoramente 2.493 comparações anteriores: M=3.726. O cenário
analítico de contagens idênticas daria 0,7023%; só o artefato de execução pode
confirmar o resultado físico desse novo protocolo. O histórico integral anterior
de buscas ainda não foi autenticado; a tabela continua exploratória.

## Critério explícito de independência

Manifesto do gerador e SHA provam qual contrato e qual massa foram declarados.
Não provam independência dos resultados finais. A observação de uma pessoa pode
competir com outra no blocking, no Gold e no Resolve. Reamostrar identidades
depois dessas decisões não desfaz a dependência. Bootstrap sem divergência é
diagnóstico de estabilidade, insuficiente para justificar Clopper–Pearson IID.

`ClusterIidIndependentlyValidated` só pode abrir a seleção depois de uma revisão
estatística independente que vincule ao protocolo e aos SHAs: unidade amostral,
processo de geração/amostragem, todas as fontes de dependência após Resolve,
justificativa matemática de validade da concentração usada, cap populacional,
contagem completa de comparações e critérios definidos antes de nova avaliação.
Se a independência de identidades não for defensável, é necessário mudar a unidade
e o estimador para um desenho com unidades independentes ou usar limite válido
sob a dependência demonstrada. Não basta emitir uma atestação textual; o método
e suas condições precisam ser verificáveis. O código produtor conserva `false`.

## Runner real e próximos gates

O próximo ensaio deve usar o Runner e a recomposição Gold existentes, em
JornadaHarness isolado, com ingestão via API/Processor e ordem de ondas fixada
antes da execução. Não deve derivar UUIDs Gold de `BasePersonId`. Os rótulos só
podem entrar na avaliação posterior. Deve registrar ligações falsas, reavaliações,
conflitos, cobertura e latência após persistência, além dos SHAs do modelo, corpus,
ordem de ingestão e estado inicial do Gold. O replay de coorte atual não mede isso.

Há uma diferença de contrato a resolver antes desse ensaio: o v0 embute a política
experimental `BLIND_FS_COMPLETE_LINK_CANDIDATES`; o Runner consulta identidades
Gold. Ignorar esse campo silenciosamente mudaria a configuração efetiva. Qualquer
adaptação precisa de protocolo e identidade explícitos, mantendo os parâmetros
ajustados e a linhagem. O loader operacional ainda não consome o fit integral;
o gate V9 segue preservado. Não foi acrescentado outro agrupador paralelo.

TF positiva permanece fora do v0. Uma versão seguinte precisa definir frequência
e U no mesmo universo TRAIN SQL, com unidade e condicionamento comuns e papel
explícito do IBGE na suavização; não apenas trocar um operando da fórmula.

TEST novo fica reservado para avaliação única depois do congelamento certificado
e da definição do desenho estatístico; o v0 experimental não autoriza abertura.
Persistência em `ref`, binding integral do modelo e aprovação DT-15 continuam
necessários para HML. A PR #1156 já está encerrada por substituição pela #1157.
O conteúdo da #1134 foi consolidado na #1158, ainda draft. Nenhuma branch foi
apagada; reconciliação de histórico não autoriza remoção em massa das branches.

## Validação local deste incremento

SDK 10.0.113; build Release da solução com zero avisos/erros; suíte completa
`Jornada.Tests`: 1.777 aprovados, zero falhas/skips. A regeneração `clean` seed42
com 30 mil pessoas produziu as mesmas 58.148 observações e CSV SHA-256
`28bbb9f4c63c97c353e7f29cdbc77091a4773b2f4c596e46103edadd93635d17`.
O plano cap4 foi criado antes de qualquer execução física nova, com M=3.726.
O script reproduziu os bytes e o recibo experimental exatamente a partir do ZIP
fixado. A CI física nova deve confirmar materialização, modelo idêntico e uso
efetivo do cap no bound; esses resultados não são presumidos por este documento.
