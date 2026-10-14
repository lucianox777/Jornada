# V9 — experimento controlado: comprimento do prenome, inicial da mãe e nascimento

**Natureza:** diagnóstico original seed 42 e protocolo pré-registrado
com 34 células SQL indexadas SQLite `:memory:`. **Não** altera o
blocking ativo, a política CPF, os dados do IBGE, o scorer FS nem o Calibrador.
TEST permanece fechado.

## Questão experimental

Variações de 1, 2, 3 e 4 caracteres **do primeiro token** do nome
da pessoa, combinadas com nascimento exato, ano+mês ou nome da mãe
em 1, 2, 3 e 4 caracteres. Se existe nome válido da pessoa e
nascimento utilizável, PM não é obrigatório: o estrato **P+D** deve
continuar disponível mesmo quando a mãe está ausente.
Quando não existe DOB, testar **P+M** com inicial da mãe,
nome integral da pessoa ou fonética, sem exigir D.

**Regras de integridade:** a projeção integral e fonética são produzidas
pelo exportador C# existente; a divisão do primeiro token e comprimento
do prefixo ainda constituem transformação **experimental Python**, não
paridade comprovada com `BrazilianNameComponents.FirstName`. O uso
de uma letra nunca pode, por si só, estabelecer vínculo. Um índice
composto contém pelo menos uma assinatura da **pessoa** e da **mãe
ou nascimento**, jamais M+D sem pessoa.

Configurações: `P1..P4+DOB`, `P1..P4+YEAR_MONTH`,
`P1..P4+M1+DOB`, `P1..P4+M1+YEAR_MONTH`,
`P2..P4+M2/M3/M4+YEAR_MONTH`, `P2..P4+M1+MONTH_DAY`,
`P1..P4+M1` sem DOB, `P_FULL+M1` sem DOB,
`P_PHON+M1` sem DOB. Total **34 variantes predefinidas**.

O fluxo usa o mesmo snapshot congelado C# e seu SHA-256 e compara
TRAIN e VALIDATION **sem ajuste**; verifica splits por identidade-base,
exclusão CPF, M verdadeiros globais e elegíveis, abstenções,
M e U de cada célula, grau médio `2U/N`, P95/P99 incluindo zeros,
e M/U incremental sobre a referência `P3_DOB` no MESMO conjunto
de observações. Os incrementos **não** são ganhos sobre o plano
otimizado de dez células e não podem ser somados entre variantes.
Pré-censo SQL por chave/gestor conta colisões antes do join:
células acima de cap são rotuladas `CAP_EXCEEDED_UNMEASURED`,
nunca truncadas ou tratadas como candidatas válidas.

## Sinal preliminar independente: fonte original, normalização proxy

Um ensaio **separado de triagem** sobre o CSV bruto original
da semente 42 (30 mil pessoas-base), com normalização Python
NFKD e primeiros tokens, produziu estes valores em TRAIN.
**São proxies exploratórios, NÃO o resultado deste workflow canônico
C# + SQLite nem evidência de paridade com SQL Server**:

| Célula | M | U |
| --- | ---: | ---: |
| P1 + nascimento exato | 10.618 | 853 |
| P2 + nascimento exato | 10.505 | 378 |
| **P3 + nascimento exato** | **10.364** | **178** |
| P4 + nascimento exato | 9.979 | 97 |
| P2 + ano/mês | 14.821 | 10.280 |
| **P3 + ano/mês** | **14.491** | **5.159** |
| P4 + ano/mês | 13.832 | 2.908 |
| P2 + M1 + ano/mês | 9.721 | 601 |
| **P3 + M1 + ano/mês** | **9.509** | **267** |
| P3 + M2 + ano/mês | 9.302 | 138 |
| P3 + M3 + ano/mês | 9.031 | 108 |
| P3 + M4 + ano/mês | 8.359 | 75 |
| P2 + M1 **sem DOB** | 13.247 | **609.134** |
| P3 + M1 **sem DOB** | 12.907 | **306.902** |
| P4 + M1 **sem DOB** | 12.272 | **179.897** |
| P1 + M1 **sem DOB** | não medido | cap 1,2 mi ultrapassado |

Os números demonstram o **trade-off** entre cobertura e colisões, não
que qualquer variante seja melhor do que a união operacional ou o
planejamento de dez células. Sob P3+ano/mês, reduzir M de três para
uma letra aumentou M recuperados de 9.031 para 9.509 (+478), mas
U de 108 para 267 (+159); as células são aninhadas nesta comparação.
É ganho potencial para estudar, não aprovação de índice.
Para estratos PM sem DOB, uma inicial materna gera U demais.
O DOB + mês exato preserva PD e oferece recuperação intermediária
entre DOB completo e ano sozinho.

## Camadas interativa / lote / tempo: hipótese, não SLA aprovado

Camada 1: usar conjunto seletivo sob **orçamento parametrizado**
de grau U e latência SQL Server, mantendo capacidade de
**abstenção**, não um recall interativo pré-fixado de 83–90%.
A recomendação de **1 segundo** é candidata e carece de medição.
Camada 2: executar busca mais larga **sob demanda/suficiência** para
casos sem **vínculo confiável aceito** — não só casos `NAO_RESOLVIDO
sem candidato`, porque uma pessoa pode receber candidatos U
e nenhum M. Restringir a quantidade de consultas não garante custo
baixo por consulta: cada uma pode enfrentar a população inteira;
precisa de fila idempotente, quota, monitoramento de custo e política
de desempate/abstenção, sem decisões probabilísticas automáticas
fora do fluxo autorizado. Camada 3: CPF tardio **válido** ou nova
observação pode recuperar links adicionais de modo determinístico
ou tornar comparações possíveis, mas não está garantido chegar,
e não integra o recall do blocking atual sem uma métrica longitudinal
com horizonte definido.

**Não reduzir a meta normativa de 95% silenciosamente**: se for
adotado serviço em camadas, registrar formalmente a meta separada
de recall interativo, lote e acumulado, tempos máximos, perdas
por abstenção e risco FS/FDR, e testes de carga em SQL Server real.

## Residuais e gerador

Com sobrenomes estruturados C# na semente 42, a UNIÃO de 19 células
deixou **626** M elegíveis sem recuperar em TRAIN, **não apenas 526**.
Destes, 426 tinham apenas assinatura da pessoa, 100 nenhuma
assinatura de pessoa, e **100 outros** ainda apresentavam
sinais compartilhados em pessoa e mãe/nascimento. Fora dos
elegíveis havia mais **383** M no denominador global. Portanto,
526 são um subconjunto de difíceis, não o total residual.

O gerador pode ter múltiplos erros simultâneos no nome, mãe e DOB;
a contagem de corrupção simultânea dos M residuais exige os **mesmos
IDs sintéticos do corpus** e pares da união canônica, que não
fazem parte dos relatórios agregados. Não inferir porcentagens nem
ajustar o gerador para produzir um recall mais confortável.
Inspecionar padrões de erro com respeito à proveniência e à amostra
original antes de rever qualquer meta.

**Limites de evidência:** candidato U não é FP aceito/FDR; extrapolar
`2U/N` uniformemente por 380 não garante ordem de grandeza na
cidade, depende da distribuição conjunta das chaves e gestores.
Não se conhece P95 físico de consultas SQL Server, custo noturno,
TF/prior recalibrados, nem certificação FDR. Resultados de uma
semente sintética não permitem abrir TEST nem ativar V9/DT-15.
