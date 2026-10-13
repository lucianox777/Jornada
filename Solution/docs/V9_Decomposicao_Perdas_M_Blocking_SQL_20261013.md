# V9 — decomposição dos M verdadeiros perdidos no blocking físico SQL (diagnóstico)

## Escopo e hipótese

A CI anterior (PR #998, relatórios TEST de seeds 42/43/44) encontrou **18.036 pares verdadeiros elegíveis** sem CPF entre gestores, com nascimento presente em ambas as observações. O SQL físico ATIVO+C1 recuperou **7.786 M**, perdendo **10.250 M** antes do FS; o recall de ponta a ponta com o modelo proxy congelado foi 43,1581%. A PR #1002 adicionou **capacidade opt-in** de buscar até quatro passes, mas **não alterou** o ruleset ATIVO nem resolveu a discrepância entre o gate de 95% no corpus do Calibrador e 43% no frame SQL.

Esta auditoria mede **qual combinação de atributos observados coincide ou diverge em cada M perdido**, a fim de separar possíveis limitações de recuperação. **Não** declara que divergência decorreu causalmente do gerador ou que um novo passe seria operacionalmente seguro.

## Contagem reproduzível

`V9BlockingTruthLossAudit` integra o censo `V9PhysicalSqlBlockingProbe` somente em `DOTNET_ENVIRONMENT=Development` e banco efêmero `JornadaHarness`.

1. Primeiro enumera **todos** os pares M verdadeiros do mesmo `base_person_id`, no mesmo split, entre gestores distintos, sem CPF em ambas as observações e com nascimento presente. Esta enumeração é **anterior e independente** do conteúdo de `#v9pairs`.
2. Para cada par, registra só estados observados: nome `EXACT/DIFFERENT/MISSING`; nome da mãe `EXACT/DIFFERENT/MISSING`; nascimento `EXACT/DIFFERENT`. Nome e mãe usam as projeções `NameFull`/`MotherFull` canônicas do próprio probe SQL, não um classificador inventado ou uma comparação de texto bruto. Ausência nunca vira igualdade nem desacordo.
3. No censo de `#v9pairs` **físico**, recupera a pertinência exata de M por ordinais temporários dos registros. Cada M tem contagem `Eligible=Recovered+Lost`. Se SQL produz M fora do denominador, duplicados ou inconsistências por split, o ensaio **falha fechado**.
4. Publica somente `TruePairLossAudit.Partitions` e as **54 células fixas** (3 splits × 3 estados de nome × 3 estados de mãe × 2 estados de nascimento), com `EligibleTruePairs`, `RetrievedTruePairs` e `LostTruePairs`. A métrica `SimultaneousNameMotherBirthDisagreementLost` conta apenas quando os **três campos estão presentes e divergentes em ambas as observações**; `MISSING` tem categoria própria.
5. O gate Python independente da CI exige unicidade/completude das 54 células, conciliação dos três denominadores com `Splits`, valores não negativos e igualdade da contagem de discordâncias triplas com sua célula. Reporta TEST por seed e total observado, **não seleciona regras por TEST**.

Os IDs temporários, `base_person_id`, nomes e datas individuais **não entram no relatório ou logs**. O status explícito é `DESCRIPTIVE_ONLY_NOT_GENERATOR_CAUSALITY_OR_SQL_ALIAS_PARITY`.

## Interpretação correta e limitações

Um par com nome, mãe e nascimento divergentes é mais difícil para regras que exigem igualdade exata, mas esta auditoria **não prova** qual corrupção gerou a divergência. Para atribuir ao gerador seria necessário conferir, em uma auditoria separada de **TRAIN**, os rótulos `Corruptions` de cada observação e os valores da pessoa-base, sem usar TEST para otimização. O perfil `correlated` tenta alterar simultaneamente os três atributos com probabilidade `0.18`; isso **não** é a porcentagem medida de triplas divergências. O recorte não avalia mãe/nascimento faltantes em conjunto amplo, pares com CPF, roteamento determinístico CPF nem todos os aliases históricos `VERSIONED_ALIAS`.

O indicador de 95% do `BlockingRuleSetOptimizer` vale para o frame amostrado do Calibrador, e **não foi reconciliado** com o frame físico SQL ATIVO+C1. Comparar fingerprints, passes e gabaritos idênticos continua sendo bloqueio antes de qualquer promoção. A versão V9 e TF permanecem desligadas no ensaio; futuro ajuste de `m/u`, prior e pesos TF usará a união SQL **selecionada apenas em TRAIN/VALIDATION**, referências IBGE congeladas por unidade compatível e, para dados conjuntos, a massa-base sintética única (não pontas repetidas de pares candidatos); TEST é apenas para validação congelada.

**Barreira:** merge de instrumento de diagnóstico após gates técnicos **não significa** recall >=95%, baixo FDR, aprovação institucional, DT-15 ou ativação V9. Não tocar `JornadaLocal`, IBGE original, NODE/NAS, Gold, HML/PROD ou histórico V8.
