# Jornada — Teste real “as is” SEHAB 2024-07-17

**Base testada:** `DADOS_SEHAB_20240717.zip`  
**SHA-256:** `305d577839dacf7e3cf09046cfa9b4b58c9793a5b862b896e971578326fbd548`  
**Jornada usada como referência:** Base Normativa **v3.64**, Solution Engenharia **v4.05**, SolutionSchema **3.69**.

## 1. Critério do teste

O teste não corrige, completa nem “melhora” os dados da SEHAB. Normalizações puramente estruturais podem existir num futuro adapter/exportador (por exemplo, transformar uma data-hora em data ou converter `1.000` para número), mas o teste não inventa informação de negócio, não deduz situação de concessão a partir de pagamento e não preenche campos cadastrais ausentes.

Os arquivos reais também não são incorporados ao repositório por conterem dados pessoais. A suíte adicionada usa a variável `JORNADA_SEHAB_ASIS_ZIP` e lê o ZIP externo em streaming.

## 2. Snapshot da origem

O ZIP possui **16 CSVs**, **179.427.484 bytes descompactados** e todas as contagens abaixo foram confirmadas:

| Arquivo | Linhas de dados |
|---|---:|
| beneficios_cencedidos_aa.csv | 2.484.518 |
| beneficios_cencedidos_ce.csv | 6.410 |
| con_cbp_aa.csv | 48.679 |
| con_contatos_emails_aa.csv | 18.097 |
| con_contatos_emails_ce.csv | 34 |
| con_contatos_endereco_aa.csv | 48.688 |
| con_contatos_endereco_ce.csv | 1.060 |
| con_contatos_telefones_aa.csv | 43.967 |
| con_contatos_telefones_ce.csv | 1.325 |
| con_informações_complementares_aa.csv | 48.670 |
| con_informações_complementares_ce.csv | 1.060 |
| con_relações_familiares_aa.csv | 59.323 |
| con_relações_familiares_ce.csv | 242 |
| dados_base_central_aa.csv | 45.774 |
| dados_base_central_ce.csv | 6.828 |
| dados_cbp_ce.csv | 5.947 |

## 3. Resultado direto contra o envelope da Jornada

**REJEIÇÃO ESPERADA.** O ZIP original não é um envelope Jornada v2. O contrato atual exige exatamente três arquivos na raiz: `manifest.json`, `pessoas.jsonl` e `registros.jsonl`.

Isto não é defeito da SEHAB nem da Jornada: confirma a necessidade de um **adapter/exportador do Gestor** que leia a estrutura nativa e produza o envelope canônico, preservando a origem.

## 4. AA01 — Auxílio Aluguel

A origem contém **45.774 linhas**, todas com código `AA01`, nome `Auxílio Aluguel` e CPF estruturalmente com 11 dígitos. Há 45.774 beneficiários distintos.

O contrato `AA01/v1` já existe na Solution atual. Entretanto, o arquivo `dados_base_central_aa.csv` não informa uma **situação de vigência da concessão**. O schema atual exige `situacaoVigencia` (`VIGENTE`, `SUSPENSA` ou `ENCERRADA`). O arquivo de pagamentos contém estados como `Ativo`, `Excluido`, `Atendido` e `Bloqueado`, mas tratá-los automaticamente como estado da concessão seria uma inferência semântica. O teste “as is” deliberadamente não faz essa conversão.

Também há **2.484.518 linhas de pagamento/competência** entre 201501 e 202407, para 45.776 CPFs e 45.776 números de benefício. Para os 45.774 beneficiários da base central existe exatamente um número de benefício por CPF; existem ainda 2 CPFs no arquivo de pagamentos que não aparecem na base central.

### Núcleo de Pessoa AA01

Dos 45.774 beneficiários:

- **45.639** podem satisfazer CPF + nome + nascimento + nome da mãe sem inventar dados;
- **135** não podem;
- 25 não possuem linha correspondente no CBP;
- 109 têm nome da mãe ausente;
- 3 têm nascimento ausente/inválido.

Os motivos podem se sobrepor em um mesmo beneficiário.

## 5. AE01 — Auxílio Emergencial R$ 1.000,00

A origem contém **6.828 linhas**, código `AE01`, para **5.492 beneficiários distintos**. Portanto, existem 1.336 linhas além da cardinalidade de um registro por CPF.

