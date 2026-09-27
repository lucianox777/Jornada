# DT-15 — dossiê comparativo e página de decisão do modelo

**Decisão de produto/arquitetura (27/09/2026):** o objeto principal da decisão é a comparação **atual × proposto** nas mesmas condições de avaliação, não uma classificação histórica de modelos. O histórico agregado continua disponível como contexto. O **Monitor Operacional** e a **página restrita de governança para o operador master** são superfícies distintas, com permissões, responsabilidades e efeitos diferentes.

**Estado deste documento:** implementação **parcial de segurança e comparação de blocking na amostra de treino**. O wrapper Windows agora encerra em RASCUNHO depois da conferência, sem promoção automática; o Worker compara os passes D do ATIVO-base e RASCUNHO sobre as **mesmas observações rotuladas de treino** e persiste métricas agregadas em `identidade.estatistica_linkage`, com versão do ATIVO e identidade do ATIVO-base no campo `metodo`. **Página master, dossiê pareado completo de decisões FS, autorização corporativa e gates de aprovação humana ainda não implementados.** Isso não equivale a benchmark populacional, execução contrafactual FS nem autorização de promoção. Vinculado ao [Plano de desenvolvimento](Plano_Desenvolvimento.md), detalhado em [Dívidas técnicas](Dividas_Tecnicas.md) e distinto de DT-14, DT-09, DT-05 e Trilha 4.

## 1. Duas superfícies sem confusão de responsabilidade

| Superfície | Finalidade | Usuários e privilégio | Mutação de modelo |
|---|---|---|---|
| [Monitor Operacional](Monitor_Operacional.md), rota existente `/monitor` | Estado atual do sistema, saúde, alertas, modelo ATIVO, resumo do último RASCUNHO, condição da comparação e link para a página restrita quando autorizada | Operadores com `jornada.monitor.read` segundo o contrato já publicado; **sem** exposição de parâmetros/thresholds sensíveis nem detalhes de pares | **Nunca**; o monitor não valida, aprova, rejeita nem ativa |
| **Página de governança de modelos**, rota **proposta** `/governanca/modelos` | Revisão do dossiê atual × proposto, explicação das divergências, histórico compacto, registro da decisão humana e ações separadas `VALIDATE`/`ACTIVATE` | **Operador master autorizado** por identidade individual corporativa e permissão explícita de leitura/decisão de modelo; não reaproveitar indiscriminadamente `jornada.monitor.read` | Somente APIs específicas com autorização centralizada, auditoria, estado/fingerprint vinculado e gates em transação |

