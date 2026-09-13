# Semântica territorial do seed de desenvolvimento

Este arquivo registra a interpretação canônica do seed corrente enquanto a correção textual de `Jornada_Seed_Dev.sql` é aplicada.

- `REFERENCIA_TERRITORIAL` é a fonte territorial da visualização e da análise.
- `ENDERECO_RESIDENCIAL` é um atributo cadastral de residência e não é sinônimo da referência territorial.
- Quando a referência territorial é derivada de `ENDERECO_RESIDENCIAL`, a natureza permanece `DOMICILIAR` e `fonte_semantica=ENDERECO_RESIDENCIAL` deve permanecer explicitamente rastreável.
- Baselines em `Solution/database/baselines/` são históricos e não devem ser reescritos para refletir a semântica corrente.

A proteção automatizada correspondente está em `Solution/tests/Jornada.Tests/Unit/TerritorialSemanticsDocumentationTests.cs`.
