# Gold Pessoa — universo definido pela âncora CPF, nome civil e nome social

**Status:** proposta de especificação para a candidata v5.00 (ainda não publicada) — revisão 6.
**Base analisada:** `master` em `e69d6f77`. A comparação `14b7130..e69d6f77` altera apenas workflows/scripts; não há mudança em `Solution/src` nem em `Solution/database`, portanto as referências técnicas herdadas da revisão 5 permanecem materialmente válidas.
**Subordinação:** Especificação Técnica vigente → `Arquitetura_Identidade_Linkage.md` (§1, §2, §5, §6) → este documento → implementação.

**Mudanças desta revisão**

- **Revisão 6:** fecha `codigoPessoaOrigem` como obrigatório na v5 e bloqueia produção para sistema de origem não certificado; explicita o rito de certificação sem fixar SLA institucional arbitrário; corrige T29 para separar origem sem CPF de outra origem não ancorada versus linkage de uma origem sem CPF para âncora existente; classifica a proibição de ligação entre duas origens não ancoradas como decisão de política pública ainda pendente de aprovação institucional; mantém `nomeAusenteMotivo` como proveniência da observação, não como atributo único da Pessoa Gold; restringe `EM_CONFLITO` a conflito de identidade/âncora; explicita a supremacia da âncora CPF quando uma origem já existente informa depois CPF ancorado em outro UUID; acrescenta diagnósticos de qualidade do linkage para posição da verdade, coortes sintéticas e saturação do posterior.
- **Revisão 5:** resolução em dois escopos. Dentro do namespace de código, a resolução é pelo código interno. Entre namespaces, é por observações: âncora CPF e, sem CPF, linkage para âncora. Entram a certificação da semântica do código por sistema de origem e a observação mínima contendo só o código.
- **Revisão 4:** o contrato passa a permitir nome nulo sem rejeição; o motivo fica opcional e sua falta é registrada como `NAO_INFORMADO_ORIGEM`. Entra a Parte D (contrato Pessoa), com a identificação posterior: estabilidade de `codigoPessoaOrigem`, usando a fusão e o desmembramento de UUID já previstos.
- **Revisão 3:** a ausência de nome passa a ser declarada por valor nulo com motivo (`nomeAusenteMotivo`), no mesmo padrão do CPF (RN-GP-03b). Valores textuais como "Desconhecido" deixam de ser uma forma aceita de declarar ausência.
- **Revisão 2:** nome obrigatório no contrato; tratamento de nomes sentinela; Parte B (nome civil e nome social). O D4 da revisão 1 ficou prejudicado.

---

## Parte A — Universo e composição de `gold.pessoa`

### A.1 Problema

A regra pretendida é que `gold.pessoa` contenha todas as pessoas identificadas por CPF, e somente elas. A implementação atual não aplica essa regra; o universo resulta de efeitos colaterais que falham nos dois sentidos.

**CPFs válidos ficam fora do Gold.** A composição exige nome, nascimento e nome da mãe não nulos:

- `SqlProcessorRepository.Materialization.cs:247`: `WHERE nome_src.nome_completo IS NOT NULL AND nasc_src.data_nascimento IS NOT NULL AND mae_src.nome_mae IS NOT NULL`
- `Jornada_Fase1.sql:2843`: mesmo filtro em `identidade.sp_recompor_gold_pessoa`

A migração `20260912_Nome_Mae_Anulavel.sql` tornou `nome_mae` anulável em Silver e Gold, mas o filtro não foi ajustado.

**Uma pessoa já publicada é apagada quando chega uma remessa mais nova sem algum campo.** Cada `OUTER APPLY TOP(1)` escolhe a observação mais recente sem excluir valores nulos. Com o valor selecionado nulo, `@src` fica vazio e a procedure executa `DELETE FROM gold.pessoa`.

**UUIDs sem CPF não estão impedidos.** Três pontos permitem isso:

- o schema aceita `cpf NULL` com `status_cpf IN ('SEM_CPF','EM_REGULARIZACAO')`;
- `CORRECAO_GOVERNADA` grava `RESOLVIDO` para qualquer UUID, sem exigir âncora;
- nada relaciona `gold.pessoa` a `identidade.cpf_ancora`.

**O CPF publicado vem da observação, não da âncora.** O valor sai de `identity_map` ou da observação mais recente.

**O mesmo núcleo tem duas implementações.** A composição existe em C# (`RefreshGoldPersonAsync`) e em T-SQL (`sp_recompor_gold_pessoa`), sem teste de paridade.

### A.2 Regras

**RN-GP-01 — Universo.** `gold.pessoa` contém exatamente os UUIDs *u* que atendem às três condições:

1. *u* consta em `identidade.cpf_ancora.pessoa_uuid`;
2. `identidade.pessoa.status = 'ATIVO'` para *u* (exceção em RN-GP-07);
3. existe ao menos um vínculo corrente `RESOLVIDO` para *u*.

