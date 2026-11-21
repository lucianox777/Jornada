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
