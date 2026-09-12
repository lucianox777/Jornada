# Documentação técnica complementar — estado corrente

- `API.md` - contrato funcional resumido da API vigente.
- `Bronze_Operacao.md` - runbook da Bronze externa: integridade, GC, locks por objeto, backup/restore e ZIP determinístico.
- `Runbook_Operacao.md` - ordem operacional, contrato do scheduler corporativo, watchdog observacional e gate de Power BI Desktop.
- `Runbook_Testes_Tecnicos.md` - massa sintética, escala, fault injection e ensaio backup/restore.
- `Release_Evidence.md` - conjunto agregado e hash-addressed das evidências de promoção.
- `Hardening_Estatico_v3.76.md` - compatibilidade retroativa, DDL destrutivo, dependências, cobertura, build determinístico, SAST e attestation da release.
- `Operabilidade_Contratos_v3.74.md` - autorização, minimização, compatibilidade, invariantes de upgrade, Bronze profundo/GC dry-run, observabilidade e validade das calibrações.
- `Possibilidades_Regras.md` - motor versionado, catálogo governado e dry-run de regras de Possibilidades.
- `../tests/fixtures/ingestao/` - payloads de referência usados como fixtures dos testes automatizados.

## Hierarquia normativa publicada

A última release de engenharia efetivamente selada declara **Base Normativa v3.64**, Solution Engenharia v4.05 e SolutionSchema 3.69 em `../../RELEASE_INFO.txt`. Essa declaração de release deve ser preservada como registro histórico e **não implica que exista um arquivo `Especificacao_Tecnica_Jornada_v3.64.*` publicado no repositório**.

A Especificação Técnica materializada e publicamente disponível nesta árvore é `../../Documentos/Especificacao_Tecnica_Jornada_v3.62.docx` e sua contraparte `.pdf`. Portanto, para conteúdo textual verificável da especificação, a referência publicada é **v3.62**. Os snapshots `../../Documentos/Estado_Engenharia_v4.04.md` e `v4.05.md` registram a evolução de engenharia declarada sob Base Normativa v3.64; eles não são substitutos de uma Especificação Técnica v3.64 inexistente e não autorizam reconstruí-la por inferência.

Até que uma Especificação Técnica v3.64 seja formalmente publicada, a leitura correta é: `RELEASE_INFO.txt` identifica a base normativa declarada da release v4.05; a Especificação Técnica v3.62 é o último documento normativo materializado; estados/notas de engenharia registram deltas e contexto técnico, sem elevar-se automaticamente a nova especificação normativa. Divergência entre esses níveis deve permanecer explícita, nunca ser resolvida por arquivo fictício ou renomeação.

A Referência Territorial permanece a fonte territorial única da visualização; `ENDERECO_RESIDENCIAL` permanece cadastral e não existe persistência geográfica paralela. Pagamento e Recebimento permanecem apenas conceituais na Fase 1.

## Material histórico

Exemplos de integração de versões anteriores não fazem parte da arquitetura vigente nem são distribuídos como pasta de exemplos. Payloads executáveis e de referência ficam exclusivamente em `../tests/fixtures/ingestao/`, onde são consumidos pelos testes automatizados.

## Anexos independentes

- Arquitetura Fase 1 v1.39 - fronteiras, compartilhamento municipal por padrão, restrições de projeção e territorialização origin-only.
- Modelo Físico v1.40 - modelo físico corrente do candidato técnico, derivado do SolutionSchema 3.70. O DER v1.39 é histórico.
- SLA/Atraso de Recebimento v1.22 - prazo e marco por Tipo/versão.
- Estrutura de Artefatos v1.43 - organização e regra do ZIP único.
- Segurança, Auditoria e Controle de Acesso v1.34 - Pessoa compartilhada em âmbito municipal, exceções negativas de projeção, anti-enumeração e auditoria individual/lote.
- Telas/BI v1.48 - dashboards; frontends operacionais pertencem aos sistemas finalísticos.
- Fluxos de Sequência Fato/Identidade v1.12 - fluxos operacionais e de governança entre fato, identidade e correção.
- Pendências de Desenvolvimento v1.47 - snapshot histórico; o backlog corrente está nas issues abertas.

## Estado de implementação

A API de referência possui serviços SQL, rate limiting, inspeção segura de ZIP, Processor de referência, QC e completude derivada. A implementação consolidada autoriza por credencial, scope e recurso, mantém a Pessoa compartilhada em âmbito municipal sem vínculo prévio, aplica exceções negativas de projeção e registra auditoria restrita do cidadão consultado sem exigir finalidade declarada.

Em Development, chaves sintéticas pré-geradas são carregadas somente pelo `DevelopmentAccessContextResolver`; seus `credentialId` coincidem com o seed de `controle.credencial_api`. Em HML/Produção, `CorporateIdentityPendingAccessContextResolver` + `DenyByDefaultPolicyEngine` mantêm a API fechada até a integração corporativa.

O workflow CI possui gates unitários e de integração SQL. Execução local dos testes de integração continua usando `JORNADA_TEST_SQL_CONNECTION` apontando para banco Test/Dev.

Bronze: os bytes do ZIP ficam fora do SQL Server, sob chave `sha256/ab/cd/<sha256>.zip`. O provider inicial `FileSystem` usa `BronzeStorage:RootPath`; em HML/Produção a raiz deve ser absoluta, compartilhada e durável. A chave relativa é persistida no SQL e o objeto não é movido após processamento.

### Endereço de casa-abrigo-sigilosa

`ENDERECO_CASA_ABRIGO_SIGILOSA` é um atributo reservado para o endereço de uma **casa-abrigo-sigilosa**. Ele não é sinônimo de `ENDERECO_RESIDENCIAL`, não cria uma categoria de “Pessoa protegida” e não é inferido pela Jornada. O atributo só é aceito em Entrega de `SERVICO` cujo `ref.tipo_registro_versao.origina_endereco_casa_abrigo_sigilosa=1`; portanto, o dado tem de vir do próprio serviço de casa-abrigo cadastrado para essa finalidade.

Por regra estrutural, esse atributo não é projetado pela API a Gestor diferente do Gestor responsável, independentemente da ausência de restrições manuais em `controle.restricao_projecao_jornada_versao`. Os demais dados da Pessoa seguem as regras normais de compartilhamento. O endereço sigiloso também não alimenta `REFERENCIA_TERRITORIAL`.

## Engenharia local, release e harnesses

- `Runbook_Desenvolvimento_Local.md`: SQL Server 2022 Developer em Docker, bootstrap, reset e testes.
- `Fabric_SQL_Compatibility.md`: harness para provar compatibilidade da mesma Solution/DDL/Integration em SQL Database in Microsoft Fabric sem criar Adapter específico antes da evidência.
- `Runbook_Git_Release.md`: Git como fonte oficial, proteção de segredos, tag e `RELEASE_INFO.txt`.
- `Runbook_Operacao.md`: scheduler corporativo/HML; não é substituído pelo Compose local.
- `Governanca_Tecnica_Readiness.md`: schemas, retenção/DR, ciclo de vida de identidade, scheduler e preflight de ambiente.
- `HML_Evidencias_SQL_API.md`: coleta/gates de Query Store/waits/deadlocks e consulta em lote 1/10/100/1000 sem PII.
