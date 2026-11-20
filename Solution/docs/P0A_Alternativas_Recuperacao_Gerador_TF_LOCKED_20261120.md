# P0-A — Comparação histórica e alternativas de recuperação do gerador TF LOCKED

**Data:** 20/11/2026. **Estado atual:** alternativa A **aprovada pelo PO em 20/11/2026**; alternativas B/C não aprovadas. Implementação e validação pendentes. **Issue:** #1734. **Precedência:** GOV-DOC-01, os dois manifests LOCKED e o commit histórico de referência `b98a01180737909edf7cbd0bee4ba2757d52fe0f`.

## Evidência verificada

A auditoria executada no merge checkout da PR #1769 (run `44764078274`, artefato `14643085698`) identificou 25 arquivos rastreados, mas digest agregado `f23874cf21fff462a815a33d08ac1140cd1e9541210624bad0cf2fb3871f94cc` contra `924ad6455e15b73093f7d4834174966717de13be9b89b14b9640d11ffbea8deb` esperado, e digest individual de `SyntheticCorpusGenerator.cs` `70cbd9280417238411ff7f7e6c1384c2ad1c2458c08dcf6d38962397bbc558a2` contra `83015950790a5be15a5fb843c8330c8cccb10610f1a208ee46eeb7fc348c18e4` esperado. Ambos os contratos retornaram `matches=false`.

## Comparação textual do commit histórico com master

Comparação de arquivos obtidos pelo GitHub no commit histórico `b98a011...` e na master, **sem inferir que somente estes quatro arquivos mudaram em toda a árvore**:

| Arquivo | Diferença relevante | Implicação |
| --- | --- | --- |
| `SyntheticCorpusGenerator.cs` | Partição histórica fixa em 60%/20%/20%; master utiliza `options.TrainPercent` e `options.ValidationPercent`. | O byte do arquivo diverge mesmo se os defaults preservarem a distribuição. |
| `Program.cs` | Master adiciona `--calibrator-config`, população configurável e percentuais de partição. | Reversão direta pode retirar funcionalidade parametrizada. |
| `SyntheticCorpusMaterializer.cs` | Master adiciona `synthetic_population` e `partition_percentages` ao manifest, com exceção explícita para cenário histórico seed42/30k/clean. | A forma do manifest depende da configuração; não é prova de identidade do gerador. |
| `SyntheticCorpusModels.cs` | Master adiciona percentuais configuráveis e validação da soma em 100. | O contrato de opções foi ampliado. |

A comparação acima é textual e seletiva; **não substitui** diff completo dos 25 arquivos, reconstrução do commit de referência nem teste de compatibilidade funcional.

## Alternativas avaliadas pelo Product Owner

**A — Recuperar a árvore histórica dos 25 arquivos no caminho congelado e mover a parametrização para outro projeto/caminho.** Vantagem: favorece identidade byte a byte com os LOCKED, se as referências também corresponderem. Custo: novo limite de projeto/CLI, testes de consumidores, eventual migração dos workflows atuais. Verificar antes que o commit histórico reproduz exatamente ambos os hashes esperados; não assumir.

**B — Manter a implementação atual e executar exclusivamente as campanhas históricas a partir de checkout imutável do commit de referência.** Vantagem: reduz risco de regressão da CLI atual. Custo: precisa estabelecer e aprovar procedimento explícito de checkout e proveniência; os manifests exigem identidade dos bytes do checkout, não apenas seleção de flags. Não equivale a alterar ou relaxar o gate.

**C — Reverter seletivamente alterações do gerador no caminho congelado.** Vantagem: escopo potencialmente menor. Risco: uma reversão parcial não necessariamente reproduz o digest agregado dos 25 arquivos; pode quebrar dependências da parametrização. Não aplicar sem comparação integral e testes.

**Não recomendado como solução:** regravar os hashes nos manifests, ignorar `matches=false`, afrouxar o gate ou declarar equivalência semântica como substituta de identidade byte a byte.

## Critérios de aceite propostos (ainda não aprovados)

