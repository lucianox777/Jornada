# Manual de Integração do Gestor — Jornada do Cidadão

## 1. Objetivo

Este manual descreve o caminho técnico para uma Secretaria/Gestor publicar dados na Jornada do Cidadão sem alterar a semântica do sistema finalístico e sem exigir que a origem adote internamente o formato da Jornada.

A regra central é simples: **o Gestor continua dono do seu sistema, da qualidade e da semântica dos dados; a integração produz um envelope canônico Jornada v2 a partir da origem**.

A Jornada não é o sistema transacional do Gestor. Benefícios e serviços são registrados primeiro no sistema finalístico. A publicação para a Jornada deve ser idempotente e, quando possível, desacoplada por outbox ou mecanismo equivalente.

## 2. Responsabilidades

### Gestor/finalística

O Gestor é responsável por:

- designar interlocutor técnico para integração e qualidade cadastral;
- declarar o `codigoSistemaOrigem` de cada sistema finalístico integrado;
- mapear os campos nativos para os contratos vigentes de Pessoa e de Registro;
- gerar chaves locais estáveis (`codigoPessoaOrigem` e `codigoRegistroOrigem`) quando existirem;
- não inventar informação ausente nem inferir estados de negócio não declarados pela origem;
- resolver a geografia exigida pelos contratos quando aplicável;
- autenticar e autorizar seus agentes humanos; a Jornada não substitui a autenticação da finalística;
- proteger a credencial técnica fornecida para a integração;
- manter o adapter/exportador necessário para transformar sua estrutura nativa no envelope Jornada.

### Jornada

A Jornada é responsável por:

- autenticar a credencial técnica de integração;
- validar Gestor, sistema de origem, versão dos contratos e envelope;
- validar limites, hashes, nome canônico e idempotência;
- preservar o pacote recebido na Bronze conforme a política vigente;
- processar Pessoa e fatos segundo os contratos versionados;
- manter trilha de auditoria, proveniência e estados de processamento;
- rejeitar pacotes inválidos sem silenciosamente completar ou corrigir dados da finalística.

## 3. Pré-requisitos de onboarding

Antes da primeira carga, devem estar definidos:

1. código institucional do Gestor, por exemplo `SEHAB`;
2. um ou mais códigos de sistema de origem;
3. versão de schema de Pessoa autorizada para o Gestor;
4. Natureza e Tipo dos fatos enviados (`BENEFICIO` ou `SERVICO`, com código de Tipo contratado);
5. credencial técnica e scopes aplicáveis;
6. estratégia de geração de `codigoPessoaOrigem` e `codigoRegistroOrigem`;
7. regra de extração incremental ou de carga inicial;
8. adapter/exportador para produzir o envelope Jornada v2;
9. massa de homologação sem dados pessoais reais sempre que possível;
10. validação do pacote real em ambiente controlado antes da produção.

Os contratos máquina ficam em `Solution/config/contracts/`. O contrato OpenAPI fica em `Solution/openapi/jornada-v1.openapi.json`.

## 4. O adapter/exportador do Gestor

O sistema finalístico **não precisa armazenar seus dados no formato Jornada**. O adapter lê o formato nativo e produz um ZIP canônico.

O adapter pode normalizar apenas aspectos estruturais explicitamente definidos pelo contrato, por exemplo representação de data ou número. Ele não pode:

- criar nome da mãe inexistente;
- transformar pagamento em situação de vigência sem regra institucional;
- criar data de nascimento presumida;
- inventar código geográfico;
- converter um atributo nativo em conceito transversal sem mapeamento aprovado;
- descartar silenciosamente campos relevantes cuja destinação ainda não foi decidida.

Quando a origem contém um conceito ainda não contratado pela Jornada, a integração deve tratá-lo como pendência de contrato, e não forçar seu encaixe em outro campo.

## 5. Envelope Jornada v2

Cada entrega é um arquivo ZIP contendo **exatamente três arquivos na raiz**:

```text
manifest.json
pessoas.jsonl
registros.jsonl
```

