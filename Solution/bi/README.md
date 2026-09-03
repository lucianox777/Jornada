# Power BI — fontes da Jornada

**O fonte do Power BI da Fase 1 fica diretamente em `bi/`.**

Estrutura versionada:

```text
bi/
├── Jornada.pbip
├── Jornada.SemanticModel/
│   ├── definition/
│   │   ├── tables/
│   │   └── expressions.tmdl
│   └── definition.pbism
├── Jornada.Report/
│   ├── definition/
│   │   ├── pages/
│   │   └── report.json
│   ├── definition.pbir
│   └── StaticResources/   # quando houver recursos estáticos
└── .gitignore
```

Pastas `.pbi/`, `localSettings.json`, `cache.abf` e `.pbix` são artefatos locais/gerados e ficam fora do versionamento.

## Ferramenta obrigatória de autoria e validação

Os artefatos `Jornada.pbip`, PBIR e TMDL são **fontes de Microsoft Power BI Desktop**. A Fase 1 exige abertura, validação e salvamento do projeto em **Microsoft Power BI Desktop na versão homologada pelo ambiente municipal** antes da promoção para HML/Produção. Validações estruturais de JSON/TMDL ou geração de arquivos fora do Desktop não substituem esse gate.

A publicação no Power BI Service/Gateway, credenciais e refresh são atividades do ambiente corporativo. Arquivos `.pbix` gerados localmente permanecem fora do versionamento; o código-fonte oficial continua sendo PBIP/PBIR/TMDL.

Esses arquivos são fontes de BI, não projetos MSBuild/.NET. Por isso **não são incluídos em `Jornada.sln`**. O modelo consome exclusivamente as views `serving.v_bi_*` do SQL Server. Credenciais reais não são versionadas.

## Linkage versionado

`serving.v_bi_linkage` expõe o vínculo corrente com `modelo_fallback_versao`, `linkage_run_id`, estado/tipo do run e `T_LINKAGE`.
`serving.v_bi_linkage_runs` expõe a história de execuções por versão, duração e contagens. Medidas de score devem ser segmentadas por `modelo_versao`; versões distintas não são agregadas como se fossem diretamente equivalentes.


## Observabilidade do linkage probabilístico

`serving.v_bi_linkage_runs` expõe `sem_candidato_no_bloco`, metadados da amostra de treinamento e os limites do prior condicionado ao tamanho do bloco. O BI deve segmentar scores por `modelo_versao`/`linkage_run_id` e não comparar numericamente scores de versões diferentes sem essa dimensão.

## Benefícios Concedidos, Serviços Prestados e Registros

A persistência física de Serving foi consolidada em `serving.registro_integrado`, mas **o Power BI nunca deve usar essa tabela diretamente**. O modelo consome exclusivamente as views autorizadas:

- `serving.v_bi_beneficios_concedidos` preserva os atributos próprios de Benefício Concedido e pode expor valor/quantidade quando aplicáveis;
- `serving.v_bi_servicos_prestados` preserva os atributos próprios de Serviço Prestado e **não expõe campos monetários**;
- `serving.v_bi_registros` é a linha do tempo comum do Histórico da Jornada e **não expõe campos monetários**.

Essa separação por view é a barreira de exposição após a consolidação física do Serving. O modelo semântico continua com tabelas `BeneficiosConcedidos`, `ServicosPrestados` e `Registros` separadas.


## Versionamento factual automático

As tabelas semânticas factuais consomem apenas a versão lógica `VIGENTE` de cada `(sistema_origem, codigo_registro_origem)`. A finalística não gerencia número de versão. Reenvio de conteúdo idêntico não gera nova versão lógica; conteúdo diferente gera `versao_interna` na Jornada. A trilha completa, incluindo `HISTORICO`, `RETIFICADO` e `EXCLUSAO`, fica em `serving.v_bi_registro_versoes` para auditoria e não entra nas métricas factuais padrão.

## Atraso de Recebimento

A v3.16 inclui a tabela semântica `Atrasos`, baseada em `serving.v_bi_atrasos`, e a página **Atraso de Recebimento**. O cálculo é transversal a Benefícios Concedidos e Serviços Prestados e usa a configuração versionada do Tipo (`monitorar_atraso`, `prazo_recebimento_dias`, `marco_atraso_codigo`).

- `recebido_em`: instante oficial gerado pelo servidor da API e persistido em UTC;
- `data_recebimento_sla`: data civil derivada de `recebido_em` no fuso institucional de São Paulo;
- `dias_para_recebimento`: latência em dias entre o marco configurado e `data_recebimento_sla`;
- `dias_atraso`: excedente sobre o prazo, nunca negativo;
- `status_atraso`: `NO_PRAZO`, `EM_ATRASO`, `SEM_MARCO` ou `DATA_FUTURA`;
- `faixa_atraso`: `NO_PRAZO`, `1-3_DIAS`, `4-7_DIAS`, `8-30_DIAS`, `31+_DIAS`, além dos estados de qualidade.

