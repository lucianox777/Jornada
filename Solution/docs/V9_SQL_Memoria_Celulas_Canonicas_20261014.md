# V9 — SQL indexado em memória para explorar células de blocking sem atrasar a CI principal

## Objetivo e contrato

A prioridade continua sendo **recall de candidatos M ≥ 95%** nos conjuntos TRAIN e VALIDATION sob a hipótese `nome da pessoa obrigatório + (nome da mãe OU nascimento)`; medir simultaneamente U, fanout, M excluídos por abstenção e os denominadores antes de qualquer alegação de sucesso. Não confundir recall de candidatos com vínculos aceitos, FDR, reconhecimento de cidadãos reais, parâmetros m/u ou calibração final de Fellegi–Sunter.

O laboratório `Solution/scripts/v9-sqlite-memory-cells.py` executa consultas SQL verdadeiras em `sqlite3.connect(':memory:')`, disponível no Python 3 sem dependência externa. Esta escolha reduz preparação de infra, custo de runner e a frequência com que reconstruímos SQL Server durante **exploração**. Não se assume equivalência de planos, latência, collation ou índices físicos do SQL Server operacional. A seleção permanece não publicável e com `fdr_measured=false`.

## Proveniência obrigatória e divisão de etapas

O *input* deve ser o snapshot congelado de `v9-freeze-canonical-name-frequency.py`: `canonical_observations.csv` gerado pelo exportador C# `V9CanonicalObservationCsvExporter`, manifest/projection version exata `BLOCKING_PROJECTION_KEY_PROJECTOR_V4`, arquivo de frequências marginais TRAIN por **identidade-base distinta** e arquivo de marginais conjuntas TRAIN. Todos os arquivos têm SHA-256 conferido antes do teste. A saída não pode modificar o diretório congelado nem sobrescrever relatórios. Se a origem for alterada, abortar.

A frequência TRAIN é mantida independente da seleção de candidatos; a etapa em memória **não** estima TF usando as extremidades dos pares retornados. A escolha gananciosa de células por ganho M / (incremento U + 1) utiliza **somente TRAIN**; VALIDATION é executada sem ajustar novamente o plano. TEST não participa de frequência, escolha, avaliação, ranking nem model selection, e identidades compartilhadas entre splits disparam erro.

## Células e semântica

A prova atual de 14 passes e 4 células físicas SQL Server continua independente. Este laboratório compara **BASE própria simplificada** (nome normalizado+DOB e nome da mãe normalizado+DOB), UNIÃO das quatro células já modeladas e conjunto ampliado com nomes, sobrenomes, prefixos e nascimento:

- P fonético + último sobrenome significativo da mãe; P fonético + ano válido; último sobrenome da pessoa + M fonético; P fonético + qualquer sobrenome da mãe.
- Qualquer sobrenome da pessoa + M fonético; último sobrenome da pessoa + qualquer sobrenome da mãe.
- Primeiro nome da pessoa + primeiro nome da mãe, + último sobrenome da mãe ou + M fonético; P fonético + primeiro nome da mãe.
- Primeiro nome da pessoa + ano; prefixo 3 do primeiro nome + DOB exata, + prefixo 3 da mãe ou + último sobrenome da mãe.

**A presença de nome utilizável nas duas pontas** é obrigatória em qualquer célula; `MD` isolado abstém-se; CPF é separado no ramo determinístico. As representações `nome_normalizado` / `nome_fonetico` do snapshot são calculadas em C# (fonética `MetaphoneBR`). Os componentes de *sobrenome* obtidos dividindo tokens normalizados em Python são **proxies heurísticos não certificados**, mesmo que excluam algumas partículas e agnomes; o código exporta essa limitação de modo permanente. A referência IBGE de SOBRENOME é presença em qualquer posição, não marginal observada da última posição. A última posição significativa recebe a primeira chave de acesso, mas as demais permanecem elegíveis.

Nenhum corte de Jaro–Winkler é usado como igualdade indexável. Uma próxima etapa poderá computar Winkler em pares já recuperados, com o comparador C# do contrato, não como `B-tree` presumido. Nenhuma célula operacional é alterada.

## Índices, limites e resultados

Um SQLite realmente em memória materializa `observations` e `cellkeys`, com índice `PRIMARY KEY (cell,part,composite,oid)` e junção composta `cell+part+composite`, gestor diferente, partição igual e ordinais únicos. A chave composta é JSON estruturado, não concatenação ambígua com caracteres separadores. A query produz `DISTINCT` pares físicos por célula; `LIMIT cap+1` serve **apenas para detectar excesso**, abortando todo o teste, **nunca truncando** uma célula enorme para maquiar recall.

O relatório traz M/U de cada célula, contribuição exclusiva, incremento contra BASE, conjuntos BASE/UNIÃO-4/UNIÃO-AMPLIADA, elegibilidade, abstenções, recalls nos denominadores elegível e geral, tempo da execução SQLite, plano aprendido exclusivamente em TRAIN e sua projeção intocada em VALIDATION. U é candidato de não-vínculo, **não** ligação falsa aceita; o tempo é execução SQLite local, não P95 de SQL Server. M são gabaritos de mesma pessoa-base em gestores diferentes e não registros reais.

## Integração e gates seguintes

Depois que o motor em memória indicar uma configuração que chegue a ≥95% com U/fanout controlados, **reenviar somente as chaves vencedoras** ao harness SQL Server `JornadaHarness` e comparar *pair ids* e predicados completos, inclusive projeção C# `BrazilianNameComponents`, `MetaphoneBr`, normalização BIN2, DOB sentinela, aliases, partículas e agnomes. O teste de equivalência, latências, P95/P99, custos e planejamento de índices deve ser real. A aproximação de sobrenomes em Python não pode ser promovida sem substituição pela projeção C# versionada e a mesma materialização física.

O Calibrador só pode selecionar um ruleset congelado após medir M/U em desenho independente, população-base sintética de TRAIN para TF, suporte efetivo/ESS por conglomerados M e rede U, estabilidade das 81 células nominais e limiares de risco; depois VALIDATION, e finalmente TEST cego. Os 30 mil indivíduos sintéticos por seed não bastam por definição para certificar esse risco. Gates podem devolver **INCONCLUSIVO**.

A CI completa `jornada-ci` permanece inalterada e obrigatória; a workflow de até 8 minutos deste laboratório executa apenas Python `unittest` hermético para feedback cedo. Isso reduz *feedback latency* sem dispensar SQL físico, segurança nem merge protegido.

Não se toca em `JornadaLocal`, bancos HML/PROD, Gold, NODE/NAS, IBGE original, Run/Resolve operacional, V8, DT-15 nem m/u, TF ou prior publicados.
