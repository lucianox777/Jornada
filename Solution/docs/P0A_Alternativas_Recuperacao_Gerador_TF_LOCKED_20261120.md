# P0-A — Comparação histórica e alternativas de recuperação do gerador TF LOCKED

**Data:** 20/11/2026. **Estado:** análise técnica e opções **não aprovadas**; sem autorização de implementação da recuperação. **Issue:** #1734. **Precedência:** GOV-DOC-01, os dois manifests LOCKED e o commit histórico de referência `b98a01180737909edf7cbd0bee4ba2757d52fe0f`.

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

## Alternativas a deliberar pelo Product Owner

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

**Nenhuma alternativa está aprovada ou implementada por este documento.** A PR #1769 continua limitada à instrumentação diagnóstica.

## Medição comparativa do commit histórico (instrumentação, 20/11/2026)

O commit `6d2efe7` adiciona um segundo job diagnóstico ao workflow P0-A. O job faz dois checkouts separados: o código do auditor da PR e o repositório no SHA histórico **fixo** `b98a01180737909edf7cbd0bee4ba2757d52fe0f`. Executa o auditor somente em leitura sobre a árvore histórica e compara seus bytes com os dois manifests presentes naquele checkout; publica JSON próprio (`p0-tf-locked-generator-historical-reference`) e resumo explícito de MATCH/MISMATCH. Justificativa: verificar fisicamente se o commit indicado como origem reproduz as identidades registradas antes de escolher a alternativa A, B ou C. Limitação: o teste compara com os manifests **da revisão histórica**; deve-se também confrontar seus valores com os manifests atuais antes de alegar equivalência completa. O sucesso do job não substitui `matches=true`. O resultado ainda não estava disponível na criação deste registro. Nenhum checkout executa geração científica ou modifica LOCKED.

## Correção da medição histórica (20/11/2026)

No run `44764677612`, o job histórico concluiu **verde de forma não bloqueante**, porém o auditor lançou `FileNotFoundError`: o commit `b98a011...` não continha `Solution/docs/V9_TF_SEEDS_44_47_LOCK.json`. O artefato de 192 bytes foi produzido sem comparação válida; **não constitui evidência de MATCH ou MISMATCH histórico**. Os commits `5051bab` e `b62c4b9` corrigem a instrumentação: o auditor aceita `--locks-root` e o job usa os dois manifests **atuais da PR** para comparar os bytes do checkout histórico. Nenhum manifest é copiado, regenerado ou editado. A conclusão física permanece pendente da nova execução. Esta correção substitui a descrição anterior de comparação com manifests históricos, que não existiam nesse commit.

## Resultado conclusivo de identidade do checkout histórico (20/11/2026)

O run `44764842253`, job `136386576723`, publicou o artefato `p0-tf-locked-generator-historical-reference` (ID `14643755922`). A comparação **dos 25 arquivos efetivos** no checkout fixado em `b98a01180737909edf7cbd0bee4ba2757d52fe0f` com os **dois manifests LOCKED atuais** produziu `matches=true` para ambos os experimentos, com digest agregado `924ad6455e15b73093f7d4834174966717de13be9b89b14b9640d11ffbea8deb` e digest de `SyntheticCorpusGenerator.cs` `83015950790a5be15a5fb843c8330c8cccb10610f1a208ee46eeb7fc348c18e4`, exatamente iguais aos esperados. Portanto, a identidade histórica é **recuperável sem alterar os manifests**, desde que os bytes desse commit sejam usados. Isto valida **identidade de código**, não corpus, compilação, execução científica ou adoção arquitetural. Em contraste, o checkout atual da PR retorna `matches=false` em ambos os LOCKED. A alternativa A/B/C permanece decisão pendente do PO. Rastreabilidade: issue #1734, PR #1769 e artefato `14643755922`.

## Regressão da comparação entre revisões (20/11/2026)

O commit `1416b62` adiciona teste offline de duas árvores independentes: gerador histórico sem manifests e contratos atuais em outro diretório. Verifica `matches=true` com 25 arquivos e hashes correspondentes e `matches=false` após alteração de bytes no arquivo histórico. Contexto: a primeira tentativa do job histórico falhou porque os LOCKED ainda não existiam no commit de referência; o teste protege o parâmetro `--locks-root` contra regressão. Impacto restrito a testes, sem alteração de decisões aprovadas, sem execução científica. Validação CI do novo commit ainda pendente.