Nenhum outro UUID é materializado. A âncora continua permanente mesmo sem vínculo corrente; só a linha Gold deixa de existir enquanto não houver vínculo (ver D2).

**RN-GP-02 — CPF da âncora.** `gold.pessoa.cpf` é obrigatoriamente o valor de `identidade.cpf_ancora.cpf` para o UUID, e `status_cpf` é sempre `PRESENTE`.

**RN-GP-03 — Obrigatoriedade no contrato e ausência no Gold.**

- O contrato de entrada aceita `nomeCompleto` nulo (Parte D). A ausência nunca rejeita a observação.
- `dataNascimento` e `nomeMae` são opcionais (ver §C).
- No Gold, `nome_completo`, `data_nascimento` e `nome_mae` são anuláveis. Nenhum campo cadastral é condição para a linha existir.

**RN-GP-03b — Ausência de nome é representada por nulo, com motivo quando conhecido.** A fonte que não possui o nome envia `nomeCompleto` nulo e, quando souber, `nomeAusenteMotivo` com um valor do enum versionado:

- `NAO_IDENTIFICADO`: pessoa atendida sem identificação possível, como em atendimento emergencial;
- `RECEM_NASCIDO_SEM_REGISTRO`: recém-nascido ainda sem registro civil;
- `RECUSA_INFORMAR`: a pessoa se recusou a informar;
- `NAO_INFORMADO_ORIGEM`: a fonte não informou o motivo.

A ausência não é motivo de rejeição. Se o nome vier nulo sem motivo, a Jornada registra `NAO_INFORMADO_ORIGEM`. Esse valor é metadado verdadeiro sobre a entrega, e não um dado inventado sobre a pessoa. A única combinação rejeitada é nome preenchido junto com motivo, que é contraditória.

`NAO_INFORMADO_ORIGEM` é monitorado por Gestor, porque distingue a pessoa genuinamente não identificada de uma exportação que perdeu o nome.

`nomeAusenteMotivo` permanece metadado da observação/proveniência. Ele **não é materializado como um motivo único em `gold.pessoa`**: fontes diferentes podem declarar motivos diferentes, e `NAO_INFORMADO_ORIGEM` descreve a entrega, não uma propriedade intrínseca da pessoa. O BI pode agregá-lo por observação, origem e Gestor.

Um valor textual que representa ausência ("Desconhecido", "Não identificado", "Fulano de Tal") não é forma válida de declarar ausência. Recebido, ele é preservado como original e tratado pela RN-GP-03a.

*Justificativa:* uma string padronizada continua sendo um valor mágico, sujeita a variação entre fontes e indistinguível, em nível de dado, de um nome real. O nulo com motivo torna a ausência explícita, auditável e impossível de confundir com evidência.

**Tratamento por camada:**

| Camada | Tratamento |
|---|---|
| Bronze | envio intacto |
| Silver | `nome_completo` nulo e `nome_ausente_motivo` preservados; nada é preenchido |
| Linkage | nome ausente é evidência `MISSING`; não gera concordância nem chave de blocking; dois registros sem nome nunca se ligam pelo nome |
| Gold | `nome_completo` nulo; a linha existe normalmente se houver âncora CPF (RN-GP-01) |
| Serving / interface | `nome_tratamento` apresenta o rótulo "Pessoa não identificada", constante de apresentação e não dado persistido |
| BI | contagem de registros sem nome por Gestor e por motivo |

**RN-GP-03a — Nomes sentinela (legado e não conformidade).** Esta regra cobre dados legados e fontes que ainda não adotaram a RN-GP-03b. Um valor que não é nome passa a ser detectado por uma lista versionada de padrões (`NOME_SENTINELA_V1`). Exemplos: "NÃO IDENTIFICADO", "DESCONHECIDO", "SEM NOME", "RN DE …", "RECEM NASCIDO", "IGNORADO".

- O valor original é preservado no Silver com qualidade `SENTINELA_PROVAVEL` (§6 da Arquitetura).
- Esse valor não é candidato na composição do Gold (RN-GP-04) nem evidência no linkage, onde conta como nome ausente.
- Se todas as observações elegíveis tiverem nome sentinela, `gold.pessoa.nome_completo` fica nulo.

A lista é um parâmetro versionado, e não literais no código. As ocorrências detectadas são reportadas por Gestor como **não conformidade ao contrato**, porque indicam uma fonte que deveria enviar nulo com motivo.

**RN-GP-04 — Composição por campo, ignorando ausência.** Cada campo é escolhido de forma independente, entre as observações elegíveis (RN-GP-05) com valor não nulo, não vazio e não sentinela. A ordem de precedência é:

1. valor verificado;
2. `verificado_em` mais recente;
3. `source_as_of` mais recente;
4. `pessoa_observacao_id` mais alto.

