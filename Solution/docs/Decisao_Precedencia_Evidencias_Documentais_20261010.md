# Decisão — precedência das evidências por data do documento (10/10/2026)

**Estado:** decisão de negócio para a candidata v5; adequação de contrato, persistência, seleção Gold e testes **a verificar/implementar**. Não declarar comportamento já implantado. Esta decisão não altera retroativamente a versão publicada.

## Regra de prioridade dos valores de um mesmo atributo

Entre evidências válidas e pertinentes que comprovem o mesmo atributo, a prioridade é:

1. **Documento comprobatório mais recente**, segundo a **data do próprio documento**.
2. **Documento comprobatório mais antigo**, segundo a **data do próprio documento**.
3. **Autodeclaração mais recente**, segundo a **data da autodeclaração**.
4. **Autodeclaração mais antiga**, segundo a **data da autodeclaração**.

Formalmente, ordenar primeiro pela classe de evidência (**documento válido > autodeclaração**) e **somente dentro da mesma classe** pela data própria da evidência, em ordem decrescente. Assim, um documento antigo prevalece sobre uma autodeclaração recente.

**A data do atendimento, da ingestão, da digitalização, do registro no sistema ou da última atualização cadastral não substitui a data própria do documento.** Essas datas são metadados de auditoria e não promovem a evidência na hierarquia.

A regra aplica-se **por atributo efetivamente comprovado**. Um documento não prova automaticamente campos que não contém. Preservar a proveniência, o valor original e as divergências entre fontes; a escolha de referência não apaga observações nem altera a identidade por si só. A Secretaria/Gestor não ganha prioridade por sua origem.

## Datas ausentes e desempates

A ausência ou incerteza da data própria **não autoriza usar a data de atendimento como fallback**. Marcar a evidência como sem data comprovada, preservá-la e encaminhar a seleção para critério de desempate/abstenção governado e auditável. Não inventar data nem assumir que o registro mais recente é o documento mais novo. Entre evidências de mesma classe e mesma data, exigir desempate determinístico documentado, sem promoção por atendimento.

## Cenários mínimos de aceite

- Documento emitido em 2020, atendido em 2026, contra documento emitido em 2024, atendido em 2025: **vence o documento de 2024**.
- Documento emitido em 2015 contra autodeclaração de 2026: **vence o documento de 2015**.
- Duas autodeclarações de 2022 e 2025: **vence a de 2025**.
- Documento antigo registrado hoje contra documento novo registrado ontem: **vence o documento novo**.
- Documento sem data própria: **não inferir data pelo atendimento**; registrar a lacuna e aplicar tratamento governado.
- Comprovação de apenas um atributo: **não promover outros atributos**.


## Modelo de evidência: data no documento, não no atributo

**A data pertence à instância da evidência/documento**, identificada por seu tipo e identificador de ocorrência; **não criar uma coluna de data por atributo**. Cada tipo documental tem um conjunto próprio e explícito de atributos que pode comprovar, mantido em catálogo versionado de tipos e cobertura. A mesma instância pode comprovar vários atributos, todos associados à mesma data documental e à mesma proveniência.

Estrutura conceitual mínima:
- **TipoDocumento**: código estável, versão e conjunto de atributos admitidos; não presumir que todo tipo comprova nome, nascimento, filiação, endereço etc.
- **Documento/Evidência**: identificador, tipo, classe (DOCUMENTO ou AUTODECLARACAO), **data própria da evidência**, proveniência, validade e referência de auditoria; datas de atendimento/registro são metadados separados.
- **Valores comprovados**: relação da evidência com cada atributo que efetivamente comprova e seu valor; sem replicar a data na linha do atributo.
- **Seleção Gold**: para cada atributo, considerar somente evidências válidas cujo tipo admite aquele atributo e que de fato o contenham; ordenar as evidências por classe e data própria; preservar o vínculo ao documento vencedor.

**Tipo não é ocorrência**: duas certidões do mesmo tipo emitidas em datas diferentes são duas instâncias documentais, cada uma com sua data. Não manter uma única data global para todas as certidões de nascimento, por exemplo.

Exemplo: uma certidão de nascimento pode comprovar nascimento e filiação; um comprovante de residência pode comprovar endereço, mas não deve automaticamente substituir nome ou data de nascimento. O catálogo decide o escopo de cada tipo; estes exemplos não substituem a validação institucional dos campos aceitos.

