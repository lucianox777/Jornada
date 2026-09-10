# Arquitetura de Identidade e Linkage — especificação corrente

**Status:** normativa para a V1 ainda não publicada.  
**Princípio de versionamento:** como a Jornada ainda não foi publicada, este documento descreve diretamente a arquitetura vigente. Não há necessidade de manter ADRs como registro de decisões históricas internas que ainda podem ser consolidadas antes da primeira publicação.

Este documento incorpora as decisões anteriormente distribuídas entre documentos ADR de identidade progressiva, âncora CPF, cutover do Processor, composição reversível e Linkage multievidência/Fellegi–Sunter.

## 0. Hierarquia normativa e tecnologia relacional

A hierarquia de precedência da Jornada é única: **Especificação Técnica vigente → requisitos normativos e documentos de arquitetura corrente subordinados → implementação**. O código validado na `master` é realização e evidência de conformidade; não cria norma por si mesmo. Divergência entre implementação e norma deve ser tratada como defeito ou resultar em alteração formal prévia da documentação normativa aplicável.

**Microsoft SQL Server é a tecnologia relacional normativa da Jornada.** O DDL canônico, o contrato de prontidão do banco e o baseline de instalação da Fase 1 são definidos para SQL Server. PostgreSQL permanece como provider operacional paralelo para escopos explicitamente suportados de desenvolvimento, teste, calibração e avaliação de Linkage; sua presença no adapter/Npgsql não substitui nem rebaixa o SQL Server como tecnologia relacional normativa. Microsoft Fabric permanece destino/ambiente analítico e de compatibilidade quando aplicável, sem transformar o Lakehouse ou SQL Endpoint em substituto implícito do banco relacional operacional normativo.

Durante a consolidação de engenharia v5.00, novas features ficam suspensas: ideias adicionais devem ser registradas como issues até o fechamento do schema, requisitos, UML, artefatos normativos e release.

## 1. Identidade progressiva

A Jornada atribui um `initial_uuid` a cada identidade de origem admitida, inclusive sem CPF. A chave `(sistema_origem_id, codigo_pessoa_origem)` recupera de forma idempotente a mesma referência inicial. O UUID inicial é aleatório, não deriva de PII, nunca é reciclado, transferido ou alterado e não constitui prova de unicidade municipal.

A referência canônica corrente é representada por `canonical_uuid` e pode evoluir somente por decisão explícita, versionada e auditável. Os únicos estados públicos da identidade progressiva são:

- `PROVISORIA`: UUID inicial criado, sem referência canônica publicada;
- `REFERENCIA`: referência canônica estabelecida segundo política e evidências admitidas;
- `INDEFINIDA`: execução completa não conseguiu estabelecer referência suficientemente segura.

`RESOLVIDO` permanece válido em `identidade.vinculo_fonte`, onde significa atribuição corrente de uma observação; não é estado da identidade progressiva. Os resultados `NOVA_IDENTIDADE`, `ASSOCIACAO_EXISTENTE` e `INDEFINIDA` são resultados de execução, não novos estados da Pessoa.

Execução incompleta, timeout, erro de banco, universo truncado ou limite excedido não equivalem a ausência de candidato e não publicam referência. Fatos válidos permanecem independentes do estado da identidade e podem existir como `ATRIBUIDA`, `PENDENTE_IDENTIDADE` ou `CONFLITO_IDENTIDADE`.

## 2. Âncora permanente de CPF

Um CPF válido, confiável e admitido pela Jornada possui exatamente uma âncora permanente CPF→UUID. Uma vez constituída, ela não é transferida, apagada, reciclada nem substituída por decisão probabilística. Cada UUID de âncora admite no máximo um CPF.

A permanência independe da existência corrente de fatos, observações ou mapas ativos. Se o CPF reaparecer, a Jornada recupera o UUID reservado. Uma atribuição factual incorreta é corrigida movendo vínculos/registros com preservação de histórico; a âncora não é transferida.

O Linkage pode associar identidade sem CPF a uma âncora existente quando a política homologada permitir, mas não pode criar segunda âncora para o mesmo CPF nem fundir automaticamente âncoras de CPFs distintos. A existência da âncora não prova que todos os registros associados pertençam ao titular e não amplia autorização de acesso.

`identidade.cpf_ancora` é append-only e protegida por unicidade/FK. Reserva e consulta são transacionais e idempotentes. Backfill não escolhe automaticamente entre conflitos e não inventa resolução de Linkage.

## 3. Persistência e cutover do Processor

`identidade.pessoa_origem_progressiva` mantém uma linha por origem, com `initial_uuid`, `canonical_uuid` opcional, estado, versão, timestamps e proveniência legada. O histórico de eventos é append-only.

O cutover operacional segue duas fases:

1. backfill paginado, retomável e reentrante até backlog zero;
2. garantia transacional para novas observações no caminho do Processor.

