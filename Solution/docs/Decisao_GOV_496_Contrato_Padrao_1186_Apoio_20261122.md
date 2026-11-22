# GOV-ING-496-1186 — Contrato padrão de ingestão e separação da entrega operacional (22/11/2026)

**Origem:** avaliação e concordância do Product Owner na auditoria da issue [#496](https://github.com/lucianox777/Jornada/issues/496); desdobramento [#1186](https://github.com/lucianox777/Jornada/issues/1186). **GOV-DOC-01.**

## Decisão e alcance

1. O contrato de recepção é o **Contrato Padrão de Ingestão da Jornada — Envelope Canônico v2**, compartilhado por todos os Gestores; **não** é contrato proprietário da SEHAB. A rota única é `POST /api/v1/ingestao/entregas`, com acompanhamento por `GET /api/v1/ingestao/entregas/{entregaId}`. Não criar endpoint particular para SEHAB/AA01.
2. O cenário **SEHAB / benefício AA01** permanece como caso de teste e referência factual de contrato específico, sem renomear o Gestor nem o código AA01. O ZIP nativo original da SEHAB **não** é o envelope v2 e sua ingestão direta não é requisito. A caracterização do snapshot original é distinta dos testes E2E **sintéticos** documentados.
3. A **#496** tem núcleo técnico de contrato, segregação e testes históricos atendido (PR #541, gate 6). Recomenda-se encerrar a #496 como concluída **após rastrear expressamente** as pendências de distribuição na #1186 e integrar esta documentação. A #1186 permanece **aberta** para demonstrar build/instalação do executável Windows, imagem Docker, CI no HEAD, testes operacionais e disponibilidade do aplicativo independente.
4. Não exigir teste com dados reais como condição adicional para reconhecer a implementação do contrato padrão ou o E2E sintético. Homologação de mapeamento real e autorização institucional são gates separados, sem autorização implícita de HML/PROD.

## Evidências verificadas

- `Solution/src/Jornada.Api/IngestionApi.cs`: POST genérico e GET de estado, autorização por escopo, recibo 202 e idempotência.
- `Solution/src/Jornada.Ingestion/IngestionPackageInspector.cs`: versão v2, ZIP com exatamente `manifest.json`, `pessoas.jsonl`, `registros.jsonl`, limites, nome canônico e SHA-256.
- `Solution/tests/Jornada.Tests/Unit/IngestionContractTests.cs`: testes com `SEHAB`/`AA01` e cenários genéricos positivos/negativos.
- `Solution/docs/Manual_Integracao_Gestor.md`: contrato comum e exemplo SEHAB/AA01; `Solution/docs/Gate_06_Segregacao_SEHAB_Evidencias.md`: PR #541, CI e E2E sintético histórico.
- `Produtos/ApoioSecretarias/README.md`: protótipo V0.3, com lacunas de build Windows/Docker e execução remota explicitadas; issue #1186 fixa os gates de entrega.

## Limites, impactos e precedência

**Implementação em código e E2E histórico não comprovam implantação online atual do endpoint**; isso não cria uma rota nova por Gestor. Não afirmar que o ZIP real foi ingerido, nem que o protótipo foi distribuído/homologado. A decisão esclarece o escopo da #496 e **não revoga** requisitos específicos da #1186, de segurança, de autenticação ou de Ensaio/HML. O gate 6 histórico permanece fonte de evidência, não de implantação atual.

**Rastreabilidade:** #496 → #1186; PR #541; `Manual_Integracao_Gestor.md`; `Gate_06_Segregacao_SEHAB_Evidencias.md`; índice de decisões vigentes. A decisão de fechamento da #496 será registrada no próprio ticket quando a documentação estiver integrada.
