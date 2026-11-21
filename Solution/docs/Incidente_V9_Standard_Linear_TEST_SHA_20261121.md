# Incidente V9 Standard Linear 100k — SHA do recibo do TEST

**Data** 21/11/2026. **Estado** TRAIN, AUDIT e VALIDATION validados no escopo do run. TEST medido pelo Worker. Recibo do TEST rejeitado. Workflow concluído com falha.

## Execução e impacto

O proprietário `lucianox777` despachou o [run #44930426056](https://github.com/lucianox777/Jornada/actions/runs/44930426056) às 14:30:36 BRT na branch `master`, tentativa `1`, SHA `9c69cc6f8556a2057ad5255e77834f394302845a`. O [pré-requisito de autorização do PO](https://github.com/lucianox777/Jornada/pull/1826#issuecomment-7066232618) e a [atestação de preflight](https://github.com/lucianox777/Jornada/actions/runs/44930426056/job/136925621562) se vinculam a esse despacho. O [recibo técnico](https://github.com/lucianox777/Jornada/pull/1826#issuecomment-7066473262) documenta LOCKED, IBGE e isolamento. A campanha usou a configuração padrão de 100 mil identidades-base por coorte sintética `clean`, com partições 60/20/20.

| Etapa | Resultado no run |
| --- | --- |
| ADMISSION e PREPARE | Aprovadas. PREPARE gerou TRAIN, seed 42, e publicou o [pacote de transferência](https://github.com/lucianox777/Jornada/actions/runs/44930426056/artifacts/14717028583). |
| TRAIN e AUDIT | Aprovados. Foram medidas 324 de 324 hipóteses TF4 e auditadas 324 medições de P95. O AUDIT encaminhou três finalistas. [Recibos e hashes](https://github.com/lucianox777/Jornada/pull/1826#issuecomment-7070371179) |
| VALIDATION | Aprovada. Na coorte independente seed `20261112001`, mediu três finalistas sem refit e selecionou `6655a82cdda7f46f95896ce0a9abe26cc17ff290665a8347adb1a13683ec68ee`. O único arquivo operacional congelado permanece disponível. [Métricas](https://github.com/lucianox777/Jornada/actions/runs/44930426056/artifacts/14728278990) · [Arquivo](https://github.com/lucianox777/Jornada/actions/runs/44930426056/artifacts/14728905189) |
| TEST | O job confirmou um único vencedor, carregou seus bytes no SQL descartável próprio, reservou a seed `14141555920699831456` e gerou a coorte independente. O Worker informou `TEST_MEASURED_NOT_CERTIFIED_OR_ACTIVATED` às 19:50:40 BRT. O primeiro step reprovado, **Apply one frozen winner to TEST, no refit and no winner selection**, recebeu `V9_E2E_CLOSEOUT_TEST_EVIDENCE_SHA_INVALID` na verificação estrita, antes da publicação do recibo de métricas. [Log do TEST](https://github.com/lucianox777/Jornada/actions/runs/44930426056/job/137012002316) · [Coorte e reserva](https://github.com/lucianox777/Jornada/actions/runs/44930426056/artifacts/14728623457) |
| CLOSEOUT | O job publicou `e2e-status.json` e o relatório comparativo, com `testResult=failure`, `independentTestReportStatus=INCOMPLETE` e `independentTestScientificAssessment=INCONCLUSIVE`. O vencedor da VALIDATION ficou preservado. O workflow terminou com falha às 19:51:07 BRT. [Recibo](https://github.com/lucianox777/Jornada/actions/runs/44930426056/artifacts/14728144830) · [Comparativo](https://github.com/lucianox777/Jornada/actions/runs/44930426056/artifacts/14728174973) |

## Causa reproduzida e correção

O [leitor TEST no SHA executado](https://github.com/lucianox777/Jornada/blob/9c69cc6f8556a2057ad5255e77834f394302845a/Solution/src/Jornada.Linkage.Parameters.Worker/V9IndependentTestCorpusReader.cs#L49) confere os bytes de cada arquivo do manifesto com comparação de SHA-256 que aceita as duas formas hexadecimais. No retorno da coorte, repassa a grafia do manifesto de `observacoes.csv` ao recibo. No pacote desse run, o manifesto contém `7A316C43FC05738BE4862462E1A109AFE066B5DDF09F6A3354495156A5945BD7`. A leitura dos bytes produziu `7a316c43fc05738be4862462e1a109afe066b5ddf09f6a3354495156a5945bd7`. A [verificação estrita](https://github.com/lucianox777/Jornada/blob/9c69cc6f8556a2057ad5255e77834f394302845a/Solution/scripts/v9-tf4-e2e-closeout.py#L139) exige 64 caracteres hexadecimais minúsculos no recibo.

Uma fixture do verificador com o SHA do manifesto reproduziu a mesma exceção. A forma canônica do mesmo SHA passou sem alterar o verificador. A correção normaliza a grafia do digest que o leitor devolve, depois da verificação existente dos bytes. A fixture SQL usa os hashes maiúsculos emitidos pelo gerador real e confere no relatório o SHA minúsculo calculado do CSV. O manifesto, os arquivos LOCKED, a seleção, o modelo congelado e as verificações estritas preservam seus contratos.

## Evidências preservadas e recuperação

| Evidência | SHA-256 do ZIP |
| --- | --- |
| [Vencedor congelado](https://github.com/lucianox777/Jornada/actions/runs/44930426056/artifacts/14728905189) | `d5e57af869513903cbbd14c7ec3dfa078de81a1b2851681fb6c43c6716bbe845` |
| [Coorte TEST e recibo de seed](https://github.com/lucianox777/Jornada/actions/runs/44930426056/artifacts/14728623457) | `4b5352c28cce5af3c21065ca328a8275db76e41bd8549e9402c74537e925ccd3` |
| [Estado E2E](https://github.com/lucianox777/Jornada/actions/runs/44930426056/artifacts/14728144830) | `bb1f77f049cf22583371fbb6a27ac769f7b0bcf129009eeb5434d359506538d9` |
| [Relatório comparativo](https://github.com/lucianox777/Jornada/actions/runs/44930426056/artifacts/14728174973) | `0e748ff871f0faa430aaf5c8c9e84981d66a7d213227613442f9094e139bb804` |

A seed TEST fica reservada no [ledger global](https://github.com/lucianox777/Jornada/tree/v9-tf4-test-seed-v2-14141555920699831456). A recuperação exige correção com teste, integração por squash, gates pós-merge e novo despacho do proprietário com atestação do preflight no HEAD validado, tentativa 1 e nova seed TEST. O resultado científico do TEST deste run permanece inconclusivo porque a medição não gerou o recibo agregado verificável. A autorização produtiva pertence ao operador MASTER, sob DT-15.

[GOV-DOC-01](Politica_Registro_Decisoes_Discussoes.md) · [Runbook](Runbook_Operacao.md#operação-do-calibrador-v9)