A ativação falha fechada enquanto houver origem histórica sem `initial_uuid`. Novas observações asseguram a referência inicial na mesma transação do lote; rollback não pode deixar Pessoa, evento ou referência progressiva órfãos. Em SQL Server, a garantia fica no ponto compatível com o `OUTPUT INSERTED` do writer, sem trigger incompatível em `silver.pessoa_origem`.

Rollback operacional pode interromper a automação futura, mas não apaga nem recicla referências já materializadas.

## 4. Composição reversível

O decisor determina a atribuição admissível; o mecanismo de composição aplica uma decisão explícita e versionada. O mecanismo não calcula similaridade, não procura candidatos, não corrige CPF e não escolhe sobrevivente por UUID mínimo, ordem, tamanho do agregado ou score.

O destino é declarado pela política/decisão. Quando houver âncora CPF admitida, o destino deve respeitar essa âncora. Duas âncoras distintas não podem ser fundidas automaticamente. Separações podem retornar uma origem ao próprio `initial_uuid`, usar referência admissível ou deixá-la sem referência canônica quando a atribuição for indefinida.

A operação persistente usa `decision_id` estável, versão de política, evidência opaca, instante UTC e partição completa dos membros. A camada de persistência deve carregar estado autoritativo, provar fechamento do conjunto afetado, verificar versões sob locks determinísticos, validar reservas e autoridade CPF e rejeitar decisão obsoleta.

Replay idêntico retorna o recibo persistido sem novas escritas; reutilização do mesmo ID com conteúdo diferente falha. Hash identifica conteúdo, mas não autentica solicitante nem prova verdade/completude.

Referências históricas preservam os membros do agregado. Se uma referência antiga tiver múltiplos sucessores, o resultado histórico é `AMBIGUA`; ausência de referência suficiente é `INDEFINIDA`. Nunca se escolhe sucessor arbitrário para simplificar uma separação.

Publicação de composição deve manter decisão, eventos progressivos, vínculos factuais, histórico e Gold/Serving numa unidade atômica ou protocolo versionado equivalente que impeça estado misto. Fatos válidos e proveniência são preservados.

## 5. Linkage multievidência

Todos os campos recebidos e preservados pela Jornada podem ser evidências candidatas, desde que sua utilização seja semanticamente legítima, segura, calibrada e mensurável. Não existe lista fechada limitada ao núcleo cadastral e nenhum campo entra automaticamente no score apenas por existir.

O núcleo probabilístico canônico é explicável e baseado em Fellegi–Sunter. Não se usa IA generativa como mecanismo de resolução e não se acrescenta segundo modelo de ML apenas para substituir o papel estatístico do calibrador. Comparadores como Jaro–Winkler produzem estados de concordância; não concorrem com Fellegi–Sunter.

Cada feature habilitada possui identificação, origem, semântica, normalizador/comparador versionados, estados de qualidade, política de ausência, parâmetros `m/u`, proveniência e evidência de validação. O modelo publicado congela features, versões, parâmetros, regras de dependência e ruleset de blocking.

Evidências candidatas incluem nome, nome da mãe, nascimento e seus componentes, documentos conforme política, telefone, e-mail, identificadores estáveis de origem, endereço/referência territorial e vínculos familiares quando governança e calibração demonstrarem utilidade. Atributos correlacionados não devem ter pesos somados como se fossem independentes sem validação do efeito conjunto.

## 6. Qualidade e normalização

O valor original nunca é alterado pela normalização de Linkage. Qualidade é metadado separado, com estados mínimos `VALIDA`, `SUSPEITA`, `SENTINELA_PROVAVEL`, `IMPOSSIVEL`, `AUSENTE` e `INCONSISTENTE`.

**A ausência, indisponibilidade ou má qualidade de qualquer campo — inclusive nome da mãe — nunca elimina a observação recebida.** Ela reduz ou neutraliza a evidência disponível conforme política versionada, mas não autoriza descarte do fato, preenchimento sintético ou invenção de valor. Valores ausentes/impossíveis/sentinelas são neutros no score salvo política calibrada específica. Contradições permanecem preservadas e não são corrigidas silenciosamente.

Esta regra arquitetural não altera, por si só, a obrigatoriedade dos contratos de entrada vigentes: eventual mudança de `nomeMae` de obrigatório para opcional é decisão funcional/normativa separada e deve ser tratada em change-set próprio.

Nome e nome da mãe usam normalização versionada. A normalização pode remover diacríticos, pontuação irrelevante, espaços redundantes e partículas nominais isoladas para comparação, preservando o original.

A data de nascimento é preservada integralmente e pode ser decomposta para Linkage em `NASC_DIA`, `NASC_MES` e `NASC_ANO`, cada componente com estados e parâmetros próprios. Componentes inválidos não podem transformar data ruim em evidência positiva.

