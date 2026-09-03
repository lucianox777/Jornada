# Contratos de operabilidade — engenharia v3.74

A v3.74 transforma pendências locais de segurança/operabilidade em contratos executáveis, sem promover como aprovado nenhum valor dependente de HML.

## Autorização e minimização

`config/security/authorization-matrix.json` inventaria as 15 rotas `/api` protegidas. `authorization-matrix-gate.py` compara essa matriz diretamente ao `Program.cs`, incluindo `allowTypeCredentials` e a primeira permission consultada. `data-minimization-policy.json` + `security-surface-gate.py` varrem templates de log de toda a Solution, proíbem identificadores sensíveis e interpolação direta de `Exception.Message`, e preservam projeção de Pessoa dirigida por allowlist/schema.

## Compatibilidade e upgrade

`config/release/compatibility-matrix.json` distingue release de engenharia de versão persistida do schema. `Jornada_Upgrade_Invariants.sql` captura contagens e violações estruturais antes/depois do upgrade v3.65 → v3.68; `upgrade-invariant-gate.py` falha se dados esperados regredirem ou surgirem vínculos/CPFs/Gold estruturalmente inválidos.

## Bronze profundo e GC

`Jornada.Bronze.Verify --deep` reconcilia referências SQL, objeto físico, hash e metadados. `--gc-plan` produz somente um plano `DRY_RUN_ONLY` de objetos sem referência viva e além da grace period. O verificador não possui operação de deleção; qualquer expurgo real continua no worker operacional com reaplicação do applock e rechecagem de referência.

O drill de backup/restore injeta um órfão físico controlado, verifica os casos ausente/corrompido, executa a varredura profunda e exige o plano dry-run sem remover o objeto.

## Observabilidade e calibração

`metrics-slo-catalog.json` define nomes, unidades e dimensões de baixa cardinalidade para dez métricas, além de SLOs e alertas. SLOs/condições permanecem `PENDENTE` até HML.

`calibration-validity-policy.json` define validade máxima e um fingerprint técnico calculado sobre código/DDL/harnesses relevantes. Quando um contrato HML for `APROVADO`, `hml-staleness-gate.py --strict` exige que o fingerprint aprovado ainda coincida com o software atual; uma alteração relevante invalida automaticamente a aprovação anterior.