A primeira lacuna é objetiva: **não existe `config/contracts/registros/AE01` na Solution v4.05**. Logo, AE01 ainda não pode ser publicado como Tipo factual canônico sem criação e aprovação formal do contrato.

Além disso:

- 900 linhas não informam início nem fim de vigência;
- o arquivo de pagamentos possui 6.410 linhas;
- há 5.954 pares CPF+número de benefício;
- 415 beneficiários possuem mais de um número de benefício, chegando a 3 números por CPF;
- a base central não carrega o número do benefício, portanto o vínculo linha de concessão ↔ número de benefício não é unívoco apenas pelos campos disponíveis.

### Núcleo de Pessoa AE01

Dos 5.492 beneficiários:

- **2.673** podem satisfazer o núcleo obrigatório sem inventar dados;
- **2.819** não podem;
- 19 não possuem linha correspondente no CBP;
- 2.800 têm nome da mãe ausente.

O `dados_cbp_ce.csv` contém 424 CPFs repetidos; em 3 CPFs há divergência de nome e em 1 há divergência de nome da mãe entre as linhas de origem.

## 6. Pagamentos/competências

Os arquivos `beneficios_cencedidos_aa.csv` e `beneficios_cencedidos_ce.csv` representam um conceito próprio: pagamento/competência, com número de benefício, data de pagamento, ano/mês, situação e valor.

A Solution v4.05 declara explicitamente `PAGAMENTO_E_RECEBIMENTO_CONCEITUAIS_NAO_IMPLEMENTADOS_FASE1`. Portanto, o teste confirma uma fronteira já declarada pela release: **não devemos achatar os 2,49 milhões de pagamentos como se fossem novas concessões**.

## 7. Campos da origem que não cabem automaticamente no contrato atual

O núcleo de Pessoa atual absorve CPF, nome, nascimento e nome da mãe. O catálogo transversal atual permite endereço residencial/referência territorial, telefone, e-mail e nome social.

A origem SEHAB também traz, entre outros, gênero, local de nascimento, RG, país, título de eleitor, CPF/nome do pai, raça, filhos/escola, acesso à internet, RNE, passaporte, redes sociais e relações familiares. Esses conceitos **não devem ser silenciosamente descartados nem enviados como propriedades extras**, pois os contratos atuais são fechados. Cada grupo exige decisão explícita: integrar na Jornada, preservar somente em Bronze/Silver ou manter fora do escopo transversal.

Também precisam de regra de adapter, sem invenção semântica:

- `con_decreto`;
- `con_tipo_beneficio`;
- `con_valor_desconto`;
- códigos geográficos de Distrito/Subprefeitura (a origem traz principalmente nomes, enquanto o contrato de geografia resolvida exige códigos e referência de malha).

## 8. Conclusão do teste

O teste real **não recomenda mudar os dados para fazê-los passar**. Ele demonstra onde o contrato atual e a origem real se encontram e onde ainda não se encontram.

### Já suportado estruturalmente

- Gestor SEHAB e schema de Pessoa;
- Tipo AA01;
- núcleo de Pessoa quando a origem fornece os campos obrigatórios;
- contatos/endereço/nome social através dos atributos transversais, mediante regras de normalização/evidência;
- envelope Jornada v2 como destino do adapter.

### Pendências objetivas reveladas pela SEHAB

1. definir como AA01 recebe `situacaoVigencia` sem inferência indevida;
2. criar/aprovar o contrato factual de AE01 se o benefício fizer parte da Fase 1;
3. decidir formalmente se pagamento/competência entra agora ou permanece fora da Fase 1;
4. definir tratamento dos registros de Pessoa sem nome da mãe/nascimento exigidos pelo núcleo atual;
5. decidir destino dos campos reais da SEHAB que hoje não pertencem ao núcleo/catálogo transversal;
6. definir regra inequívoca de identidade do registro de concessão, especialmente AE01, onde há múltiplas ocorrências por CPF.

## 9. Teste permanente adicionado

Foi preparado `Sehab20240717CharacterizationTests.cs`, categoria NUnit `ExternalRealData`. Ele não contém PII e não embute o ZIP. Para executar em uma máquina com .NET:

```powershell
.\scripts\run-sehab-as-is-tests.ps1 -ZipPath "C:\caminho\DADOS_SEHAB_20240717.zip"
```

A suíte caracteriza o snapshot, confirma a rejeição do ZIP bruto pelo envelope v2, testa a cobertura dos contratos AA01/AE01 e mede a incompletude real do núcleo de Pessoa.
