# Avaliação de volumetria da ingestão — issue #1348 (21/11/2026)

## Contexto e achado crítico
O PO pediu avaliar se os limites por tamanho de ZIP e por quantidade de pessoas devem coexistir, se 10 mil pessoas por pacote é adequado e se a #1348 deve abranger a volumetria do sistema. **Nenhum novo teto numérico foi aprovado.**

**Divergência a reconciliar antes de decidir:** o PO recorda determinação de 10 MB por pacote, porém o código atual `Solution/src/Jornada.Ingestion/IngestionPackageInspector.cs` fixa `MaxCompressedBytes = 250L * 1024 * 1024` e o `Solution/docs/API.md` declara teto contratual **250 MiB**. `Solution/src/Jornada.Api/Program.cs` aplica esse teto à borda HTTP. Não substituir silenciosamente 250 MiB por 10 MB; localizar decisão normativa/precedência e avaliar migração, proxy, compatibilidade e consumidores antes de propor mudança.

## Evidência atual
- `Solution/scripts/v9-http-volume-10k.sh` processa **10 mil pessoas totais em 10 remessas de mil**, não 10 mil por ZIP; exige opt-in e sandbox descartável, SHA e checks de idempotência/cardinalidade.
- A #1348 descreve rampas de **1k, 10k e 100k pessoas totais via HTTP**. Isso é **volumetria de ensaio**, não limite contratual por arquivo.
- `IngestionPackageInspector` aplica teto de ZIP compactado de 250 MiB e outro teto de expansão descompactada de 2 GiB. Não foi demonstrado teto geral de 10 mil registros por pacote.
- A #1349 permanece ensaio técnico 100k do motor V9, distinto de volumetria HTTP→Serving.

## Avaliação técnica
Limites de **bytes** e **registros** são controles complementares, não substitutos. Bytes protegem transporte, armazenamento, decompression bomb e memória; registros protegem cardinalidade, tempo de processamento, tamanho das transações, locks e custo do linkage. Um pacote pode ter poucos registros grandes ou muitos registros pequenos. A capacidade municipal deve ser medida por **campanha** (milhões de pessoas e milhares de remessas), com retomada, idempotência, backpressure, concorrência e observabilidade; não pressupor que todos os cidadãos serão importados numa só janela.

Exemplo aritmético: 12 milhões / 10 mil = **1.200 pacotes**, não 12 mil. Se cada remessa contiver mil pessoas, são **12.000 pacotes**. Para 11 milhões, 1.100 ou 11.000, respectivamente. Não inferir tamanhos reais de ZIP sem amostras de payload.

## Proposta para deliberação, não decisão de teto
1. Preservar o limite atual enquanto se investiga a alegada norma de 10 MB e sua precedência; não alterar contrato nesta PR.
2. Generalizar o **plano de avaliação** da #1348 para matriz de volumetria da ingestão: tamanho comprimido, tamanho expandido, pessoas/registros por pacote, número de remessas, concorrência, duração, recuperação, idempotência e capacidade ponta a ponta.
3. Ensaiar combinações representativas (1k, 10k, 25k, 50k registros por pacote e ZIPs próximos dos limites contratuais) como **hipóteses de benchmark**, não limites autorizados; ajustar faixas conforme payload real sintético, orçamento e segurança. Medir p50/p95/p99, throughput, CPU/RAM/SQL, rejeições, latência, reconciliação Bronze→Serving, backpressure e retomada.
4. Definir teto de registros e de bytes somente depois das medições e da análise normativa; testar explicitamente ambos os limites e respostas HTTP.
5. Não converter #1348 em certificação científica V9 nem duplicar #1349. Testes pesados exigem autorização, SHA e ambiente isolado.

## Estado
**Avaliação preliminar documentada; decisão de limites PENDENTE.** A generalização de escopo da #1348 é proposta para aprovação do PO, não alteração unilateral de critérios. PRs fechadas não serão reabertas. Relacionadas #1348, #1349, #1488, #1584 e PR documental #1836.

## Auditoria de configurabilidade — 21/11/2026
**Solicitação do PO:** verificar se controles volumétricos são configuráveis e avaliar atribuição da entrega à #1348. A decisão sobre novos tetos numéricos segue pendente.