## 7. Rotas determinísticas e origem

CPF válido, confiável e não conflitado é rota determinística prioritária. Regras determinísticas adicionais só podem operar quando explicitamente definidas, qualificadas e únicas no corpus/política vigente; não substituem a preservação dos campos originais.

`codigoPessoaOrigem` identifica e versiona o registro dentro do namespace Gestor + Sistema de Origem. Não é identidade municipal transversal. Quando existe código interno estável, ele é preferível para rastreabilidade da origem mesmo que atributos cadastrais sejam corrigidos.

## 8. Blocking e geração de candidatos

Blocking reduz o universo de candidatos; nunca decide identidade. O runtime usa regras dinâmicas imutáveis e versionadas produzidas pelo Calibrador e consumidas exatamente pelo Avaliador/Runner correspondente.

A projeção `identidade.blocking_chave` é derivada, reconstruível e indexada; não é fonte de verdade. Passes combinam campos conforme a álgebra versionada e podem usar componentes de nome, nome da mãe e nascimento, inclusive aliases históricos de nome quando aprovados. Data de nascimento corrigida não gera automaticamente alias histórico equivalente.

Nenhum bloco pode ser truncado silenciosamente. Limite excedido exige estratégia alternativa ou falha operacional explícita. Soundex não integra o score e somente poderia ser usado futuramente como chave adicional de blocking se experimento demonstrar ganho.

O otimizador seleciona regras por critérios objetivos e reproduzíveis, preservando evidência separada de recall de verdadeiros vínculos, retenção/redução de não-vínculos, cobertura e complexidade estrutural. Não se usa score composto oculto para mascarar trade-offs.

## 9. Calibrador, Avaliador e IBGE

O Calibrador estima parâmetros e regras a partir de corpus controlado e publica pacote/ruleset imutável versionado. O Avaliador consome exatamente essa versão; não mistura regras ou parâmetros de versões diferentes. Replay com mesmas entradas, snapshots e versão deve ser reproduzível.

Etapas independentes do Calibrador/Avaliador podem usar paralelismo quando medição demonstrar ganho sem alterar determinismo, semântica ou segurança.

Dados oficiais agregados do IBGE podem enriquecer atributos semanticamente compatíveis, especialmente estatísticas de nomes, inclusive recortes Brasil/UF/Município quando aplicáveis. IBGE é evidência estatística contextual, nunca verdade individual. A ausência de fonte IBGE compatível não exclui um atributo da calibração.

Antes de materializar novo snapshot IBGE, a rotina verifica validadores baratos disponíveis, preferencialmente ETag/Last-Modified/Content-Length, e mantém fingerprint/hash do conteúdo incorporado. Igualdade de tamanho é sinal de otimização, não prova criptográfica de identidade.

## 10. BI, segurança e governança

O BI deve distinguir identidade de origem de referência canônica e evitar dupla contagem de Pessoas. Indicadores de qualidade devem ser segmentáveis por Gestor, Sistema, tipo de origem e data de referência, sem expor identificadores pessoais em claro apenas para produzir métricas.

Dados sensíveis seguem minimização, finalidade e controles de acesso compatíveis com LGPD. A identidade técnica não amplia automaticamente o compartilhamento de dados. Correções cadastrais permanecem responsabilidade das áreas finalísticas; a Jornada preserva versões e evidências recebidas.

Não existe módulo obrigatório de Regularização Cadastral nem decisão humana caso a caso como requisito do fluxo normal.

## 11. Gates de ativação

CI, testes de banco, DDL e contratos são obrigatórios para integração técnica, mas não autorizam por si só ativação probabilística real. A ativação exige corpus representativo, rótulos independentes, recall, calibração, falsos vínculos, cobertura, subgrupos, variância, escala e aprovação institucional aplicável.

A arquitetura deve falhar fechada quando não puder provar completude, versão, autoridade ou consistência. Nenhum booleano, fingerprint, hash ou status isolado substitui essas provas.

## 12. UML

Os diagramas normativos correspondentes ficam em `Solution/docs/uml/` e `Solution/docs/diagrams/`. O padrão de documentação gráfica normativa é UML com fontes PlantUML versionadas no repositório.

O modelo estrutural deve ser representado por **diagrama de classes UML**, e não por DER/DRE como substituto do artefato UML. O fluxo de resolução de identidade deve possuir **diagrama de atividade UML**. O DER pode permanecer apenas como artefato físico auxiliar de banco de dados, sem ser classificado como UML.

Fontes normativas desta consolidação:

- `Solution/docs/uml/Jornada_Identidade_Linkage_Classes.puml` — diagrama de classes;
- `Solution/docs/uml/Jornada_Resolucao_Identidade_Atividade.puml` — diagrama de atividade;
- demais diagramas UML de componentes, implantação, estados e sequência já versionados no repositório.
