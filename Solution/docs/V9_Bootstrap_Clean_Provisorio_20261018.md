# V9 — clean como bootstrap sintético provisório (18/10/2026)

## Decisão e limites
A V9 usa **clean** como cenário automático único de geração dos valores
**iniciais** de calibração, sem alterar o histórico das seeds/artefatos ou
declarar os valores como definitivos. O gerador C# conserva os perfis
`correlated`, `independent` e `field` **somente para reprodução histórica
manual**, não para seleção corrente, relatórios automáticos ou gate principal.
A referência demográfica IBGE-derived continua sendo **sintética**; frequências
de prenome/população não comprovam taxas de erro de cadastro. A escolha de
`clean` **não foi feita para atingir artificialmente recall de 95%**.

O calibrador único FS C# estima m/u/prior exclusivamente em TRAIN, compara
parâmetros no VALIDATION por métricas finais pós-Resolve com candidato
**identidade**, não observação; congela fingerprints, base e catálogo.
Sem TEST novo lacrado, FDR por identidade de suporte estatístico defensável,
paridade HML real e aprovação manual, não há promoção/ativação.
TF positiva ainda depende de denominadores/condicionamentos compatíveis.

## Hipóteses do gerador, NÃO taxas de divergência medidas

| Tentativa por observação | clean | correlated histórico |
| --- | ---: | ---: |
| alterar texto do nome completo | 6% | 12% + evento comum 18% |
| alterar texto do nome da mãe | 10% | 16% + comum 18% |
| alterar nascimento | 4% | 8% + comum 18% |
| corrupção comum dos três | **0%** | 18% |
| retirar nome da mãe | 5% | 18% |
| retirar data de nascimento | 1% | 5% |

As taxas acima não medem discrepância efetiva de pares.
No `clean`, evento de alterar DOB ou torná-la ausente por observação
é `1-(1-0,04)(1-0,01)=4,96%`. Sob *independência* entre pontas,
o risco aproximado de ao menos uma ponta afetada num par é
`1-(1-0,0496)^2=9,67%`. Isso **não é uma medição** de DOB
divergente: omite coincidência de duas datas corrompidas, seleção
de pares, partições, regras/gestores e distribuição de tipos de erro.
O workflow SQL `v9-physical-seed42-probe` agora publica em artefato
separado `field-quality-train-val.json` uma auditoria **observada**
apenas dos rótulos e pares verdadeiros TRAIN/VALIDATION, com hashes,
denominadores `ALL` e `NO_CPF`, TEST excluído das contagens.

## Evidência externa: aplicabilidade e comparação correta

**Brasil, Rio de Janeiro** — estudo de linkage entre Cadastro Único,
Estratégia Saúde da Família, prontuário, SIH e SIM reporta
**completude**, não exatidão nem discordância entre pares:

| Fonte | Nome completo presente | Nome da mãe presente | DOB presente |
| --- | ---: | ---: | ---: |
| Cadastro Único (CadU) | 100% | 99,6% | 100% |
| Cadastro Saúde da Família (FHR) | 100% | 100% | 100% |
| Prontuário eletrônico (EMR) | 99,2% | não disponível | 100% |
| Internações (SIH) | 96,6% | 90,2% | 100% |
| Mortalidade (SIM) | 99,8% | 96,6% | 97,2% |

Fonte: *Record linkage under suboptimal conditions for data-intensive
evaluation of primary care in Rio de Janeiro, Brazil* (2021), Tabela 4:
https://pmc.ncbi.nlm.nih.gov/articles/PMC8204416/

**Reino Unido** — RiO/SystmOne, pares conhecidos pelo NHS number:
primeiro nome divergente 2,36%, sobrenome 4,86%, DOB divergente 0,492%.
Nome da mãe não foi analisado; sobrenome pode mudar legitimamente.
Essas percentagens **por par e por componente** não se transferem
diretamente para probabilidade de corrupção de **nome completo
por observação** no Brasil.
Fonte: BMC Medical Informatics and Decision Making (2023),
https://doi.org/10.1186/s12911-023-02176-6

Harron et al. (2017) identificaram 0,11% de DOB ausente ou divergente
em admissão HES contra cadastro PDS, amostra de faixas etárias
específicas; não mediram nome/nome materno.
Fonte: https://doi.org/10.1186/s12874-017-0306-8

Outro estudo nacional brasileiro estima probabilidades **m** de
concordância (nome da pessoa 0,8523, nome da mãe 0,8448) no
contexto de deduplicação; **não** são frequências independentes de
erros individuais e não servem como entradas diretas de `ErrorProfile`.
Fonte: https://ijpds.org/article/view/446

**Veredito técnico:** clean é **mais plausível do que correlated**
para iniciar estudos brasileiros porque não impõe 18% de
corrupção simultânea. Sua ausência de mãe (5%) está dentro da
variação entre fontes reportada no Brasil (0% a 9,8%), e ausência
de DOB (1%) dentro da faixa observada (0% a 2,8%).
Mas a **corrupção da data (4% por observação)** pode ser
substancialmente pessimista comparada a registros britânicos.
Não há evidência brasileira pública suficiente para chamar as
taxas de divergência de `clean` de *representativas do município*.
Não inventar uma taxa municipal nem trocar limiares para passar CI.

## Governança

- Usar **somente clean** para novas execuções automáticas, seed fixada e
  snapshots novos com hashes; dados históricos correlated imutáveis.
- Ler `field-quality-train-val.json` e separar taxa por observação de
  divergência por pares M sem CPF **antes** de interpretar desempenho.
- Não reutilizar o TEST histórico consumido para seleção nem antecipar
  leitura do TEST lacrado novo. Não mudar truth nem reescrever corpus
  para encaixar taxas.
- Calibração em SQL/Gold **real**, quando houver amostras rotuladas
  independentes por gestor, substitui as hipóteses sintéticas. Os
  valores bootstrap NÃO são parâmetros operacionais definitivos.
- Não modificar V8, Gold/PROD/HML, referência IBGE original,
  NODE/NAS, JornadaLocal, DT-15 ou ativar V9 por esse estudo.


## Auditoria observada em SQL físico clean — run #39147073026

A HEAD `ce2ad3186da36b02b93ae97e6a5d5cf3a895a7ef` executou `JornadaHarness`
real descartável no GitHub Actions e publicou o
[artefato #12142909368](https://github.com/lucianox777/Jornada/actions/runs/39147073026/artifacts/12142909368),
incluindo `field-quality-train-val.json`, `physical-fs-resolve-diagnostic.json`
e `physical-source-resolve-3x3x3-TF0.csv`.
A auditoria inclui **46.780 observações TRAIN+VALIDATION**
(35.091 TRAIN, 11.689 VALIDATION; TEST excluído) e
**27.127 pares M verdadeiros entre gestores sem CPF**.
Nesta coorte as discordâncias observadas entre pares M sem CPF são
**11,6858% nome completo**, **27,2091% nome da mãe divergente ou ausente**
e **10,1449% nascimento divergente ou ausente**, contra
probabilidades **por registro** de tentativa de corrupção 6%/10%/4%
e de ausência 5% materna/1% nascimento.
Essas medições não estimam a qualidade cadastral de São Paulo.
A massa é input **inicial**; não é snapshot operacional `ref`.
Hashes e planilha de decisões finais foram consolidados em
[V9_Reconciliacao_Decisoes_Evidencias_Runners_20261018.md](V9_Reconciliacao_Decisoes_Evidencias_Runners_20261018.md). Nenhum resultado aqui é autorização
de ativação V9, TF positiva ou teste final.
