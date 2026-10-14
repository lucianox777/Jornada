# V9 — proveniência dos 18% e diagnóstico independente da corrupção sintética

**Escopo:** análise de sensibilidade e proveniência, **não** calibração de
parâmetros m/u/TF, revisão automática de recall, nem autorização de
ativação do blocking V9. A massa demográfica IBGE é sintética.

## Fonte e semântica efetiva

A hipótese `CommonCorruptionProbability = 0.18` foi introduzida
no código Python `gen_corpus_v2.py` pelo commit
`8dcf5b60580254087c6f36e18e6f24b9316a3922`, de 13/09/2026.
A tradução C# manteve-a em
`SyntheticCorpusV2Rules.Profiles["correlated"]`,
com os seis parâmetros por observação:

| perfil | nome | mãe | data | comum | mãe ausente | data ausente |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| clean | 0,06 | 0,10 | 0,04 | 0 | 0,05 | 0,01 |
| independent | 0,22 | 0,30 | 0,15 | 0 | 0,18 | 0,05 |
| **correlated (default)** | **0,12** | **0,16** | **0,08** | **0,18** | **0,18** | **0,05** |
| field | 0,35 | 0,45 | 0,28 | 0,22 | 0,40 | 0,12 |

A taxa **comum é um Bernoulli único por observação**. Quando dispara,
**tenta** corromper simultaneamente nome, mãe e nascimento, antes
de sortear independentemente ausência da mãe e data. Os eventos
individuais de nome, mãe e DOB são utilizados somente se o `common`
não disparou (operador `||` de curto-circuito).

Assim, a taxa de tentativa de corrupção do nome é
`1-(1-0,18)*(1-0,12)=27,84%`; da mãe
`1-0,82*0,84=31,12%`; e do nascimento
`1-0,82*0,92=24,56%`. Com DOB ausente sorteada **depois**,
a taxa nominal de DOB alterado ou ausente é
`1-(1-0,2456)*(1-0,05)=28,332%` por observação.
Uma corrupção textual pode não alterar o valor efetivo quando há
caracteres iguais ou condições de borda. `CorruptDate` C# foi
projetado para sempre alterar a data quando invocado.

**Outro 0,18** é a probabilidade de gerar **prenome composto**
em `SyntheticCorpusGenerator.MakePerson`: distribuição demográfica
sintética, independente da corrupção simultânea.

Os números são **hipóteses de engenharia**, não taxas estimadas
a partir de população de São Paulo, não parâmetros vindos do IBGE
e **não** possuem citação científica de origem no commit histórico.
`clean` é um cenário alternativo menos adversarial, também
**não calibrado** empiricamente. **Manter `correlated` como stress
test**, sem apagar evidências, e usar `clean` como estudo separado.

## Medição confirmada no artefato original seed 42

Corpus original congelado `v9-physical-sql-active-candidate-wrong-link-30k`,
workflow GitHub `38593005691`, pasta
`e2-demographic-primary/observacoes.csv`: **58.264 observações**,
**40.835 pares M** entre gestores (todos os pares verdadeiros,
**não** só os pares sem CPF usados pelo blocking). O relatório
`v9-synthetic-corruption-impact.py` vincula SHA-256 da observação
ao `generation-manifest.json`, sem exportar identidades nem nomes.

Resultados observados por rótulos do gerador:

| Medida | Base | Percentual |
| --- | ---: | ---: |
| Nome com corrupção efetiva etiquetada | observações | 27,741% |
| Mãe com corrupção efetiva etiquetada | observações | 30,736% |
| Data com corrupção efetiva etiquetada | observações | 24,370% |
| Mãe apagada | observações | 17,939% |
| Data apagada | observações | 5,152% |
| Os **três atributos com operações efetivas de corrupção no mesmo registro** | observações | **17,912%** |
| Ao menos uma ponta do par com as três corrupções | pares M entre gestores | **32,470%** |
| Data divergente **ou ausente** entre as duas pontas | pares M entre gestores | **47,758%** |
| Alguma ponta com alteração/ausência de DOB etiquetada | pares M entre gestores | 48,605% |

