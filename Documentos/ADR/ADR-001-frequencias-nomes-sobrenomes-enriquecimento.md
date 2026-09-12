# ADR-001 — Frequências de nomes e sobrenomes como referência interna e enriquecimento materializado

- **Status:** Aceita como decisão arquitetural; implementação parcial (dívida técnica remanescente)
- **Data:** 2026-09-12
- **Escopo:** identidade, linkage probabilístico, Calibrador, modelo físico e replay

## Contexto

A Jornada precisa usar informação populacional sobre frequência de nomes e sobrenomes como evidência auxiliar no linkage probabilístico. A fonte de referência prevista é o produto oficial de nomes do Censo Demográfico 2022.

A decisão deve atender simultaneamente a quatro requisitos:

1. o Calibrador não deve depender de consulta externa ao IBGE em tempo de execução;
2. o resultado usado pelo linkage deve ser reproduzível em replay;
3. atributos frequentemente usados no blocking/scoring devem poder ser materializados e indexados;
4. a semântica incorporada pela Jornada não deve ser adulterada para acomodar uma convenção interna, nem carregar sufixos de fonte como `_ibge` no domínio.

Também fica reafirmada a decisão semântica de território: quando o dado representa o local em que a pessoa reside, usar explicitamente **residência**, e não o termo genérico **referência**. Assim, preferir `municipio_residencia`, `uf_residencia`, `endereco_residencia` etc.

## Decisão

### 1. Internalizar a referência estatística

A distribuição oficial de frequências de nomes e sobrenomes será carregada no banco da Jornada como **dado de referência interno, versionado e imutável por versão**.

Essa referência será consumível pelo Calibrador e pelos processos de enriquecimento. Uma nova edição da fonte não sobrescreve silenciosamente a anterior: cria nova versão, preservando a capacidade de reproduzir calibrações e decisões históricas.

A estrutura física exata deve seguir a granularidade efetivamente publicada pela fonte. Não serão fabricadas combinações de dimensões que o produto oficial não forneça.

Exemplo conceitual:

```text
referência de frequência
- tipo: nome | sobrenome
- valor normalizado
- dimensões efetivamente publicadas (sexo, período/década, UF, município quando existentes)
- frequência
- versão da fonte
- data de referência
```

### 2. Preservar a semântica de nome e sobrenome

A Jornada deve absorver os conceitos de domínio sem acrescentar `_ibge` aos nomes dos atributos apenas para indicar a origem.

A proveniência é metadado, não semântica do atributo.

Não se deve redefinir `sobrenome` como “último nome”. Tampouco serão criadas, como representação canônica, colunas posicionais `sobrenome_1`, `sobrenome_2`, `sobrenome_3` etc. apenas para atender ao linkage.

A decomposição/tokenização necessária ao algoritmo permanece responsabilidade da normalização/linkage e deve respeitar a semântica documentada da fonte. Qualquer transformação adicional da Jornada deve ser explicitamente nomeada e versionada como transformação, e não apresentada como se fosse dado original da referência estatística.

### 3. Materializar enriquecimentos úteis em `gold.pessoa`

Os atributos de enriquecimento necessários ao blocking, scoring e replay devem ser **persistidos em colunas fixas de `gold.pessoa`**, em vez de depender exclusivamente de joins ou consultas à referência durante cada resolução.

Isso permite:

- criação de índices sobre atributos usados no blocking;
- scoring em lote sem dependência de consulta externa;
- reprodução do estado utilizado em uma resolução histórica;
- auditoria da evidência disponível ao linkage;
- desempenho previsível em grandes volumes.

Os nomes finais das colunas serão definidos após a validação do layout/granularidade oficial, evitando antecipar uma semântica que a fonte não possua.

### 4. Separar frequência observada de regra metodológica

A frequência populacional é **evidência empírica de referência**. Peso, raridade, transformação logarítmica, probabilidade de coincidência ao acaso ou qualquer função usada no score são **decisões metodológicas do linkage/Calibrador**.

Portanto, a implementação não deve cristalizar uma fórmula de “raridade” como se fosse fornecida pela fonte. O Calibrador poderá consumir as frequências observadas para estimar/ajustar parâmetros, mas a função adotada deverá possuir versão metodológica própria.

### 5. Replay deve fixar versão da referência e do método

Uma execução reprodutível deve conseguir identificar pelo menos:

- versão da referência de frequências utilizada;
- versão da normalização/tokenização;
- versão dos parâmetros/metodologia do linkage;
- valores materializados relevantes na pessoa quando aplicável.

Um replay histórico não pode passar automaticamente a usar uma versão mais recente da distribuição de nomes.

## Consequências

A solução passa a ter dois níveis complementares, e não concorrentes:

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

A internalização aumenta armazenamento e exige processo controlado de ingestão/versionamento, mas elimina dependência externa em execução e melhora reprodutibilidade e desempenho.

A materialização em `gold.pessoa` introduz redundância deliberada. Essa redundância é aceita porque a tabela participa do núcleo operacional de resolução de identidade e precisa suportar replay e índices eficientes.

## Alternativas rejeitadas

