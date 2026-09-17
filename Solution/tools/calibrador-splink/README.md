# Calibrador Splink — runner de referência

Este diretório contém o executor Python usado **somente em desenvolvimento/calibração** para produzir estimativas de referência com Splink. Ele não é dependência do runtime operacional da Jornada e não ativa nem substitui automaticamente o modelo SQL Server.

## Contratos

Entrada: `JORNADA_SPLINK_EXCHANGE_V1`, produzida por `SplinkCalibrationExchange` a partir de uma partição do benchmark versionado.

Saída: `JORNADA_SPLINK_ESTIMATES_V1`, validada em C# antes de virar `ParameterEstimate`.

A versão inicial do runner tem escopo **NOME** e replica a semântica vigente de `IdentityComparison.CompareName`:

- `EXACT`: igualdade após a normalização feita antes do benchmark;
- `HIGH`: Jaro-Winkler `>= 0.92`;
- `MEDIUM`: Jaro-Winkler `>= 0.80` e `< 0.92`;
- `LOW`: restante.

Os thresholds não são argumentos do runner V1 porque alterá-los quebraria a equivalência semântica necessária para comparar `M_SPLINK/U_SPLINK` com as estimativas Jornada.

## Estimação de m e u

`m` é estimado apenas com labels positivos (`clerical_match_score = 1`) por `estimate_m_from_pairwise_labels`.

`u` é estimado por `estimate_u_using_random_sampling` com seed explícita. Para preservar a hipótese de pares entre identidades distintas, o runner reduz a população de amostragem para **uma observação canônica por `base_person_id`**. As observações derivadas/perturbadas continuam disponíveis para estimar `m`, mas não duplicam artificialmente a população usada em `u`.

Quando o pacote de entrada for produzido a partir do benchmark nominal IBGE, a distribuição e o fingerprint da referência viajam no contrato de entrada/saída. O runner não consulta o IBGE em rede, não inventa frequência ausente e não transforma o benchmark sintético em evidência de produção.

A composição `first_name + surname` é a representação nominal sintética usada para tornar o experimento comparável ao estado `NOME` do scorer. Ela **não afirma** que o produto Nomes no Brasil publique distribuição conjunta de nomes completos. Qualquer população-base construída a partir das marginais IBGE continua sendo um bootstrap sintético, com essa limitação registrada.

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

O `max_pairs` metodológico não é escolhido pelo script. O valor faz parte da especificação da execução e da evidência publicada pelo Calibrador.

## Limite do V1

O runner V1 produz apenas `M_NOME_*` e `U_NOME_*`. Ele serve para provar a ponte, comparar a estimação nominal Jornada/Splink e alimentar experimentos fatoriais nominais. **Não constitui sozinho um conjunto completo de parâmetros promovível para o scorer operacional**, que também possui nascimento, nome da mãe, prior, thresholds e política de blocking.

A promoção continua condicionada à #31: corpus de avaliação separado, negativos/impostores, fronteira de threshold/margem exercitada, recall de blocking e validação estatística representativa.
