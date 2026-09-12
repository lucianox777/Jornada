# ADR-001 — Frequências de nomes e sobrenomes como referência interna e enriquecimento materializado

- **Status:** Aceita como decisão arquitetural; referência interna implementada, materialização em `gold.pessoa` ainda pendente
- **Data:** 2026-09-12
- **Escopo:** identidade, linkage probabilístico, Calibrador, modelo físico e replay

## Contexto

A Jornada precisa usar informação populacional sobre frequência de nomes e sobrenomes como evidência auxiliar no linkage probabilístico. A fonte de referência prevista é o produto de nomes do Censo Demográfico 2022.

A decisão deve atender simultaneamente a quatro requisitos:

1. o Calibrador não deve depender de consulta externa ao IBGE em tempo de execução;
2. o resultado usado pelo linkage deve ser reproduzível em replay;
3. atributos frequentemente usados no blocking/scoring devem poder ser materializados e indexados;
4. a semântica incorporada pela Jornada não deve ser adulterada para acomodar uma convenção interna, nem carregar sufixos de fonte como `_ibge` no domínio.

Também fica reafirmada a decisão semântica de território: quando o dado representa o local em que a pessoa reside, usar explicitamente **residência**, e não o termo genérico **referência**. Assim, preferir `municipio_residencia`, `uf_residencia`, `endereco_residencia` etc.

## Decisão

### 1. Internalizar a referência estatística

A distribuição de frequências de nomes e sobrenomes é mantida no banco da Jornada como **dado de referência interno, versionado e imutável por versão**.

A referência é consumível pelo Calibrador e pelos processos de enriquecimento. Uma nova edição da fonte não sobrescreve silenciosamente a anterior: deve criar nova versão, preservando a capacidade de reproduzir calibrações e decisões históricas.

A estrutura física segue somente granularidades efetivamente presentes no snapshot incorporado. Não serão fabricadas combinações de dimensões ausentes da fonte.

O modelo vigente representa:

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

### 2. Preservar a semântica de nome e sobrenome

A Jornada absorve os conceitos de domínio sem acrescentar `_ibge` aos nomes dos atributos apenas para indicar origem. Proveniência é metadado, não semântica do atributo.

Não se deve redefinir `sobrenome` como “último nome”. Tampouco serão criadas, como representação canônica, colunas posicionais `sobrenome_1`, `sobrenome_2`, `sobrenome_3` etc. apenas para atender ao linkage.

A decomposição/tokenização necessária ao algoritmo permanece responsabilidade da normalização/linkage e deve respeitar a semântica documentada da fonte. Qualquer transformação adicional da Jornada deve ser explicitamente nomeada e versionada como transformação, e não apresentada como se fosse dado original da referência estatística.

### 3. Materializar enriquecimentos úteis em `gold.pessoa`

Os atributos de enriquecimento necessários ao blocking, scoring e replay devem ser **persistidos em colunas fixas de `gold.pessoa`** quando sua semântica e sua utilidade operacional estiverem definidas, em vez de depender exclusivamente de joins à referência durante cada resolução.

Isso permite criação de índices, scoring em lote, replay, auditoria da evidência e desempenho previsível em grandes volumes.

A referência interna já está implementada. A seleção das colunas de enriquecimento em `gold.pessoa` permanece como etapa separada: os nomes e a granularidade devem expressar a semântica real dos valores materializados, sem inventar dimensão inexistente, sem transformar `SOBRENOME` em posição no nome completo e sem cristalizar uma fórmula metodológica de raridade.

### 4. Separar frequência observada de regra metodológica

A frequência populacional é **evidência empírica de referência**. Peso, raridade, transformação logarítmica, probabilidade de coincidência ao acaso ou qualquer função usada no score são **decisões metodológicas do linkage/Calibrador**.

Portanto, a implementação não deve cristalizar uma fórmula de “raridade” como se fosse fornecida pela fonte. O Calibrador poderá consumir as frequências observadas para estimar ou ajustar parâmetros, mas a função adotada deverá possuir versão metodológica própria.

### 5. Replay deve fixar versão da referência e do método

Uma execução reprodutível deve conseguir identificar pelo menos:

- versão da referência de frequências utilizada;
- versão da normalização/tokenização;
- versão dos parâmetros/metodologia do linkage;
- valores materializados relevantes na pessoa quando aplicável.

Um replay histórico não pode passar automaticamente a usar uma versão mais recente da distribuição de nomes.

## Consequências

A solução possui dois níveis complementares:

```text
referência estatística interna e versionada
        |                     |
        v                     v
   Calibrador           enriquecimento
        |                     |
        v                     v
parâmetros/método       gold.pessoa
                              |
                              v
                    blocking + scoring + replay
```

A internalização aumenta armazenamento e exige ingestão/versionamento controlados, mas elimina dependência externa no caminho operacional e melhora reprodutibilidade e desempenho.

A futura materialização em `gold.pessoa` introduzirá redundância deliberada. Essa redundância é aceita porque a tabela participa do núcleo operacional de resolução de identidade e precisa suportar replay e índices eficientes.

## Alternativas rejeitadas

**Consultar IBGE/API em tempo de calibração ou linkage.** Rejeitada por dependência externa, variação temporal e dificuldade de replay.