O tratamento de documento sem data própria permanece governado: não usar data do atendimento como substituta. O contrato e o banco devem representar a associação documento → valores comprovados, com data documental única na instância, e não data repetida por campo.


## Catálogo relacional configurável de tipos documentais

**Decisão:** não codificar em C#, SQL procedural ou JSON estático a relação entre tipo documental e campos. Persistir o catálogo em tabelas de referência, consultadas pela validação e pela seleção Gold. Proposta de esquema lógico (nomes físicos a confirmar com a convenção de `ref`):

| Tabela | Colunas essenciais | Regra |
| --- | --- | --- |
| `ref.tipo_documento` | `tipo_documento_id`, `codigo` único, `descricao`, `classe_evidencia` (DOCUMENTO/AUTODECLARACAO), `ativo`, `vigencia_inicio`, `vigencia_fim`, `versao` | Define o tipo e sua classe; vigência e versionamento auditáveis. |
| `ref.tipo_documento_atributo` | `tipo_documento_id`, `atributo_codigo`, `vigencia_inicio`, `vigencia_fim` | Associação N:N entre tipo e atributo que **pode** comprovar; FK para catálogo de atributos; chave/índice impedem associações duplicadas vigentes. |
| `identidade.documento_evidencia` (proposta) | `documento_evidencia_id`, `tipo_documento_id`, `data_documento`, `data_atendimento`, `proveniencia`, `status_validacao` | **Uma data própria por instância documental**, independente do atendimento; não repetir data em cada atributo. |
| `identidade.documento_evidencia_valor` (proposta) | `documento_evidencia_id`, `atributo_codigo`, `valor`/referência ao valor original | Só aceita atributo autorizado pelo tipo e vigente na política aplicável; conserva origem e divergências. |

**Governança:** cadastrar/alterar tipos e associações por migração ou operação administrativa autorizada, com trilha de auditoria e versão da política; nunca permitir que mudança retroativa silenciosa reinterprete evidências históricas. Incluir `data_documento` e `data_atendimento` como campos distintos; a segunda jamais participa do desempate temporal. A validação da evidência verifica tipo, atributo permitido, conteúdo, validade e versão do catálogo; não basta constar do catálogo para que o valor seja automaticamente considerado comprovado.

**Consultas:** a seleção Gold une valores → instância documental → tipo e associação tipo/atributo, filtra evidências válidas e ordena `classe_evidencia` (DOCUMENTO antes de AUTODECLARACAO), `data_documento DESC` dentro da classe. Para autodeclaração, `data_documento` representa a data própria da declaração; pode receber nome físico mais neutro `data_evidencia`. Data ausente/empate exige regra explícita e auditável, sem fallback para atendimento.

**Aceite técnico adicional:** testes de cadastro de novo tipo e seus atributos **sem recompilar o sistema**; rejeição de atributo não permitido; atualização de vigência/versionamento; múltiplas instâncias do mesmo tipo; datas de atendimento invertidas; preservação da evidência histórica quando o catálogo muda. Não criar tabelas diretamente em ambiente real sem migração e testes em banco descartável.


## Bootstrap inicial do catálogo — proposta pesquisada em fontes oficiais (10/10/2026)

O catálogo nasce em `ref` com **versão inicial 1**, e não requer incremento de versão a cada execução do bootstrap. O bootstrap é **idempotente**: insere apenas tipos/associações ainda ausentes, sem sobrescrever alterações administrativas posteriores. Incrementar versão **somente ao publicar uma mudança efetiva** de cobertura, sem reescrever a versão anterior. A publicação em produção é controlada e auditável.

