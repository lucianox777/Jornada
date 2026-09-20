# Arquitetura de Identidade e Linkage — especificação corrente

**Status:** normativa para a V1 ainda não publicada.  
**Princípio de versionamento:** como a Jornada ainda não foi publicada, este documento descreve diretamente a arquitetura vigente. Não há necessidade de manter ADRs como registro de decisões históricas internas que ainda podem ser consolidadas antes da primeira publicação.

Este documento incorpora as decisões anteriormente distribuídas entre documentos ADR de identidade progressiva, âncora CPF, cutover do Processor, composição reversível e Linkage multievidência/Fellegi–Sunter.

## 0. Hierarquia normativa e tecnologia relacional

A hierarquia de precedência da Jornada é única: **Especificação Técnica vigente → requisitos normativos e documentos de arquitetura corrente subordinados → implementação**. O código validado na `master` é realização e evidência de conformidade; não cria norma por si mesmo. Divergência entre implementação e norma deve ser tratada como defeito ou resultar em alteração formal prévia da documentação normativa aplicável.

**Microsoft SQL Server é a única tecnologia relacional operacional suportada pela Jornada candidata v5.00.** O DDL canônico, o contrato de prontidão e o baseline independente de ambiente permanecem definidos e testados em SQL Server 2022. PostgreSQL/Npgsql não integram o runtime, a persistência, a calibração ou os gates correntes; a implementação anterior permanece apenas no histórico Git para eventual projeto independente. **SQL Database in Microsoft Fabric não é alvo operacional nem gate desta candidata**; evidências anteriores são histórico de compatibilidade. **Lakehouse e SQL Analytics Endpoint permanecem no escopo analítico/compatibilidade** e não substituem o banco relacional operacional.

Durante a consolidação de engenharia v5.00, mudanças estruturais devem ser integradas com contrato, testes e documentação no mesmo change-set. O motor probabilístico permanece tecnicamente implementável/evolutivo, mas sua ativação automática real continua sujeita ao gate estatístico e institucional.

## 1. Identidade progressiva

A Jornada atribui um `initial_uuid` a cada **identidade persistente de origem** admitida, inclusive sem CPF. A chave autoritativa é `(base_pessoa_origem_id, codigo_pessoa_origem)`; sistemas autorizados podem compartilhar a mesma Base de Pessoa sem criar identidades paralelas. Uma observação que não possua `codigoPessoaOrigem` continua válida, mas não recebe origem sintética nem `initial_uuid` inventado. O UUID inicial é aleatório, não deriva de PII, nunca é reciclado, transferido ou alterado e não constitui prova de unicidade municipal.

O `initial_uuid` é **proveniência e continuidade**, não evidência de semelhança. Ele não participa de blocking, geração de candidatos, features, LLR, posterior, margem ou calibração. Seu único papel na decisão probabilística é poder tornar-se o próprio `canonical_uuid` em `NOVA_IDENTIDADE` quando um run íntegro e completo termina sem candidato para uma origem persistente; observação sem origem persistente não recebe UUID artificial para viabilizar esse caminho.

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

`identidade.linkage_resultado` preserva o **resultado bruto reproduzível** do scorer. A publicação operacional é uma camada separada: `ASSOCIACAO_EXISTENTE`, `NOVA_IDENTIDADE` ou `INDEFINIDA`, com política, universo, run e versão progressiva auditáveis. O vínculo corrente e Gold/Serving consomem a decisão publicada; a política não reescreve score, ranking ou motivo bruto para fabricar um match.

Cada feature habilitada possui identificação, origem, semântica, normalizador/comparador versionados, estados de qualidade, política de ausência, parâmetros `m/u`, proveniência e evidência de validação. O modelo publicado congela features, versões, parâmetros, regras de dependência e ruleset de blocking.

Evidências candidatas incluem nome, nome da mãe, nascimento e seus componentes, documentos conforme política, telefone, e-mail, identificadores estáveis de origem e vínculos familiares quando governança e calibração demonstrarem utilidade. Endereço e referência territorial são semanticamente conhecidos, mas permanecem **inelegíveis no modelo corrente**: valores institucionais ou compartilhados têm alta frequência e não podem produzir evidência positiva por simples concordância. Qualquer uso futuro exige versão de modelo própria, limite de bloco e ajuste explícito por frequência; `ENDERECO_CASA_ABRIGO_SIGILOSA` permanece fail-closed para resolução de identidade.