Se nenhuma observação trouxer valor utilizável, o campo fica nulo. Ausência numa remessa não apaga o valor vigente; retificação para "sem valor" exige ato explícito da fonte, fora do escopo deste documento.

**RN-GP-05 — Observações que compõem o núcleo.** Entram na composição somente observações com vínculo corrente `RESOLVIDO` e método `CPF_DETERMINISTICO` ou `CORRECAO_GOVERNADA`. Observações ligadas por `LINKAGE_PROBABILISTICO`:

- mantêm a atribuição factual ao UUID;
- não alteram campos publicados, inclusive o nome social (Parte B);
- não entram em `fontes_distintas` nem em `estado_concordancia`.

*Justificativa:* o registro Gold é o alvo de comparação do Linkage. Se um vínculo probabilístico pudesse alterar o representante, um falso vínculo mudaria o alvo e atrairia novos falsos vínculos.

**RN-GP-06 — Concordância com ausência.** A divergência é calculada campo a campo, somente entre valores comparáveis (não nulos e não sentinela):

- `DIVERGENTE`: algum campo tem dois ou mais valores comparáveis distintos;
- `CORROBORADO`: nenhuma divergência e mais de uma fonte;
- `BASELINE_FONTE_UNICA`: nenhuma divergência e uma única fonte.

Nome social diferente do nome civil **não** é divergência (RN-NS-02).

**RN-GP-07 — Pessoa em conflito de identidade (proposta, ver D1).** `EM_CONFLITO` nesta regra significa conflito de identidade/âncora que impede afirmar com segurança o núcleo publicado; não é sinônimo de mera divergência cadastral entre fontes. Um UUID com âncora e `identidade.pessoa.status = 'EM_CONFLITO'` permanece no Gold com:

- CPF da âncora;
- núcleo cadastral e nome social nulos;
- `estado_concordancia = 'CONFLITO_EVIDENCIA'`.

**RN-GP-08 — Implementação única.** A composição tem uma única implementação normativa, `identidade.sp_recompor_gold_pessoa`, chamada pelo Processor dentro da própria transação. Se houver motivo de desempenho para manter duas implementações, o teste de paridade (T8) passa a ser gate obrigatório.

---

## Parte B — Nome civil e nome social

### B.1 Situação atual

- `NOME_SOCIAL` existe como atributo transversal (`config/catalog/atributos-transversais.json`) e gera chaves de blocking como alias (`nome_social__*`, semântica `VERSIONED_ALIAS`).
- O score Fellegi–Sunter compara somente `nomeCompleto` × `nomeCompleto` (`FellegiSunterScoring`, evidência `NOME`). O blocking pode encontrar o candidato pelo nome social, e o score o penaliza em seguida.
- O Processor só promove para `gold.pessoa_atributo` atributos com `statusEvidencia = 'COMPROVADO'` (`SqlProcessorRepository.Persistence.cs:252`). Os tipos de evidência aceitos para `NOME_SOCIAL` são `DOCUMENTO`, `FONTE_INSTITUCIONAL` e `VERIFICACAO_PRESENCIAL`. Nome social apenas declarado pela pessoa nunca chega ao Gold.
- O contrato não declara se `nomeCompleto` é o nome civil. Uma Secretaria que envie o nome social nesse campo gera divergência com o nome ligado ao CPF, o que fragmenta a pessoa.
- A entrada do catálogo não tem classificação de sensibilidade.

### B.2 Regras

**RN-NS-01 — Semântica dos campos.**

- `nomeCompleto` é o nome civil.
- O nome social é informado somente no atributo `NOME_SOCIAL`.
- A documentação do contrato Pessoa deve explicitar isso.

A regra RN-NS-03 garante que um envio fora dessa convenção não fragmente a pessoa. A convenção continua necessária para a exibição (RN-NS-05) e para a proteção do dado (RN-NS-06).

**RN-NS-02 — Equivalência.** Nome civil e nome social são igualmente legítimos como nome da pessoa. Nenhum dos dois é tratado como erro, apelido ou evidência de segunda classe. Diferença entre eles não produz `DIVERGENTE`, `CONFLITO` nem penalidade de score.

**RN-NS-03 — Linkage por conjunto de nomes.** A evidência `NOME` passa a comparar conjuntos:

- o conjunto da observação é {`nomeCompleto`} ∪ {`NOME_SOCIAL` da observação, se houver};
- o conjunto do candidato é {nome civil publicado} ∪ {nome social vigente publicado, se houver};
- o estado de concordância é o **melhor estado** entre todos os pares observação × candidato;
- nomes sentinela são excluídos dos conjuntos antes da comparação.

Quem não tem nome social compara conjuntos unitários, e o resultado é idêntico ao comparador atual.

O comparador recebe versão própria (proposta: `NOME_CONJUNTO_V1`) e entra em nova versão de algoritmo; não altera retroativamente o V7. Os pesos m/u são produto da calibração, não fixados por equivalência legal (ADR-002). A equivalência legal está expressa no fato de os dois nomes formarem a mesma evidência.

