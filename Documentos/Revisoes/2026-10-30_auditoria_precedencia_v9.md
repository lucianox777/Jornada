# Auditoria documental V9 — precedência e pendências de 30/10/2026

**Natureza:** nota de auditoria técnica, não normativa. **Base consultada:** `master` em 30/10/2026; revalidar SHA e gates antes de integrar. **Não certifica** implementação, homologação Fabric, DT-15 ou publicação v5.00.

## Fonte de decisão e precedência

- **Contrato candidato unificado:** `Documentos/Especificacao_Tecnica_Jornada_v5.00_Visao_Unificada_SQL_Fabric_Alternativo_2026-10-29.md`. Declara-se candidata à aprovação, não publicada. Separa DECIDIDO, IMPLEMENTADO, MEDIDO, AVALIADO e ATIVADO.
- **Histórico:** `Documentos/Especificacao_Tecnica_Jornada_v5.00_Candidata.md` contém aviso expresso de superação, em particular quanto a Fabric, à escolha de parâmetros e à governança V9.
- **Estado de engenharia histórico:** `Documentos/Estado_Engenharia_v5.00_Candidato.md` ainda afirma que SQL Server é o único runtime relacional e que Fabric SQL Database não é alvo operacional. Essas frases descrevem a candidata de setembro e **não podem ser interpretadas como veto atual** à alternativa de hospedagem definida na visão unificada.
- **Índice histórico:** `Documentos/README.md` ainda orienta a leitura da candidata antiga como entrada principal. Não confundir índice de releases com precedência da decisão técnica mais recente. A especificação publicada v3.62 e a release selada permanecem distintas da candidata unificada.

## Matriz de reconciliação (não confundir decisão com execução)

| Tema | Decisão documental corrente | Evidência necessária para marcar IMPLEMENTADO/MEDIDO |
| --- | --- | --- |
| Hospedagem HML/Produção | SQL Server institucional **ou** SQL Database in Microsoft Fabric; única autoridade transacional SQL por implantação | Homologação do DDL, permissões, transações e gates no ambiente escolhido; não inferir paridade de um texto |
| Calibrador V9 | Solicitação, seleção automática de parâmetros e autorização de uso são três atos separados | Código único, snapshots imutáveis, testes físicos e autorização explícita; busca exploratória não ativa modelo |
| TF | Peso zero é baseline; pesos positivos exigem U condicionado ao frame e referência nominal versionada | TRAIN sem vazamento, mesmo blocking/candidatos, comparação dos seis pesos; TEST exposto não seleciona |
| Política inferior DT-10 | Não ativar por decisão técnica isolada; manter default-off e rollback governado | Prova física, matriz DT-15, checks do SHA exato e ato de autorização |
| Gold incremental | Comparar três ordens determinísticas com IDs estáveis e B-cubed convencional/com cobertura | Execução física com corpus idêntico, métricas de criação, fusão, fragmentação e pendências |
| Estatística | FDR de vínculos aceitos separada da criação de novas identidades; dependência por identidade-base | Inferência com agrupamento por identidade, intervalos/pressupostos e testes independentes |

## Integração observada em 30/10 (estado transitório; reconsultar)

- PR #1199 (`feat/dt10-v9-governed-lower-policy-20261026`): **aberta**, mergeable segundo API; não significa gates aprovados.
- PR #1215 (`integration/v9-soft-merge-20261030`): **aberta**, mergeable segundo API; validar checks/SHAs antes de merge.
- PR #1224 (`experiment/v9-tf-30k-sensitivity-20261030`): **aberta**; preflight e teste contratual pequeno não constituem resultado TF 30k.
- Action #41153351132: job físico 30k ainda em calibração TRAIN no momento da consulta. Não usar tempo de checks rápidos como estimativa do ensaio físico.

## Ações documentais propostas

1. Atualizar a porta de entrada do índice `Documentos/README.md` com remissão explícita à visão unificada, preservando a distinção entre especificação candidata e publicada.
2. Acrescentar aviso de documento histórico ao estado de engenharia de setembro, sem apagar seu conteúdo ou alterar `RELEASE_INFO.txt`.
3. Conferir afirmações IMPLEMENTADO/MEDIDO no capítulo 26 da visão unificada contra HEAD, PRs e evidências físicas antes de qualquer promoção de estado.
4. Manter a documentação de TF em branch separada das frentes DT-10 e integração; não disparar nova calibração física por simples ajuste documental.
