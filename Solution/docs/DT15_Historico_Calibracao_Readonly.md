# DT-15 — histórico agregado de calibração na página master DEV

**Escopo desta fatia:** recuperar o trabalho de `feat/calibration-lifecycle-history-ibge-20260927` sem adicionar consultas históricas ao refresh do Monitor Operacional. A consulta dos últimos 20 modelos fica exclusivamente em `ModelGovernanceReadOnlyService`, servida pela página `/governanca/modelos` já protegida, disponível apenas em Development mediante permissão `jornada.modelos.governanca.read`.

## Dados e limites

A página master apresenta versão e estado de cada modelo, datas, código/hash da referência IBGE, contagens e denominadores VALIDATION/TEST, tamanho das amostras m/u, versão de algoritmo/normalização e presença/tipo de avaliação sintética. As linhas vêm de `identidade.modelo_linkage`, `ref.frequencia_nome_versao`, `identidade.parametro_linkage` e `auditoria.linkage_avaliacao_sintetica`. A consulta é somente leitura e limitada a 20 modelos; não envolve CPF, nomes, pares individuais, limiares de decisão nem atualização de Gold. A área histórica utiliza `textContent` no navegador.

**Não calcular deltas entre versões diferentes** sem igualdade verificada de corpus, truth, recortes, fingerprints, seed, comparadores e denominadores. Valores ausentes permanecem ausentes; presença de avaliação sintética é `SINTETICA_NAO_PROMOVIVEL`, não aprovação. A evidência de replay FS pareado já existente na DT-15 e a avaliação independente #31 continuam fluxos próprios. O Monitor Operacional segue limitado ao estado atual e ao último resumo, sem nova consulta histórica a cada atualização.

## Fluxo e separação de poderes

A inicialização IBGE usa o bootstrap/caches existentes; a consulta histórica **não** carrega arquivos censitários nem recalcula Monte Carlo. O bundle técnico de implantação (`config/release/configuration-bundle.json`) é diferente do modelo linkage versionado no SQL (parâmetros, passes, comparadores, referência e evidências). O wrapper Windows, após #571, termina em RASCUNHO conferido: não chama VALIDATE/ACTIVATE. A página master continua somente leitura. A aprovação individual, ledger decisório, IdP corporativo, replay completo/custos SQL e gates humanos nos caminhos diretos CLI/SQL permanecem tarefas distintas e bloqueiam promoção governada futura.

## Regressão

A integração SQL existente `LinkageModelGovernanceLedgerTests` invoca a mesma leitura restrita e passa a checar o limite de 20 itens e os estados descritivos. O contrato da página e a separação de privilégios permanecem sob o gate da matriz de autorização. Nenhuma mudança de schema SQL, runtime do Runner, scoring, calibração ou regras de publicação nesta fatia.

## Frente paralela de visualização — 29/09/2026

[PR #616](https://github.com/lucianox777/Jornada/pull/616) implementa, isoladamente na página DEV read-only, linha do tempo dos até 20 modelos **já retornados** por `calibrationHistory`: versão/estado, data, referência IBGE vinculada, suporte m/u e evidência. Ordenação cronológica de apresentação, sem novo endpoint, SQL, autenticação, alteração de parâmetros ou promoção. **A referência IBGE vinculada não comprova nova carga a cada geração**; esta fatia não inventa a proveniência m/u ausente do payload nem presume que o primeiro item é o bootstrap inicial se modelos anteriores não estiverem nos 20 registros. Merge sujeito a gates no HEAD.

**Paralelização aprovada:** (A) UI/histórico read-only com contrato atual, sem depender de alteração do Worker; (B) evolução separada do read model e da proveniência do bootstrap inicial IBGE, parâmetros m/u, thresholds, versões e fingerprints do bundle, com testes SQL e privacidade; (C) replay pareado FS/custos e dossiê DT-15; (D) aprovação individual e publicação atômica do bundle, dependentes de RBAC/IdP, gates SQL/Worker e coordenação do Runner. A–C podem progredir paralelamente em arquivos e branches distintos; D só é habilitada depois dos contratos e gates. O Monitor continua separado e somente leitura. **Não** alterar a CI compartilhada nem migrations em branches de UI.

## Proveniência parcial do bundle lógico — frente isolada de leitura DEV

`GovernanceBundleProvenanceReader` complementa o JSON restrito já servido pelo endpoint master DEV, sem nova rota, permissão ou consulta no Monitor. Lê exclusivamente metadados SQL dos modelos ATIVO/RASCUNHO: contagem de linhas de parâmetros (sem expor m/u, thresholds, margem ou guardas), versão/fingerprint do ruleset e da projeção, versões de algoritmo e normalização, e código/hash da referência IBGE **associada ao modelo**. A leitura rejeita modelo sem proveniência correspondente ou duplicado e conserva a checagem final do ATIVO/RASCUNHO existente na API.

Os três blocos do **bundle lógico** (parâmetros e decisão; blocking; contrato de runtime/comparadores/projeções) **não** são três arquivos físicos. Contagem de parâmetros ou hashes parciais não comprovam integridade conjunta, cobertura de guardas ou identidade do bundle publicado. Por isso o payload marca `PROVENIENCIA_PARCIAL`/`PROVENIENCIA_INCOMPLETA` e sempre `SEM_FINGERPRINT_GLOBAL_NAO_PROMOVIVEL`; o contrato dos comparadores e guardas fica `NAO_VERIFICADO` nesta leitura. O hash IBGE associado **não prova recarga nem origem efetiva dos parâmetros m/u** de calibrações posteriores. Nenhum destes indicadores autoriza `VALIDATE`, `ACTIVATE` ou promoção implícita.

A integração SQL de governança verifica o formato e a não atribuição de fingerprint global. Persistência/versionamento atômico do bundle, demonstração integral de hash/fingerprints, proveniência efetiva do bootstrap, dossiê pareado e decisão individual com RBAC/IdP são entregas separadas. A página visual da #616 pode avançar paralelamente usando apenas o contrato já existente; este bloco de leitura não é requisito para sua renderização.