Não são aceitas pastas adicionais, arquivos auxiliares ou o ZIP nativo do sistema finalístico como substituto do envelope.

### `manifest.json`

Exemplo para a SEHAB / Auxílio Aluguel:

```json
{
  "formatoVersao": 2,
  "pessoaSchemaVersao": 2,
  "codigoSistemaOrigem": "SEHAB",
  "natureza": "BENEFICIO",
  "codigoTipo": "AA01",
  "tipoVersao": 1,
  "dataReferencia": "2026-09-06T00:00:00-03:00"
}
```

Se `registros.jsonl` tiver conteúdo, `natureza`, `codigoTipo` e `tipoVersao` são obrigatórios. Em carga exclusivamente cadastral, os três podem ser nulos.

### `pessoas.jsonl`

Cada linha contém um JSON independente e deve obedecer ao schema de Pessoa autorizado para o Gestor.

A entrega deve conter, no mínimo:

- Pessoas incluídas no período;
- Pessoas alteradas cadastralmente no período;
- todas as Pessoas referenciadas pelos fatos de `registros.jsonl`.

`codigoPessoaOrigem` é a chave local opaca da finalística. Na versão de Pessoa que admite o fallback implementado, quando `codigoPessoaOrigem` não for informado e houver CPF válido, a Jornada pode usar o CPF como código interno de origem. Isso **não transforma o código em CPF semanticamente**: somente o campo `cpf` é tratado como CPF.

### `registros.jsonl`

Cada linha contém um fato finalístico compatível com o Tipo declarado no manifesto. `codigoRegistroOrigem` é obrigatório e deve ser estável no namespace do sistema de origem.

A finalística não envia número de versão. Para a mesma chave:

- mesmo conteúdo de negócio = retransmissão idempotente;
- conteúdo de negócio diferente = nova versão interna calculada pela Jornada;
- `operacao` expressa a semântica (`INCLUSAO`, `ALTERACAO`, `RETIFICACAO` ou `EXCLUSAO`), e não o número da versão.

`registros.jsonl` pode ter zero bytes quando não houver fatos no período.

## 6. Nome canônico do arquivo

A entrega deve usar o padrão:

```text
ENTREGA_<GESTOR>_<SISTEMA_ORIGEM>_v2_<sha256>.zip
```

O SHA-256 deve corresponder ao pacote efetivamente enviado. O nome canônico e o hash fazem parte das validações de borda e de rastreabilidade.

## 7. Envio pela API

Endpoint:

```text
POST /api/v1/ingestao/entregas
```

Headers mínimos:

```text
Content-Type: application/zip
Content-Disposition: attachment; filename="<NOME_CANONICO>.zip"
Idempotency-Key: <chave idempotente da entrega>
X-Jornada-Gestor: <CODIGO_DO_GESTOR>
X-Jornada-Access-Key: <segredo>
```

Nunca colocar a chave de acesso em código-fonte, arquivo de exemplo, documentação pública ou pacote de dados.

O cliente não envia `entregaId`, `loteSeq`, `loteTotal` nem versão interna de Pessoa/fato.

## 8. Idempotência e retransmissão

`Idempotency-Key` identifica a tentativa lógica de entrega. Retransmissão deve usar a mesma chave quando representar a mesma entrega.

A integração deve estar preparada para falha de rede após o envio e antes da confirmação. Nessa situação, o comportamento correto é consultar o estado e retransmitir de forma idempotente, não criar artificialmente uma nova entrega.

## 9. Consulta de acompanhamento

Após o recebimento, o Gestor acompanha a entrega pelos endpoints de ingestão documentados em `Solution/docs/API.md` e no OpenAPI. O sistema integrador deve registrar, no mínimo:

- instante de envio;
- nome e SHA-256 do pacote;
- `Idempotency-Key`;
- código do Gestor e sistema de origem;
- identificador retornado pela Jornada;
- status final e eventual código/motivo de rejeição;
- `correlation_id` quando disponibilizado.

Não registrar a chave de acesso nem dados pessoais desnecessários em logs técnicos.

