# GOV-V9-TEST-CENSO-PARES-DISTINTOS-20261118 — diagnóstico proposto

**Estado:** instrumentação diagnóstica em PR, sujeita a CI e revisão; nenhuma mudança científica aprovada.

## Contexto e evidências
- Execução diagnóstica não científica [#44314407095](https://github.com/lucianox777/Jornada/actions/runs/44314407095/job/134836621276): 170.941 observações, 494.189 assinaturas, cap de 1.000.000 incidências.
- Contagens por passe: P_FIRST_BIRTH_YEAR=902.410; P_LAST_M_PHON=103.612; P_PHON_M_LAST=84.770. Soma 1.090.792 incidências, incluindo potenciais repetições entre passes.
- A admissão conservadora interrompe antes do SQL; não há resultado de TEST científico nem certificação.

## Escopo proposto
- Somente quando o limite conservador falhar, contar pares cross-gestor distintos por chave ordenada de IDs, com HashSet limitado a cap+1; contabilizar por passe os pares novos e as repetições já vistas.
- Publicar exclusivamente contagens agregadas e sinalizador de censo completo/censurado, sem IDs, assinaturas, verdade de TEST ou dados pessoais.
- Preservar integralmente o bloqueio fail-closed por incidências, o vencedor congelado, o cap, o SQL, o protocolo de TEST, o TRAIN e a VALIDATION.
- Censo diagnóstico não autoriza substituição do cap conservador por limite de pares distintos; eventual alteração exige decisão científica separada do PO.

## Impactos, precedência e rastreabilidade
Complementa [GOV-V9 diagnóstico de incidências](Decisao_GOV_V9_Diagnostico_Cap_Incidencias_20261118.md), sem revogar limites vigentes. Aguardar CI, revisão e nova execução manual SHA-pinned para medir sobreposição. GOV-DOC-01.
