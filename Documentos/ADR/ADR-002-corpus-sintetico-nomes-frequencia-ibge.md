# ADR-002 — Composição de nomes no corpus sintético a partir da referência IBGE

- **Status:** Aceita
- **Data:** 2026-09-16
- **Escopo:** corpus sintético, testes de escala, linkage e referência de frequências de nomes
- **Relacionada:** ADR-001 — Frequências de nomes e sobrenomes como referência interna e enriquecimento materializado

## Contexto

O Censo Demográfico 2022 separou a coleta em dois campos semanticamente distintos. O campo de **nome** admite o primeiro nome ou um nome composto; o campo de **sobrenome** recebe os demais sobrenomes. Assim, uma estrutura como `nome = José Carlos` e `sobrenome = Alves dos Santos` é compatível com a semântica de coleta.

O produto estatístico **Nomes no Brasil**, porém, não publica uma distribuição de nomes completos nem preserva a composição original necessária para reconstruí-los. Para a divulgação das frequências, a referência operacional da Jornada contém frequências marginais de `NOME` e `SOBRENOME`: `NOME` representa a unidade publicada para frequência de nomes e `SOBRENOME` representa componentes de sobrenome sem posição canônica. Conectivos e coocorrências não constituem uma distribuição conjunta de nome completo.

A distinção é importante para o corpus sintético. Sortear uniformemente de uma lista curta de nomes, como fazia o primeiro diversificador do SCALE, elimina a distribuição populacional que justamente motiva a referência IBGE. Por outro lado, concatenar tokens sorteados e afirmar que a composição resultante é uma amostra de nomes completos do IBGE também seria incorreto.

## Decisão

### 1. Identidade dos componentes é amostrada pela frequência publicada

O corpus SCALE passa a selecionar os valores de `NOME` e `SOBRENOME` com probabilidade proporcional à frequência presente no snapshot local, versionado e imutável do **Censo 2022 — Nomes no Brasil**.

A seleção é determinística para um mesmo `seed`, identificador sintético e versão da referência. A função de sorteio usa SHA-256 para transformar esses valores em uma posição na distribuição acumulada de frequências.

Para o nome da mãe, o corpus utiliza o estrato feminino nacional publicado quando disponível no snapshot canônico. Sobrenomes continuam vindo da distribuição nacional de `SOBRENOME`.

### 2. Composição do nome completo é fixture sintética, não estimativa do IBGE

O snapshot publicado não informa a distribuição conjunta necessária para estimar, de forma populacionalmente fiel:

- a proporção de nomes próprios simples e compostos;
- a quantidade de sobrenomes por pessoa;
- a coocorrência entre nomes próprios;
- a coocorrência entre sobrenomes;
- a frequência e posição de conectivos como `de`, `da`, `dos`;
- a distribuição de sufixos como `Filho` ou `Junior`.

Por isso, o SCALE varia deliberadamente essas estruturas apenas para **cobertura de teste**. As proporções usadas para nomes compostos, um/dois/três sobrenomes e conectivos não são rotuladas nem consumidas como estatística do IBGE.

Exemplo conceitual válido para o corpus:

```text
componente NOME:       José + Carlos
componentes SOBRENOME: Alves + Santos
composição sintética:  José Carlos Alves dos Santos
```

Nesse exemplo, `José`, `Carlos`, `Alves` e `Santos` são selecionados a partir das frequências marginais publicadas. A decisão de compor dois nomes próprios, dois sobrenomes e inserir `dos` pertence exclusivamente à fixture sintética.

### 3. Não existe `sobrenome_1`/`sobrenome_2` como semântica de domínio

A geração de múltiplos componentes no corpus não altera a decisão da ADR-001. A Jornada não passa a afirmar que o IBGE publica sobrenomes posicionais nem deriva `SOBRENOME` de cadastros reais tomando o último token do nome completo.

Posições usadas internamente pelo gerador (`S1`, `S2`, `S3`) são apenas slots de geração para montar casos de teste e não são atributos canônicos do domínio.

### 4. A referência usada pelo corpus é verificável

O diversificador SCALE consome os arquivos da projeção canônica versionada em `Solution/data/reference/ibge-nomes-2022/`.

Antes da geração ele:

1. lê `projection-manifest.json`;
2. identifica os artefatos nacionais necessários;
3. valida o SHA-256 físico dos arquivos compactados;
4. valida o `rowCount` depois da descompressão;
5. constrói as distribuições acumuladas de frequência;
6. inclui o código da referência no hash do conteúdo sintético.

O SQL Server local recebe o snapshot por montagem read-only. Nenhuma API externa participa do teste de escala.

## Consequências

O corpus deixa de ter primeiros nomes e sobrenomes distribuídos artificialmente de forma uniforme. Nomes frequentes passam a aparecer mais vezes e nomes raros permanecem possíveis, criando pressão de blocking e distribuição fonética mais próximas do fenômeno que o linkage deverá enfrentar.

Ao mesmo tempo, a massa continua inteiramente reprodutível. O mesmo commit, snapshot, seed e tamanho de população produzem as mesmas escolhas de componentes.

A distribuição estrutural de nome completo continua sendo artificial e deve ser tratada como tal nos relatórios e gates. Ela serve para exercitar o sistema, não para estimar a população brasileira.

## Alternativas rejeitadas

**Listas curtas com escolha uniforme.** Rejeitada porque cria distribuição artificial, subestima concentração de nomes comuns e enfraquece o teste de blocking e frequência.

**Tratar a referência IBGE como catálogo de nomes completos.** Rejeitada porque o produto publicado não preserva a distribuição conjunta necessária para essa interpretação.

**Fixar sempre um primeiro nome e exatamente dois sobrenomes.** Rejeitada porque reduz a cobertura estrutural do teste e pode mascarar problemas de normalização e comparação.

**Consultar o IBGE durante o SCALE.** Rejeitada porque quebra replay, introduz dependência externa e diverge da referência local canônica já adotada pela Jornada.

## Critério de manutenção

Uma nova versão da referência IBGE pode alterar a distribuição do corpus somente de forma explícita e versionada. Mudanças futuras nas proporções sintéticas de composição do nome completo devem ser documentadas como mudanças de fixture, nunca como atualização de frequência populacional do IBGE sem evidência correspondente.
