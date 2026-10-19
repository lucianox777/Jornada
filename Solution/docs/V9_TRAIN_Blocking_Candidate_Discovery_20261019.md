# V9: propostas de blocking no TRAIN

`V9CalibrationBlockingProjection` é um contrato opt-in compartilhado de Contracts,
com versão e fingerprint próprios. Reutiliza todas as chaves do projector canônico
e acrescenta `name_prefix_K` e `mother_name_prefix_K`. Os prefixos usam o nome
**integral** normalizado, incluindo espaços e partículas. K é parametrizado de 1
até o limite explicitamente solicitado, padrão 4 e teto técnico 64. Nome ausente
ou menor que K não produz chave para K; ausência nunca funciona como wildcard.
Os catálogos obrigatórios globais e a projeção física legada permanecem preservados.

`V9TrainBlockingCandidateDiscovery.Discover` aceita somente observações TRAIN.
VALIDATION e TEST são rejeitados antes da busca. Todos os pares positivos entre
gestores são enumerados antes do blocking, inclusive pares que nenhuma chave
recupera. Um limite explícito de positivos causa falha, nunca truncamento. Os
não-vínculos são amostrados uniformemente sem reposição, com seed, tamanho e
orçamento de tentativas explícitos. Frames pequenos usam enumeração e reservoir;
frames grandes usam rejection sampling de pares. Se o orçamento impedir completar
a amostra declarada, a operação falha.

Amostras e retenções servem apenas para gerar propostas. Não estimam m/u e não
medem custo físico. Cada união deve ser posteriormente materializada em SQL para
ajustar parâmetros no frame realmente produzido; a seleção global usa VALIDATION
pós-Rank/Resolve. Nenhum TEST participa desta etapa.

`BlockingRuleSetSearch.EnumerateCandidateRuleSets` reutiliza a busca existente:
avalia os passes primitivos e preserva o pool determinístico de recall/redução.
Retorna **todas** as uniões do pool, sem escolher um vencedor antecipado. O caminho
legado `SearchBest` aplica seu seletor existente ao mesmo conjunto. O orçamento
de uniões é configurável; o padrão legado continua 2.048 e a descoberta opt-in usa
100.000. Solicitações acima do orçamento falham. `RequireExhaustivePrimitivePool`
exige que o pool comporte todos os primitivos; caso contrário, falha explicitamente.
Sem essa opção a busca é declaradamente limitada ao pool, não exaustiva global.

O resultado registra opções efetivas, schema/fingerprint, fingerprint dos pares,
fingerprint do TRAIN projetado, número de observações e identidades, universo
positivo/não-vínculo antes do blocking, amostra, tentativas, comparações de features,
primitivos avaliados/retidos, uniões enumeradas e indicador de pool exaustivo.
São hashes e contagens; nomes e valores pessoais não são publicados no resultado.

Validação: testes de regressão para mudança efetiva de K, normalização, nomes
curtos/ausentes, preservação de catálogos legados, isolamento TRAIN, positivos
perdidos no blocking, amostra determinística, fingerprints sensíveis ao conteúdo,
falhas de orçamento e enumeração completa acima de 2.048 candidatos. Estes testes
comprovam a implementação; certificação estatística exige o ensaio SQL posterior.
