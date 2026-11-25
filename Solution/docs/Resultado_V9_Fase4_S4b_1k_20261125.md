# Resultado V9 — Fase 4, primeiro contraste S4b 1k (25/11/2026)

**Estado:** MEDIDO em DEV sintético, não publicante; política oficial S4b(i) inalterada. #1939, #1851, #1926, GOV-DOC-01. HEAD físico `79b45599c9877c984647b37ec7b02dab9d9b416f`, [run 45624044593](https://github.com/lucianox777/Jornada/actions/runs/45624044593) SUCCESS inclusive descarte, artefato 15028342328, ZIP 7660 bytes, SHA-256 `a8ba7a9f03a0ab566855d87addd31f27c075acfca6b7868fd391ca36b73fa48c`, conferido por bytes/CRC/commit.txt. Vencedor #38 congelado `6655a82cdda7f46f95896ce0a9abe26cc17ff290665a8347adb1a13683ec68ee`. Uma tentativa independente com mesmo seed/perfil/coorte, pseudônimos novos; baseline física reproduzida (1000 pessoas-base, 2349 versões, 1957 origens, TP563/FP0/FN1842/2405 pares verdadeiros). Sem TRAIN/TEST científico, refit, ativação, 10k/100k ou publicação de alternativas.

## Resultados globais (pares de versões)

| Variante | TP | FN recuperados | FN restantes | FP introduzidos | Fusões falsas | Novas IDs | Pessoas desdobradas | Recall |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| (i) oficial | 563 | 0 | 1842 | 0 | 0 | 0 | 0 | 23,4096% |
| (ii) grupo literal | 1591 | 1028 | 814 | 0 | 0 | 457 | 1 | 66,1538% |
| (iii) registro literal | 563 | 0 | 1842 | 0 | 0 | 1170 | 456 | 23,4096% |
| (ii) grupo + continuidade da origem | 2255 | 1692 | 150 | 0 | 0 | 456 | 0 | 93,7630% |
| (iii) registro + continuidade da origem | 789 | 226 | 1616 | 0 | 0 | 1167 | 456 | 32,8067% |

457 grupos inteiramente elegíveis, 1170 versões, nenhum grupo misto excluído. Posterior mínimo de todas as direções internas em [0,9999;1] nos 457; nenhuma outra banda ocupada. Indicador de elo mais fraco, não posterior probabilístico conjunto. Zero FP/fusões observados, não garantia populacional. Gabarito externo não entra na projeção; somente mede propostas. A análise de continuidade usa líder maior versão/ID em memória; não é execução física de outra regra de publicação.

| Tamanho Silver | Grupos | Versões | FN recuperados grupo literal | FN recuperados grupo + origem | FN recuperados registro + origem |
|---|---:|---:|---:|---:|---:|
| 2 | 260 | 520 | 260 | 451 | 94 |
| 3 | 138 | 414 | 414 | 705 | 90 |
| 4 | 59 | 236 | 354 | 536 | 42 |
| Total | 457 | 1170 | 1028 | 1692 | 226 |

FP/fusões zero em todas as bandas/tamanhos. Grupo + origem recupera 1466 intergestor e 226 intragestor; sobram 132 e 18 respectivamente. A exclusão intragestor é deliberada na construção SQL e guarda complete-link. Nesta coorte, 244 pares intragestor são da mesma origem persistente, não perdas de blocking intergestor. Manter FN global 1842; reportar separados 1598 intergestores e 244 continuidade. O par de motivos do líder SEM_INDICIO soma 1692, mas o bruto dos dois registros soma 1036; recuperação literal 1028 e com continuidade 1692, conforme medição.

## Precedência e limite

S4b(i) continua única política oficial. As alternativas são contrafactuais, não propostas automaticamente aprovadas. Nenhuma inferência de qualidade real ou de outro perfil de erros. Não inserir S4b(ii) na baseline da matriz de 24 fatores sem nova decisão do PO. Prosseguir com matriz original sob recibo S4b da mesma tentativa, estados independentes, regra r2 hipotética a especificar e validar; zero células fatoriais medidas neste run. Antes de 10k, relatório 1k e autorização expressa. O resultado está registrado na PR #1939, ainda draft, sem merge antecipado.
