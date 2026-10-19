# Calibrador V9 — implementação e validação local de 19/10/2026

Base: `452c5d3bf870553ac896c6620f2bedc5e48ca05b`. Branch de trabalho: `feat/v9-calibrador-integral-local-20261019`. Este documento registra a fase local anterior à publicação da PR. O estado do merge e da CI deve ser conferido na PR e na HEAD vigente; não declara homologação nem ativação. Os documentos datados anteriores continuam como evidência histórica.

## Entregas desta alteração

O Calibrador C# existente continua como única implementação. O adaptador FS V9 estima os parâmetros em TRAIN, avalia o ajuste congelado em VALIDATION e agora exporta um snapshot portátil completo. Não foi criado outro motor ou treino na inicialização.

`V9FittedSnapshot` contém versões de contrato e projeção, fingerprints, passes ordenados, 81 células conjuntas pessoa×mãe, prior, probabilidades binárias de nascimento, threshold, margem e peso TF. TF positiva inclui a referência nominal congelada, frequências por termo e U por termo condicionado ao TRAIN. O artefato tem JSON canônico e SHA-256 externo obrigatório para recarga; dicionários/listas são copiados para impedir mutações após o congelamento.

Exportar exige que blocking/projeção já estejam vinculados ao frame usado no fit. Não se aceita acrescentar proveniência arbitrária a um fixture de pares depois do treino. A referência TF deve corresponder ao mesmo frame de blocking e ao lado pessoa/mãe correto. Parâmetros desconhecidos ou termo necessário ausente não são convertidos silenciosamente em BF neutro.

A recarga executa o mesmo núcleo FS/Rank/Resolve do preview, sem estimar novamente. O armazenamento portátil é create-only, inclusive sob concorrência. Não sobrescreve artefatos existentes. O artefato não representa publicação na camada SQL `ref`, autorização ou modelo ATIVO.

O seletor pós-Resolve usa uma decisão final por observação-fonte e denominador elegível anterior ao blocking. Reconcilia contagens, coorte, recall, FDR pontual e fanout. Oracle não pode ser selecionado. Sugestões exigem grupos FS sem gabarito, margem positiva, suporte explícito da hipótese de independência e bound condicional de FDR corrigido pelo número de comparações. O melhor resultado descritivo é apresentado separadamente; pode existir sem sugestão aceitável. No probe atual a independência não está atestada, portanto a sugestão permanece nula.

Quando o probe mede Resolve final, a sugestão antiga por pares é suprimida no relatório. FP/aceites após Resolve não são confundidos com U/M do blocking. O seletor não publica nem ativa modelo.

## TF e ausência de atributos

- BF neutro significa LR=1, portanto LLR=0. Isso não é uma frequência de termo igual a 1.
- Peso TF=0 desliga a contribuição TF; peso TF=1 aplica integralmente o ajuste contratado.
- Frequência=1 é um termo extremamente comum: quando aplicável, o ajuste é log(U/1), não necessariamente zero.
- Frequência/parâmetro desconhecido com TF positiva falha explicitamente.
- No contrato implementado, o ajuste portátil aplica-se ao nível `ExactFirst`, na unidade prenome, com referência e U compatíveis. HIGH, EXACT_FULL e outros níveis não recebem artificialmente o ajuste de outra unidade.
- Não existe invariante universal EXACT>HIGH: o LLR depende de m/u e TF condicionados. Não foram impostos pesos ou ordenação para fazer testes passarem.
- Ausência do dado no registro é diferente de ausência dos parâmetros do modelo. DOB pode faltar em uma ou ambas as pontas e é neutro no modelo com estratégia binária calibrada. O schema portátil atual exige essa estratégia completa; não aceita uma configuração que cairia implicitamente no scorer histórico de componentes.

A TF reproduz fielmente o experimento implementado. Isso não comprova representatividade municipal, equivalência estatística a qualquer uso externo do Splink nem validade operacional do condicionamento. A justificativa estatística e a conferência independente permanecem gates próprios.

## Cadeia auditada e situação real