`SEM_MARCO` e `DATA_FUTURA` não entram como atraso. O marco temporal não é inferido universalmente: cada versão de Tipo declara a semântica correta.

## Auditoria de acesso

`serving.v_bi_api` expõe somente metadados operacionais agregáveis: Gestor, rota, método, status, duração, bytes, `recurso_codigo`, quantidade de Pessoas e instante. A v3.47 não expõe nem persiste finalidade declarada ou flag de auditoria reforçada. UUID individual, HMAC do agente, `credencial_id` e `api_evento_id` permanecem fora da view de BI.

O modelo semântico mantém `Requisições API - últimos 10 min` como indicador volumétrico genérico. A proteção contra enumeração é feita pelos rate limits de borda e pós-autenticação por classe de endpoint; não existe bucket especial de primeiro atendimento.


## Geografia analítica

Não existe entidade analítica autônoma nem API de “Território de Referência”. O BI usa exclusivamente o snapshot de `REFERENCIA_TERRITORIAL` efetivamente selecionado para a Pessoa, com `natureza_referencia_territorial`, Distrito e Subprefeitura. Referência explícita da fonte prevalece; na ausência, `ENDERECO_RESIDENCIAL` pode gerar fallback `DOMICILIAR`. `ACOLHIMENTO_INSTITUCIONAL` e `REFERENCIA_TERRITORIAL_DECLARADA` nunca são inferidos pela Jornada. Quando nenhuma referência territorial possui geografia conhecida, as views exibem `SEM_REFERENCIA_TERRITORIAL`. Cada Benefício Concedido/Serviço Prestado guarda `referencia_territorial_observacao_id`, preservando a linhagem histórica sem recodificar fatos passados. `ENDERECO_RESIDENCIAL` não é fonte direta de mapa ou segmentação; quando funciona como fallback DOMICILIAR, sua geografia já está materializada no snapshot territorial selecionado. O BI não publica endereço individual.


## Qualidade por Secretaria e Tipo

As superfícies especializadas de qualidade de dados incluem:

- `serving.v_bi_qualidade_beneficios_concedidos` / tabela semântica `QualidadeBeneficios`, para análise Secretaria (Gestor) × Benefício Concedido;
- `serving.v_bi_qualidade_servicos_prestados` / tabela semântica `QualidadeServicos`, para análise Secretaria (Gestor) × Serviço Prestado.

As views trabalham somente com a versão factual `VIGENTE` e expõem flags aditivas, sem criar um score único opaco. São comuns: completude da Entrega, classificação geográfica residencial, cobertura/resultado de QC, versionamento e retificação. Para Benefícios também há coerência de vigência e preenchimento compatível com `tipo_medida`; para Serviços, unidade e situação são taxas de preenchimento (campos opcionais não são tratados automaticamente como erro contratual). As páginas **Qualidade — Benefícios Concedidos** e **Qualidade — Serviços Prestados** permitem comparação por Secretaria e por Tipo.

`ingestao.item_processado` possui índice de apoio `(lote_id, classe_item)` para o caminho natural do BI. A retenção da granularidade por item deve possuir janela aprovada pela governança; após essa janela, a política pode consolidar históricos e preservar a última confirmação de cada origem, sem apagar a trilha Golden/Silver.

## Qualidade dos Pacotes — v3.33

`serving.v_bi_qualidade_envios` / tabela `QualidadeEnvios` sustentam o dashboard **Qualidade dos Pacotes**. Indicadores: Entregas recebidas, taxa processada, itens/pessoas/registros recebidos, retransmissões idênticas, novas versões, falhas, períodos factuais com zero ocorrências, duração e recortes por Secretaria/Gestor, Sistema de Origem e Tipo.

`serving.v_bi_qualidade_identidade_origem` / tabela `QualidadeIdentidadeOrigem` sustentam **Qualidade de Identidade por Origem**. O painel expõe cobertura de CPF apenas como flag agregada, cobertura UUID, conflitos, `SEM_CANDIDATO_NO_BLOCO_DATA_NASCIMENTO`, concentração de nascimento em 01/01 e cobertura geográfica. O valor do CPF não é exposto.


## Territorialização, carga inicial e manutenção da Bronze — v3.38

A v3.38 adiciona superfícies executáveis no projeto PBIP, além das views SQL:

- tabela `Territorializacao` + página **Territorialização**, baseadas em `serving.v_bi_territorializacao`;
- tabela `CargaInicial` + página **Carga Inicial**, baseadas em `serving.v_bi_carga_inicial`;
- tabela `ManutencaoBronze` + página **Manutenção da Bronze**, baseadas em `serving.v_bi_manutencao_bronze`.

Na Fase 1 a geografia analítica é responsabilidade da origem/Gestor: `situacaoGeografia` é explícita e `RESOLVIDA` exige Distrito, Subprefeitura e `referenciaMalha`. O BI deve acompanhar cobertura por Gestor, situação e versão/referência de malha, sem depender de chamadas geográficas online no Processor.