A página não é uma extensão de autenticação da rota `/monitor`. Aplicar `Jornada.Access.Security` (DT-04) às APIs específicas; a matriz de acesso deve distinguir visualizar comparação, registrar decisão, validar e ativar, sem pressupor que um gestor de Secretaria ou qualquer portador da credencial de monitor tenha papel master. Em HML/Produção, **deny-by-default** enquanto a autorização e identidade corporativa individual necessárias não estiverem homologadas (dependência institucional #378/#379). Não abrir endpoint administrativo com a chave genérica do monitor.

## 2. Hierarquia visual da página master

**Painel principal — decisão atual:**

- **À esquerda:** o modelo **ATIVO no momento da leitura**, versão e hash do modelo/ruleset, conjunto de passes habilitados e referência nominal, data de ativação, limitações conhecidas.
- **À direita:** o modelo **RASCUNHO selecionado**, versão e hashes, parâmetros de blocking e decisão **visíveis apenas ao perfil autorizado**, alterações em passes D/C/D∪C e evidência de calibração; status `AGUARDA_EVIDENCIA`, `COMPARAVEL` ou `NAO_COMPARAVEL`.
- **No centro:** *deltas medidos* no **mesmo corpus congelado** e com a mesma verdade rotulada: recall do blocking, redução de não-vínculos, quantidades e caudas P50/P95/P99/máximo de candidatos, falsos positivos/negativos **após o FS**, conflitos e abstenções, latência e custos disponíveis. Separar métricas de blocking de decisões do FS e de custos operacionais; não supor que cada métrica já exista ou que resultados sintéticos representem São Paulo. Exibir tamanho do estrato, intervalo/incerteza quando disponível, unidade, direção da mudança e o identificador da evidência.
- **Divergências decisórias:** agregado comparativo (por exemplo, RESOLVIDO→CONFLITO, RESOLVIDO→NÃO_RESOLVIDO, mudança de Pessoa candidata, CONFLITO→RESOLVIDO) produzido por *replay contrafactual* sobre os **mesmos pares elegíveis e mesmas versões de dados**, não por subtração de runs históricos. Detalhes por observação, quando autorizados e necessários para a investigação, permanecem em arquivo/serviço restrito fora do JSON público do Monitor.

O Calibrador **apresenta alternativas e evidências, não emite o veredito "melhor"**. A página informa ganhos/perdas e riscos para que o master escolha manter o ATIVO, pedir novo RASCUNHO ou autorizar a promoção após os gates. Evitar ordenar alternativas por pontuação composta opaca, sobretudo quando recall, segurança e custo divergem.

**Histórico resumido — área secundária/expansível:** versões recentes, estados, data, ator técnico/individual quando disponível, SHA do dossiê e motivo registrado, principal alteração de configuração e link restrito à evidência de cada transição. Ler `auditoria.modelo_linkage_estado_evento` e `identidade.linkage_run` apenas como proveniência histórica. **Não** calcular "ganho" subtraindo estatísticas de versões avaliadas em corpora ou universos distintos. No histórico anterior à DT-15, marcar `SEM_COMPARATIVO_PAREADO` quando não existir evidência.

## 3. Dossiê de decisão versionado: produção e comparabilidade

A saída proposta do Parameters Worker após `GENERATE_DRAFT` é um **dossiê de decisão**, com identidade própria e hash SHA-256 do conteúdo canônico. **Não** usar o nome *bundle de configuração*: `config/release/configuration-bundle.json` já é a identidade técnica da implantação e não contém justificativas de promoção de modelos.

1. Identificar e congelar o par **modelo ATIVO que serviu de base** e **modelo RASCUNHO** (IDs, versões, parâmetros/fingerprints, rulesets, versão da normalização e referência IBGE); registrar instante de corte e snapshot/corpus/verdade, método/seed/versão do avaliador e origem DEV/sintética/representativa.
2. Reexecutar **ambos**, sem publicar, nas **mesmas** observações e na mesma verdade rotulada. Quando D, C e D∪C forem candidatos, avaliar cada política como versão distinta sobre o universo comparável, inclusive casos com mãe/data ausentes; se a população elegível mudar, registrar *denominadores por braço* e reportar recall geral e condicional separadamente. Reestimar ou explicitar `u` condicionado ao universo de candidatos de cada política; não aplicar cegamente `u` do ATIVO a um novo blocking.
3. Calcular deltas com denominador, métrica, método e unidade idênticos. Faltas de evidência, fingerprints incompatíveis, diferença de corpus/snapshot, rótulos insuficientes, limites de candidatos, truncamento ou falha de execução resultam em `NAO_COMPARAVEL` ou `INCOMPLETO` explícito. Não interpolar resultados, preencher ausências com zero nem autorizar promoção usando valores exemplificativos.
4. Reexecutar **contrafactualmente** scorer e regras de decisão completos sobre o conjunto de candidatos de cada política, sem mutar identidade/Gold/ledger produtivo. Para cenários operacionais que exijam escrita durante a simulação, usar banco isolado DEV/HML e impedir publicação por contrato. Discriminar ganho de recall de candidatos de alteração de estado final do FS, guardas e ambiguidade.
5. Produzir um **resumo público-minimizado para o Monitor** (status, versões, hashes, data e disponibilidade; nunca parâmetros sensíveis, nomes, CPF, pares ou UUID de cidadão) e o **dossiê restrito master** com gráficos/tabelas, divergências agregadas, referência de evidência restrita, validade temporal e limitações.
6. Preservar a evidência vinculada por hash no ledger append-only de decisão, com evento do master individual, justificativa, estado anterior/novo, prazo de validade da evidência e referência ao documento congelado. Uma nova geração ou alteração de qualquer hash exige *nova revisão*, não herdando aprovação anterior.

**Critério de comparação:** principal = atual versus proposto no mesmo corpus/verdade; histórico resumido = contexto e rastreabilidade. Métricas anteriores podem aparecer **sem delta** quando não for possível equiparar as populações.

## 4. Ações e segurança da decisão do operador master

Ações planejadas na página restrita, **não** no monitor:

- **Manter modelo atual:** registrar justificativa, hash do dossiê examinado, versão ATIVA e RASCUNHO; o ATIVO permanece intocado e o RASCUNHO não é automaticamente apagado. O banco **não tem atualmente** estado canônico `REJEITADO` para `modelo_linkage`; representar a rejeição no ledger decisório separado até uma migração/versionamento específico.
- **Pedir nova calibração ou evidência:** não promover; criar tarefa/pendência operacional identificável, sem iniciar automaticamente workloads concorrentes nem alterar estatísticas congeladas.
- **Aprovar a comparação e iniciar `VALIDATE`:** exigir confirmação explícita e motivo, permissão de validação do master, vínculo ao dossiê exato e nova checagem das precondições em transação; preservar todos os gates DT-09 (budget FP, conferência governada do **mesmo modelo/fingerprint**, ruleset completo etc.).
- **Autorizar `ACTIVATE`:** ação **separada** para modelo VALIDADO, depois de nova conferência de que os hashes/dossiê e o ATIVO-base continuam aqueles aprovados; lock transacional, registro do ator e reversibilidade governada. Outro modelo ATIVO, mudança no corpus condicionante ou dossiê expirado exigem nova revisão. Não exibir botões funcionais antes da implementação de autorização, persistência e anti-TOCTOU.

Separar **aprovação humana** de **VALIDATE técnico**: mesmo que todos os gates numéricos sejam `CONFORME`, o Parameters Worker não deve presumir consentimento. Vincular autorização explícita ao hash em `VALIDATE` **e** `ACTIVATE`, inclusive quando o comando é chamado fora da UI; não aceitar a página como proteção apenas visual.

**Proteção parcial implementada, ainda sem aceite completo:** `install/windows-production/Invoke-JornadaLinkageCalibration.ps1` agora executa somente `GENERATE_DRAFT → CONFERENCIA`, verifica se o ATIVO-base não mudou e termina com RASCUNHO preparado. Não executa `VALIDATE`/`ACTIVATE`. **Os caminhos diretos do Parameters Worker e as automações antigas ainda não exigem um parecer humano persistido e verificável**: proteger ambos os gates no SQL/Worker é condição para a futura página master não ser apenas barreira visual. Não confundir a conferência técnica, o script iniciado manualmente ou o diagnóstico do treino com aprovação do operador master.

## 5. Fronteira com entregas existentes e aceite

- **DT-14:** determina *quando* repetir conferência independente scorer/comparadores/runtime. **DT-15:** mostra *o que mudou* entre modelos e registra decisão de promoção. Não reexecutar conferência decimal×float64 por causa de um novo dossiê se não houver gatilho; preservar o gate implementado por modelo.
- **DT-09:** guardas técnicos atualmente executados por `VALIDATE`/`ACTIVATE`; DT-15 **soma** autorização humana vinculada e evidência comparativa, sem relaxá-los.
- **Trilha 4 / DT-05:** reprocessamento por mudanças/ondas e replay auditável; a comparação contrafactual pode reutilizar seu universo capturado, mas não depende de concluir toda a migração NAS/GC para especificar ou implementar o dossiê em DEV.
- **PRs #566–#568:** diagnóstico paralelo D/C/D∪C sintético, ledger agregado e auditoria SQL real amostral DEV/HML são **insumos parciais**; não garantem, por si, reexecução pareada de **dois modelos** nem evidência estatística de população real.

**Aceite técnico da DT-15, em entregas separáveis:**
1. Contrato tipado e versionado do dossiê/estado comparável; worker gera comparação *replay* de ATIVO-base e RASCUNHO na mesma massa, com fingerprints e limites fail-closed, sem publicar. Testes de igualdade, denominadores, faltantes, mudanças de decisão e snapshots divergentes.
2. Persistência append-only de dossiê/parecer e API restrita sob DT-04; privilégios independentes de leitura, decisão, validação e ativação; testes 401/403, concorrência, autoria individual (ou indicação explícita de ausência de identidade corporativa), não-repúdio e ausência de PII em Monitor/logs.
3. Página master `/governanca/modelos` com comparação atual×proposto proeminente, histórico resumido secundário, estados `COMPARAVEL`/`NAO_COMPARAVEL` e botões somente quando autorizados. Monitor mantém apenas link e sinalização read-only, sem mutações.
4. Implementar aprovação humana verificável também nos caminhos CLI/Windows/automação de `VALIDATE` e `ACTIVATE`. E2E demonstra que o wrapper não contorna a decisão master, mudança do modelo ATIVO invalida dossiê, evidência divergente bloqueia promoção, rejeição mantém o modelo ATIVO e rollback é rastreável.
5. Evidência de desempenho e qualidade do Ensaio como insumo posterior: versões dos modelos, D/C/D∪C, corpus e observações, custo/latência de comparação e avaliação por estrato. **Nenhum threshold de superioridade inventado**, nenhum replay sintético apresentado como medição municipal representativa e nenhuma certificação estatística automática (#31).
