# Documentação técnica complementar — base normativa v3.62 / engenharia v3.76

- `API.md` - contrato funcional resumido da API vigente.
- `Bronze_Operacao.md` - runbook da Bronze externa: integridade, GC, locks por objeto, backup/restore e ZIP determinístico.
- `Runbook_Operacao.md` - ordem operacional, contrato do scheduler corporativo, watchdog observacional e gate de Power BI Desktop.
- `Runbook_Testes_Tecnicos.md` - massa sintética, escala, fault injection e ensaio backup/restore.
- `Release_Evidence.md` - conjunto agregado e hash-addressed das evidências de promoção.
- `Hardening_Estatico_v3.76.md` - compatibilidade retroativa, DDL destrutivo, dependências, cobertura, build determinístico, SAST e attestation da release.
- `Operabilidade_Contratos_v3.74.md` - autorização, minimização, compatibilidade, invariantes de upgrade, Bronze profundo/GC dry-run, observabilidade e validade das calibrações.
- `Possibilidades_Regras.md` - motor versionado, catálogo governado e dry-run de regras de Possibilidades.
- `../tests/fixtures/ingestao/` - payloads de referência usados como fixtures dos testes automatizados.

A fonte normativa é `../../Documentos/Especificacao_Tecnica_Jornada_v3.62.docx`. A base normativa v3.62 preserva a Referência Territorial como fonte territorial única da visualização; ENDERECO_RESIDENCIAL permanece cadastral e não existe persistência geográfica paralela.

## Material histórico

Exemplos de integração de versões anteriores não fazem parte da arquitetura vigente nem são distribuídos como pasta de exemplos. Payloads executáveis e de referência ficam exclusivamente em `../tests/fixtures/ingestao/`, onde são consumidos pelos testes automatizados.

## Anexos independentes

- Arquitetura Fase 1 v1.39 - fronteiras, compartilhamento municipal por padrão, restrições de projeção e territorialização origin-only.
- Modelo Físico/DER v1.39 - modelo físico vigente; inclui `controle.restricao_projecao_jornada_versao` e snapshot de REFERENCIA_TERRITORIAL com linhagem até os fatos.
- SLA/Atraso de Recebimento v1.22 - prazo e marco por Tipo/versão.
- Estrutura de Artefatos v1.43 - organização e regra do ZIP único.
- Segurança, Auditoria e Controle de Acesso v1.34 - Pessoa compartilhada em âmbito municipal, exceções negativas de projeção, anti-enumeração e auditoria individual/lote.
- Telas/BI v1.48 - dashboards; frontends operacionais pertencem aos sistemas finalísticos.
- Fluxos de Sequência Fato/Identidade v1.12 - fluxos operacionais e de governança entre fato, identidade e correção.
- Pendências de Desenvolvimento v1.47 - somente itens ainda externos ou dependentes de ambiente/regra institucional.

## Estado de implementação v3.55

A API de referência deixou de ser apenas scaffold: há serviços SQL, rate limiting, inspeção segura de ZIP, Processor de referência, QC e completude derivada. A implementação consolidada autoriza por credencial, scope e recurso, mantém a Pessoa compartilhada em âmbito municipal sem vínculo prévio, aplica exceções negativas de projeção e registra auditoria restrita do cidadão consultado sem exigir finalidade declarada.

Em Development, chaves sintéticas pré-geradas são carregadas somente pelo `DevelopmentAccessContextResolver`; seus `credentialId` coincidem com o seed de `controle.credencial_api`. Em HML/Produção, `CorporateIdentityPendingAccessContextResolver` + `DenyByDefaultPolicyEngine` mantêm a API fechada até a integração corporativa.

O workflow CI possui dois gates: unitário (build com warnings como erro, testes não-Integration e auditoria de vulnerabilidades NuGet) e `integration-sql`, que sobe SQL Server 2022, cria banco de Teste e executa os testes `[Category("Integration")]` contra DDL/seed reais. Execução local dos testes de integração continua usando `JORNADA_TEST_SQL_CONNECTION` apontando para banco Test/Dev.


Bronze v3.39: os bytes do ZIP ficam fora do SQL Server, sob chave `sha256/ab/cd/<sha256>.zip`. O provider inicial `FileSystem` usa `BronzeStorage:RootPath`; em HML/Produção a raiz deve ser absoluta, compartilhada e durável. A chave relativa é persistida no SQL e o objeto não é movido após processamento.

## Complementos v3.39

- `Territorializacao_Fase1.md`: obrigação da origem/Gestor e carga inicial.
- `Retencao_Item_Processado.md`: consolidação/retensão da trilha granular.
- `HML_Observabilidade_SQL.md`: medições antes de qualquer mudança de isolamento ou política de índices.

A Fase 1 não usa enriquecimento geográfico online no Processor.


## Compartilhamento v3.55