**Consultar IBGE/API em tempo de calibração ou linkage.** Rejeitada por dependência externa, variação temporal e dificuldade de replay.

**Manter apenas a tabela de referência e calcular tudo por join em execução.** Rejeitada como estratégia exclusiva porque reduz as vantagens de materialização e indexação no núcleo de identidade.

**Guardar apenas os valores materializados em `gold.pessoa`.** Rejeitada porque o Calibrador precisa da distribuição de referência completa e versionada, não apenas das frequências associadas às pessoas já existentes na Jornada.

**Usar `_ibge` em todas as colunas de domínio.** Rejeitada: a fonte deve ser registrada em proveniência/versionamento; o nome da coluna deve expressar a semântica do dado.

**Interpretar `sobrenome` como último componente do nome ou criar sobrenomes posicionais fixos.** Rejeitada por alterar a semântica e introduzir fragilidade desnecessária diante de inversões, omissões e múltiplos sobrenomes.

## Implementação realizada

A migração `Solution/database/migrations/20260912_Frequencia_Nomes_Referencia.sql` materializa o núcleo físico:

- `ref.frequencia_nome_versao` registra edição, data de referência, estado, SHA-256 e ativação;
- `ref.frequencia_nome` armazena `NOME` e `SOBRENOME` sem posição artificial;
- a chave dimensional impede duplicidade lógica dentro da mesma versão;
- versões publicadas tornam-se imutáveis; uma nova versão ativa torna a anterior obsoleta sem excluí-la;
- `ref.v_frequencia_nome_ativa` expõe a versão corrente para consumidores operacionais;
- `identidade.modelo_linkage.frequencia_nome_versao_id` permite fixar a referência estatística usada por um modelo;
- novos modelos `GERANDO` capturam atomicamente a versão ativa e falham de forma controlada quando não existe referência ativa;
- o vínculo do modelo com a referência torna-se imutável, preservando replay histórico.

O importador `NameFrequencyReferenceImporter`, executado por `LinkageParameters:Operation=IMPORT_NAME_FREQUENCY`, implementa o primeiro corte de carga oficial:

- consulta os rankings nacionais completos de `nome` e `sobrenome` da API pública usada pelo produto Censo 2022 — Nomes no Brasil;
- usa a mesma `IdentityComparison.NormalizeText` versionada utilizada pelo linkage;
- valida forma e frequências positivas, limita comprimento ao contrato físico e rejeita duplicidades;
- calcula SHA-256 canônico independente da ordem de chegada das páginas;
- carrega com `SqlBulkCopy` dentro de transação serializável;
- se uma execução anterior ficou em `CARREGANDO`, a versão é reconstruída de forma determinística;
- se a mesma versão já foi publicada com o mesmo hash, a reexecução é idempotente e não altera o banco;
- se o mesmo código de versão reaparecer com conteúdo diferente, a execução falha e exige novo `VersionCode`, impedindo sobrescrita histórica;
- somente após a carga completa a versão é publicada/ativada.

Este primeiro corte materializa o **total Brasil**. Distribuições detalhadas por período, UF e município permanecem fora deste incremento para que células suprimidas pelo sigilo estatístico sejam preservadas como ausência, e nunca convertidas implicitamente em zero.

## Dívida técnica aberta

A implementação está parcial. Permanecem como dívida técnica:

1. documentar integralmente o contrato observado da API oficial, inclusive endpoints detalhados e regras de supressão;
2. **concluído:** DDL das tabelas internas de referência e de versionamento;
3. **parcialmente concluído:** importador idempotente dos rankings nacionais oficiais, checksum/proveniência e retenção de versões; falta ampliar a carga às dimensões detalhadas publicadas e criar prova automatizada ponta a ponta contra fixture fiel ao contrato externo;
4. definir exatamente quais frequências serão materializadas em `gold.pessoa`, sem inventar dimensões ausentes da fonte;
5. revisar a nomenclatura de residência no modelo, substituindo usos semanticamente indevidos de `referencia` por `residencia`, sem alterar conceitos que sejam genuinamente mais amplos que residência;
6. implementar enriquecimento/recomposição de `gold.pessoa` e garantir comportamento determinístico em replay;
7. criar índices orientados aos blockings efetivamente aprovados, após medição de seletividade e custo;
8. usar a referência no estimador apenas após explicitar a metodologia estatística aprovada, sem transformar frequência em “raridade” por convenção arbitrária;
9. registrar a versão da referência também nos artefatos de auditoria/replay que descrevem cada execução, além do vínculo já criado em `identidade.modelo_linkage`;
10. ampliar testes de replay para provar que um modelo antigo continua consultando sua referência após a ativação de versão nova;
11. atualizar DER, modelo físico, contratos e documentação normativa quando o modelo final for implementado.

## Critério de encerramento da dívida

A dívida só poderá ser considerada encerrada quando a carga de referência estiver versionada e reproduzível em todas as granularidades aprovadas, `gold.pessoa` possuir os enriquecimentos aprovados e indexáveis, o Calibrador consumir explicitamente a referência segundo metodologia versionada, e um teste de replay demonstrar que uma execução histórica produz o mesmo conjunto de evidências mesmo após a instalação de uma versão mais nova da referência.