| Controle | Código atual | Situação |
|---|---|---|
| ZIP compactado | `IngestionPackageInspector.MaxCompressedBytes = 250L * 1024 * 1024`; `Program.cs` aplica o mesmo valor em `IHttpMaxRequestBodySizeFeature` | **Fixo em compilação; não configurável por ambiente** |
| ZIP descompactado | `IngestionPackageInspector.MaxUncompressedBytes = 2L * 1024 * 1024 * 1024` | **Fixo em compilação** |
| Manifest | `MaxManifestBytes = 64L * 1024` | Fixo; limite de segurança distinto |
| Requisições por janela | `ApiRateLimiting` lê seção `ApiRateLimiting` de configuração; `Program.cs` injeta políticas | **Configurável** (conforme contrato e implantação) |
| Registros por ZIP | Nenhum limite global demonstrado na inspeção; o ensaio 10k divide 10.000 pessoas em dez ZIPs de mil | **Não comprovado como controle configurável** |
| Concorrência e filas | Há rate limiting de borda, mas não foi auditado o controle de workers, filas e backpressure por campanha | **Auditoria pendente** |

**Outra PR de volumetria:** busca encontrou PRs históricas #154, #295, #369, #664 (respectivamente observabilidade de SCALE, projeção de blocking, evidência HML e DT-10), com escopos distintos. Não foi identificada, por essa busca textual, PR aberta que entregue a configurabilidade do limite de ingestão. Não reabrir PRs fechadas.

**Avaliação recomendada para a #1348 (sujeita a aprovação):** adicionar à issue uma entrega funcional de política de ingestão configurável por ambiente/gestor, com limites de bytes compactados, bytes expandidos, quantidade de pessoas e registros, concorrência e controle de recursos. Manter limites máximos de segurança não eleváveis sem decisão específica; usar validação de configuração fail-closed, consistência entre Kestrel/proxy/inspector/processor, resposta clara a violações, métricas e testes de fronteira. Diferenciar teto normativo de teto operacional menor. Não implementar nem alterar os 250 MiB nesta PR documental.

**Riscos:** alteração unilateral pode quebrar consumidores existentes, permitir bypass entre API e Processor ou enfraquecer proteção contra ZIP bomb. A escolha de parâmetros deve passar por testes e precedência documental. A PR #1836 é apenas de organização; a implementação deve ser PR de código própria vinculada à issue, sem reabrir PR encerrada.

## Diretriz PO: defaults DEV e calibração por ambiente — 21/11/2026
O PO orientou **preservar os números técnicos atualmente praticados no DEV como defaults**, colocá-los em arquivos de configuração existentes e **não transportar automaticamente os mesmos valores para HML/PROD**. A capacidade de cada ambiente é diferente; HML e PROD precisam de avaliação e aprovação próprias. A política de valores máximos definitivos e eventual teto de 10 MB ainda não foi deliberada.

**Correção factual de auditoria anterior:** os tetos por quantidade **já são configuráveis** em `Solution/src/Jornada.Processor.Worker/appsettings.json`: `Processor.MaxPessoasPorEntrega=10000` e `Processor.MaxRegistrosPorEntrega=10000`, ambos previstos em `ProcessorOptions`. `Solution/config/hml/parameters.json` e `Solution/docs/HML_Parametros.md` já os registram como `CALIBRAR` para HML, sem valores aprovados. A avaliação anterior de 'nenhum limite global demonstrado' não descrevia corretamente o Processor; a verificação de aplicação exata e testes de borda permanece necessária.

**Permanecem constantes no código** os tetos de 250 MiB ZIP compactado, 2 GiB expandido e 64 KiB manifest em `IngestionPackageInspector`. A API usa o teto de 250 MiB em `Program.cs`. Proposta de implementação: tornar configuráveis **somente os controles que ainda estão fixos**, reutilizando o padrão de appsettings/overrides por ambiente, com defaults iguais aos atuais, validação fail-closed e consistência API/Processor. Nenhuma alteração imediata de valores ou de política de segurança.

**Critério de evidência:** DEV mantém baseline de regressão, não certificação de capacidade de HML/PROD; calibrar separadamente cada ambiente com testes representativos de payload, concorrência, tempo, memória, SQL e recuperação. Não presumir que a configuração DEV é ótima apenas porque funciona. Manter HML `CALIBRAR` e PROD sem valores inventados.