1. Comprovar em checkout reproduzível o SHA-256 agregado e individual exatos dos dois LOCKED e a contagem de 25 arquivos, sem regenerar os manifests.
2. Preservar os arquivos de referência, planos por seed, fingerprints e parâmetros históricos.
3. Executar testes de compilação e regressão de consumidores da parametrização, sem campanhas científicas TRAIN/TEST.
4. Registrar decisão aprovada, justificativa, precedência, impactos e rollback em documentação oficial e no índice GOV-DOC-01.
5. Somente depois avaliar adoção de gate obrigatório e eventuais mudanças de branch protection.

**Atualização de precedência:** a alternativa A foi aprovada posteriormente pelo PO, conforme deliberação ao final deste documento; B/C não aprovadas. A PR #1769 permanece limitada à instrumentação diagnóstica até a implementação segregada.

## Medição comparativa do commit histórico (instrumentação, 20/11/2026)

O commit `6d2efe7` adiciona um segundo job diagnóstico ao workflow P0-A. O job faz dois checkouts separados: o código do auditor da PR e o repositório no SHA histórico **fixo** `b98a01180737909edf7cbd0bee4ba2757d52fe0f`. Executa o auditor somente em leitura sobre a árvore histórica e compara seus bytes com os dois manifests presentes naquele checkout; publica JSON próprio (`p0-tf-locked-generator-historical-reference`) e resumo explícito de MATCH/MISMATCH. Justificativa: verificar fisicamente se o commit indicado como origem reproduz as identidades registradas antes de escolher a alternativa A, B ou C. Limitação: o teste compara com os manifests **da revisão histórica**; deve-se também confrontar seus valores com os manifests atuais antes de alegar equivalência completa. O sucesso do job não substitui `matches=true`. O resultado ainda não estava disponível na criação deste registro. Nenhum checkout executa geração científica ou modifica LOCKED.

## Correção da medição histórica (20/11/2026)

No run `44764677612`, o job histórico concluiu **verde de forma não bloqueante**, porém o auditor lançou `FileNotFoundError`: o commit `b98a011...` não continha `Solution/docs/V9_TF_SEEDS_44_47_LOCK.json`. O artefato de 192 bytes foi produzido sem comparação válida; **não constitui evidência de MATCH ou MISMATCH histórico**. Os commits `5051bab` e `b62c4b9` corrigem a instrumentação: o auditor aceita `--locks-root` e o job usa os dois manifests **atuais da PR** para comparar os bytes do checkout histórico. Nenhum manifest é copiado, regenerado ou editado. A conclusão física permanece pendente da nova execução. Esta correção substitui a descrição anterior de comparação com manifests históricos, que não existiam nesse commit.

## Resultado conclusivo de identidade do checkout histórico (20/11/2026)

O run `44764842253`, job `136386576723`, publicou o artefato `p0-tf-locked-generator-historical-reference` (ID `14643755922`). A comparação **dos 25 arquivos efetivos** no checkout fixado em `b98a01180737909edf7cbd0bee4ba2757d52fe0f` com os **dois manifests LOCKED atuais** produziu `matches=true` para ambos os experimentos, com digest agregado `924ad6455e15b73093f7d4834174966717de13be9b89b14b9640d11ffbea8deb` e digest de `SyntheticCorpusGenerator.cs` `83015950790a5be15a5fb843c8330c8cccb10610f1a208ee46eeb7fc348c18e4`, exatamente iguais aos esperados. Portanto, a identidade histórica é **recuperável sem alterar os manifests**, desde que os bytes desse commit sejam usados. Isto valida **identidade de código**, não corpus, compilação, execução científica ou adoção arquitetural. Em contraste, o checkout atual da PR retorna `matches=false` em ambos os LOCKED. A alternativa A/B/C permanece decisão pendente do PO. Rastreabilidade: issue #1734, PR #1769 e artefato `14643755922`.

## Regressão da comparação entre revisões (20/11/2026)

O commit `1416b62` adiciona teste offline de duas árvores independentes: gerador histórico sem manifests e contratos atuais em outro diretório. Verifica `matches=true` com 25 arquivos e hashes correspondentes e `matches=false` após alteração de bytes no arquivo histórico. Contexto: a primeira tentativa do job histórico falhou porque os LOCKED ainda não existiam no commit de referência; o teste protege o parâmetro `--locks-root` contra regressão. Impacto restrito a testes, sem alteração de decisões aprovadas, sem execução científica. Validação CI do novo commit ainda pendente.

## Deliberação do Product Owner — 20/11/2026