**RN-NS-04 — Calibração estratificada.** Tomar o melhor estado entre vários pares aumenta a chance de concordância por acaso (u) para quem tem dois nomes. Por isso:

- o Calibrador estima e reporta m/u separadamente para os estratos "conjunto unitário" e "conjunto com dois ou mais nomes";
- a publicação de parâmetros únicos exige evidência de que os estratos não diferem de forma relevante;
- se diferirem, os parâmetros são publicados por estrato.

Essa verificação integra a #31.

**RN-NS-05 — Armazenamento e exibição.**

*Armazenamento.* O nome social é composto no Gold pelo mecanismo existente de `gold.pessoa_atributo`, com vigência e proveniência, respeitando RN-GP-05. **Não se copia o nome civil para o nome social** de quem não o declarou. O Gold precisa distinguir "declarou nome social" de "não declarou", e preencher o campo seria criar valor sintético, o que a §6 proíbe.

*Exibição.* O Serving expõe um único campo `nome_tratamento`, igual ao nome social vigente se existir, ao nome civil caso contrário, e ao rótulo "Pessoa não identificada" se nenhum dos dois existir (RN-GP-03b). A regra fica na projeção, não no dado.

**RN-NS-06 — Promoção por autodeclaração.** `NOME_SOCIAL` é promovido ao Gold também com `statusEvidencia = 'DECLARADO'`, com um novo tipo de evidência `AUTODECLARACAO` no catálogo. O nome social decorre de requerimento da própria pessoa, e exigir comprovação documental excluiria a maior parte dos casos. O enquadramento jurídico preciso deve ser confirmado pela assessoria jurídica (D5).

**RN-NS-07 — Proteção.** O dado sensível é a ligação entre dois nomes diferentes da mesma pessoa, porque ela pode revelar identidade de gênero. Por isso:

- `NOME_SOCIAL` recebe classificação sensível no catálogo;
- quando existe nome social, o nome civil só é exposto em projeções explicitamente autorizadas para finalidade que o exija, como atos registrais e pagamento;
- `nome_tratamento` pode ser exposto às projeções autorizadas que hoje recebem nome. Ele não expõe um **indicador explícito** de existência de nome social; ainda assim, conhecimento externo do nome civil pode permitir inferência, razão pela qual finalidade, autorização e minimização continuam obrigatórias;
- relatórios e BI não exibem indicador "possui nome social" por pessoa. Agregados seguem as regras de supressão de células pequenas vigentes, ou as que vierem a ser definidas.

---

## Parte C — Schema, consumidores e dependências

### C.1 Schema

- `gold.pessoa.nome_completo` e `gold.pessoa.data_nascimento` passam a `NULL`; `nome_mae` já é anulável.
- `cpf` passa a `NOT NULL`, e `ck_gold_pessoa_status_cpf` passa a aceitar somente `PRESENTE`.
- Nova FK `gold.pessoa.pessoa_uuid → identidade.cpf_ancora.pessoa_uuid`, coluna que já é `UNIQUE`.
- A tabela variável `@src` em `sp_recompor_gold_pessoa` declara os campos do núcleo como anuláveis.
- Novo tipo de evidência `AUTODECLARACAO` para `NOME_SOCIAL`, com classificação sensível no catálogo.
- Nova projeção Serving com `nome_tratamento` (RN-NS-05) e nome civil restrito (RN-NS-07).
- Novo parâmetro versionado `NOME_SENTINELA_V1`.
- `silver.pessoa_observacao.nome_completo` e `nome_cmp` passam a `NULL`.
- Nova coluna `silver.pessoa_observacao.nome_ausente_motivo`, com check análogo a `ck_pessoa_cpf_motivo`: `(nome_completo IS NOT NULL AND nome_ausente_motivo IS NULL) OR (nome_completo IS NULL AND nome_ausente_motivo IN ('NAO_IDENTIFICADO','RECEM_NASCIDO_SEM_REGISTRO','RECUSA_INFORMAR','NAO_INFORMADO_ORIGEM'))`. O Processor preenche `NAO_INFORMADO_ORIGEM` quando a fonte omite o motivo.

**Migração.** A candidata não foi publicada, então linhas existentes sem âncora são removidas; a contagem é registrada para auditoria. Os fatos não são afetados: `gold.beneficio_concedido` e `gold.servico_prestado` não têm FK para `gold.pessoa`. Em seguida, a migração recompõe todas as âncoras elegíveis de forma paginada e retomável, no mesmo padrão do backfill progressivo.

### C.2 Consumidores

