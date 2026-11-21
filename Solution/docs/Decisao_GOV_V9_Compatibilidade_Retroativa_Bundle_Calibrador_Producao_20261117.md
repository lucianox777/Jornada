# GOV-V9-BUNDLE-COMPAT-RETROATIVA-20261117

**Autoridade:** decisão expressa do Product Owner em 17/11/2026 (horário de Brasília). **Estado:** aprovada pelo PO em 17/11/2026; registro original na PR #1620, reconciliado documentalmente com a `master` em 21/11/2026. Implementação e validação de retrocompatibilidade não são atestadas por este registro. **Política:** GOV-DOC-01.

## Contexto
O trabalho de oráculos e paridade TEST/produção gerou interpretação excessiva de um processo obrigatório de homologação/promoção. O PO esclareceu que a comparação entre resultados do TEST e da verificação do calibrador, assim como a decisão de promover o bundle, são escolhas do operador, não um fluxo novo a implementar.

## Decisão vigente aprovada pelo PO
1. A única exigência de implementação decorrente deste esclarecimento é que **a versão produtiva consiga executar o mesmo bundle de artefatos gerado pelo teste científico**, sem reinterpretação incompatível.
2. A versão de produção deve manter **compatibilidade retroativa** com os bundles produzidos pela versão do calibrador em execução. A evolução do software produtivo não pode invalidar unilateralmente esses bundles.
3. A verificação do bundle candidato à produção é **opcional e conduzida pelo operador**, que pode comparar os resultados do teste científico com os resultados da etapa de verificação do calibrador. Não criar nova obrigação automatizada de comparação, homologação, promoção ou seleção de vencedor com base nesta decisão.
4. Esta decisão **não autoriza** promoção automática, alteração de parâmetros do bundle, retreinamento no runtime ou mudança dos critérios científicos existentes.

## Justificativa e impactos
Simplificar a fronteira entre avaliação científica e execução operacional, evitando acoplamento artificial do TEST ao seletor e gates adicionais não aprovados. Impacto técnico: garantir leitura, validação contratual e execução de bundles anteriores suportados pela versão de calibrador em uso. Impacto operacional: manter a análise e eventual promoção sob escolha do operador. Não exige que TRAIN/TEST e produção usem o mesmo binário ou versão de software.

## Precedência e limites
Este esclarecimento **prevalece sobre interpretações anteriores** que propunham obrigatoriedade de uma nova comparação TEST versus produção como condição de promoção ou automatização da decisão do operador. Não revoga, por si só, contratos de segurança, integridade, isolamento científico, regras de publicação existentes ou evidências técnicas de testes anteriores. Os testes de paridade já construídos podem permanecer como regressões técnicas, sem se tornarem novos requisitos de aprovação por esta decisão. A compatibilidade retroativa deve ser verificada em implementação posterior; este registro documental não prova que ela já está completa.

## Rastreabilidade
PO: esclarecimento explícito na conversa do Projeto Jornada do Cidadão em 17/11/2026. Relacionado à issue #1584 e às PRs #1606, #1609, #1611, #1619. Índice: `Solution/docs/Indice_Decisoes_Vigentes.md`. GOV-DOC-01.
