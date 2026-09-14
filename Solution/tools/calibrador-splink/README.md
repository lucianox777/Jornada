# Calibrador Splink — runner de referência

Este diretório contém o executor Python usado **somente em desenvolvimento/calibração** para produzir estimativas de referência com Splink. Ele não é dependência do runtime operacional da Jornada.

## Contratos

Entrada: `JORNADA_SPLINK_EXCHANGE_V1`, produzida por `SplinkCalibrationExchange`.

Saída: `JORNADA_SPLINK_ESTIMATES_V1`, importável pela mesma fronteira C#.

A versão inicial do runner tem escopo **NOME** e replica a semântica vigente de `IdentityComparison.CompareName`:

- `EXACT`: igualdade após a normalização feita antes do benchmark;
- `HIGH`: Jaro-Winkler `>= 0.92`;
- `MEDIUM`: Jaro-Winkler `>= 0.80` e `< 0.92`;
- `LOW`: restante.

Os thresholds não são argumentos do runner V1 porque alterá-los quebraria a equivalência semântica necessária para combinar `M_SPLINK` com `U_JORNADA` (ou o inverso).

## Estimação

`m` é estimado apenas com labels positivos (`clerical_match_score = 1`) por `estimate_m_from_pairwise_labels`.

`u` é estimado por `estimate_u_using_random_sampling` com seed explícita. Para não violar a hipótese de que pares aleatórios sejam predominantemente não-matches, o runner deduplica a população de amostragem para **uma observação canônica por `base_person_id`**. A tabela expandida por pares continua sendo usada para `m`.

## Versão de referência

O ambiente está fixado em `splink==4.0.17`. Atualizar essa versão exige novo teste de referência/paridade e nova versão de evidência se houver mudança semântica.

## Execução

```bash
python3 -m venv .venv
.venv/bin/python -m pip install -r requirements.txt
.venv/bin/python run_calibration.py \
  --input pacote.json \
  --output estimativas.json \
  --seed 20260913 \
  --max-pairs 10000000
```

O `max_pairs` de produção metodológica não é escolhido pelo script. O valor deve fazer parte da especificação da execução e da evidência publicada pelo Calibrador.

## Limite do V1

O runner V1 produz apenas `M_NOME_*` e `U_NOME_*`. Ele serve para provar a ponte, comparar a estimação nominal Jornada/Splink e alimentar experimentos fatoriais nominais. **Não constitui sozinho um conjunto completo de parâmetros promovível para o scorer operacional**, que também possui outras evidências como nascimento e nome da mãe.