| Local | Ajuste |
|---|---|
| `BlockingProjectionCandidateLoader.cs` | ler nome e nascimento como anuláveis; carregar o nome social vigente do candidato |
| `SqlProbabilisticIdentityLinkage.cs` (loader legado por data) | nascimento anulável; sem data, o bloco por nascimento não gera candidato |
| `BlockingProjectionPersistence.cs` | nome e nascimento anuláveis; campo ausente ou sentinela não gera chave |
| `LinkageCandidate` / `IdentityObservation` | tipos anuláveis; conjunto de nomes em lugar de nome único |
| `FellegiSunterScoring` / `IdentityComparison` | comparador `NOME_CONJUNTO_V1` sob nova versão de algoritmo; nome ausente conta como `MISSING` |
| Trigger `gold.tr_pessoa_nome_publicacao` | já tolera nome nulo; deriva do nome civil, sem alteração |
| Calibrador | estratos RN-NS-04; exclusão de sentinelas dos pares de treino |
| BI (tabelas que leem Pessoa) | inventariar; medidas não assumem núcleo completo; usar `nome_tratamento` |

### C.3 Semântica de contagem

"Pessoas" no Gold e no BI significa **pessoas identificadas por CPF**. A população sem CPF não desaparece:

- os fatos dela continuam no Gold como `PENDENTE_IDENTIDADE`;
- as origens continuam em `serving.v_identidade_origem_progressiva` como `PROVISORIA`.

O BI deve expor um indicador separado de **origens** sem CPF, sem chamá-las de pessoas.

### C.4 Dependências

- **Contrato Pessoa.** Especificado na Parte D. Um único change-set de contrato, como a §6 exige.
- **Publicação probabilística via composição.** Tema separado.
- **#31.** A ativação do comparador `NOME_CONJUNTO_V1` depende da calibração representativa.
- **Política sem âncora.** A decisão D11 deve ser resolvida antes da homologação: a restrição contra ligar duas origens não ancoradas reduz risco de falsa fusão, mas pode manter fragmentada a trajetória intersetorial de pessoas sem CPF. A solução deve medir esse efeito em vez de tratá-lo como detalhe técnico.

---

## Critérios de aceitação

**Universo e composição**

- **T1.** CPF com `nomeMae` e `dataNascimento` nulos gera linha Gold.
- **T2.** Uma remessa nova sem `nomeMae` não remove a linha nem anula o valor anterior.
- **T3.** UUID sem âncora não gera linha; a FK rejeita inserção direta.
- **T4.** `gold.pessoa.cpf` coincide com `cpf_ancora.cpf` para toda linha.
- **T5.** Gate de totalidade: o conjunto de `gold.pessoa` é igual ao conjunto de âncoras elegíveis (RN-GP-01), verificado por diferença de conjuntos nos dois sentidos, não apenas por contagem.
- **T6.** Observação ligada probabilisticamente não altera nenhum campo publicado, nem `fontes_distintas` ou `estado_concordancia`.
- **T7.** Observação sem nome da mãe, ao lado de outra com nome da mãe e demais campos iguais, resulta em `CORROBORADO`.
- **T8.** Paridade entre Processor e procedure (dispensado se RN-GP-08 eliminar a duplicidade).
- **T9.** Runner, loader e projeção de blocking operam sem exceção com nascimento nulo.

**Sentinelas**

- **T10.** "NÃO IDENTIFICADO" não é escolhido como nome Gold quando existe outra observação com nome válido. Se só houver sentinela, o nome fica nulo e a linha existe.
- **T11.** Nome sentinela não gera chave de blocking nem concordância de nome no score.

**Nome social**

- **T12.** Observação com nome social em `nomeCompleto` e candidato com o mesmo nome em `NOME_SOCIAL` produzem concordância de nome, sem penalidade.
- **T13.** Para quem não tem nome social, o score com `NOME_CONJUNTO_V1` é idêntico ao do comparador de nome único, nos mesmos parâmetros.
- **T14.** `NOME_SOCIAL` com `DECLARADO` e evidência `AUTODECLARACAO` é promovido ao Gold.
- **T15.** O Gold não contém nome social para quem não o declarou, e `nome_tratamento` retorna o nome civil nesse caso.
- **T16.** Projeção sem autorização para nome civil recebe apenas `nome_tratamento` para pessoa com nome social.
- **T17.** Nome civil diferente do nome social não produz `DIVERGENTE`.

**Nome ausente**

- **T18.** O contrato aceita nome nulo sem motivo, registrando `NAO_INFORMADO_ORIGEM`, e rejeita somente nome preenchido junto com motivo.
- **T19.** Pessoa com CPF, nome nulo e motivo `NAO_IDENTIFICADO` gera linha Gold com `nome_completo` nulo; o motivo permanece preservado na observação/proveniência e disponível para auditoria/BI, sem criar `gold.pessoa.nome_ausente_motivo`.
- **T20.** Duas observações sem nome, com demais campos iguais, não produzem concordância de nome no score.
- **T21.** `nome_tratamento` retorna "Pessoa não identificada" sem que o valor seja persistido em Silver ou Gold.

---

## Decisões a confirmar