**Manter apenas a tabela de referência e calcular tudo por join em execução.** Rejeitada como estratégia exclusiva porque reduz as vantagens de materialização e indexação no núcleo de identidade.

**Guardar apenas os valores materializados em `gold.pessoa`.** Rejeitada porque o Calibrador precisa da distribuição de referência completa e versionada, não apenas das frequências associadas às pessoas existentes na Jornada.

**Usar `_ibge` em todas as colunas de domínio.** Rejeitada: a fonte deve ser registrada em proveniência/versionamento; o nome da coluna deve expressar a semântica do dado.

**Interpretar `sobrenome` como último componente do nome ou criar sobrenomes posicionais fixos.** Rejeitada por alterar a semântica e introduzir fragilidade desnecessária diante de inversões, omissões e múltiplos sobrenomes.

## Implementação realizada

A referência interna está implementada e integrada no `master` pelo trabalho consolidado na PR #115.

### Modelo SQL e versionamento

- `ref.frequencia_nome_versao` registra versão, fonte, datas, estado e SHA-256 do conteúdo;
- `ref.frequencia_nome` armazena `NOME` e `SOBRENOME` sem posição artificial, com as dimensões disponíveis;
- `ref.frequencia_nome_cobertura` registra cobertura por tipo/escopo e explicita que ausência de célula detalhada pode significar `NAO_PUBLICADA_OU_SUPRIMIDA`, nunca zero implícito;
- `ref.v_frequencia_nome_ativa` e `ref.v_frequencia_nome_cobertura_ativa` expõem a versão ativa;
- `identidade.modelo_linkage.frequencia_nome_versao_id` fixa a referência estatística do modelo e preserva replay histórico;
- versões publicadas são imutáveis e uma reexecução só é idempotente quando o conteúdo é o mesmo.

### Snapshot local imutável

`Solution/data/reference/ibge-nomes-2022/` contém o snapshot bruto versionado e uma projeção operacional determinística. O snapshot operacional canônico é local: Calibrador e replay não dependem de rede.

A projeção cobre as granularidades incorporadas da fonte:

- total Brasil;
- sexo no Brasil;
- período/década no Brasil;
- UF;
- município.

Os arquivos operacionais são NDJSON UTF-8 gzip versionados. `manifest.json` referencia `projection-manifest.json`. O loader valida, de forma fail-closed, a concordância de versão/formato/origem/lista de arquivos e, para cada arquivo, SHA-256 físico, `rowCount` e SHA-256 canônico do conteúdo NDJSON descomprimido.

### Carga e operações

`LOAD_NAME_FREQUENCY_SNAPSHOT` executa a carga offline para SQL Server. O hash canônico da versão inclui tipo, valor, valor normalizado, sexo, período de nascimento, escopo geográfico, UF, município e frequência.

`CHECK_NAME_FREQUENCY_SOURCE` permanece separado como verificação remota leve e não autoritativa, sem escrita no banco ou mutação do snapshot.

`CAPTURE_NAME_FREQUENCY_SNAPSHOT` é operação excepcional e explicitamente habilitada; não é refresh automático nem participa do caminho normal de produção.

O workflow temporário usado para a captura inicial foi removido depois que o snapshot e a projeção determinística foram incorporados ao repositório.

### Evidência de integração

A PR #115 foi integrada no `master` `a995113ee7e1ff8f0d060f8f2f4dfabbb1f94814`. Os 9 workflows aplicáveis da PR e os 9 workflows `push` pós-merge concluíram em `success`; o `jornada-ci` pós-merge #2131 cobriu, entre outros, DDL upgrade/idempotência, unitários, OpenAPI, E2E, harness, integração SQL, fault injection e CodeQL/SARIF.

## Dívida técnica remanescente

A internalização da referência deixou de ser dívida. Permanecem:

1. definir exatamente quais frequências/evidências serão materializadas em colunas fixas de `gold.pessoa`, preservando a semântica da referência e sem criar sobrenomes posicionais artificiais;
2. implementar o enriquecimento e a recomposição determinística de `gold.pessoa`, incluindo a proveniência/versão necessária ao replay;
3. criar índices orientados aos blockings efetivamente usados, após medição de seletividade e custo;
4. usar a referência no estimador somente segundo metodologia estatística explicitamente versionada, sem converter frequência em “raridade” por convenção arbitrária;
5. registrar a versão da referência nos artefatos de auditoria/replay que descrevem cada execução, além do vínculo já existente no modelo;
6. ampliar testes de replay para provar que um modelo antigo continua vinculado à sua referência após a ativação de versão nova;
7. revisar a nomenclatura de residência no modelo, substituindo usos semanticamente indevidos de `referencia` por `residencia`, sem alterar conceitos genuinamente mais amplos que residência;
8. atualizar DER, modelo físico, contratos e documentação normativa quando a materialização final de enriquecimento for implementada.

## Critério de encerramento da dívida

A dívida remanescente só poderá ser considerada encerrada quando `gold.pessoa` possuir os enriquecimentos aprovados e indexáveis, o Calibrador consumir explicitamente a referência segundo metodologia versionada e um teste de replay demonstrar que uma execução histórica conserva a mesma referência e o mesmo conjunto de evidências mesmo após a instalação de uma versão mais nova.
