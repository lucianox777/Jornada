# Pessoa v5 — contrato estrutural do trem SolutionSchema 3.71

**Estado:** implementação técnica em andamento no trem coordenado por #411.  
**Issues:** #392, #402, #393.  
**Ativação:** os schemas v5 são cadastrados como `RASCUNHO`; Pessoa v4 permanece ativa até a ativação coordenada do trem.  
**SolutionSchema:** esta fatia não promove `Jornada.SolutionSchema`; o rebind 3.71 ocorre somente após as migrations finais do trem.

## 1. Ausência de CPF

Pessoa v5 separa ausência na origem de condição documental. Quando o CPF final da observação estiver ausente, `cpfAusenteMotivo` aceita somente:

- `NAO_INFORMADO_ORIGEM`;
- `SEM_DOCUMENTACAO_BASE_DECLARADA`;
- `COM_DOCUMENTACAO_SEM_CPF_CONHECIDO`;
- `EM_REGULARIZACAO`.

`cpf = null` nunca infere uma dessas categorias. A categoria deve vir explicitamente da origem.

O valor histórico `SEM_CPF` continua persistível apenas para dados/contratos anteriores. A migration não o converte para nenhum estado v5. A view `serving.v_bi_cpf_ausencia_taxonomia` o publica como `LEGADO_SEM_CPF_NAO_DECOMPOSTO`, preservando a incerteza histórica.

A coerência é validada pelo Processor **depois** da convergência entre o campo legado `cpf` e `identificadores[]`: isso evita rejeitar uma Pessoa que omitiu o campo legado, mas trouxe um CPF explícito válido na coleção de identificadores.

## 2. Identificadores secundários

Pessoa v5 mantém NIS/PIS/PASEP/NIT e RG como identificadores secundários e acrescenta CNH.

### RG

- `valor` obrigatório;
- `emissor` opcional;
- `ufEmissor` opcional;
- valor original preservado;
- normalização lexical remove somente espaço/brancos, ponto, hífen, barra e contra-barra;
- letras são canonizadas para caixa alta;
- `X` é preservado;
- zeros à esquerda são preservados;
- caracteres fora do conjunto lexical permitido são rejeitados, não descartados silenciosamente.

Não existe algoritmo nacional único de dígito verificador fabricado para RG.

### CNH

- namespace `BR`;
- valor original + valor normalizado;
- `DECLARADO|COMPROVADO` preservado;
- sem writer determinístico;
- sem criação/fusão automática de `pessoa_uuid`;
- sem entrada automática no score ou blocking.

A view `serving.v_bi_identificador_secundario_v5` expõe somente contagens/classificações agregadas; números de documento não são publicados.

## 3. Referência territorial

Pessoa v5 introduz `estadoReferenciaTerritorial`.

### `INFORMADA`

Exige uma natureza:

- `DOMICILIAR`;
- `ACOLHIMENTO_INSTITUCIONAL`;
- `INSTITUCIONAL_PRISIONAL`;
- `SERVICO_REFERENCIA`;
- `PERNOITE`.

`SERVICO_REFERENCIA` e `PERNOITE` permanecem semanticamente distintos de residência.

O valor v4 `REFERENCIA_TERRITORIAL_DECLARADA` permanece apenas no histórico e é rejeitado pelo contrato v5.

### `SEM_ENDERECO_FIXO_DECLARADO`

É uma declaração explícita da origem. Não é:

- `null`;
- ausência de informação;
- `SEM_ENDERECO_APTO`;
- uma natureza territorial.

Esse estado não admite natureza, situação geográfica, Distrito/Subprefeitura nem endereço fino. A Silver guarda o estado separadamente em `estado_referencia`. O token técnico usado na coluna legada `valor` existe somente porque essa coluna continua `NOT NULL` durante o trem e não representa endereço.

## 4. Segurança das projeções

A view compartilhada `silver.v_pessoa_referencia_territorial`:

- seleciona somente `estado_referencia=INFORMADA`;
- exclui `INSTITUCIONAL_PRISIONAL` por padrão;
- não transforma `SEM_ENDERECO_FIXO_DECLARADO` em endereço;
- não converte acolhimento, serviço de referência ou pernoite em `DOMICILIAR`.

A view agregada `serving.v_bi_referencia_territorial_v5` omite integralmente linhas `INSTITUCIONAL_PRISIONAL`; a superfície compartilhada não publica nem contagem que revele essa natureza. A Silver preserva o dado para eventual fluxo institucional autorizado futuro.

As regras existentes de casa-abrigo sigilosa permanecem intactas. Exposição institucional futura de informação prisional continua dependente das decisões externas de autorização/autenticação.

## 5. Linkage

Esta fatia não altera o scorer, o blocking nem writers determinísticos.

- CPF continua âncora externa determinística principal.
- NIS/RG/CNH não constituem Pessoa.
- Referência territorial/endereço não é evidência positiva de Linkage.
- Uso probabilístico futuro permanece em #31 e depende de calibração/evidência.
- `initial_uuid` continua linhagem, não feature estatística.

## 6. Compatibilidade

O projeto permanece pré-HML/Produção. A compatibilidade preservada aqui é semântica e de rastreabilidade:

- v4 continua legível com `SEM_CPF`, RG qualificado e natureza territorial histórica;
- v5 não reinterpreta linhas antigas;
- linhas territoriais anteriores recebem `estado_referencia=INFORMADA` porque já representavam uma referência informada;
- o default SQL `INFORMADA` existe para replay/fixtures v4 que não conhecem a coluna nova;
- o runtime v5 envia o estado explicitamente.

## 7. Evidência automatizada

- `PersonV5ContractTests`: contrato JSON, taxonomia CPF, normalização RG/CNH, preservação v4, hashes e invariantes estáticos.
- `PersonV5ContractSqlServerTests`: instalador + seed + persistência SQL real, incluindo RG parcial, CNH secundária, ausência de `identity_map`, estado sem endereço fixo e bloqueio prisional.