- **D1.** Pessoa `EM_CONFLITO`: manter no Gold com o núcleo suprimido (recomendado) ou manter a remoção atual.
- **D2.** Âncora sem vínculo corrente: não publicar (recomendado) ou publicar com núcleo nulo.
- **D3.** Linhas `SEM_CPF`/`EM_REGULARIZACAO` existentes em DEV/HML: remover na migração (recomendado) ou falhar fechado.
- **D5.** Enquadramento jurídico da promoção por autodeclaração (RN-NS-06) e do acesso restrito ao nome civil (RN-NS-07). Deve ser confirmado pela assessoria jurídica antes da homologação.
- **D6.** Lista inicial de padrões de `NOME_SENTINELA_V1`. Recomenda-se levantá-la nas bases reais das Secretarias, e não arbitrar.

---

## Parte D — Contrato Pessoa (nova versão v5)

### D.1 Situação atual (v4, verificado em `config/contracts/gestores/*/pessoa/v4/pessoa.schema.json`)

- `required: ["nomeCompleto", "dataNascimento"]`, e `nomeCompleto` é `string` com `minLength: 1`. Um atendimento emergencial sem nome é rejeitado, e a fonte é empurrada para inventar um nome.
- `nomeMae` já é opcional.
- `codigoPessoaOrigem` não é obrigatório. Quando ausente, o Processor usa o CPF como código de origem (`ProcessorModels.cs:173–178`); sem CPF e sem código, a observação é rejeitada.
- No fluxo normal do Processor, não encontrei reatribuição dos fatos de observações anteriores da mesma origem quando uma versão nova é resolvida. A desativação de `vinculo_fonte` aparece só nos procedimentos governados. **Isso precisa ser confirmado por teste (T24) antes de ser tratado como defeito.**

### D.2 Regras

**RN-CT-01 — Campos cadastrais opcionais.** `nomeCompleto`, `dataNascimento` e `nomeMae` aceitam `null` ou ausência. Nenhum deles é condição de aceitação da observação.

**RN-CT-02 — Motivo de ausência do nome.** `nomeAusenteMotivo` é opcional, com o enum da RN-GP-03b. Só é válido com `nomeCompleto` nulo; nome preenchido junto com motivo é rejeitado.

**RN-CT-03 — Formato quando presente.** Um campo informado continua sujeito às regras de formato: `nomeCompleto` não vazio e até 500 caracteres; `dataNascimento` em data ISO válida. String vazia não é forma de ausência; ausência é `null`.

**RN-CT-04 — Estabilidade do código de origem.** `codigoPessoaOrigem` identifica a pessoa no sistema da fonte e deve permanecer o mesmo durante toda a vida do cadastro, inclusive quando a pessoa for identificada depois. A identificação posterior de uma pessoa atendida sem nome ou sem CPF é enviada como **nova versão da mesma origem**, com o mesmo `codigoPessoaOrigem`, e nunca como cadastro novo.

*Justificativa:* a Jornada não liga registros sem CPF entre si. Se a fonte criar um cadastro novo ao identificar a pessoa, o registro emergencial fica órfão, sem CPF, para sempre.

**RN-CT-05 — Código de origem obrigatório.** `codigoPessoaOrigem` é obrigatório na v5, e a derivação a partir do CPF deixa de existir para novas entregas. Derivar do CPF amarra a origem a um dado que pode chegar depois ou ser corrigido, e cada mudança criaria uma origem nova.

**RN-CT-06 — Identificação posterior pelo código interno.** O mecanismo principal da identificação posterior é o `codigoPessoaOrigem`, que é a rota determinística dentro do namespace Gestor + Sistema de Origem (§7 da Arquitetura). A identificação chega como nova versão da mesma origem, e a mesma origem tem o mesmo `initial_uuid`. Com isso:

- o nome, o CPF e os demais campos compõem o Gold pela RN-GP-04, sem linkage e sem decisão caso a caso;
- havendo CPF, a publicação determinística de `REFERENCIA` pela âncora vale para a origem;
- os fatos de versões anteriores da mesma origem acompanham a atribuição corrente da origem, porque pertencem ao mesmo cadastro no sistema da fonte. Isso não é inferência: a própria fonte declarou que é a mesma pessoa ao manter o código.

A composição reversível de UUID (fusão e desmembramento) fica para os casos entre origens diferentes, inclusive unificações feitas no sistema da fonte, sem campo novo no contrato.

O T24 verifica essa regra: depois da identificação, os fatos do atendimento anterior não podem permanecer `PENDENTE_IDENTIDADE`. Hoje o vínculo é por observação, e não encontrei no Processor a propagação para versões anteriores da origem. Se o T24 falhar, a correção é implementar a atribuição por origem, e não criar um mecanismo novo.

**RN-CT-07 — Dois escopos de resolução.**