| Código sugerido | Documento | Atributos candidatos no catálogo inicial | Ressalvas |
| --- | --- | --- | --- |
| `CPF_COMPROVANTE` | Comprovante de inscrição/situação cadastral CPF (Receita Federal) | `CPF`, `NOME_COMPLETO`, `DATA_NASCIMENTO` | Distinguir comprovante emitido de formulário de consulta; não inferir filiação pelos campos solicitados no formulário. |
| `RG` | Carteira de identidade estadual (modelo tradicional) | `RG`, `NOME_COMPLETO`, `DATA_NASCIMENTO`, `NOME_MAE`, `NOME_PAI`, `NATURALIDADE`, `CPF` | CPF só se efetivamente presente; cobertura pode variar por modelo/UF. |
| `CIN` | Carteira de Identidade Nacional | `CPF`, `NOME_COMPLETO`, `DATA_NASCIMENTO`, `NOME_MAE`, `NOME_PAI`, `NATURALIDADE` | Manter separada do RG tradicional; filiação pode variar e não implica mãe/pai identificáveis automaticamente. |
| `CERTIDAO_NASCIMENTO` | Certidão de nascimento | `NOME_COMPLETO`, `DATA_NASCIMENTO`, `NOME_MAE`, `NOME_PAI`, `NATURALIDADE`, `CPF` | CPF somente se constar na certidão; registros antigos e averbações variam. |
| `CNS` | Cartão Nacional de Saúde | `CNS`, `NOME_COMPLETO`, `DATA_NASCIMENTO`, `NOME_MAE` | **Distinguir cartão exibido do cadastro CADSUS**: nem todo campo da base nacional está impresso no cartão. |
| `TITULO_ELEITOR` | Título de eleitor / e-Título | `TITULO_ELEITOR`, `NOME_COMPLETO`, `DATA_NASCIMENTO`, `NOME_MAE`, `NOME_PAI` | Filiação se presente; zona/seção/município eleitoral são metadados eleitorais, **não naturalidade/endereço residencial**. |

**Fontes oficiais consultadas:** Receita Federal (comprovante CPF): https://solucoes.receita.fazenda.gov.br/Servicos/cpf/ConsultaSituacao/ConsultaPublica.asp ; Lei 7.116/1983 (RG): https://www.planalto.gov.br/ccivil_03/leis/1980-1988/l7116.htm ; Modelo informacional CIN: https://www.gov.br/participamaisbrasil/mi-cin ; SIRC (registro de nascimento): https://www.sirc.gov.br/guias/guia-sirc-cartorios/menu-registros-civis/registro-de-nascimento/ ; DATASUS CNS: https://datasus.saude.gov.br/cartao-nacional-de-saude/ ; TSE e-Título: https://www.tse.jus.br/servicos-eleitorais/e-titulo-perguntas-frequentes/2-quais-servicos-voce-encontra-no-e-titulo .

**Limite da pesquisa:** a tabela é **seed inicial candidata**, não uma declaração de que cada campo é impresso em toda variante do documento, nem homologação institucional. Validar o campo efetivamente presente e o tipo/versão antes de atribuir comprovação; separar `RG` e `CIN` e distinguir `CPF_COMPROVANTE` de simples número CPF. Nunca promover `DATA_EMISSAO` como atributo pessoal: ela é metadado temporal da instância documental.

**Bootstrap em produção:** somente dados de referência, com migração/seed idempotente e aprovação; não reativar tipos desativados, não recriar associações removidas, não apagar versões, não fazer update massivo sobre decisões Gold existentes. Registrar versão da política usada na decisão para preservar interpretação histórica.


## Modelos e gerações de um mesmo documento — RG tradicional, CIN e transições

**Não confundir versão do catálogo com modelo/geração do documento.** Uma versão de política (`versao_catalogo`) registra alterações administrativas; o **modelo documental** (`modelo_documento_id`) representa layouts/gerações diferentes do mesmo tipo e determina quais campos podem ser comprovados. O bootstrap precisa cadastrar modelos distintos, inclusive coexistentes.

Estrutura proposta:
- `ref.tipo_documento`: família estável (ex.: IDENTIDADE_CIVIL, CERTIDAO_NASCIMENTO, CNS, TITULO_ELEITOR, CPF_COMPROVANTE).
- `ref.modelo_documento`: FK para tipo, código do modelo (ex.: `RG_ESTADUAL_TRADICIONAL`, `CIN_NACIONAL`), descrição, órgão emissor/jurisdição quando aplicável, `emissao_inicio` e `emissao_fim` **quando comprovadas por norma**, estado e versão da configuração.
- `ref.modelo_documento_atributo`: FK para modelo, atributo permitido, vigência/versão da associação. É **aqui** que varia a lista de campos entre modelos.
- `identidade.documento_evidencia`: FK para **modelo** efetivamente apresentado e `data_documento` da **instância**, mais metadados de proveniência. Não atribuir automaticamente modelo por data do atendimento.