## Reavaliação: monitoramento contínuo de recursos por ambiente — 21/11/2026
O PO questionou a utilidade de uma calibração pontual de infraestrutura DEV e sugeriu substituir esse trabalho por monitoramento contínuo governado, com parâmetros por ambiente. **Avaliação, não aprovação de nova DT nem encerramento de gate HML.**

**Itens existentes e precedência:** #407 (aberta) já prevê OpenTelemetry acionável e runbook ao ingressar em HML; #379 rege ferramenta corporativa, RPO/RTO e política operacional; #93 (aberta) exige evidência representativa de capacidade HML para aceite institucional; #1348 (aberta) mede cadeia HTTP→Serving com rampas e controles; #1349 (aberta) mede escala do motor V9. Não criar nova DT/issue sem reconciliar esses escopos. PRs fechadas não reabrir.

**Proposta de decomposição:**
- **#1348, entrega DEV/CI:** garantir controles configuráveis, testes de limites, instrumentação mínima e regressão funcional de carga sintética; não dimensionar HML/PROD a partir de DEV.
- **#407 + #379, entrega HML/PROD e governança contínua:** monitor de recursos por ambiente com coleta de CPU, RSS/memória, SQL waits/deadlocks, IO, filas, throughput, latências p50/p95/p99, falhas, retries, limites e saturação; dashboards, alertas, responsáveis, revisão periódica, trilha de mudanças e procedimento de reação. Escolha de backend corporativo permanece #379.
- **#93, aceite HML:** preservar um teste representativo inicial e de mudança material de capacidade, além do monitoramento contínuo. Monitoramento de baixa carga não substitui comprovação da carga-alvo e recuperação. Não transformar snapshot DEV em baseline HML.
- **#1349:** ensaio técnico 100k V9 após pré-requisitos; não é substituído por métricas de infraestrutura genéricas.

**Governança proposta:** perfis de configuração versionados por ambiente; defaults DEV atuais, HML/PROD calibrados por observação real; limites de segurança e validação fail-closed; alarmes por tendências e faixas aprovadas, sem inventar SLA. Critérios de conclusão para monitor: telemetria real, métricas e alertas testados, runbook e responsável operacional; o monitor continua executando após o fechamento da issue de implantação.

**Estado:** proposta documentada para decisão do PO; nenhuma issue criada/fechada ou critério de aceite alterado nesta revisão.

## Auditoria de observabilidade interna e redução de escopo — 21/11/2026
O PO orientou restringir eventual entrega de código à substituição de limites hardcoded por configuração existente e, somente se houver lacuna comprovada, ao registro de eventos internos indispensáveis; monitoramento HTTP e infraestrutura externa não integra escopo do produto.

**Evidência estática:** `ApiAuditMiddleware` já cria/propaga `X-Correlation-Id`, mede tempo/status/rota com `JornadaTelemetry.RecordApiRequest`, persiste auditoria e registra falha; `ProcessorWorker` registra início, falhas e recuperação de leases; `LinkageRunnerWorker` registra execução/cancelamento/erro e duração via `JornadaTelemetry.RecordLinkageRun`; `V9CalibrationRuntimeScheduler` possui coleta de amostras e recibos de agendamento/recurso de calibração. Portanto **não abrir tarefa genérica de logs** nem criar monitor próprio. Auditoria não prova completude de todos os eventos internos nem execução real; lacunas devem ser demonstradas em testes antes de codificar.

**Escopo proposto para #1348:** (a) manter limites 10k pessoas/10k registros já configuráveis; (b) parametrizar 250 MiB comprimido, 2 GiB expandido e, se justificável, 64 KiB manifest, preservando defaults; (c) testes positivos/negativos e consistência entre API e Processor; (d) registrar somente lacunas comprovadas de eventos internos de falha/retry/reconciliação. Excluir novo dashboard, logs HTTP redundantes, coleta CPU/RAM/disco, estudo de dimensionamento DEV, calibração HML/PROD. A campanha HTTP 100k original da #1348 deve ser **reavaliada explicitamente pelo PO** antes de alterar seus critérios: não foi automaticamente cancelada. #1349 permanece prova de motor V9 independente.

**Estado:** diretriz PO registrada, implementação e reclassificação da issue pendentes; PR #1836 exclusivamente documental. Nenhuma alteração de runtime.
