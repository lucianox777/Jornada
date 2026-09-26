# ADR-007 — Conferência externa Jornada × Splink por arquivos, sem acoplamento ao runtime

- **Status:** **SUPERADA como documento normativo** pela [decisão consolidada de conferência externa](../../Solution/docs/Decisoes_Linkage_Calibracao_IBGE_20260926.md#21-conferência-externa-jornada--splink--decisão-consolidada-de-26092026); preservada somente para histórico e rastreabilidade
- **Data:** 2026-09-26
- **Escopo:** DT-01, Plano item 1, comparação externa **diagnóstica** do motor C#
- **Relacionados:** [ADR-002](ADR-002-calibrador-fs-u-condicionado.md), [conferência governada](../../Solution/docs/Linkage_Implementation_Conference.md), [exportação de auditoria já existente](../../Solution/docs/Linkage_Calibration_Audit_Export.md), [DT-01](../../Solution/docs/Dividas_Tecnicas.md)

**Atenção:** decisões operacionais atuais, fonte do bootstrap IBGE, escopo do replay e critérios de evidência constam unicamente da seção 2.1 da Decisão consolidada. A fixture de nove pessoas desta ADR histórica é smoke de contrato e **não** valida o cálculo do bootstrap do Censo; não usar este texto histórico como autorização para duplicar exportador nem considerar comparações não executadas como prova.

## Contexto e correções do inventário

O runner nominal Python/Splink e `SplinkCalibrationExchange.cs` existiram em setembro de 2026. A remoção do estágio DF e de seu runner foi integrada pelo commit [`a8f49c6b`](https://github.com/lucianox777/Jornada/commit/a8f49c6bf4d74d8e4f6086b8527d1a4f4d0e975f) de 20/09. O contrato histórico foi efetivamente implementado **em `Jornada.Linkage.Parameters.Worker`**, e não em `Jornada.Contracts`; dependia do benchmark DF que também foi retirado. A semântica histórica V1 tinha escopo **somente `NOME`**, níveis `EXACT/HIGH/MEDIUM/LOW`, thresholds Jaro–Winkler 0,92/0,80, `m` por labels positivos e `u` por pares aleatórios entre indivíduos-base canônicos. O runner de referência usava `splink==4.0.17`.

O repositório **ainda tem Python** em gates de CI, segurança, migrações e scripts de desenvolvimento. Esta ADR **não** promete `zero *.py` nem remove esses gates. Sua fronteira é **nenhuma nova dependência Python/Splink de calibração no runtime, build ou deploy operacional**. Revisão da retirada dos scripts Python auxiliares é outro trabalho, não pré-condição da conferência externa.

Existe também o exportador C# `Jornada.Linkage.Evaluation --export-calibration`, cujo `JORNADA_CALIBRATION_AUDIT_ROUNDTRIP_V1` já inclui parâmetros, blocking, proveniência e vetores sintéticos TF. Esse arquivo de **auditoria de modelo** não é o conjunto de registros/labels que o runner Splink espera; não misturar contratos nem reintroduzir seu exportador. O exportador de auditoria pode selecionar modelos `ATIVO`/`VALIDADO`, portanto **não** será uma rota automática para enviar informação operacional ao projeto externo.

## Decisão

Manter **um único motor operacional C#**, sem referência, submódulo, imagem, pacote, Action dependente ou chamada runtime ao runner externo. Criar um repositório de pesquisa independente, a provisionar separadamente, sugerido `jornada-splink-conformance`. Ele é responsável por `requirements.txt`, Python, runner Splink, testes, ambiente isolado e seu próprio CI; a Jornada recebe somente **arquivos JSON versionados mediante transferência manual e autorizada**. Não habilitar `repository_dispatch` como default: uma automação futura requer contrato de permissão e prova de que não há acesso a conteúdo operacional.

A fronteira C# atual oferece dois comandos **offline** em `Jornada.Linkage.Evaluation`:

```bash
dotnet run --project src/Jornada.Linkage.Evaluation -- --export-splink-synthetic ./fixture-splink.json
dotnet run --project src/Jornada.Linkage.Evaluation -- --check-splink-estimates ./fixture-splink.json ./estimativas-externas.json ./diagnostico.json
```

O primeiro só pode serializar uma **fixture literal compilada de nove pessoas sintéticas e dezoito registros**, recuperada do smoke histórico; não aceita conexão SQL, diretório de entrada, linhas arbitrárias ou campo de origem autodeclarado. O importador regenera e compara cada registro e label da fixture; rejeita payload extra, alterado ou adulterado. O segundo exige retorno com schema, versões, seed, partição, fingerprint, níveis, thresholds e probabilidades completos. Gerar/receber artefatos JSON + hash SHA-256 e registrar a proveniência do runner separadamente; transferência manual não é gate de CI do projeto principal.

Os contratos `JORNADA_SPLINK_EXCHANGE_V1` e `JORNADA_SPLINK_ESTIMATES_V1` conservam a **forma nominal histórica do runner**. O identificador de origem da fixture offline é explicitamente `SYNTHETIC_FIXTURE_NO_IBGE`, com fingerprint SHA-256 da fixture, **não** uma alegação de massa ou representatividade do Censo. O gerador é `EMBEDDED_NOMINAL_CONFORMANCE_V1`; a partição numérica 0 é apenas convenção do smoke histórico, **não** evidência real TRAIN/VALIDATION/TEST nem amostra representativa. Alterar escopo (mãe/nascimento), contratos, canal de erro, comparador ou acrescentar corpus sintético real exige desenho de **V2**, mapeamento/normalização explícitos e testes de compatibilidade; não ampliar silenciosamente V1.

## Comparações permitidas e limites metodológicos

A Jornada calcula `m` nominal local dos labels positivos com suavização Dirichlet `alpha=0,5` e `u` **exato de todos os pares entre pessoas canônicas distintas** da fixture; o runner Splink V1 estima `m` via labels positivos e `u` por amostragem aleatória versionada. Reportar TVD de `m` e `u`, delta de LLR natural por nível `ln(m/u)`, suportes, seed, fingerprint, versão do runner e divergências de mapeamento. Diferenças podem decorrer de estimador, suavização ou amostragem, **não automaticamente de defeito de scorer**. `u` aleatório incondicional **não** equivale ao `u` operacional condicionado à união deduplicada de candidatos do blocking (ADR-002); tampouco equivale à referência nominal IBGE (mãe nacional V1 / pessoa SP V2 candidata). Não renomear dados mistos como calibração de `u` operacional.

Para uma **conferência independente de scorer**, injetar os **mesmos parâmetros m/u, prior, estados já formados e política** nos dois implementadores e comparar por par, incluindo decisões em fronteira; para comparar comparadores, usar dados brutos e versão explícita, não estados originados exclusivamente pelo próprio C#. O gate governado corrente `JORNADA_IMPLEMENTATION_CONFERENCE_STATE_VECTOR_V1` continua sendo C# `decimal` × C# `float64`, com tolerância congelada 0,01 e equivalência exata de decisão final. A comparação Splink aqui é **diagnóstico suplementar**, não preenche automaticamente o gate persistente `VALIDATE/ACTIVATE`.

## Governança: dados reais não saem pela ponte

1. Apenas fixture embutida com conteúdo integral comprovado pelo C# é exportável neste V1. **Não há opção de exportar banco, CSV livre, corpus inserido por terceiro ou dados de cidadão**, mesmo quando um campo textual se declara `synthetic`. Uma futura rota `JornadaSyntheticDev` exigirá verificação no **banco** do perfil `Development`, proveniência verificável do gerador e manifesto/assinatura do corpus, além de testes de rejeição em dados mistos ou não marcados; declaração do chamador nunca basta.
2. O runner externo só recebe arquivos de fixture transferidos explicitamente, sem token GitHub privilegiado, acesso à rede/banco da Jornada, credencial municipal ou segredo de Produção. Vetar logs com payload bruto quando houver corpus não literal aprovado no futuro. A repo externa não é destino para artefatos `--export-calibration` de modelos reais.
3. Versão de runner, dependências Python, hash do input, hash do output, método de sampling, parâmetros e ambiente de execução devem ficar nos relatórios. `splink==4.0.17` é ponto inicial **histórico**, não instalação já verificada do novo repo.
4. Contratos importados são **read-only** e não invocam `GenerateDraft`, `VALIDATE`, `ACTIVATE`, Gold, ledger, thresholds ou ajuste de tolerância. TEST de um experimento futuro permanece intocado durante seleção. Validação representativa da [#31](https://github.com/lucianox777/Jornada/issues/31) é independente.

## Execução e aceite

- **Integrado à Jornada neste PR:** tipos C# de intercâmbio, fixture sintética fechada, comandos offline de exportação/validação, diagnóstico e testes de shape, conteúdo, seed/fingerprint, limiares, níveis e rejeição de origem forjada; documentação e CI .NET sem runner Splink.
- **Pendente fora do repo:** criação/ownership do repositório de pesquisa, instalação/pin efetivos do Splink, migração revisada do runner histórico para esse ambiente, smoke determinístico repetido e publicação manual dos resultados. Sem esses itens **não há evidência de paridade empírica C# × Splink** e **DT-01 permanece aberta** para caracterização independente.
- **Exclusões:** nenhum gatilho CI automático entre repos; nenhuma inferência de HML/Produção ou representatividade; nenhuma reintrodução do DF como estágio operacional.

## Consequências

A arquitetura continua C#-only no **runtime de Linkage**, a implementação C# pode testar e comparar a estrutura do intercâmbio sem Python/Splink, o estudo Python fica isolado e a segurança é garantida por **origem sintética verificável**, não por um booleano. O primeiro experimento é deliberadamente pequeno e não deve ser vendido como validação científica do modelo.