| Etapa | Implementação/evidência | Limite restante |
| --- | --- | --- |
| Catálogo e contratos | Catálogo versionado, projeção e fingerprint vinculados ao fit/exportação | Atributos novos exigem nova calibração, não inclusão automática em modelo publicado. |
| Blocking | Probe SQL materializa união deduplicada e mede M elegível antes dos passes; frame vinculado ao snapshot | Não executado nesta sessão por ausência de SQL descartável. A grade fixa da CLI física é experimental, não busca universal de passes/prefixos concluída. |
| FS e m/u | 81 células conjuntas, prior e nascimento estimados apenas em TRAIN | Suavização não comprova suporte nem suficiência estatística de células raras. |
| TF | Validação de estados brutos, referências, unidade, lado, frame e exportação/recarga com TF positiva | Fundamentação estatística do baseline/referência e avaliação de risco independentes ainda necessárias. |
| Calibração de limiares | Configurações de threshold/margem avaliadas em VALIDATION; snapshot preserva a escolha exata | Não declarar que a grade experimental substitui a descoberta adaptativa integral de todas as fronteiras/LS. O calibrador histórico de limiares não foi usado como fallback V9. |
| Seletor adaptativo | Seleção por fonte pós-Resolve; exclusão Oracle/margem zero; orçamento e bound explícitos | Não há sugestão estatisticamente aprovada sem comprovação das hipóteses e suporte; Pareto/persistência da PR #1134 não foram integrados por esta branch. |
| Scorer, Rank e Resolve | Recarga do artefato usa núcleo compartilhado V9; testes verificam UUID, posterior e status reais | Preview/replay matemático não equivale a dispatch operacional autorizado. |
| Vínculo/não vínculo/pendência | Decisões finais permanecem conservadoras; falta de informação não gera vínculo inventado | Nenhuma alteração automática do modelo ativo. |
| Não resolvidos | Runner existente seleciona pendentes/conflitos no incremental | Replay governado de RESOLVIDOS/DT-22 permanece postergado e não é alegado como entregue. |
| API e Processor | Suíte unitária inclui pipeline HTTP real em processo e guardas; Processor mantém `AGUARDA_LINKAGE_SOB_DEMANDA` no fluxo sem CPF | API→Processor→SQL→Runner V9 com publicação final não executado. Não iniciar scoring automático no Processor. |
| SQL e promoção | Nenhuma migration ou alteração de banco nesta execução | `GENERATE_DRAFT` operacional e `LinkageModelPolicy` continuam bloqueando V9: ainda falta vincular snapshot certificado, referência SQL, modelo e DT-15 numa publicação transacional verificável. |

## Separação de dados e evidências

A leitura do corpus agora verifica a partição de cada identidade-base antes de filtrar CPF. Isso detecta vazamento TRAIN/VALIDATION/TEST mesmo em linhas excluídas do ramo probabilístico. TEST tem apenas metadados de partição inspecionados; seus atributos e rótulos não entram em fit ou seleção. O avaliador por fonte também rejeita coorte diferente daquela congelada no frame.

Foi gerado um corpus **histórico de regressão**, clean seed42, com **30.000 pessoas-base e 58.148 observações**, em diretório novo `.local/v9-integral-clean-seed42-20261019`. O SHA de `observacoes.csv` foi `28bbb9f4c63c97c353e7f29cdbc77091a4773b2f4c596e46103edadd93635d17`, igual ao artefato histórico documentado de 18/10. A auditoria com `--exclude-test` mediu **46.780 observações TRAIN/VALIDATION** e **32.782 pares M cross-gestor**, reproduzindo o universo publicado.

Isso comprova geração determinística e aritmética do corpus, não novos TP/FP/recall/FPR do Linkage. Não foi ampliada a massa, executado novo correlated ou reutilizado TEST histórico para otimização. Um novo TEST cego só poderá ser aberto após congelamento do modelo e protocolo.

## Ferramentas e regressões de plataforma

SDK 10.0.113 instalado e fixado em `global.json`, pins, ambientes, workflows e gates correspondentes. `System.Security.Cryptography.Pkcs` 9.0.21 tem uma única referência direta no projeto SQL; a duplicação local foi removida. Restore force-evaluate seguido de locked-mode foi realizado. O grafo vigente e sua proveniência foram conferidos para os 22 projetos, sem alterar o atestado histórico da release v4.05.