## 10. Carga inicial e cargas seguintes

A carga inicial deve representar a fotografia acordada para entrada na Jornada. Depois dela, o Gestor deve publicar inclusões, alterações cadastrais e fatos novos/corrigidos conforme a cadência acordada.

A Jornada preserva histórico e proveniência. Uma correção posterior não autoriza reescrever a origem histórica nem apagar a observação original.

## 11. Homologação mínima do Gestor

Antes de produção, a integração deve comprovar:

1. geração do ZIP com somente os três arquivos canônicos;
2. nome canônico e SHA-256 corretos;
3. autenticação por Gestor;
4. rejeição de credencial inválida;
5. rejeição de Gestor/sistema incompatível;
6. validação dos schemas de Pessoa e Registro;
7. idempotência de reenvio;
8. rejeição de pacote alterado/corrompido;
9. tratamento de `registros.jsonl` vazio;
10. acompanhamento até estado final;
11. ausência de segredo e PII desnecessária nos logs do adapter;
12. reconciliação de contagens entre origem, pacote e processamento.

## 12. Caso real SEHAB — 17/07/2024

O snapshot `DADOS_SEHAB_20240717.zip` foi usado como teste de caracterização **fora do repositório**, porque contém dados pessoais.

A caracterização congelada encontrou 16 CSVs e confirmou que o ZIP nativo não é um envelope Jornada v2. Isso é comportamento esperado: a SEHAB precisa de um adapter/exportador que converta a estrutura nativa para `manifest.json`, `pessoas.jsonl` e `registros.jsonl`.

A suíte permanente fica em:

```text
Solution/tests/Jornada.Tests/ExternalRealData/Sehab20240717CharacterizationTests.cs
```

Ela não contém PII e só usa o arquivo real quando a variável `JORNADA_SEHAB_ASIS_ZIP` aponta para o snapshot externo.

No Windows, executar a partir de `Solution`:

```powershell
.\scripts\run-sehab-as-is-tests.ps1 -ZipPath "C:\dados\DADOS_SEHAB_20240717.zip"
```

O teste caracteriza, sem corrigir a fonte:

- inventário e contagens congeladas do snapshot;
- rejeição esperada do ZIP bruto como envelope Jornada v2;
- existência do contrato `AA01`;
- ausência atual de contrato factual `AE01`;
- ausência de `situacaoVigencia` diretamente declarada pela origem AA01;
- arquivos de pagamento/competência como conceito distinto ainda não modelado na Fase 1;
- incompletude real do núcleo de Pessoa sem preenchimento artificial.

## 13. Pendências reveladas pela SEHAB

O teste real mantém explícitas as decisões ainda necessárias:

1. definir como AA01 recebe `situacaoVigencia` sem inferência indevida;
2. criar/aprovar o contrato factual `AE01` caso entre na Fase 1;
3. decidir se pagamento/competência entra na Fase 1 ou permanece fora;
4. definir o tratamento dos registros sem todos os campos exigidos pelo núcleo vigente de Pessoa;
5. decidir o destino dos campos SEHAB que não pertencem hoje ao núcleo/catálogo transversal;
6. definir identidade inequívoca do registro de concessão, especialmente em ocorrências múltiplas por Pessoa.

Essas pendências são de contrato e governança. **O adapter não deve resolvê-las por inferência própria.**

## 14. Referências do repositório

- `Solution/docs/API.md` — contrato operacional da API;
- `Solution/openapi/jornada-v1.openapi.json` — contrato máquina;
- `Solution/config/contracts/` — schemas versionados de Pessoa e Registros;
- `Solution/docs/Runbook_Testes_Tecnicos.md` — execução técnica e evidências;
- `Solution/tests/Jornada.Tests/ExternalRealData/Sehab20240717CharacterizationTests.cs` — caracterização real SEHAB sem PII no Git;
- `Documentos/Testes/SEHAB/Relatorio_SEHAB_20240717_AS_IS.md` — resultado consolidado do snapshot de 17/07/2024.