**DECISÃO APROVADA: GOV-P0-TF-LOCKED-ISOLAMENTO-GERADOR-HISTORICO-20261120.** O Product Owner respondeu expressamente **“Sim”** à pergunta sobre a aprovação da **alternativa A**, isto é, **isolar o gerador histórico e preservar a implementação parametrizada separadamente**. Esta deliberação **substitui o estado “não aprovada” exclusivamente para a alternativa A**; alternativas B e C permanecem não aprovadas. A aprovação é arquitetural e **não autoriza** modificar os manifests LOCKED, os 25 bytes/arquivos de referência congelados, fingerprints, seeds, executar TRAIN/TEST, certificar produção ou fazer merge sem os gates aplicáveis.

**Justificativa:** o checkout histórico `b98a01180737909edf7cbd0bee4ba2757d52fe0f` reproduz exatamente ambos os hashes atuais, enquanto o gerador parametrizado da master diverge; a separação deve permitir manter a evolução da parametrização sem comprometer a identidade histórica.

**Impactos e implementação condicionada:** definir fronteiras de código e consumidores; restaurar os 25 arquivos históricos **somente no caminho contratualmente congelado**, realocar/encapsular as capacidades parametrizadas em outro caminho sem perda funcional; atualizar invocações, testes, CI e documentação; comprovar os hashes exatos em checkout da PR; preservar evidências e prever rollback. Não considerar a implementação concluída antes de CI obrigatória e gate real de identidade aprovados. A aprovação **não é prova** de que a implementação já ocorreu.

**Precedência e rastreabilidade:** GOV-DOC-01; decisão de preservação de identidade sintética GOV-SYNTH-IDENTIDADE-MANTER-20261120; issue #1734; PR #1769 (diagnóstico); workflow `44765081016`, artefatos `14644136083` (histórico MATCH) e `14644620671` (atual MISMATCH). A decisão aprovada deverá constar do índice de decisões vigentes.

## Início da implementação controlada da alternativa A (20/11/2026)

Os commits `0f1deee`, `158236b` e `0f1f1ac` introduzem **somente o planejador read-only** `Solution/scripts/plan-tf-locked-generator-isolation.py`, seu teste de regressão offline e sua execução no job diagnóstico histórico. O planejador usa `git ls-tree` e `git show` para inventariar bytes por arquivo e digest agregado nas duas revisões, listar adições/remoções/alterações e emitir JSON sem modificar arquivos, commits, manifests ou corpus. Isto permite determinar o escopo físico da migração aprovada antes de restaurar os 25 arquivos e realocar a implementação parametrizada. **A separação física ainda não foi implementada; nenhum gate obrigatório de identidade foi ativado.** CI dos novos commits pendente. A aprovação arquitetural do PO permanece limitada ao isolamento histórico com preservação da parametrização e da identidade congelada.

## Inventário físico publicado pela CI (20/11/2026)

O commit `1fdef11` conecta o planejador read-only ao job histórico da CI, com `git fetch` limitado ao SHA de referência e geração do artefato JSON `p0-tf-locked-generator-isolation-inventory`. O relatório discrimina arquivos adicionados, removidos e modificados e digests por revisão; é pré-requisito de análise de impacto da alternativa A, não migração efetiva. O teste offline do planejador no run `44765927428`, job `136390395917`, foi executado com sucesso; a CI do novo inventário ainda precisa ser verificada. A comparação continua não bloqueante e não autoriza TRAIN/TEST.

## Inventário físico verificado: quatro arquivos divergentes (20/11/2026)

