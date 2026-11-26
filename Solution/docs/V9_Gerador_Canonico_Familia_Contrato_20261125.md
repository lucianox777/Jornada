# Gerador canônico V9 — contrato inicial de estrutura familiar (DEV)

**Estado:** código submetido; não validado. Não é manifesto aprovado, não gera massa nem habilita CLI ou workflow. Precedência: GOV-V9-GERADOR-CANONICO-20261125, plano v7, #1940; cap pré-declarado vigente 4/8/32, sem ratificação de novo teto. Nenhum TRAIN/TEST ou campanha.

A PR #1941 demonstrou paridade no HEAD 07a670797e42b288ac62026bffbcee9fec61446e (runs 45722605777 e 45722605763); os dois modos DEV passaram comparação de arquivos (10 e 17), o E2E núcleo 1k passou, integral não certificado. A #1940 permanece bloqueada no gate de paridade SQL, rank.Best/rank.Second (run 45726063101). Esta branch empilhada preserva o HEAD medido da #1941.

## Implementação técnica inicial

`SyntheticFamilyPlanner` fica exclusivamente no projeto evolutivo parametrizado. Recebe população, três caps explícitos e quotas por tipo/tamanho, sem defaults, rejeita excesso populacional e de tamanho familiar, quotas duplicadas ou inválidas. Planeja ordinais determinísticos de pessoa, família, identidade materna, evento de nascimento e caso diagnóstico. Irmãos compartilham identidade materna, gêmeos compartilham também evento de nascimento; mãe/filha inclui vínculo explícito ao ordinal da mãe. Homônimos e coincidências maternas entre não aparentados ficam em famílias unitárias distintas, não inferir parentesco por igualdade de atributos. Nenhum nome/data/CPF/observação é criado. A ordem canônica dos tipos/tamanhos não consome RNG do gerador histórico.

A cobertura é disjunta por caso planejado nesta etapa; não é modelo completo de sobreposições populacionais. O residual é preenchido por famílias unitárias: comportamento técnico do planejador, não prevalência demográfica. O fixture de 20 ordinais é somente teste de contrato, não quota proposta nem perfil representativo. Caps de versões são consistência prévia aritmética, ainda não validados contra versões reais de ondas. A seleção de famílias, mães/datas/nomes coerentes, a correlação de atributos, o gabarito selado e seu censo observacional, ausência de CPF em pares pertinentes, a partição por unidade de dependência, e a integração opt-in ainda precisam ser implementados. Não usar esse plano diretamente para estimar M/U ou risco de produção.

## Validação e bloqueios

Smoke isolado no projeto PartitionIntegritySmoke verifica determinismo, cobertura, relações e rejeições sem geração de pessoas. CI pendente. Próximo passo técnico: materializador opt-in de verdade familiar independente e validação de atributos/versões, preservando defaults, histórico e hashes. Não habilitar geração real antes de manifesto completo, autorização e gates do plano v7/#1940. Política S4b(i), #38 e ciência permanecem congelados. GOV-DOC-01.
