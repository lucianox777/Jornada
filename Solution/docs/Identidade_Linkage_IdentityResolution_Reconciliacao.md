# Reconciliação — IdentityResolution.zip x Linkage.Parameters x Processor

## Decisão

`IdentityResolution.zip` não substitui `Jornada.Linkage.Parameters.Worker` nem o `Jornada.Linkage.Runner`.

A arquitetura canônica permanece:

`Linkage.Parameters` → modelo versionado/aprovado → `Linkage.Runner`/Processor → decisão de linkage → `IdentityCompositionPlanner` → ledger `PREPARADA` → revalidação autoritativa → aplicação governada futura.

O ZIP deve ser tratado como material técnico de referência e fonte de ideias para o Gerador de Parâmetros. Nenhuma saída de `ClusterResolver` pode escrever identidade, `identity_map`, vínculo factual, composição, Gold ou Serving diretamente.

## Comparação com o código atual

### Universo e blocking

O Processor atual materializa o universo do run e só publica uma execução quando todos os itens elegíveis produziram resultado. O contrato canônico de blocking já está congelado em `BirthBlockingPlan` e possui cinco passes:

1. data exata;
2. mês/ano com inicial;
3. dia/ano com inicial;
4. transposição dia/mês;
5. ano vizinho dentro da tolerância configurada.

O ZIP possui apenas duas regras padrão e, portanto, não representa o universo canônico de candidatos da Jornada.

### Estimação m/u

`FellegiSunterModel.Calibrate()` do ZIP executa EM não supervisionado sobre `CandidatePair` já produzido pelo blocking. Assim, as estimativas são condicionadas à seleção do blocking e não podem ser promovidas diretamente a `u` populacional/canônico.

O Gerador atual já contém desenho amostral explícito sobre o universo efetivo de candidatos, probabilidades de inclusão, pesos de desenho, fingerprints, cinco passes, separação treino/avaliação e diagnóstico ponderado. Esse desenho continua sendo a base governada da issue #31.

O EM do ZIP pode ser estudado futuramente como estimador experimental, desde que receba uma amostra cujo universo e mecanismo de inclusão estejam explicitamente definidos e que sua saída permaneça diagnóstica até homologação independente. Não deve sobrescrever silenciosamente o estimador canônico.

### Frequência de termos

`TermFrequencyTable` representa uma ideia útil: concordância em valor raro pode carregar evidência diferente de concordância em valor frequente. O repositório já reserva `FrequencyCalculator` para profiling/term-frequency adjustment futuro.

Qualquer adoção deve entrar primeiro no `Linkage.Parameters`, com versão de feature/modelo, corpus congelado, limites e validação independente. Não deve ser aplicado diretamente no scorer em produção.

### Ausência por origem

`MissingFieldPolicy` modela probabilidades de ausência por par de sistemas de origem. A hipótese é plausível, mas ausência/cobertura por Secretaria é uma característica do processo de coleta e pode introduzir dependência com outras evidências.

Deve ser tratada como evidência/estratificação candidata no Gerador de Parâmetros, não como fallback operacional configurado à mão. A inclusão exige evidência representativa, versionamento e validação de calibração.

### ClusterResolver

O `ClusterResolver` usa union-find. Portanto, pares aceitos A-B e B-C podem formar um componente A-B-C sem que A-C tenha sido diretamente aceito. Isso é útil para construir componentes candidatos, mas não é uma política suficiente para composição de identidade.

Na Jornada, qualquer composição persistente deve passar por `IdentityCompositionPlanner`, autoridade CPF, fechamento autoritativo do componente, versões esperadas, reservas, ledger `PREPARADA` e revalidação antes da aplicação. Separação/fusão deve permanecer reversível e historicamente rastreável.

## Fronteiras de responsabilidade

### Linkage.Parameters

Responsável por amostragem, calibração, avaliação, versionamento e produção governada dos parâmetros/modelos. Não aplica identidades.

### Processor / Linkage.Runner

Responsável por executar o modelo aprovado sobre um universo materializado, produzir decisões versionadas e recusar publicação incompleta. Não deve fazer fusão estrutural por transitividade de cluster.

### IdentityCompositionPlanner

Responsável por verificar admissibilidade estrutural da composição proposta, incluindo versões, partição fechada, reservas, continuidade de UUID inicial e autoridade CPF.

### Executor de composição

Próxima fatia. Será responsável por aplicar um plano já `PREPARADA` e revalidado dentro de uma unidade transacional, persistir histórico aplicado e coordenar recomposição/invalidação dos derivados. A criação desse executor não autoriza ativação probabilística real; a issue #31 permanece gate independente.

## Consequência para a próxima implementação

A interface do executor não recebe clusters brutos nem `CandidatePair`. Recebe apenas `decision_id` de um plano persistido `PREPARADA`. Dentro da transação, relê o ledger, o componente autoritativo e as reservas, exige replanning idêntico e só então pode efetivar as mudanças previstas.

Nenhum componente do ZIP será importado para o caminho operacional nesta fatia.