`nome_referencia` pertence ao serving, não ao conjunto de evidências. Nome civil, nome social e variantes históricas permanecem observações independentes para blocking/calibração conforme contrato homologado; trocar silenciosamente o nome técnico do candidato pela referência de apresentação é proibido. A evolução do scorer para comparar conjuntos de variantes deve ocorrer em nova versão calibrada do modelo, sem reutilizar a regra de serving como peso estatístico. Atributos correlacionados não devem ter pesos somados como se fossem independentes sem validação do efeito conjunto.

## 6. Qualidade e normalização

O valor original nunca é alterado pela normalização de Linkage. Qualidade é metadado separado, com estados mínimos `VALIDA`, `SUSPEITA`, `SENTINELA_PROVAVEL`, `IMPOSSIVEL`, `AUSENTE` e `INCONSISTENTE`.

**A ausência, indisponibilidade ou má qualidade de qualquer campo — inclusive nome da mãe — nunca elimina a observação recebida.** Ela reduz ou neutraliza a evidência disponível conforme política versionada, mas não autoriza descarte do fato, preenchimento sintético ou invenção de valor. Valores ausentes/impossíveis/sentinelas são neutros no score salvo política calibrada específica. Contradições permanecem preservadas e não são corrigidas silenciosamente.

Esta regra arquitetural não altera a obrigatoriedade declarada por cada contrato de entrada: nos schemas v4 correntes, `nomeCompleto` e `dataNascimento` continuam obrigatórios onde já o eram e `nomeMae` é opcional. O Processor não replica essas obrigatoriedades depois da validação do JSON Schema; contratos futuros podem admitir outras combinações sem transformar requisito de fonte em condição universal de existência da Pessoa.

Nome e nome da mãe usam normalização versionada. A normalização pode remover diacríticos, pontuação irrelevante, espaços redundantes e partículas nominais isoladas para comparação, preservando o original.

A data de nascimento é preservada integralmente e pode ser decomposta para Linkage em `NASC_DIA`, `NASC_MES` e `NASC_ANO`, cada componente com estados e parâmetros próprios. Componentes inválidos não podem transformar data ruim em evidência positiva.

## 7. Rotas determinísticas e origem

CPF válido, confiável e não conflitado é rota determinística prioritária. Regras determinísticas adicionais só podem operar quando explicitamente definidas, qualificadas e únicas no corpus/política vigente; não substituem a preservação dos campos originais.

`codigoPessoaOrigem`, quando informado, identifica e versiona a Pessoa dentro da **Base de Pessoa/namespace** declarada. Não é identidade municipal transversal e nunca é derivado de CPF. `idPessoaEntrega` é a chave obrigatória apenas dentro da remessa. A ausência de código local não invalida Pessoa nem fatos. Quando existe código interno estável, ele é preferível para continuidade da origem mesmo que atributos cadastrais sejam corrigidos.

## 8. Blocking e geração de candidatos

Blocking reduz o universo de candidatos; nunca decide identidade. O runtime usa regras dinâmicas imutáveis e versionadas produzidas pelo Calibrador e consumidas exatamente pelo Avaliador/Runner correspondente.

A projeção `identidade.blocking_chave` é derivada, reconstruível e indexada; não é fonte de verdade. Passes combinam campos conforme a álgebra versionada e podem usar componentes de nome, nome da mãe e nascimento, inclusive aliases históricos de nome quando aprovados. Data de nascimento corrigida não gera automaticamente alias histórico equivalente.

Nenhum bloco pode ser truncado silenciosamente. Limite excedido exige estratégia alternativa ou falha operacional explícita. Soundex não integra o score e somente poderia ser usado futuramente como chave adicional de blocking se experimento demonstrar ganho.

O otimizador seleciona regras por critérios objetivos e reproduzíveis, preservando evidência separada de recall de verdadeiros vínculos, retenção/redução de não-vínculos, cobertura e complexidade estrutural. Não se usa score composto oculto para mascarar trade-offs.

## 9. Calibrador, Avaliador e IBGE

O Calibrador estima parâmetros e regras a partir de corpus controlado e publica pacote/ruleset imutável versionado. O Avaliador consome exatamente essa versão; não mistura regras ou parâmetros de versões diferentes. Replay com mesmas entradas, snapshots e versão deve ser reproduzível.

O resolvedor estatístico operacional é um único **Fellegi–Sunter**. O estágio DF nominal anteriormente estudado não integra a arquitetura corrente. Term frequency pode ser reutilizada como evidência dentro do próprio FS somente após calibração, sem criar um segundo decisor ou contornar guards.

