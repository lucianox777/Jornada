# Gold Pessoa — identidade progressiva, completude e qualidade

**Status:** implementado no ciclo de identidade progressiva / PR #354.

A existência da representação canônica não depende de um núcleo cadastral completo. A Jornada separa quatro dimensões: **linhagem/existência**, **resolução da identidade**, **completude da representação** e **qualidade/concordância**.

## 1. Regras normativas

- `initial_uuid` é âncora imutável de linhagem. Ele nunca é evidência estatística nem participa de blocking, features, LLR, posterior, margem ou calibração.
- `gold.pessoa` aceita `NULL` em `nome_completo`, `data_nascimento`, `nome_mae` e `cpf`.
- `estado_identidade` é `PROVISORIA`, `REFERENCIA` ou `INDEFINIDA`.
- `completude_nucleo` é `COMPLETO` quando nome, nascimento e nome da mãe estão disponíveis; caso contrário, `PARCIAL`.
- `estado_concordancia` permanece independente da completude.
- A recomposição escolhe, campo a campo, a melhor evidência **não nula** disponível. Campo ausente não elimina a Pessoa da Gold.
- Se um `initial_uuid` passa a apontar para outra referência canônica, sua casca deixa a Gold corrente; a linhagem permanece no ledger e nos eventos.
- Somente `estado_identidade='REFERENCIA'` participa do corpus de candidatos e possui chaves de blocking.
- Se nenhuma regra de blocking puder ser executada, o resultado é `EVIDENCIA_INSUFICIENTE_PARA_BLOCKING`. Isso não equivale a “o universo foi pesquisado e não existe candidato” e não autoriza `NOVA_IDENTIDADE`.
- Raridade de nome pode integrar um modelo calibrado, mas não é chave determinística. Ausência na referência de frequência não prova unicidade.
- A obrigatoriedade de atributos pertence ao **schema versionado da fonte**, não à ontologia universal de Pessoa nem ao parser do Processor.

## 2. Composição da Gold

`identidade.sp_recompor_gold_pessoa` é a implementação canônica da composição. Processor e publicação do Linkage usam a mesma procedure, evitando duas regras concorrentes.

A procedure considera:

1. observações resolvidas pela visão de vínculo corrente;
2. observações cuja origem progressiva já referencia o UUID canônico;
3. para uma casca `PROVISORIA` ou `INDEFINIDA`, observações da própria origem cujo `initial_uuid` é o UUID materializado.

CPF, nome, nascimento e nome da mãe são selecionados independentemente. Conferência documental tem precedência, seguida de recência. A ausência de uma evidência não bloqueia a seleção dos demais campos.

## 3. Identidade parcial e Linkage

Uma Pessoa pode existir na Gold antes de estar resolvida como referência canônica. Essa representação é necessária para QC, rastreabilidade e evolução progressiva, mas não pode contaminar o corpus probabilístico.

Por isso:

- `PROVISORIA` e `INDEFINIDA` não geram nem mantêm `identidade.blocking_chave`;
- candidate generation e bootstrap usam somente `REFERENCIA`;
- uma `NOVA_IDENTIDADE` publicada ganha blocking ainda na transação de publicação, ficando visível ao run seguinte;
- ausência de nome no scorer é evidência indisponível/neutra enquanto não existir estado `NOME_MISSING` calibrado explicitamente;
- ausência de data no blocking legado impede a execução daquele blocking; não cria inferência negativa sobre a existência de candidatos.

## 4. Contratos de fonte

Os schemas v4 atuais continuam exigindo `nomeCompleto` e `dataNascimento` onde já os exigiam; `nomeMae` permanece opcional.

O Processor não replica mais essas obrigatoriedades em código depois da validação do JSON Schema. Assim, um contrato futuro específico de benefício/serviço pode admitir uma fonte parcial — por exemplo, uma folha de pagamento contendo CPF e concessão — sem alterar a ontologia, o DDL ou o runtime.

Quando um campo é obrigatório pelo contrato e não é enviado, trata-se de **não conformidade da integração**. Quando é opcional e não está disponível, trata-se de **estado de conhecimento**. Esses dois casos devem permanecer distinguíveis no QC.

## 5. CPF e qualidade

O CPF válido continua sendo rota determinística. A avaliação de consistência do CPF só pode declarar conflito quando existem evidências observadas suficientes; campo ausente não é divergência.

`status_cpf` distingue:

- `PRESENTE`;
- `SEM_CPF`;
- `EM_REGULARIZACAO`;
- `NAO_INFORMADO_ORIGEM`.

Condição documental da pessoa e ausência na fonte são fenômenos diferentes e permanecem visíveis para QC/BI.

## 6. Serving, QC e BI

`serving.v_pessoa` expõe `estado_identidade` e `completude_nucleo`.

`serving.v_bi_qualidade_pessoa` mantém os indicadores por observação e acrescenta o estado/completude da representação correspondente, inclusive para identidades progressivas ainda não referenciais.

`serving.v_bi_completude_pessoa` agrega volumes e ausências por estado de identidade, completude e status de CPF.

Consumidores podem impor requisitos próprios. Uma tela de atendimento, por exemplo, pode exigir nome para exibição sem transformar esse requisito de UX em condição de existência na Gold.

## 7. Invariantes

1. `initial_uuid` é linhagem, nunca evidência.
2. Nenhum atributo demográfico individual é condição universal de existência da Pessoa.
3. Ausência de evidência não é evidência de divergência.
4. Completude não altera, por si só, o estado de resolução.
5. Identidade não referencial não participa do corpus probabilístico.
6. Regras de obrigatoriedade pertencem ao contrato versionado da fonte.
7. A Gold representa o melhor conhecimento atual e pode evoluir ou ser corrigida sem perder a história de identidade.
