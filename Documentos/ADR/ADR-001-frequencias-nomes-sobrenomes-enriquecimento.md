# ADR-001 — Frequências de nomes e sobrenomes como referência interna e enriquecimento materializado

- **Status:** Aceita; referência interna, replay da referência e materialização da chave semântica de `NOME` implementados; `SOBRENOME` e uso estatístico permanecem condicionados à semântica/metodologia explícitas
- **Data:** 2026-09-12
- **Escopo:** identidade, linkage probabilístico, Calibrador, modelo físico e replay

## Contexto

A Jornada usa informação populacional sobre frequência de nomes e sobrenomes como evidência auxiliar no linkage probabilístico. A fonte de referência é o produto **Censo Demográfico 2022 — Nomes no Brasil**.

A decisão atende a quatro requisitos:

1. o Calibrador não deve depender de consulta externa ao IBGE em tempo de execução;
2. o resultado usado pelo linkage deve ser reproduzível em replay;
3. atributos usados no blocking/scoring devem poder ser materializados e indexados quando sua semântica estiver fechada;
4. a semântica incorporada pela Jornada não deve ser adulterada para acomodar convenções internas nem carregar sufixos de fonte como `_ibge` no domínio.

## Decisão

### 1. Internalizar a referência estatística

A distribuição de frequências de nomes e sobrenomes é mantida no banco da Jornada como **dado de referência interno, versionado e imutável por versão**.

Uma nova edição da fonte não sobrescreve silenciosamente a anterior. O modelo vigente representa somente granularidades efetivamente presentes no snapshot incorporado:

```text
referência de frequência
- tipo: NOME | SOBRENOME
- valor e valor normalizado
- sexo, quando publicado
- período/década de nascimento, quando publicado
- escopo geográfico: BRASIL | UF | MUNICIPIO
- frequência
- versão da referência
- data de referência
- cobertura e semântica de ausência
```

### 2. Preservar a semântica publicada de NOME e SOBRENOME

A Jornada absorve os conceitos de domínio sem acrescentar `_ibge` aos nomes dos atributos apenas para indicar origem. Proveniência é metadado, não semântica do atributo.

A Nota Técnica 01/2025 informa que o campo de nome podia conter primeiro nome ou nome composto, mas para divulgação foi considerado **somente o primeiro nome informado**. Sobrenomes eram coletados em campo separado e suas frequências são publicadas independentemente da posição em que foram registrados.

Consequências:

- `SOBRENOME` não significa “último nome”;
- não serão criadas representações canônicas `sobrenome_1`, `sobrenome_2`, `sobrenome_3` etc.;
- a Jornada não reconstruirá sobrenomes do IBGE a partir dos tokens restantes de `nome_completo`, porque a fronteira original entre nome/nome composto e sobrenomes não existe no cadastro atual;
- a transformação de `NOME` publicada é explicitamente versionada por `IBGE_CENSO_2022_NOMES_PUBLICACAO_V1`;
- normalização permanece separada da semântica publicada e possui versão própria.

### 3. Materializar somente a chave semântica estável em `gold.pessoa`

A frequência populacional **não** é materializada como valor fixo da pessoa, porque ela depende da versão de `ref.frequencia_nome` fixada no modelo/run. Materializar a frequência em Gold faria uma mesma pessoa carregar um número que muda conforme a referência histórica usada no replay.

Em vez disso, a Jornada persiste em `gold.pessoa` a chave semântica estável necessária à consulta da referência versionada:

- `nome_publicacao_normalizado`;
- `nome_publicacao_metodo_versao`;
- `nome_publicacao_normalizacao_versao`.

A chave é produzida a partir de `silver.pessoa_observacao.nome_cmp`, isto é, do normalizador canônico já executado pelo Processor. O banco **não** implementa um segundo normalizador de nomes em T-SQL.

A manutenção é centralizada por `gold.tr_pessoa_nome_publicacao`, cobrindo tanto o fluxo normal do Processor quanto recomposições governadas executadas por `identidade.sp_recompor_gold_pessoa`. O backfill da migração segue a mesma regra.

As colunas fixas deixam a chave disponível para replay, auditoria, joins com a referência e futura indexação sem recalcular a transformação a cada execução.

### 4. Separar frequência observada de regra metodológica

A frequência populacional é **evidência empírica de referência**. Peso, raridade, transformação logarítmica, probabilidade de coincidência ao acaso ou qualquer função usada no score são decisões metodológicas do linkage/Calibrador.

