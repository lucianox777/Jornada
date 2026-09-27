# Núcleo numérico em `double` — 27/09/2026

## Escopo da alteração

O scorer operacional Fellegi–Sunter passa a usar `double` (float64) para prior, m/u, razões de verossimilhança, somatório de LLR, log-odds e posterior. `LinkageModel.NumericParameters` converte os parâmetros persistidos **uma única vez** quando o modelo é materializado; o caminho operacional `Rank` usa `CalculateRaw`, sem converter pesos individuais a `decimal` nem alocar breakdown. `float` (float32) não foi adotado, pois reduz a precisão das decisões próximas às fronteiras.

## Compatibilidade e limite do escopo

O schema SQL Server, as probabilidades persistidas, o contrato público `FellegiSunterScore`, os objetos `CandidateScore`, a política decisória e os contratos de auditoria **continuam decimais**. Essa decisão evita migração simultânea de schema, API e modelo histórico. A saída `ToContractScore` faz cast de `double` para `decimal` e arredonda a oito casas, `AwayFromZero`, como no contrato anterior. `CalculateWithBreakdown` usa o mesmo núcleo `double` e materializa os valores `decimal` exclusivamente para diagnóstico. A calibração estatística, a geração de m/u e a conferência Splink não foram alteradas.

## Potencial de diferenças e gates

A conversão de `1m / quantidade_de_candidatos` para `1d / quantidade_de_candidatos`, bem como a representação binária dos parâmetros e a materialização do breakdown, podem alterar últimos dígitos. Os testes unitários incluem a equivalência entre entrada numérica e fronteira publicada; os testes de integração de fronteira do PR #514 e os de política devem confirmar limiar, margem, ranking, status e motivo.

Esta refatoração **não** elimina automaticamente o gate atualmente implementado em `VALIDATE`/`ACTIVATE`: até alteração governada separada da DT-14, ambos ainda exigem conferência `CONFORME` do mesmo modelo, tolerância e fingerprint. A conferência independente testa scorer/policy sobre estados pré-computados, não representa comparação entre aritmética decimal completa e float64, nem valida comparadores e representatividade estatística (#31).

## Aceite de engenharia

1. Compilar solução sem warnings e executar testes unitários e de integração de linkage.
2. Executar regressão dirigida perto do threshold, dual-threshold, empates, margem e priors extremos; comparar com a versão anterior antes de declarar equivalência operacional.
3. Manter os testes de contrato SQL e a conferência por modelo; qualquer divergência no status, no melhor/segundo candidato ou no motivo bloqueia a promoção.
4. Se a CI não executar os ensaios completos, manter o PR aberto; não declarar o scorer homologado apenas pela compilação.
