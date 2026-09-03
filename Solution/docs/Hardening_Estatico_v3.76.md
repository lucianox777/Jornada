# Hardening estático — engenharia v3.76

A v3.76 não altera a Base normativa v3.62 nem o schema persistido 3.68. Ela fecha a camada local de compatibilidade, supply chain e reprodutibilidade antes da primeira execução integral do pipeline.

## Gates adicionados

- `contract-backward-compatibility-gate.py`: compara OpenAPI e JSON Schemas contra a tag predecessora e falha fechado em remoções/estreitamentos incompatíveis.
- `ddl-destructive-change-gate.py`: detecta mudanças DDL potencialmente destrutivas; exceções exigem hash da linha, justificativa e evidência de migração.
- `dependency-drift-gate.py`: governa adições/remoções/major upgrades de PackageReference.
- `source-sanity-gate.py` e `workflow-action-pin-gate.py`: impedem segredo/chave privada/TLS SQL inseguro fora de Development e exigem actions externas por SHA completo.
- `coverage-evidence-gate.py`: registra Cobertura XML e aplica ratchet assim que o baseline for aprovado.
- `deterministic-build-gate.sh`: compila duas vezes e compara SHA-256 de DLL/PDB/deps/runtimeconfig.
- `vulnerability-evidence-gate.py` e `sarif-security-gate.py`: bloqueiam vulnerabilidades NuGet High/Critical e findings CodeQL bloqueantes.

## Supply chain

A promoção de tag depende dos jobs de build determinístico e segurança. O envelope final, bundle de fonte, proveniência e SBOM são atestados por `actions/attest` usando identidade OIDC/Sigstore do GitHub Actions. Nenhuma chave privada de assinatura é persistida no repositório.

## Evidência ainda externa

O pacote estático apenas configura esses controles. Cobertura real, CodeQL, builds duplos, restore NuGet bloqueado e attestation só viram evidência quando o workflow de CI for efetivamente executado.