*Intra (mesmo namespace).* Dentro do namespace Gestor + Sistema de Origem, a identidade de origem é resolvida pelo `codigoPessoaOrigem`, de forma determinística. A fonte é a autoridade sobre quais registros pertencem ao mesmo cadastro, e nenhum dado cadastral é necessário para isso. Fatos com o mesmo código pertencem à mesma origem e ao mesmo `initial_uuid`, mesmo que nenhuma observação traga nome, CPF ou outro campo.

*Inter (namespaces diferentes: outro sistema, benefício, serviço ou Secretaria).* A ligação entre origens é feita somente por observações:

1. âncora CPF, por rota determinística;
2. sem CPF, linkage probabilístico para uma âncora existente, conforme política homologada;
3. composição reversível de UUID (fusão e desmembramento) para correções e decisões governadas.

Uma origem sem CPF pode ser ligada probabilisticamente a uma **âncora CPF já existente**, conforme política homologada. Duas origens de namespaces diferentes que continuam **ambas sem associação a qualquer âncora CPF** não são ligadas automaticamente entre si. Essa última restrição é uma decisão de política pública, não mera consequência técnica, e permanece sujeita à confirmação institucional de D11. O código interno de uma fonte nunca é usado para ligar registros de outra fonte.

**RN-CT-08 — O escopo "intra" é o namespace, não a Secretaria.** Dois sistemas da mesma Secretaria têm namespaces distintos e são tratados como "inter". Eles só formam um único namespace se o Gestor declarar formalmente, no cadastro do sistema de origem, que compartilham o mesmo cadastro de pessoas com o mesmo código. A declaração é versionada, e a mudança dela passa por composição governada, não por reprocessamento silencioso.

**RN-CT-09 — Certificação da semântica do código.** A resolução intra só é válida para sistemas de origem cujo código tenha sido certificado no onboarding como:

- **por pessoa:** um código identifica uma pessoa, e não uma unidade de atendimento, episódio, prontuário local, família ou benefício;
- **estável:** o código não muda durante a vida do cadastro;
- **não reciclado:** um código nunca é reatribuído a outra pessoa, inclusive após exclusão ou inativação.

A certificação é registrada no cadastro do sistema de origem, com responsável e data. Para Produção, sistema não certificado **não pode entregar dados de Pessoa/fatos dependentes de Pessoa**: o onboarding falha fechado antes de a resolução intra ser usada. DEV/HML podem receber massa de validação em fluxo explicitamente não produtivo e sem publicar identidade operacional.

O rito mínimo de certificação inclui: declaração formal do Gestor; identificação do responsável; evidência de que o código é por pessoa, estável e não reciclado; identificação do namespace a que a declaração se aplica; data e versão da certificação; e registro da evidência/amostra usada na verificação. Mudança na semântica do código, reciclagem, fusão de cadastros ou alteração de namespace exige nova certificação antes do retorno à Produção. O prazo/SLA de certificação é parâmetro de governança a ser aprovado por Gestor e SGM/SEPE antes da homologação; não é arbitrado por esta especificação.

Se o código for por família ou por outro agrupamento que não é pessoa, o sistema não é certificado. A Jornada não tenta derivar pessoas a partir desse agrupamento.

**RN-CT-10 — Observação mínima.** Todo fato continua exigindo uma linha em `pessoas.jsonl` com o mesmo `codigoPessoaOrigem` na mesma entrega (regra vigente em `ProcessorModels.cs`). Como todos os campos cadastrais são anuláveis (RN-CT-01), essa linha pode conter só o código. Não se abre exceção para fato sem pessoa, o que quebraria a proveniência.

**RN-CT-11 — Supremacia da âncora CPF na identificação posterior.** `initial_uuid` é histórico da origem, não autoridade para deslocar um CPF já ancorado. Se uma origem criada como `initial_uuid=A` informar depois um CPF que já pertence à âncora `B`, a âncora permanece em `B`; a origem é reconciliada para a referência `B` e seus fatos acompanham a atribuição corrente. `A` continua auditável no histórico/composição reversível. O sistema nunca cria duas âncoras para o mesmo CPF nem transfere silenciosamente o CPF de `B` para `A`.

### D.3 Fragmento proposto de JSON Schema (v5)

```json
{
  "required": ["codigoPessoaOrigem"],
  "properties": {
    "codigoPessoaOrigem": { "type": "string", "minLength": 1, "maxLength": 255 },
    "nomeCompleto": { "type": ["string", "null"], "minLength": 1, "maxLength": 500,
      "description": "Nome civil. Nulo quando desconhecido; nunca usar texto convencional como 'Desconhecido'." },
    "nomeAusenteMotivo": { "type": ["string", "null"],
      "enum": ["NAO_IDENTIFICADO", "RECEM_NASCIDO_SEM_REGISTRO", "RECUSA_INFORMAR", "NAO_INFORMADO_ORIGEM", null] },
    "dataNascimento": { "type": ["string", "null"], "format": "date" },
    "nomeMae": { "type": ["string", "null"], "minLength": 1, "maxLength": 500 }
  },
  "allOf": [
    { "if": { "properties": { "nomeCompleto": { "type": "string" } }, "required": ["nomeCompleto"] },
      "then": { "properties": { "nomeAusenteMotivo": { "type": "null" } } } }
  ]
}
```