A Pessoa é compartilhada em âmbito municipal por qualquer credencial autorizada; credenciais de Tipo continuam limitadas ao próprio recurso, não a uma população histórica. Exceções de projeção são negativas e versionadas; sem exceção ativa, vale a projeção municipal padrão. Consulte `API.md`, o DDL de `controle.restricao_projecao_jornada_versao` e os diagramas `Controle_Acesso_Auditoria_v1.9.*`, `Arquitetura_Fase1_v3.54.png` e `Modelo_Fisico_Fase1_v3.54.png`.


## Auditoria de agente v3.55

`X-Jornada-Agente-CPF` é opcional e declarativo. A Jornada apenas valida o CPF e persiste HMAC-SHA-256; autenticação do agente e veracidade da associação usuário↔CPF pertencem ao Gestor finalístico. `serving.v_bi_api` não expõe identificadores individuais.

- `HML_Parametros.md`: matriz única dos parâmetros que exigem calibração/decisão em HML, com default técnico, critério, responsável e status.

## Integridade e governança consolidada (desde v3.46)

- conflito de CPF passa a bloquear o próprio identificador (`identity_map.estado=EM_CONFLITO`);
- correção/fusão/separação de UUID ganha rito auditável e API GESTOR;
- schemas ativos carregam SHA-256 aprovado e o runtime falha fechado se os bytes da mesma versão mudarem;
- modelo Power BI padrão deixa de importar `pessoa_uuid`, usando contagens distintas pré-agregadas;
- disclaimers normativos de Possibilidades e Monetário entram no PBIR.

- `diagrams/sequence/*.puml`: fontes UML dos fluxos fato/identidade, com SVG/PNG renderizados e anexo oficial correspondente.
- `../src/Jornada.Bronze.Verify`: verificador executável de integridade das referências Bronze após restore.


## Engenharia local, release e harnesses v3.55

- `Runbook_Desenvolvimento_Local.md`: SQL Server 2022 Developer em Docker, bootstrap, reset e testes.
- `Runbook_Git_Release.md`: Git como fonte oficial, proteção de segredos, tag e `RELEASE_INFO.txt`.
- `Runbook_Operacao.md`: scheduler corporativo/HML; não é substituído pelo Compose local.


## Harness v3.55

O CI ganhou `harness-smoke` com corpus sintético, geração/validação/ativação de modelo e Runner `MODEL_VALIDATION`. Localmente, `local-scale`, `local-fault-injection` e `local-backup-restore-drill` produzem evidências em `.local/`.


### Endereço de casa-abrigo-sigilosa

`ENDERECO_CASA_ABRIGO_SIGILOSA` é um atributo reservado para o endereço de uma **casa-abrigo-sigilosa**. Ele não é sinônimo de `ENDERECO_RESIDENCIAL`, não cria uma categoria de “Pessoa protegida” e não é inferido pela Jornada. O atributo só é aceito em Entrega de `SERVICO` cujo `ref.tipo_registro_versao.origina_endereco_casa_abrigo_sigilosa=1`; portanto, o dado tem de vir do próprio serviço de casa-abrigo cadastrado para essa finalidade.

Por regra estrutural, esse atributo não é projetado pela API a Gestor diferente do Gestor responsável, independentemente da ausência de restrições manuais em `controle.restricao_projecao_jornada_versao`. Os demais dados da Pessoa seguem as regras normais de compartilhamento. O endereço sigiloso também não alimenta `REFERENCIA_TERRITORIAL`.


A engenharia v3.65 não altera a base normativa v3.60; fecha atomicidade das rotinas multi-tabela identificadas na auditoria e adiciona conformidade SQL/C# verificável para TELEFONE_BR_CANONICO_V2.


## Engenharia v3.67

A v3.67 adiciona prova semântica SQL para as procedures críticas de identidade e remove a exceção de `sp_recompor_gold_pessoa` do gate multi-write. A base documental incluída nesta reconstrução permanece v3.60.


## Engenharia v3.68 / Base normativa v3.62

A v3.68 converge a linha de fechamento v3.66 e a correção robusta de recomposição da v3.67. A documentação normativa publicada nesta distribuição passa a v3.62, com EMAIL_CANONICO_V2 e a correção do contrato corrente de telefone. Modelo Físico v1.39 explicita as 53/53 tabelas; Estrutura de Artefatos v1.43, Pendências v1.47 e Fluxos v1.12 acompanham a convergência.

A ausência do ZIP binário exato v3.66 permanece registrada em `RELEASE_INFO.txt`; nenhum SHA foi inferido. O gate de integridade de predecessor verifica artefatos materializados e pode bloquear promoção em modo estrito.

- `Governanca_Tecnica_Readiness.md` - schemas, retenção/DR, ciclo de vida de identidade, scheduler e preflight de ambiente.
- `HML_Evidencias_SQL_API.md` - coleta/gates de Query Store/waits/deadlocks e consulta em lote 1/10/100/1000 sem PII.
