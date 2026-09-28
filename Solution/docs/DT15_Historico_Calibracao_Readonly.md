# DT-15 — histórico agregado de calibração na página master DEV

**Escopo desta fatia:** recuperar o trabalho de `feat/calibration-lifecycle-history-ibge-20260927` sem adicionar consultas históricas ao refresh do Monitor Operacional. A consulta dos últimos 20 modelos fica exclusivamente em `ModelGovernanceReadOnlyService`, servida pela página `/governanca/modelos` já protegida, disponível apenas em Development mediante permissão `jornada.modelos.governanca.read`.

## Dados e limites

A página master apresenta versão e estado de cada modelo, datas, código/hash da referência IBGE, contagens e denominadores VALIDATION/TEST, tamanho das amostras m/u, versão de algoritmo/normalização e presença/tipo de avaliação sintética. As linhas vêm de `identidade.modelo_linkage`, `ref.frequencia_nome_versao`, `identidade.parametro_linkage` e `auditoria.linkage_avaliacao_sintetica`. A consulta é somente leitura e limitada a 20 modelos; não envolve CPF, nomes, pares individuais, limiares de decisão nem atualização de Gold. A área histórica utiliza `textContent` no navegador.

**Não calcular deltas entre versões diferentes** sem igualdade verificada de corpus, truth, recortes, fingerprints, seed, comparadores e denominadores. Valores ausentes permanecem ausentes; presença de avaliação sintética é `SINTETICA_NAO_PROMOVIVEL`, não aprovação. A evidência de replay FS pareado já existente na DT-15 e a avaliação independente #31 continuam fluxos próprios. O Monitor Operacional segue limitado ao estado atual e ao último resumo, sem nova consulta histórica a cada atualização.

## Fluxo e separação de poderes

A inicialização IBGE usa o bootstrap/caches existentes; a consulta histórica **não** carrega arquivos censitários nem recalcula Monte Carlo. O bundle técnico de implantação (`config/release/configuration-bundle.json`) é diferente do modelo linkage versionado no SQL (parâmetros, passes, comparadores, referência e evidências). O wrapper Windows, após #571, termina em RASCUNHO conferido: não chama VALIDATE/ACTIVATE. A página master continua somente leitura. A aprovação individual, ledger decisório, IdP corporativo, replay completo/custos SQL e gates humanos nos caminhos diretos CLI/SQL permanecem tarefas distintas e bloqueiam promoção governada futura.

## Regressão

A integração SQL existente `LinkageModelGovernanceLedgerTests` invoca a mesma leitura restrita e passa a checar o limite de 20 itens e os estados descritivos. O contrato da página e a separação de privilégios permanecem sob o gate da matriz de autorização. Nenhuma mudança de schema SQL, runtime do Runner, scoring, calibração ou regras de publicação nesta fatia.
