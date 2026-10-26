# V9 Gold — candidato de bootstrap congelado e gates da candidata v5.00

**Corte factual: 26/10/2026;** master `eed351fcc4c6fedd4f565a7762b6d436c0bd3e47`.
**Natureza:** reconciliação de evidência e plano técnico de preparação; **NÃO** é homologação estatística,
alteração normativa da identidade, migration SQL, ativação DT-15, TEST executado ou release publicada.

## 1. Política experimental escolhida para preparar a integração

A configuração **KNEE lower nonmatch 0,05** permanece o **único candidato
congelado** para preparar a migration canônica e a avaliação final.
O limiar superior congelado é **0,8671875**; fit Gold
`6d93690dccac2c92dc4d3113cbb9169dd19156c4f9b3d849ededfb4c54ae5f3f`.
Não alterar prior, M/U, TF, passes, normalização, Rank→Resolve ou a margem
para melhorar retrospectivamente a matriz observada. O 0,05 é **parâmetro
de bootstrap do snapshot**, não constante definitiva do Calibrador V9 único
em C# nem licença para recalibração automática no boot.

A PR [#1192](https://github.com/lucianox777/Jornada/pull/1192)
já foi **merged**; o ensaio físico externo de seis LocalDBs descartáveis
é o [run #40250618984](https://github.com/lucianox777/Jornada/actions/runs/40250618984),
com laudo na [issue #1191, comentário #6407851791](https://github.com/lucianox777/Jornada/issues/1191#issuecomment-6407851791).
Corpus novo de **30.000 pessoas geradas, seed 20261025001**; a subcoorte
realmente avaliada no Gold tem **30.804 observações elegíveis e 16.616
identidades verdadeiras**. Não dividir contagens de erro pelas 30 mil
pessoas geradas como se fosse o denominador de decisões Gold.

| Ordem | Política | Pendências | Falsos vínculos aceitos | Falsas criações físicas | B-cubed recall com cobertura |
| --- | --- | ---: | ---: | ---: | ---: |
| Baseline | CURRENT | 11.122 | 20 | 5 | 0,637285 |
| Baseline | KNEE 0,05 | 163 | 13 | 31 | 0,992125 |
| Reversa | CURRENT | 11.222 | 10 | 3 | 0,634441 |
| Reversa | KNEE 0,05 | 163 | 12 | 22 | 0,992515 |
| Intercalada | CURRENT | 11.202 | 17 | 3 | 0,634766 |
| Intercalada | KNEE 0,05 | 169 | 13 | 30 | 0,991952 |

**Pior ordem KNEE observada:** até **13 falsos vínculos aceitos**, **31
falsas criações físicas**, **169 pendências**, e B-cubed recall mínimo
**0,991952**. Esses números **não** são limites de confiança,
orçamento populacional aceito, teto garantido para outra coorte ou
certificação operacional. Na ordem reversa houve regressão pareada:
falsos vínculos **10→12** e falsos pares Gold **14→18**. Uma avaliação
da pior ordem deve preservar o pareamento por ordem e considerar
incerteza por grupos de identidade-base independentes, sem pressupor
pares independentes.

## 2. Encerramento da exploração ampla de sintéticos

A [PR #1197](https://github.com/lucianox777/Jornada/pull/1197)
foi **CLOSED WITHOUT MERGE** após seis ensaios físicos TRAIN:
a busca indiscriminada de 19 células evitou apenas 2/4/3 falsas criações,
mas aumentou pendências em 2.269/2.381/2.343 e reduziu cobertura B-cubed
de aproximadamente 0,995 para 0,74. **Não repetir** essa variante nem
usar o holdout externo já conhecido para escolher outra.

A hipótese H1 da [issue #999](https://github.com/lucianox777/Jornada/issues/999),
`name_last ∧ mother_name_first ∧ birth_year`, é redundante com o passe
`P_LAST_M_FIRST` que já exige `mother_name_first ∧ name_last` no
fit congelado: H1 recuperaria um subconjunto dos pares existentes.
O descarte sem nova corrida foi registrado no
[comentário #6421927742](https://github.com/lucianox777/Jornada/issues/999#issuecomment-6421927742).

**Orientação desta rodada:** suspender ajustes exploratórios em TRAIN de
blocking/FS/TF/Resolve/limiares. A #999 fica aberta para lacunas de
verificação e a #1089 para o **Calibrador histórico C# único** e o
bootstrap em `ref`. Isto não declara requisitos incompletos concluídos.
Somente fato novo comprovado de erro de segurança/correção autoriza
reabrir o modelo, com novo plano de validação independente e sem
consumir TEST para escolha.

## 3. Sequência com decisões irreversíveis bloqueadas por padrão

1. **Congelamento e reprodução:** fixar snapshot com SHAs de fit,
   parâmetros, catálogo, passes, projeção, código de scorer e versão do
   Calibrador; carregar/verificar na subida sem refit. Os parâmetros
   permanecem de bootstrap, não definitivos.
2. **PR separada de migration canônica DT-10:** escrever a aceitação
   governada do motivo de **não-vínculo inferior** usando razão de
   publicação operacional distinta do motivo
   `LIMIAR_INFERIOR_GOLD_TRAIN_EXPERIMENTAL`. A alteração do harness
   descartável **não** pode ser transportada à procedure operacional
   por simples remoção das guardas de LocalDB. Definir flag
   **desabilitada por padrão**, contrato de modelo/versão e
   autorização DT-15, versão de política, rollback, idempotência,
   precedência determinística, fail-closed, e testes SQL positivos e
   negativos executados em instância descartável. **Até esse gate, a
   DT-10 canônica não aceita a nova razão.**
3. **TEST final lacrado:** estabelecer previamente coorte inédita,
   protocolo e critérios de risco por pior ordem; fazer uma única
   avaliação cega sob autorização própria, sem refit/retuning nem
   reaproveitamento de seeds 42/43/44 ou 20261025001 como TEST novo.
   O TEST pode **reprovar** a política. Registrar FDR pós-Resolve e
   limites por clusters de identidade-base; não chamar CI verde de
   certificação.
4. **Gate de release v5.00:** somente após TEST e revisões técnicas,
   CI no HEAD exato, build/instalador e verificações de segurança,
   obter decisão institucional de publicação. A mera presença de
   migration ou artefato não ativa V9/DT-15, não substitui a V8 ativa,
   não altera Gold operacional e não sela a release.
5. **Piloto real autorizado e reversível:** em ambiente autorizado,
   executar primeiro em sombra com CPF **oculto do caminho
   probabilístico**; separar gabarito independente e protegido,
   auditoria e revisão humana antes de qualquer vínculo persistente.
   Medir erros e representatividade locais por Gestor/contrato,
   abstenção, homônimos e ordens. Ajustes futuros do Calibrador
   somente por calibração manual aprovada, com novas massas.

## 4. Limites de escopo

Nenhum passo deste plano autoriza acesso ou modificação de
`JornadaLocal`, bases operacionais Gold, `ref` ATIVA,
IBGE original, NODE/NAS, HML/PROD, TEST histórico ou dados reais
sem autorização. A frente Apoio às Secretarias (#1186/#1194)
não é gate técnico da V9; mudanças de credenciais/ingestão
permanecem governadas e não devem bloquear a conclusão do
candidato de Linkage. A [PR #1196](https://github.com/lucianox777/Jornada/pull/1196)
é um verificador de replay em **DRAFT** sem orquestração efetiva
de duas invocações do gerador; não tratá-lo como evidência faltante
que invalida o ensaio externo já concluído.

**Critério de avanço:** para cada etapa, evidência concreta,
SHA e CI do HEAD, estado TEST/DT-15, decisão e rollback explícitos;
não promover só porque o status de CI está verde.
