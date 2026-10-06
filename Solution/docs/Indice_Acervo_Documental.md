> **ATUALIZAÇÃO 29/09/2026:** para decisão de identidade/linkage, começar pelo [índice vigente](Indice_Decisoes_Vigentes.md) e [decisões canônicas](Decisoes_Canonicas_Identidade_Linkage_20260929.md). O inventário anterior de 28/09 é fotografia histórica; o [catálogo de vigência](Catalogo_Vigencia_Documental_20260929.md) evita aplicar snapshots como norma.


> **Organização do monorepo (06/10/2026):** a antiga solução embarcada `Solution/ApoioSecretarias/` foi removida do repositório principal. Referências abaixo a esse caminho descrevem evidência/histórico anterior à remoção. O produto não compila nem distribui esse transmissor/preparador; apenas schemas SEHAB estritamente sintéticos necessários à regressão permanecem em `Solution/tests/fixtures/external-contracts/gestores/SEHAB/`.
# Índice do acervo — precedência e leitura mínima

**Conferência:** 28/09/2026 (`master` até o PR #587). Nenhum documento histórico foi apagado ou declarado revogado por sua versão. Para mudanças posteriores, o HEAD, o CI e as issues prevalecem sobre esta fotografia; este índice organiza a consulta, não substitui auditoria individual dos arquivos.

| Pergunta | Começar por | Consultar depois, se necessário |
|---|---|---|
| O que foi publicado? | `../../RELEASE_INFO.txt` e `../../Documentos/README.md` | Estados de engenharia e evidências históricas |
| O que existe e falta? | [Estado atual](Estado_Atual_Projeto.md) **e código/Actions** | PRs e issues |
| Quais decisões de produto? | [Diretrizes consolidadas](Diretrizes_Identidade_Progressiva_Apoio_Decisao.md) | [Núcleo](Nucleo_Linkage_Identidade_Progressiva.md), [Gold](Gold_Pessoa_Universo_CPF.md) |
| O que desenvolver? | [Plano de desenvolvimento](Plano_Desenvolvimento.md) — prioridades e dependências | [Dívidas técnicas](Dividas_Tecnicas.md) — critérios verificáveis; issues/PRs — execução e evidências |
| Como separar as ferramentas das Secretarias? | [Gate 6 — segregação SEHAB](Gate_06_Segregacao_SEHAB_Evidencias.md), [Plano](Plano_Desenvolvimento.md) | Separação técnica concluída e solução independente `Solution/ApoioSecretarias/SolucaoApoioSecretarias.sln`; aceite institucional, credenciais e prontidão HML permanecem pendentes |
| Como executar Ensaio e HML? | [Ensaio único](Ensaio_Unico_Paridade_HML.md), [testes](Testes_Operacao_Indice.md) | `Ensaio_Progressivo.md`, `Ensaio_Secretarias_Paridade_HML.md`, runbooks |
| Quais normas institucionais? | `../../Documentos/README.md` | Especificação v3.62 publicada, v5.00 candidata e requisitos v1.1 |

## Catálogo por assunto

- **Identidade e Gold:** `Identidade_*.md`, `Identity_*.md`, `Gold_Pessoa_Universo_CPF.md`, `Arquitetura_Identidade_Linkage.md`.
- **Linkage, calibração e diagnósticos:** `Linkage_*.md`, `Calibrador_*.md`, `Estudo_Comparativo_Linkage_Identidade_Progressiva.md`, `Diagnostico_Linkage_Isolamento_DEV.md`.
- **Ensaios e testes:** `Ensaio_*.md`, `Runbook_Testes_Tecnicos.md`, `Aceite_OpenAPI_BlackBox.md`.
- **Operação, HML, segurança e release:** `Runbook_*.md`, `HML_*.md`, `Governanca_*.md`, `Release_Evidence.md`.
- **Norma e histórico institucional:** `../../Documentos/README.md`, `../../Documentos/Requisitos/`, `../../Documentos/Estado_Engenharia_v*.md`.

**Regra de precedência:** `Estado_Atual_Projeto.md` é fotografia datada, não status em tempo real; confrontar com `master`, Actions e issues. Notas `NOTA_ENGENHARIA_v*`, estados de engenharia e releases anteriores permanecem históricas, sem autoridade sobre o código candidato corrente. Não copiar conclusões de CI sintética para HML/Produção.

**Regra de manutenção:** decisão nova nas Diretrizes; estado comprovado no Estado atual; prioridade e dependência no Plano; critério técnico na tabela de Dívidas, com ID estável; execução nas issues/PRs; procedimentos nos runbooks; evidências junto à execução. Não duplicar decisões em cada documento técnico. **Existe um único Ensaio**, técnico e operacional, completo antes de HML; HML muda a massa para a preparada pelas Secretarias, preservando características reais relevantes, não os contratos. Não tratar `Ensaio_Progressivo.md` nem `Ensaio_Secretarias_Paridade_HML.md` como fases separadas.
