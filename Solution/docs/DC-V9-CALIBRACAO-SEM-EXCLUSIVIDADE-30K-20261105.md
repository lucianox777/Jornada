# DC-V9-CALIBRAÇÃO-CORPUS-20261105 — 30 mil não é condição universal de execução

**Decisão do responsável (05/11/2026):** revogada qualquer leitura segundo a qual uma calibração V9 **só possa ser executada** num ensaio com exatamente 30.000 pessoas-base. O 30k, seed 42, perfil clean continua a ser a definição da **campanha histórica específica** de comparação e regressão, e não um tamanho imposto a toda calibração.

## Contrato de execução vigente

- O motor continua sendo o **único Calibrador C#**, invocado por `--v9-adaptive-study` com plano JSON, corpus CSV e **diretório novo de saída**. A solicitação de uma calibração, a seleção técnica automática e a autorização de uso operacional são atos diferentes.
- Cada novo estudo pode usar outro tamanho de corpus, desde que sua população e seus demais pressupostos sejam pré-declarados. Quando o plano inclui `ObservationCapDeclaration`, o manifesto autenticado por SHA informa `people`; o censo deve encontrar **exatamente esse número de pessoas-base distintas**, considerando TRAIN, VALIDATION e TEST, embora TEST não seja lido para estimar ou selecionar o modelo.
- Para opções diagnósticas inelegíveis, o manifesto de população é obrigatório e deve estar SHA-fixado ao plano; não eliminar o censo para contornar a conferência. A contagem de pessoas-base **não** é contagem de linhas ou pares de observações.
- Permanecem as verificações de referência IBGE imutável, partições por identidade-base, TRAIN para ajuste, VALIDATION para comparação, TEST lacrado, orçamento de multiplicidade/risco, diretório de evidências novo, SQL físico descartável `JornadaHarness`, SHA exato do código e proibição de publicação/ativação automática.
- **Tamanho do corpus não demonstra suficiência estatística.** Estudos menores, maiores ou com perfis diferentes podem ser executados sob protocolo e autorização adequados, porém a elegibilidade do modelo pode ser `INCONCLUSIVO`/não certificada. Não usar a nova liberdade para ultrapassar limites de evidência ou reaproveitar TEST consumido.

## Separação entre execução genérica e regressão histórica

O fluxo específico `v9-physical-seed42-probe.yml`, seu `execute_30k`, pin IBGE e a prova de 45 opções / 135 medições continuam **restritos à reprodução histórica de 30.000 pessoas-base**; não devem ser renomeados, reduzidos ou invocados como se executassem arbitrariamente outros tamanhos. O replay congelado V2 da PR #1283 também permanece preso ao plano/corpus/seed originais e aos seus hashes. A regra de **disparo explícito e controle de SHA para esse ensaio** não deve ser confundida com proibição de outras calibrações.

Para novo experimento não-30k, usar um plano/corpus próprios, com proveniência, validações e solicitação manual; sem alterar o plano V2 imutável ou fazer a campanha histórica parecer equivalente. Esta decisão **não afirma que já existe workflow genérico de GitHub Actions** com geração e orquestração automática de tamanhos arbitrários: o motor C# aceita o corpus declarado, e qualquer novo fluxo automatizado exige implementação e testes próprios.

## Testes e aceite

- CLI genérica não fixa `expectedBaseIdentityCount=30000` por presença de braços diagnósticos; lê a população do manifesto validado e rejeita manifesto inválido/ausente.
- Leitor C# faz contagem de identidades-base inclusive em TEST **sem ler atributos TEST**; compara com total declarado.
- Regressões cobrem contagens declaradas **3, 20.000, 30.000 e 45.000**, além de entradas inválidas; a campanha histórica de 30k conserva seus gates e hashes.
- A autorização do operador para a execução pontual mais recente da V2 é separada desta alteração normativa e não autoriza execução repetida, merge, DT-15 ou ativação operacional.

**Precedência:** esta decisão complementa `DC-V9-BOOT-CORE-20261030` e `Decisao_Calibrador_Unico_Adaptativo_Bootstrap_20261014`. Substitui somente a suposta obrigatoriedade universal de 30k; não revoga controles estatísticos, manuais ou de segurança.