O workflow `44766039794`, job `136390796766`, publicou o artefato `14646205229` com inventário Git **completo dos 25 arquivos rastreados**. Ambos os checkouts contêm 25 arquivos; **21 são idênticos e somente quatro divergem**: `Program.cs` (histórico `6239f635753f54648f7a8b4bfa43d9b541306269c079980f30da600d3a6fb3c1`, atual `4f515761ca4ac3df17ca85fda8fe8269541798fbc439217a92e25578eb75a908`); `SyntheticCorpusGenerator.cs` (histórico `83015950790a5be15a5fb843c8330c8cccb10610f1a208ee46eeb7fc348c18e4`, atual `70cbd9280417238411ff7f7e6c1384c2ad1c2458c08dcf6d38962397bbc558a2`); `SyntheticCorpusMaterializer.cs` (histórico `a01e264a815c1a5a3c063bcee0dda236198fa3af28e3e2a9c87fb0aef342e5c2`, atual `4c4daf701f84879897edcb22e34d5afa4b8d1a8c0ccf9e190e3d4d038ddbda38`); `SyntheticCorpusModels.cs` (histórico `1bc86867a4fdd89928aa6c4f5a77a221b03e8bb1c615dec9df86943ab972ffb7`, atual `499122cf3ca86d60bfa127f380efb9fec1fb1754eabae8838062cb254c239d62`). Digest agregado histórico `924ad6455e15b73093f7d4834174966717de13be9b89b14b9640d11ffbea8deb`, atual `f23874cf21fff462a815a33d08ac1140cd1e9541210624bad0cf2fb3871f94cc`. O relatório é leitura de revisões, não migração. Implicação arquitetural: a alternativa A exige restaurar **exatamente quatro arquivos** no caminho histórico e preservar suas capacidades novas em fronteira distinta; não alterar os outros 21 arquivos. A viabilidade de dependências e a compilação após separação ainda precisam ser demonstradas.

## Análise de dependência para a separação (20/11/2026)

Inspeção direta dos arquivos `Program.cs`, `SyntheticCorpusModels.cs`, `SyntheticCorpusGenerator.cs` e `SyntheticCorpusMaterializer.cs` nas revisões `master` e `b98a011...` confirma um **acoplamento de API**: o `Program.cs` atual interpreta `--calibrator-config` e passa `TrainPercent`, `ValidationPercent` e `TestPercent` para `SyntheticCorpusOptions`; o modelo histórico não possui esses três campos nem a validação de percentuais, e o `SyntheticCorpusGenerator.cs` histórico usa limites fixos de 60%/80% em vez dos campos parametrizados. O materializador também diverge em bytes e precisa ser restaurado integralmente para obter a identidade agregada. **Não é seguro substituir apenas o gerador ou apenas o modelo**, pois isso poderia quebrar compilação ou alterar a semântica do corpus. A restauração deve ser atômica para os quatro arquivos, com uma fronteira explícita para preservar a CLI/configuração parametrizada e seu contrato de saída em outro caminho. Esta é uma análise de código, **não** prova de compatibilidade de consumidores nem autorização para executar campanhas.

**Critério de entrada para migração física:** identificar invocações da CLI parametrizada, manter seus testes e sua compatibilidade, estabelecer projeto/caminho separado, demonstrar compilação de ambos os fluxos, executar o gate real de identidade dos 25 arquivos e documentar evidência de rollback. Nenhum desses critérios deve ser declarado cumprido sem execução de CI.

## Levantamento automatizado de consumidores (20/11/2026)

O commit `3ed5644` acrescenta ao workflow diagnóstico um levantamento **read-only** de referências aos switches `calibrator-config`, `train-percent`, `validation-percent`, `test-percent` e às propriedades `TrainPercent`, `ValidationPercent`, `TestPercent` em arquivos de código e configuração rastreados. O artefato `p0-tf-locked-generator-consumers` registra caminho, linha e símbolos, excluindo documentos, testes e workflows para priorizar consumidores de produção. A pesquisa é **heurística**, não substitui análise de chamadas dinâmicas, compilação, testes nem demonstração de compatibilidade. A restauração física dos quatro arquivos continua condicionada à inspeção desse resultado; a CI do commit ainda está pendente.

## Resultado e correção de abrangência do levantamento de consumidores (20/11/2026)

O run `44766597580`, job `136392740717`, produziu o artefato `14644337478` com `hits=[]` **apenas no escopo inicial** (excluía `Solution/docs/`, `Solution/tests/` e `.github/`). Isso **não prova ausência de consumidores**, pois testes, automações e documentação podem invocar a CLI, além de chamadas dinâmicas. O commit `fff0b3c` ampliou o inventário para todos esses caminhos, classificando cada ocorrência como `test`, `workflow`, `documentation` ou `source_or_config`, e registrou explicitamente a limitação de detecção dinâmica. O novo relatório e sua CI ainda precisam ser examinados antes da migração física. Não houve alteração do gerador nem dos manifests LOCKED.
