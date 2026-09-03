# RELEASE_EVIDENCE — pacote auditável de promoção

A v3.76 preserva e amplia as evidências produzidas pelos jobs de CI em um artefato único `RELEASE_EVIDENCE.json`.

A política está em `config/release/release-evidence-policy.json`. A promoção por tag exige evidência de locks NuGet, Unit, Integration, Fault Injection, SBOM, linkage evaluation, upgrade DDL, E2E, restore Bronze, escala e proveniência Git. O gate valida os sinais mínimos de PASS/OK de cada artefato, calcula SHA-256 individual e um hash combinado ordenado.

O pacote agregado **não substitui** os gates especializados. Ele prova que o conjunto completo de evidências que passou por esses gates é o mesmo conjunto promovido e retido para auditoria.

## Envelope v3.76

A consolidação não hasheia apenas os `summary.json`: inventaria **todos os arquivos** de cada artefato obrigatório (incluindo TRX, logs e relatórios auxiliares), além de ancorar o SHA-256 de `RELEASE_INFO.txt` e da própria política `release-evidence-policy.json` em `releaseEvidenceEnvelopeSha256`.

A promoção também materializa `release-static-gates`, com evidência dos gates OpenAPI, fechamento técnico, Power BI, HML, Possibilidades, autorização, minimização, compatibilidade e observabilidade, e uma cópia hash-addressed dos contratos HML/Possibilidades usados naquela tag. Assim o pacote de evidências não depende de leitura posterior do log do workflow para provar quais gates estáticos foram executados.

A política V4 do envelope também inclui `environment-preflight`, `governance-readiness`, `scheduler-contract` e os contratos HML de SQL/API. Esses itens provam a presença/coerência dos mecanismos; aprovação externa permanece responsabilidade do `hml-readiness` estrito.


## Hardening v3.76

A política V4 acrescenta ao conjunto promovido a evidência de build determinístico, cobertura em formato Cobertura, auditoria NuGet estruturada e análise CodeQL/SARIF. Na promoção por tag, o workflow produz também attestation OIDC/Sigstore para `RELEASE_EVIDENCE.json`, bundle fonte, `SOURCE_PROVENANCE.json` e SBOM. A attestation só existe quando o workflow real é executado; nenhum arquivo de attestation é fabricado no pacote montado sem GitHub Actions.
