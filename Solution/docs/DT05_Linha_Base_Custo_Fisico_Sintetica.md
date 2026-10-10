# DT-05 — linha de base mensurável de custo físico e latência (sintética)

**Etapa técnica incremental.** A DT-05 global **não** é encerrada:
o replay histórico e a manutenção física têm provas anteriores,
enquanto custo/latência representativos e política institucional
de retenção NAS permanecem pendentes.

## O que esta mudança entrega

A suíte Unit executa o **código real**
`Dt05SnapshotMaintenance.ScanAsync()` sobre uma raiz temporária
**exclusiva** `jornada-dt05-synthetic-cost-<GUID>` no runner,
criada com um conjunto reproduzível de **64 partições
sintéticas content-addressed**, referenciadas por **8 manifestos**
(512 ocorrências lógicas). Também cria um órfão sintético
intencional, sem tocar nos objetos Bronze. O comando é
**somente leitura**, com `deleteOrphans: false`.

Os resultados são medidas, não números fixados no teste:
`logical_bytes`, `unique_bytes`, `physical_bytes`,
`deduplicated_bytes`, proporção de deduplicação,
`scanned_elapsed_ms`, total de objetos/manifestos/ocorrências
e exclusões observadas. As **relações esperadas** são travadas
por asserções reais. A latência em milissegundos **não tem
SLA imposto** neste ensaio: sofre variação do runner hospedado,
do filesystem temporário e do número de hashes.

Quando executada **somente no GitHub Actions do repositório
autorizado**, a suíte grava
`Solution/.local/test-evidence/unit/dt05-synthetic-cost.json`.
O workflow `unit` executa um validador estrito antes de fazer
upload do artefato já existente `unit-test-evidence`.
O validador exige todos os campos e seus tipos, números
não negativos e contagens, deduplicação coerente,
**zero exclusões** e marcadores explícitos:

- `scope=SYNTHETIC_EPHEMERAL_READ_ONLY`;
- `real_nas_measurement=false`;
- `real_cpf_or_ibge_data=false`;
- `performance_threshold_enforced=false`.

O validador ainda possui casos negativos (schema alterado,
contagem falsa, eliminação não autorizada, declaração
enganosa de NAS real e campo CPF arbitrário). Nenhum
endpoint, scheduler ou opção de GC é criado/modificado.

## O que esta mudança NÃO prova

**Não** prova custo real de hardware ou NAS, compactação
Parquet/ZSTD, custo anual, capturas de produção, retenção,
concorrência NAS, latência de **replay do Runner**, throughput
de 50–150 GB/ano, representatividade de dados IBGE,
qualidade estatística, aceitação institucional ou
reavaliação extraordinária de RESOLVIDOS.

Para encerrar a DT-05 global ainda são necessárias:
política de retenção/backup NAS e revisão de concorrência
sob aprovação institucional; medições representativas de
captura + replay histórico com corpus autorizado;
custos/latência sustentados e critérios de produção próprios.
A prova atual é útil como **baseline comparável no CI**
para regressões do scanner e da deduplicação física,
não substitui esses gates.

**Segurança:** não executar comandos de maintenance/GC
na base `JornadaLocal`, IBGE original, diretório de
dados do usuário ou mount Bronze real. A suíte de teste
não abre qualquer path de configuração da aplicação,
usa temp isolado criado por ela e faz cleanup somente
dessa pasta de fixture única.

Referências: [DT-05](DT05_Snapshots_Parquet_NAS.md),
[Estado atual](Estado_Atual_Projeto.md),
[proposta do sistema](Manual_Sistema_Consolidado_20261009.md).
