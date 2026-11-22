# GOV-V9-TF4-SEM-CONGELAMENTO-DE-GRADE-20261122 — decisão do Product Owner

**Data:** 22/11/2026. **Origem:** manifestação expressa do PO: “Não, eu não quero proteger o sistema contra alteração da grade.” **Política:** GOV-DOC-01.

## Contexto e decisão

A PR #1879 restaurou a configuração TF4 anterior após a avaliação insatisfatória da campanha #39; a PR #1882 acrescentou um teste que fixava explicitamente estados (0, 0.5, 1), margens (0.5, 2), M/U (0.5, 1) e 324 hipóteses, visando impedir mudanças posteriores. O PO rejeitou **a proteção contra alteração da grade**, não a restauração histórica em si.

**Decisão vigente:** não tratar os valores atuais da grade TF4 como imutáveis por meio do teste introduzido na PR #1882. Remover esse teste específico. A grade poderá ser alterada por trabalhos futuros sujeitos aos controles científicos e de governança aplicáveis; esta decisão não aprova nenhuma nova grade, campanha ou alteração imediata dos parâmetros.

## Precedência, impactos e limites

Esta decisão **revoga o propósito de congelamento por teste da PR #1882**, sem desfazer a reversão de código da PR #1879 nem alterar a avaliação científica negativa da campanha #39. Preserva os demais testes funcionais existentes, a rastreabilidade de campanhas e as restrições de TRAIN/VALIDATION/TEST, certificação e produção. Não autoriza ativação de V9 ou promoção de modelos.

**Implementação:** PR de reversão do teste específico `test_reverted_tf4_search_space_contract_is_explicit` em `Solution/tests/scripts/test_v9_v3_four_name_100k.py`. O registro só se considera integrado após merge e CI exigida.

**Rastreabilidade:** PRs #1866, #1879, #1880, #1882; issue #1865; campanha #39 (run #45002707113); `Solution/docs/Indice_Decisoes_Vigentes.md`.