Os resultados empíricos dependem também da distribuição de
comprimentos de nomes, perdas por operação inefetiva,
ausências e campos efetivamente comparados. **Não confundir**
`common=true` com contagem efetiva de três operações; o bit
latente `common` **não é preservado no CSV histórico**.
O número 32,470% é de pares M do corpus, e não taxa FDR
de vínculos aprovados. Para estimar a contribuição específica
do evento latente `common`, é preciso instrumentar novo
gerador sem alterar retrospectivamente corpus/manifest antigos.

**Falha de proveniência detectada:** o antigo
`generation-manifest.json` SHA-validado **não traz o campo
`error_profile`**, apesar de `gabarito.json` registrar
`error_profile` e `declared_corruption_rates`. Consequentemente,
o relatório histórico não deve alegar que o perfil foi comprovado
**pelo manifest sozinho**. A correção acrescenta `error_profile`,
`error_profile_provenance` e taxas declaradas aos **novos**
manifests, sem reescrever o artifact de 30 mil já congelado.
O auditor identifica explicitamente o legado incompleto.

## Literatura e limites da comparação

- Harron, Hagger-Johnson et al. (2017),
  *Utilising identifier error variation in linkage of large
  administrative data sources* (BMC Medical Research Methodology,
  DOI 10.1186/s12874-017-0306-8), relataram **0,11%** de
  divergência ou ausência de DOB **por registro**, comparando
  HES com PDS via NHS number em faixas etárias específicas.
- *De-identified Bayesian personal identity matching...*
  (PMCID PMC10163749) registrou **0,492% de DOB divergente em
  pares RiO–SystmOne vinculados pelo NHS number**, e 93,3% dos
  casos tinham divergência de apenas um dos componentes da data.
- Ambos são registros de saúde do Reino Unido: sem evidência
  municipal governada, **não** são estimadores diretamente
  transferíveis aos sistemas de cadastro social paulistanos.
  **Tampouco se pode concluir que corrupção cruzada de
  identificadores inexiste na literatura**: Harron et al.
  observaram alguma dependência entre erros de data e sexo,
  além de diferenças organizacionais.

## Próximo ensaio: sensibilidade clean versus correlated

Gerar **dois corpora C# independentes**, usando os mesmos dados
de referência IBGE, mesmo modelo demográfico e mesmas seeds
42/43/44, com **`--error-profile clean` e
`--error-profile correlated`**. **Não** trocar rótulo
em um corpus existente, nem editar observações a posteriori:
o PRNG percorre ramos diferentes e a operação efetiva depende
da forma do nome e da data.

Congelar fingerprints de entrada, regras/PRNG, fonte, perfil,
frequências TRAIN, gabarito, elegibilidade P+(M ou D), e
comparar os MESMOS métodos físicos SQL Server ou SQLite
dois cenários **sem selecionar com TEST**. Registrar:
M/U, recall global e elegível, grau médio U `2U/N`,
P95/P99 por gestor, abstidos e o limite inferior
cluster-bootstrap por identidade-base. A seleção de
blocking continua automática **dentro do orçamento governado**;
o resultado do `clean` é sensibilidade, não nova verdade municipal.

Quando houver dados reais rotulados e governados das Secretarias,
estimar taxas por campo, ausência e dependência de
corruptelas por origem, período e gestor, com intervalos
de incerteza, para propor um perfil representativo.
A DT-23 diagnostica suficiência e qualidade automaticamente;
**calibrar e ativar o modelo são decisões manuais**, nunca
disparadas apenas por disponibilidade de amostras.

Não reduzir artificialmente o erro sintético para passar
o alvo ≥95%. Não alterar V8, Gold, FS/TF/m/u, IBGE original,
NODE/NAS, JornadaLocal, HML/PROD, nem abrir TEST de seleção
antes de congelamento de parâmetros.
