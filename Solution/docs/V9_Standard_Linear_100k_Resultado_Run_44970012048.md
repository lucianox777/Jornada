# Resultado da campanha V9 Standard Linear 100k — run 44970012048

**Estado em 22/11/2026 — MEDIDO, com CLOSEOUT e integridade dos recibos verificados.** A [autorização dinâmica do PO](Decisao_GOV_V9_Autorizacao_Condicional_SHA_Final_20261121.md#autorização-dinâmica-vigente) registrou o escopo aprovado. O proprietário `lucianox777` despachou o workflow `.github/workflows/v9-standard-linear-100k.yml` em 21/11/2026 22:08:17 BRT, na `master`, tentativa 1. O SHA executado permanece `30c71a0db1bdeac6ba536db5e428ade4d5c70c9c` como identidade desta campanha. [Run](https://github.com/lucianox777/Jornada/actions/runs/44970012048) · [Admissão](https://github.com/lucianox777/Jornada/pull/1837#issuecomment-7072044978) · [Recibo final](https://github.com/lucianox777/Jornada/pull/1837#issuecomment-7075300378)

| Etapa | Estado | Término | Job |
| --- | --- | --- | --- |
| ADMISSION | Sucesso | 21/11/2026 22:08:24 BRT | [137047516058](https://github.com/lucianox777/Jornada/actions/runs/44970012048/job/137047516058) |
| PREPARE | Sucesso | 21/11/2026 22:09:11 BRT | [137047545264](https://github.com/lucianox777/Jornada/actions/runs/44970012048/job/137047545264) |
| TRAIN | Sucesso | 22/11/2026 03:01:35 BRT | [137047732413](https://github.com/lucianox777/Jornada/actions/runs/44970012048/job/137047732413) |
| AUDIT | Sucesso | 22/11/2026 03:03:49 BRT | [137114509637](https://github.com/lucianox777/Jornada/actions/runs/44970012048/job/137114509637) |
| VALIDATION | Sucesso | 22/11/2026 03:10:00 BRT | [137115016216](https://github.com/lucianox777/Jornada/actions/runs/44970012048/job/137115016216) |
| TEST | Sucesso | 22/11/2026 03:13:08 BRT | [137116413457](https://github.com/lucianox777/Jornada/actions/runs/44970012048/job/137116413457) |
| CLOSEOUT | Sucesso | 22/11/2026 03:13:26 BRT | [137117126561](https://github.com/lucianox777/Jornada/actions/runs/44970012048/job/137117126561) |

## Coortes e integridade

O input `synthetic_population` em branco aplicou o padrão de 100.000 identidades-base por coorte, perfil `clean`, referência IBGE congelada e partição interna 60/20/20.

| Coorte | Identidades-base | Observações geradas | Seed |
| --- | ---: | ---: | --- |
| TRAIN | 100.000 | 194.167 | `42` |
| VALIDATION | 100.000 | 194.173 | `20261112001` |
| TEST | 100.000 | 193.824 | `8067207138595720602` |

O TEST avaliou 34.065 observações de 20.146 identidades-base na partição TEST, com 20 repetições de desempenho. O recibo `V9_TEST_SEED_RESERVATION_V2` e a [tag da reserva global](https://github.com/lucianox777/Jornada/tree/v9-tf4-test-seed-v2-8067207138595720602) vinculam seed, run, tentativa, SHA de código e vencedor. As três coortes têm seeds e hashes de observações distintos. O TEST comprovou SQL descartável isolado e TLS com certificado fixado.

## TRAIN, AUDIT e seleção na VALIDATION

Foram medidas **324 hipóteses** (81 opções de TF4 × 2 valores de λ × 2 margens), com 324 medições de P95 auditadas. O estado de parada foi `COMPUTE_BUDGET_EXHAUSTED_NOT_OPTIMALITY`. Três finalistas do envelope auditado foram medidos na coorte nova de VALIDATION, sem refit.

| Finalista | SHA-256 do arquivo | Aceites verdadeiros | Falsos aceites | Recall global | Limite condicional de FDR | P95 |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| `TF4_PF0_PL10_MF10_ML5_A0.5_T0.95` | `01d2df7e98912f6ce31ff32a769592f06d7f49981f8784758f1d9dfa1671ef41` | 24.699 | 2 | 96,40% | 0,1447% | 4.811,79 ms |
| `TF4_PF5_PL5_MF10_ML5_A0.5_T0.95` **vencedor** | `6655a82cdda7f46f95896ce0a9abe26cc17ff290665a8347adb1a13683ec68ee` | 24.724 | 2 | 96,50% | 0,1446% | 6.024,93 ms |
| `TF4_PF10_PL5_MF10_ML10_A0.5_T0.95` | `1184a58a4792f8731bfbefce2ea73b87900b93387dd096c3347e97e1035a878d` | 24.728 | 11 | 96,51% | 0,3653% | 6.248,70 ms |

Os três finalistas atenderam ao limite superior condicional de FDR de 1%, ao piso de recall de 0% do plano, ao mínimo de 1.000 grupos de identidade-base antes do blocking e ao teto de 100 no grau falso P99. Cada um teve 18.327 grupos e grau falso P99 de 26. O seletor ordenou por menos falsos aceites, maior recall, menor limite condicional e SHA ordinal. O vencedor empatou em dois falsos aceites e teve maior recall entre os empatados. P95 compõe o gate separado de operabilidade condicional, com teto de 60.000 ms. [Seletor no SHA executado](https://github.com/lucianox777/Jornada/blob/30c71a0db1bdeac6ba536db5e428ade4d5c70c9c/Solution/src/Jornada.Linkage.Parameters.Worker/V9NestedCalibrationCli.cs#L1014-L1048)

O arquivo único congelado `6655a82cdda7f46f95896ce0a9abe26cc17ff290665a8347adb1a13683ec68ee.operational.json` tem SHA-256 `6655a82cdda7f46f95896ce0a9abe26cc17ff290665a8347adb1a13683ec68ee`. Seu TF4 usa pesos 0,5 / 0,5 / 1,0 / 0,5 para primeiro e último nome da pessoa e primeiro e último nome da mãe, nessa ordem. λ foi 1, margem de conflito 0,5, limiar de vínculo 0,95 e posterior mínimo de agrupamento 0,99. O TEST conferiu os bytes desse arquivo e mediu somente o vencedor.

## Métricas e fechamento

| Métrica | TRAIN do vencedor | VALIDATION | TEST independente |
| --- | ---: | ---: | ---: |
| Aceites verdadeiros | 22.857 | 24.724 | 24.915 |
| Falsos aceites | 4 | 2 | 1 |
| Elegíveis sem aceite correto | 753 | 898 | 819 |
| Recall global | 96,81% | 96,50% | 96,82% |
| FDR observado | 0,0175% | 0,0081% | 0,0040% |
| Valor preditivo positivo | 99,9825% | 99,9919% | 99,9960% |
| Limite superior condicional de FDR | — | 0,1446% | 0,0922% |
| P95 | 8.509,43 ms | 6.024,93 ms | 4.463,70 ms |

O CLOSEOUT registrou `E2E_VALIDATION_WINNER_PRESERVED_TEST_REPORTED`, recibo `VERIFIED` e avaliação `MEASURED_NOT_ADJUDICATED`. A independência estatística dos agrupamentos ficou sem validação independente. A autorização de produção registrada é `OPERATOR_MASTER_ONLY_NOT_GRANTED`. Certificação, publicação e ativação seguem sob avaliação e decisão próprias.

| Indicador do TEST | Diagnóstico de 10 mil | Campanha de 100 mil |
| --- | ---: | ---: |
| Observações avaliadas | 3.426 | 34.065 |
| Aceites verdadeiros / falsos | 2.488 / 1 | 24.915 / 1 |
| Recall global | 97,04% | 96,82% |
| FDR observado | 0,0402% | 0,0040% |
| Limite superior condicional de FDR | 0,9876% | 0,0922% |
| Limite de Hoeffding por identidade-base | 10,47% | 3,06% |
| P95 | 580,55 ms | 4.463,70 ms |
| Grau falso P99 | 5 | 22 |

Houve um falso aceite em cada escala, entre 2.489 aceites no ensaio de 10 mil e 24.916 na campanha de 100 mil. O recall caiu 0,22 ponto percentual e o P95 foi 7,69 vezes maior. São comparações descritivas entre coortes e vencedores distintos. [Ensaio diagnóstico e critérios](https://github.com/lucianox777/Jornada/pull/1837#issuecomment-7071971145)

## Artefatos verificados

| Evidência | Artefato | SHA-256 do ZIP conferido com CRC |
| --- | --- | --- |
| PREPARE | [14732438742](https://github.com/lucianox777/Jornada/actions/runs/44970012048/artifacts/14732438742) | `ba954dd0c3711a5df55055d76f4dc8a625a1118ccce21f4ce56e05336836b1ad` |
| TRAIN | [14742152579](https://github.com/lucianox777/Jornada/actions/runs/44970012048/artifacts/14742152579) | `22029b40ab51e6892f8071fae94d8c2c2d662348ae6dd3484614d95456ac9690` |
| AUDIT | [14741794442](https://github.com/lucianox777/Jornada/actions/runs/44970012048/artifacts/14741794442) | `e1abc3e5d733836a7360a2511cb94a196ccac015407a6bdb97d447a0dd29bb41` |
| VALIDATION | [14742472448](https://github.com/lucianox777/Jornada/actions/runs/44970012048/artifacts/14742472448) | `64e2e8375b5a71f8697e0703496728552a0698998e2585b89439893246b59684` |
| Estado da VALIDATION | [14742611497](https://github.com/lucianox777/Jornada/actions/runs/44970012048/artifacts/14742611497) | `1a4da4fcca339f1dd27a1aa2ed0f29cc09d056f9ecf221798880d957b5e2dc0b` |
| Coorte VALIDATION | [14742492418](https://github.com/lucianox777/Jornada/actions/runs/44970012048/artifacts/14742492418) | `c2d682e4d90518d3584a1ed3f2a63aa8448ad183b1baec68feae284a7118bfc1` |
| Arquivo operacional congelado | [14742452561](https://github.com/lucianox777/Jornada/actions/runs/44970012048/artifacts/14742452561) | `24a890243dff4d988515ac17532ee3720bff0c0b01261aa36c9e0d74426d5de7` |
| TEST independente | [14742233807](https://github.com/lucianox777/Jornada/actions/runs/44970012048/artifacts/14742233807) | `dc1f211549a61cfed6a1b5ee81b5e84580ef2eec6ad803b5425a449a489b3344` |
| Coorte e reserva TEST | [14742890165](https://github.com/lucianox777/Jornada/actions/runs/44970012048/artifacts/14742890165) | `e828414061da54e46dfbacb161bc8e62a073be967d25cb2678b5137578b521d2` |
| CLOSEOUT | [14742477968](https://github.com/lucianox777/Jornada/actions/runs/44970012048/artifacts/14742477968) | `c2f703e0b0f155e6ae4a9c7e4e7f258f1c6b89803c803152942716834da89929` |
| Relatório comparativo | [14742621957](https://github.com/lucianox777/Jornada/actions/runs/44970012048/artifacts/14742621957) | `9659e8593fbab3d5b21d1ab57189ed4bce26c0ce8fd9d17647dbee35761dead0` |
| Referência PREPARE | [14742084383](https://github.com/lucianox777/Jornada/actions/runs/44970012048/artifacts/14742084383) | `5f0f0d1b4f6e2bbe7831650eea167fb0b75b2f2d423085992c77ddf9e4e69880` |

Hashes internos conferidos. `finalist-validation.json` `66ac2f359b811b946f52b94adadff1b98507a03a929fa31a799c4eef0bf336c7`, `independent-test.json` `23c40a35b57ac8a1f7a9929156f81b154bc1070f35c81add8872992597077bc9`, `e2e-status.json` `6887bf0d586f6285bd28fcf6ea685c4d020453b049b29ff834496240210eaa12` e `campaign-report.json` `f68680d6721899325b126d951371628dab1b5e3ab898778ed42836e77996e036`. Os pacotes das coortes vinculam manifesto, arquivos CSV, SHA de código e run por `SHA256SUMS`.

Os ZIPs de transferência excedem o limite de download do conector. O AUDIT verificou hashes, cardinalidades e origem do preaudit. A VALIDATION verificou hashes, auditoria e plano do handoff antes de acessar o holdout. Os digests abaixo são publicados pelo GitHub Actions.

| Transferência | Artefato | SHA-256 publicado |
| --- | --- | --- |
| PREAUDIT | [14742267026](https://github.com/lucianox777/Jornada/actions/runs/44970012048/artifacts/14742267026) | `7cb9b20f0602abf8a588a2dd09ec2d4422fcebcf7408cbbb51bf80511d68dced` |
| Handoff VALIDATION | [14742257307](https://github.com/lucianox777/Jornada/actions/runs/44970012048/artifacts/14742257307) | `643b59545a938b63d8dc0cfc9df1abbf0195f9620e0e95ac5d96ebf513251cc6` |

[Incidente e recuperação da execução anterior](Incidente_V9_Standard_Linear_TEST_SHA_20261121.md) · [Preparação e checks do SHA executado](https://github.com/lucianox777/Jornada/pull/1837#issuecomment-7071971145) · [GOV-DOC-01](Politica_Registro_Decisoes_Discussoes.md)