Para `u`, o alvo operacional é a distribuição entre **não-matches que sobrevivem ao ruleset de blocking efetivo**, e não a população incondicional. Como o scorer corrente recebe a união deduplicada dos passes, o modelo usa `u` condicionado a essa união. O Calibrador mede suporte por passe e só abandona o bootstrap nominal IBGE quando a união e todos os passes satisfazem critérios explícitos de suficiência; até lá o IBGE permanece fallback versionado.

Etapas independentes do Calibrador/Avaliador podem usar paralelismo quando medição demonstrar ganho sem alterar determinismo, semântica ou segurança.

Dados oficiais agregados do IBGE podem enriquecer atributos semanticamente compatíveis, especialmente estatísticas de nomes, inclusive recortes Brasil/UF/Município quando aplicáveis. IBGE é evidência estatística contextual, nunca verdade individual. A ausência de fonte IBGE compatível não exclui um atributo da calibração.

Antes de materializar novo snapshot IBGE, a rotina verifica validadores baratos disponíveis, preferencialmente ETag/Last-Modified/Content-Length, e mantém fingerprint/hash do conteúdo incorporado. Igualdade de tamanho é sinal de otimização, não prova criptográfica de identidade.

## 10. BI, QC, segurança e governança

O BI deve distinguir identidade de origem de referência canônica e evitar dupla contagem de Pessoas. QC e BI devem tornar **visíveis e segmentáveis** as classificações de qualidade de CPF e os problemas de identidade associados, incluindo no mínimo CPF ausente, estruturalmente inválido, conflito de CPF para a mesma origem, duplicação de códigos para a mesma âncora e tentativa de junção de CPFs distintos sob a mesma origem. A visibilidade operacional deve ocorrer por classes/motivos e contagens, sem exigir exposição do CPF em claro. Indicadores de qualidade devem ser segmentáveis por Gestor, Sistema, Base de Pessoa, Tipo de Registro e data de referência.

Dados sensíveis seguem minimização, finalidade e controles de acesso compatíveis com LGPD. A identidade técnica não amplia automaticamente o compartilhamento de dados. Correções cadastrais permanecem responsabilidade das áreas finalísticas; a Jornada preserva versões e evidências recebidas.

Conflito probabilístico efetivamente **publicado** é exceção governada ao fluxo automático: o Runner registra/atualiza uma divergência institucional aberta em `qualidade.divergencia_gestor`, ligada por FK ao `identidade.linkage_resultado` que originou o conflito. A fila continua única; modelo, run, candidatos, scores e margem permanecem na evidência imutável de Linkage e podem ser auditados pela superfície interna `qualidade.v_divergencia_linkage_contexto`. Conflitos apenas calculados em validação não são enviados à fila, e precedência determinística/governada não é duplicada como conflito probabilístico.

A fila não autoriza correção automática. O Gestor registra o desfecho e, quando houver mudança de identidade, usa o fluxo governado de casos/correção com ato e justificativa. Não existe módulo obrigatório de Regularização Cadastral nem decisão humana caso a caso como requisito do fluxo normal; revisão institucional é exigida somente nas exceções que permaneceram conflitantes após as guardas automáticas.

## 11. Gates de ativação

CI, testes de banco, DDL e contratos são obrigatórios para integração técnica, mas não autorizam por si só ativação probabilística real. A ativação exige corpus representativo, rótulos independentes, recall, calibração, falsos vínculos, cobertura, subgrupos, variância, escala e aprovação institucional aplicável.

A arquitetura deve falhar fechada quando não puder provar completude, versão, autoridade ou consistência. Nenhum booleano, fingerprint, hash ou status isolado substitui essas provas.

## 12. UML e formato documental

O padrão dos **diagramas** de arquitetura da Jornada é UML. O formato de entrega e leitura da documentação normativa, porém, deve permanecer em formatos institucionais comuns: **DOCX e PDF**, com os diagramas UML incorporados como figuras no próprio documento.

O modelo estrutural deve ser representado por **diagrama de classes UML**, e não por DER/DRE como substituto do artefato UML. O fluxo de resolução de identidade deve possuir **diagrama de atividade UML**. O DER pode permanecer apenas como visão física auxiliar de banco de dados, sem ser classificado como UML.

Nenhum leitor da documentação deve depender de PlantUML, Mermaid ou software específico de modelagem para compreender a arquitetura. Fontes técnicas eventualmente utilizadas durante a geração de diagramas não constituem artefatos normativos de entrega nem podem ser pré-requisito para leitura.

Na consolidação v5.00, os diagramas de classes de Identidade/Linkage e de atividade de resolução de identidade integram o **Anexo Modelo Físico v1.40 em DOCX/PDF**. Os demais documentos normativos devem seguir a mesma regra: UML quando a notação gráfica for aplicável, incorporada em Word/PDF.