**Bootstrap mínimo:** `RG_ESTADUAL_TRADICIONAL` e `CIN_NACIONAL` como modelos separados (a CIN substitui gradualmente o RG, mas o RG antigo não deixa de existir por isso). Variantes estaduais ou de layout podem exigir modelos adicionais quando a cobertura de campos for diferente. Não assumir uma única data de transição nacional: verificar legislação, UF, órgão emissor e tipo de emissão. Uma faixa de datas serve para **validar plausibilidade**, não para identificar com certeza o modelo; a identificação usa o modelo efetivo do documento. Na dúvida, registrar `MODELO_NAO_IDENTIFICADO`/abstenção e não atribuir campos não demonstrados.

**Datas distintas:** `emissao_inicio/fim` descrevem quando o modelo pode ser emitido; `data_documento` é a data da ocorrência apresentada e governa a prioridade; `data_atendimento` é somente auditoria. Um RG tradicional apresentado hoje não se torna CIN nem recebe campos da CIN. A emissão de um modelo novo não invalida automaticamente documentos antigos ainda válidos.

**Cenários de aceite:** (1) RG tradicional e CIN apresentados no mesmo atendimento mantêm modelos e conjuntos de atributos próprios; (2) documento emitido antes da implantação de um novo modelo conserva interpretação histórica; (3) coexistência temporal de RG e CIN não gera escolha automática incorreta; (4) inclusão de novo modelo/campos é possível sem recompilação; (5) alteração de política preserva versão aplicada à evidência.


## Contrato de Silver e Gold: modelo e data de expedição (decisão 10/10)

**Sim: `modelo_documento_id` e `data_expedicao` são campos de domínio obrigatórios no registro da evidência documental quando conhecidos e aplicáveis, tanto na Silver quanto na proveniência consultável da Gold.** Não multiplicar `data_expedicao` por atributo nem supor um único documento para toda a pessoa.

- **Silver:** normalizar cada **instância documental** com `documento_evidencia_id`, `modelo_documento_id` (FK `ref.modelo_documento`), `data_expedicao` (data declarada no próprio documento, opcional quando desconhecida), proveniência, estado de validação e vínculo aos atributos efetivamente comprovados. Preservar também o valor bruto e as datas de atendimento/ingestão em campos **distintos**. Para autodeclarações, usar data própria da declaração (`data_declaracao` ou `data_evidencia`), sem fingir expedição documental.
- **Gold:** a **proveniência de cada atributo selecionado** deve apontar para a instância documental vencedora e permitir consultar `modelo_documento_id` e `data_expedicao` por join/view; se houver materialização desses campos na Gold, ela será derivada, não uma segunda fonte independente. Diferentes atributos da mesma pessoa podem ter documentos vencedores diferentes; **não colocar um único modelo/data de documento em `gold.pessoa` como se valesse para todos os campos**.
- **Precedência:** dentro da classe DOCUMENTO, usar `data_expedicao DESC` da instância que comprova o atributo; dentro da classe AUTODECLARACAO, `data_declaracao DESC`. Data do atendimento não participa. Caso um documento tenha data de emissão, expedição, registro e validade distintas, definir e preservar cada semântica: a regra aqui elege **data de expedição efetivamente constante no documento**, não data de validade ou atendimento. Para documentos sem expedição mas com outra data oficial, a equivalência exige regra específica por modelo no catálogo, não substituição implícita.
- **Contratos:** transportar identificador estável do modelo, data própria e proveniência Bronze → Silver → Gold; falhar/abster em modelo não identificado conforme política, sem inventar campos; migrações e testes de rastreabilidade por atributo são necessários.

**Aceite:** (1) dois documentos de modelos distintos na Silver com datas próprias; (2) Gold de nome derivada de uma instância e Gold de endereço derivada de outra, cada qual rastreável; (3) data do atendimento invertida não altera vencedores; (4) modelo não reconhecido não recebe atributos por inferência; (5) data ausente não é substituída por atendimento; (6) replay determinístico respeita versão do catálogo.


## Multiplicidade longitudinal: vários RGs e outros documentos por pessoa