O bloco `allOf` existente para `cpf`/`cpfAusenteMotivo` é mantido. A v4 continua sendo a fronteira de compatibilidade para fontes antigas; a v5 não herda a derivação de `codigoPessoaOrigem` a partir do CPF.

### D.4 Critérios de aceitação

- **T22.** Atendimento com `nomeCompleto` nulo, sem CPF e com `codigoPessoaOrigem` é aceito, e o fato é materializado como `PENDENTE_IDENTIDADE`.
- **T23.** String vazia em `nomeCompleto` é rejeitada; `null` é aceito.
- **T24.** Origem enviada primeiro sem nome e sem CPF, e depois como nova versão com nome e CPF: os fatos da primeira versão passam a `ATRIBUIDA` à âncora do CPF, e o Gold mostra o nome. Este teste também confirma ou descarta o defeito descrito em D.1.
- **T25.** Duas versões da mesma origem com CPFs distintos seguem o tratamento de conflito de CPF previsto (anomalia de dados, correção governada), sem escolha automática.
- **T26.** O `initial_uuid` da origem é o mesmo antes e depois da identificação posterior.
- **T27.** Entrega com pessoa contendo só `codigoPessoaOrigem` e fato referenciando esse código é aceita. O fato é materializado com a origem e o `initial_uuid` corretos.
- **T28.** Dois sistemas da mesma Secretaria com o mesmo valor de código, sem declaração de namespace compartilhado, geram duas origens distintas.
- **T29.** Duas origens de namespaces diferentes que permanecem ambas sem associação a qualquer âncora CPF, mesmo com nome, nascimento e nome da mãe idênticos, não são ligadas automaticamente uma à outra; o gate fica marcado como política institucional pendente em D11.
- **T29b.** Uma origem sem CPF pode ser ligada probabilisticamente a uma Pessoa Gold já ancorada por CPF, conforme a política homologada; esse caso não é proibido por T29.
- **T30.** Sistema de origem não certificado é rejeitado para Produção antes da resolução intra. Um fluxo DEV/HML de certificação pode validar amostras, mas não publica identidade operacional.
- **T31.** Origem com `initial_uuid=A` que, em versão posterior, informa CPF já ancorado no UUID `B` não desloca a âncora para `A`: o CPF permanece ancorado em `B`, a atribuição corrente da origem e seus fatos passam a `B` conforme o mecanismo governado, e `A` permanece como histórico auditável da origem.

### D.5 Decisões

**Fechadas nesta revisão**

- **D8 — FECHADA.** `codigoPessoaOrigem` é obrigatório na v5. Não existe derivação automática a partir do CPF para novas entregas v5; compatibilidade anterior permanece no contrato v4.
- **D9 — FECHADA.** Sistema de origem não certificado é recusado em Produção até concluir o rito da RN-CT-09. DEV/HML podem ser usados para certificação em modo não produtivo. O SLA de onboarding/certificação deve ser definido institucionalmente antes da homologação e monitorado; não é codificado nesta especificação.

**Pendentes de confirmação institucional**

- **D10.** Quem certifica (RN-CT-09): o Gestor, sob sua responsabilidade como controlador, com registro na Jornada. A alternativa é uma verificação técnica adicional por amostragem feita pela SGM/SEPE.
- **D11.** Política para duas origens de namespaces diferentes que permanecem ambas sem CPF/sem âncora. A regra técnica candidata é não ligá-las automaticamente. A decisão deve ser aprovada institucionalmente porque afeta especialmente pessoas sem documentação ou sem CPF. A aprovação deve registrar responsável, justificativa, critérios de revisão e monitoramento de volume/tempo em `PENDENTE_IDENTIDADE` por Gestor/serviço; exceções seguem composição/correção governada, nunca união probabilística silenciosa.

---

## Anexo — texto proposto para `Arquitetura_Identidade_Linkage.md`, §6

Inserir após o segundo parágrafo da §6:

> A ausência de um campo cadastral é representada por valor nulo, nunca por texto convencional. Quando o contrato exige o campo, a ausência deve ser acompanhada de motivo declarado, em enum versionado, no mesmo padrão de `cpf`/`cpfAusenteMotivo`. Valores textuais que representam ausência ("Desconhecido", "Fulano de Tal", "Não identificado") não são forma válida de declaração: quando recebidos, são preservados como originais, classificados como `SENTINELA_PROVAVEL`, tratados como ausentes para composição e Linkage e reportados como não conformidade da fonte. Rótulos de apresentação, como "Pessoa não identificada", pertencem à camada de exibição e não são persistidos como dado.