# Jornada V9 — execução verificável dos testes gerais #1345

**Registro não executável.** Documento operacional associado à [decisão GOV-V9-VALIDACAO-INTEGRAL-1345-20261109](../../docs/Decisao_V9_Validacao_Integral_SHA_20261109.md).

O PO determinou a validação integral da V9 integrada. Os workflows existentes usam filtros por caminho e os testes Windows, cluster e independente não haviam rodado no último SHA de documentação. Este arquivo está deliberadamente em um diretório já monitorado pelos **três** workflows de teste, e sua alteração dispara seus gates originais sem editar os seus próprios filtros. Ele **não modifica binários, contratos, runtime, SQL, REF, plano aprovado, escolha de modelo, segredos, TEST/Gold ou a política 30k**.

A PR de validação também amplia apenas o gatilho do workflow **J de prova física** para executar a testemunha V2 na `master` com o mesmo corpus seed42, CP8, dois papéis e artefatos baseline imutáveis. Nada executa uma nova pesquisa ou habilita ativação científica.

**Resultado:** deve ser definido pelo SHA do merge na `master` e pelos jobs reais. A existência deste arquivo **não equivale a PASS**, e sua inclusão sozinha não autoriza conclusão de #1345.

**Rastreabilidade:** [issue #1345](https://github.com/lucianox777/Jornada/issues/1345); [GOV-DOC-01](../../docs/Indice_Decisoes_Vigentes.md).