Os gates NuGet agora inventariam somente as árvores canônicas `src` e `tests`, excluindo ferramentas geradas em `.local` e cópias locais do repositório. Esses diretórios não são parte da solution; antes podiam introduzir projetos antigos e sobrepor metadados da aplicação na validação local.

CSV Splink e exportação canônica V9 usam LF determinístico. A fixture do gate multiseed escreve os bytes exatos que são hasheados; teste adversarial comprova que alteração CRLF continua rejeitada. Não foi normalizado hash de artefato recebido para tolerar adulteração.

## Reproduzir a validação

Resultado consolidado local: **Release com 0 avisos/0 erros; 1.679 testes unitários aprovados, 0 falhas e 0 ignorados**, com gate TRX `--forbid-skipped` aprovado. **24 testes Python direcionados aprovados**; NuGet/proveniência dos 22 projetos, OpenAPI das 22 operações, source-sanity, analyzers e verificação de diff aprovados. A bateria ampla Python de CI teve 50 aprovações e duas falhas de execução/asserção ligadas a symlink e permissões POSIX no Windows; não equivale a CI Linux verde.

Na pasta `Solution`:

```powershell
dotnet restore Jornada.sln --locked-mode
dotnet build Jornada.sln -c Release --no-restore
dotnet test tests/Jornada.Tests/Jornada.Tests.csproj -c Release --no-build --no-restore --logger 'trx;LogFileName=unit-all.trx' --results-directory .local/v9-integral-tests
python scripts/test-evidence-gate.py .local/v9-integral-tests/unit-all.trx --forbid-skipped
python scripts/nuget-lock-gate.py --root .
python scripts/nuget-lock-provenance-gate.py --root .
```

Inspeção portátil, sem iniciar Host, treino ou conexão SQL:

```powershell
dotnet run --no-build -c Release --project src/Jornada.Linkage.Parameters.Worker -- --v9-inspect-fitted-snapshot <snapshot.json> <sha256-fixado>
```

Evidências locais ficam em `.local/v9-integral-tests/`. Os resultados consolidados estão no relatório JSON local e na descrição preparada para PR. A tentativa ampla dos testes Python de CI encontrou duas limitações Windows preexistentes: criação de symlink sem privilégio e bits executáveis POSIX. Esses testes não foram removidos, relaxados ou convertidos em PASS; devem ser executados no Linux da CI.

## Gates do incremento e pendências para conclusão integral

Este incremento entrega implementação portátil e seleção diagnóstica. Seu merge técnico exige revisão e CI integral na HEAD exata, incluindo SQL físico descartável. **Não conclui integralmente a integração operacional V9.** As pendências abaixo continuam rastreadas separadamente:

1. Revisão e reconciliação com #1156 e #1134 antes de integrar alterações sobrepostas; nenhum merge remoto ocorreu aqui.
2. Integração da busca adaptativa completa de blocking/atributos/limiares e do Pareto ao único Calibrador, com evidência dos critérios estatísticos.
3. Persistência/publicação SQL imutável e vínculo transacional snapshot-modelo-governança, seguidos de dispatch V9 nas APIs/Runner por modelo certificado. Não remover a guarda atual para esconder essa lacuna.
4. Execução SQL física clean e API→Processor→Runner em banco isolado; nesta sessão Docker não tem daemon ativo e não há SQL Server descartável disponível. JornadaLocal, IBGE original e os dois nós DEV não foram utilizados como substitutos.
5. Protocolo de risco por identidade/estrato, suporte e dependências defensáveis; conferência matemática independente; novo TEST lacrado e homologação conforme #31/DT-15.
6. CI Linux/Windows completa sobre o commit final, revisão e documentação de entrega aplicável. Build e unit local verdes não substituem esses gates.

Na fase local descrita acima não houve push, commit ou merge. Nenhum reset de banco, alteração de NODE ou ativação integra este incremento. As configurações locais e os dados existentes permanecem fora do conjunto de arquivos destinado à PR.