**Cardinalidade obrigatória:** uma pessoa pode ter **0..N instâncias** de um mesmo tipo/modelo documental ao longo da vida (incluindo RGs expedidos em datas diferentes, por órgãos/UFs diferentes e segundas vias). Não impor `UNIQUE(pessoa_id, tipo_documento_id)` nem `UNIQUE(pessoa_id, modelo_documento_id)`, nem sobrescrever o documento anterior ao receber outro.

O desenho deve distinguir **(a) identidade da pessoa**, **(b) instância/expedição do documento** e **(c) identificador impresso**. Um mesmo número de RG pode aparecer em segunda via ou reemissão; número sozinho não identifica uma instância, e diferentes RGs podem ter números distintos. Identificadores como CPF, RG e CNS possuem regras de identidade próprias e não devem ser confundidos com a chave primária da instância documental.

**Relações propostas:** `silver.documento_evidencia` (ou equivalente normalizado) contém `documento_evidencia_id` imutável, `modelo_documento_id`, `numero_documento` quando houver, `orgao_emissor`, `uf_emissora`, `data_expedicao`, `data_validade` quando aplicável, proveniência e estado. Uma tabela de associação `silver.pessoa_documento` (ou FK da observação para o documento, conforme domínio) liga uma pessoa/observação a **várias instâncias**; vinculação de identidade pode mudar sem destruir a evidência. `silver.documento_evidencia_valor` associa cada instância aos atributos efetivamente comprovados. Na Gold, cada atributo selecionado aponta para o `documento_evidencia_id` vencedor; demais documentos e divergências continuam consultáveis.

**Exemplo:** RG estadual expedido em 2005, segunda via em 2015 e CIN expedida em 2025 são **três instâncias** (não três pessoas). Todas permanecem no histórico; para um atributo comprovado por todas e com evidências válidas, vence a instância com a data própria mais recente. Para um atributo que a CIN não comprova, uma instância anterior pode continuar sendo a fonte vencedora. Atendimento/ingestão nunca define a ordem.

**Invariantes de aceite:** duas expedições do mesmo número não colapsam em uma linha; RGs de UFs diferentes coexistem; reprocessar a mesma observação é idempotente sem criar duplicatas; novo documento não apaga o anterior; identidade/CPF não é inferida da mera coincidência de RG; auditoria mostra histórico e o documento vencedor por atributo.

## Pendências técnicas para o aceite

Verificar modelagem normalizada de **instância de documento com data própria única**, **tipo de evidência e catálogo versionado de atributos admissíveis** e **valores comprovados vinculados à instância** no contrato e no SQL; implementar ordenação por classe/data própria na seleção Gold; garantir histórico/auditoria; criar regressão SQL/E2E com datas de atendimento invertidas. Até esses testes passarem, **decisão documentada ≠ implementação certificada**.

## Rastreabilidade de implementação — issue #903

**Estado (10/10/2026):** especificação integrada pela PR #902; implementação Silver/Gold **não comprovada**. O fechamento da #903 requer evidência verificável para cada etapa abaixo, não apenas documentação.

| Etapa | Artefato esperado | Verificação obrigatória |
|---|---|---|
| Catálogo | Migração idempotente de `ref.tipo_documento`, `ref.modelo_documento`, `ref.modelo_documento_atributo` e versão de política | RG/CIN coexistem; alterações administrativas não são sobrescritas pelo seed |
| Silver | Instância documental com modelo, expedição, órgão, identificador e valores comprovados | Uma pessoa possui RGs de múltiplas expedições sem perda histórica |
| Gold | Referência à instância vencedora **por atributo** | Nome e nascimento podem ter documentos vencedores distintos |
| Temporalidade | Expedição separada de atendimento, ingestão e validade | Inverter atendimentos não altera precedência; expedição ausente não recebe fallback |
| Identidade | Vínculo entre observação documental e pessoa reconciliável | RG compartilhado/segunda via não cria identidade nem colapsa instâncias automaticamente |
| Governança | Catálogo versionado e edição auditada/autorizada | Replay preserva a versão aplicada; sem mutação silenciosa em produção |
| Regressão | Testes SQL e integração em ambiente descartável | Reprocessamento idempotente, sem perdas e com proveniência completa |

**Ordem de entrega:** (1) schema/catálogo; (2) Silver; (3) Gold; (4) integração/serving. Cada fatia pode ter PR independente, mas não deve ser declarada completa sem regressão de ponta a ponta.