A implementação não cristaliza uma fórmula de “raridade”. O Calibrador poderá consumir frequências observadas somente segundo metodologia explicitamente versionada.

### 5. Replay fixa versão da referência e da transformação

`identidade.modelo_linkage.frequencia_nome_versao_id` fixa a referência durante a geração do modelo. `identidade.linkage_run` congela também o identificador, código e SHA-256 da referência efetivamente usada pelo run.

A proveniência é imutável depois da criação do run. Testes de integração demonstram que a ativação de uma referência mais nova não reendereça modelos/runs históricos e que uma nova geração não pode fixar silenciosamente uma referência obsoleta diferente da versão ATIVA.

Assim, um replay pode identificar pelo menos:

- versão e conteúdo da referência de frequências;
- versão da semântica publicada de nome;
- versão da normalização;
- versão dos parâmetros/metodologia do linkage;
- chave materializada relevante da pessoa.

## Implementação realizada

### Referência interna e snapshot

A PR #115 integrou:

- `ref.frequencia_nome_versao`;
- `ref.frequencia_nome`;
- `ref.frequencia_nome_cobertura`;
- views da referência ativa;
- snapshot local imutável em `Solution/data/reference/ibge-nomes-2022/`;
- projeção operacional determinística NDJSON gzip;
- `manifest.json` + `projection-manifest.json` com SHA físico, SHA canônico e `rowCount`;
- loader offline `LOAD_NAME_FREQUENCY_SNAPSHOT`;
- proteção de imutabilidade e captura da versão ATIVA na criação do modelo.

### Semântica publicada

A PR #135 integrou `IbgeNamePublicationSemantics` e testes que congelam a regra: somente o primeiro nome é derivável de `nome_completo`; sobrenomes não são inferidos.

### Proveniência de replay

A PR #134 integrou a proveniência da referência em `identidade.linkage_run`, incluindo proteção contra alteração posterior e teste de replay após ativação de nova versão.

### Materialização em Gold

A PR #136 integrou `20260912_Gold_Nome_Publicacao.sql` e testes SQL Server. A migração:

- materializa `nome_publicacao_normalizado` em `gold.pessoa`;
- registra versões da semântica e normalização;
- faz backfill somente a partir de `nome_cmp` canônico;
- mantém a materialização por trigger para Processor e recomposição governada;
- não materializa frequência, sobrenome posicional ou fórmula de raridade.

## Alternativas rejeitadas

**Consultar IBGE/API em tempo de calibração ou linkage.** Rejeitada por dependência externa, variação temporal e dificuldade de replay.

**Guardar somente a frequência materializada na pessoa.** Rejeitada porque a frequência depende da versão histórica da referência e não é atributo intrínseco da pessoa.

**Reimplementar a normalização em T-SQL.** Rejeitada porque criaria risco de divergência entre replay, Processor e Calibrador.

**Usar `_ibge` em colunas de domínio.** Rejeitada: origem pertence à proveniência/versionamento.

**Interpretar `sobrenome` como último componente do nome ou criar sobrenomes posicionais fixos.** Rejeitada por adulterar a semântica da fonte.

## Dívida técnica remanescente

A internalização da referência, a proveniência de replay e a primeira materialização segura de `NOME` deixaram de ser dívida. Permanecem:

1. definir como a Jornada obterá uma fronteira estruturada confiável entre nome/nome composto e sobrenomes antes de qualquer materialização de `SOBRENOME`; não derivar isso de `nome_completo` por convenção;
2. criar índices sobre a chave materializada somente após medir seletividade, cardinalidade, custo de escrita e ganho nos blockings efetivamente usados;
3. consumir frequências no estimador/Calibrador somente segundo metodologia estatística explicitamente versionada, sem transformar frequência em “raridade” por convenção arbitrária;
4. atualizar DER, modelo físico e demais documentos de arquitetura para refletir a materialização efetivamente integrada;
5. revisar, em trabalho separado, usos semanticamente indevidos de `referencia` quando o conceito real for residência, sem alterar conceitos genuinamente mais amplos que residência.

## Critério de encerramento da dívida

Esta ADR só poderá ser considerada sem dívida técnica relevante quando:

- a estratégia de `SOBRENOME` estiver semanticamente resolvida ou explicitamente declarada fora de escopo;
- os índices necessários estiverem justificados por medição;
- o Calibrador consumir a referência segundo metodologia versionada;
- DER/modelo físico/documentação estiverem sincronizados com o estado implementado.
