# V9: avaliação local CPU/GPU — 19/10/2026

Decisão: manter CPU neste ensaio. A GPU está acessível, mas nenhum speedup foi
medido. Não há evidência para substituir o cálculo exato atual por um backend GPU.
Nenhuma dependência foi instalada; não houve SQL, TEST, publicação ou ativação.

Inventário reproduzível: RTX 3060 Laptop, 6.144 MiB, driver 610.62, compute capability
8.6. `cuInit` e enumeração de uma GPU retornaram status zero. O SDK Windows contém
`dxc` x64 nas versões 10.0.22621.0 e 10.0.26100.0; as DLLs D3D12, DirectML e CUDA
estão presentes. Não foram encontrados `nvcc` no PATH nem toolkit CUDA no diretório
padrão. Metadados do Python 3.13.5 e da `.venv` existente mostram NumPy, mas não
PyTorch, CuPy, Numba, PyCUDA, cuda-python ou ONNX Runtime GPU/DirectML. Presença do
driver/compilador HLSL não demonstra execução de um kernel V9 nem ganho de tempo.

O benchmark executa as implementações C# reais do commit `24ddbfeb`, em Release e
.NET 10.0.13 x64, num i7-11800H com 8 cores/16 threads e cerca de 16 GiB de RAM.
Há um aquecimento por escala e três repetições medidas. Os hashes dos resultados
coincidiram em todas as repetições. Os tempos incluem o retorno completo da tabela
Pareto ou a descoberta completa das propostas, não a geração dos inputs artificiais.

| Carga CPU | Escala | Mediana |
|---|---:|---:|
| Pareto exato e tabela, fronteira integral | 500 configurações | 3,05 ms |
| Pareto exato e tabela, fronteira integral | 2.000 configurações | 39,28 ms |
| Pareto exato e tabela, fronteira integral | 8.000 configurações | 231,58 ms |
| Descoberta TRAIN | 300 observações / 900 não-vínculos | 211,11 ms |
| Descoberta TRAIN | 1.500 observações / 4.500 não-vínculos | 333,68 ms |
| Descoberta TRAIN | 3.000 observações / 9.000 não-vínculos | 888,76 ms |
| Descoberta TRAIN | 30.000 observações / 90.000 não-vínculos | 19.693,50 ms |

O caso Pareto força todos os pontos a permanecerem na fronteira: 63.992.000
comparações de dominância para 8.000 configurações. Os flags artificiais de
atestação apenas permitem exercitar o algoritmo; não são evidência física ou
estatística. Cada descoberta usa prefixos K=1..4, 378 primitivos avaliados, pool
explícito de 16 e todas as 136 uniões de até dois passes. Os 30.000 positivos do
maior caso são enumerados integralmente antes do blocking. Esse caso alocou em
média 8.804 MiB cumulativos por execução, com até 399 MiB de working set observado
após a execução. Alocações cumulativas não são memória simultaneamente residente.

Interpretação: a tabela Pareto já custa menos de um quarto de segundo nesta carga;
a descoberta contém normalização de strings, conjuntos, dicionários, amostragem e
alocações gerenciadas. Otimizar alocações/varreduras CPU preservando decisões é um
candidato verificável antes de implementar offload. Isto é uma inferência dos
workloads, não uma medição de superioridade da CPU sobre GPU. O custo SQL e o
Rank/Resolve final não foram medidos aqui. Há outros trabalhos simultâneos na
máquina; estas três amostras não constituem SLA nem P95.

Um próximo ensaio GPU só demonstra benefício se executar o mesmo contrato e
comparar tempo total, incluindo transferência, preparo e retorno da tabela, contra
a CPU aquecida. Deve preservar os mesmos fingerprints, comparações estritas,
empates e precisão das decisões. A matriz densa double de 100.000² elementos
ocuparia 80 GB; uma implementação nessa escala precisa de tiling ou algoritmo
alternativo, respeitando a VRAM de 6 GiB. Não foi criado um backend especulativo.

Artefatos: `V9_CPU_Workload_Benchmark_20261019.json` contém cada amostra, hashes,
contagens, tempos CPU e alocações; `V9_GPU_Local_Inventory_20261019.json` contém o
inventário. Ambos evitam caminhos pessoais e dados reais. O projeto e o script
ficam em `Solution/tools/V9CpuWorkloadBenchmark`.

Reprodução a partir da raiz do repositório, com cache NuGet existente:

```powershell
dotnet build Solution/src/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.csproj --no-restore -c Release
dotnet restore Solution/tools/V9CpuWorkloadBenchmark/V9CpuWorkloadBenchmark.csproj --source "$env:USERPROFILE/.nuget/packages"
dotnet build Solution/tools/V9CpuWorkloadBenchmark/V9CpuWorkloadBenchmark.csproj --no-restore -c Release
dotnet Solution/tools/V9CpuWorkloadBenchmark/bin/Release/net10.0/V9CpuWorkloadBenchmark.dll
python Solution/tools/V9CpuWorkloadBenchmark/gpu_inventory.py --site-packages <venv-existente>/Lib/site-packages
```
