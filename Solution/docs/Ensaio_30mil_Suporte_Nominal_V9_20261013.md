# V9 — medição efetiva do bootstrap sintético 30 mil (13/10/2026)

Esta etapa liga a geração **já existente** de 30.000 pessoas (seed 42, perfil `DEMOGRAPHIC_PRIMARY_V1`, marginais IBGE versionadas e nascimento diário versionado) a um relatório **efetivamente calculado sobre as observações geradas**: `v9-joint-support.json`. O relatório não contém nomes, CPFs, mães ou IDs individuais, somente contagens agregadas, método, versão e fingerprint de insumos; a execução da CI publica o arquivo como evidência. A mera existência do código não equivale à observação dos números: conferir o artefato e os logs do run associado ao HEAD.

## O que é mensurado

- `TRAIN`, `VALIDATION`, `TEST` segregados por `BasePersonId` do gabarito. Sem cruzamento de indivíduos entre partições.
- Um par **M verdadeiro** por indivíduo-base com duas observações de Gestores diferentes. Grupos com menos de duas observações desse tipo são contabilizados como sem par M.
- Pares **U verdadeiros** de pessoas-base distintas, obtidos por adjacência determinística dentro de bucket de **primeira letra do nome**, sempre na mesma partição. Isso é um **proxy amostral explícito**, *não* é blocking operacional.
- Para M e U, aplica-se **o classificador V9 C#** `CompositeNominalComparisonV9.Classify` a nome e nome da mãe e acumula-se a matriz completa 9×9 = 81 células, inclusive as vazias. O relatório indica quantas células não têm M, não têm U ou não têm as duas classes observadas.

## O que ainda NÃO está demonstrado

- A seleção U por primeira inicial/adjacência **não equivale** aos passes SQL `VERSIONED_ALIAS` nem fornece probabilidades de inclusão; por isso **não calcular m/u populacional, ESS, intervalo, FDR nem limites de ativação** a partir desta seleção. Contagens de pares não são contagens de grupos independentes.
- Não há nesta rodada comparação do scorer C# V8 × V9 no **mesmo holdout SQL**, calibração TF por snapshot congelado, seleção de limiares, relatório de FP/recall ou certificado TEST. A suficiência estatística dos 30 mil para as 81 células **não é afirmada**, qualquer que seja o número observado.
- Nenhuma execução ativa `V9` ou altera o modelo V8 histórico. DT-15, HML e produção continuam fora do escopo.

**Condição de avanço:** a próxima rodada deve usar as mesmas identidades/partições e os passes SQL reais e confrontar esse diagnóstico proxy com o frame elegível materializado, antes de concluir suficiência por estrato, pesos TF ou decisões.
