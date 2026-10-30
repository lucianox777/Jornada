# Jornada — agente auxiliar de correção de código (30/10/2026)

**Estado:** política de segurança decidida; implementação proposta na [PR #1218](https://github.com/lucianox777/Jornada/pull/1218), sujeita a merge, CI e verificação no SHA exato. O documento registra o contrato de engenharia, não a aprovação/ativação de V9, DT-10 ou DT-15.

## 1. Finalidade e distinções

O agente da Action `.github/workflows/jornada-coding-agent.yml` pode sugerir uma correção localizada de C# para avaliação humana. É um assistente **diagnóstico**, não é o Calibrador adaptativo V9, não opera a resolução de identidade e **não** altera dados, Gold, políticas SQL, modelos ativos nem código versionado automaticamente.

**Correção automática efetuada:** NÃO. A automação limita-se a analisar contexto e a construir uma **proposta** (`proposed-correction.patch`). A decisão de aplicar, testar e integrar depende de PR revisada e gates ordinários. A execução de `git apply --check` é um dry run de aplicabilidade, não aplicação, compilação, teste de comportamento ou aprovação.

## 2. Âmbito autorizado

- Gatilho exclusivamente manual (`workflow_dispatch`), com opção `front=v9`. Não há agenda de autorreparo nem atuação no DT-10.
- Origem da leitura: branch `integration/v9-soft-merge-20261030` e PR #1215. **PR #1199 e sua branch DT-10 estão excluídas** do escopo do agente.
- Contexto limitado a até quatro arquivos `.cs` alterados na PR, dentro de `Solution/src/` ou `Solution/tests/`, cada um com menos de 24 KiB; texto de contexto truncado a 16.000 caracteres por arquivo.
- Inferência por GitHub Models, usando `GITHUB_TOKEN` limitado ao necessário para leitura (`contents: read`, `pull-requests: read`, `checks: read`, `models: read`). O checkout utiliza `persist-credentials: false`.
- A sugestão só é aceita para **um arquivo da lista** e exige `old` não vazio, ocorrência única no conteúdo, `new` não vazio/diferente e até 10.000 caracteres. Patch emitido em formato unified diff.
- O processo **não escreve fonte ou teste no workspace**: compõe a substituição em memória, gera o arquivo de patch para artefato da Action e verifica sua aplicabilidade usando `git apply --check` sem aplicar.
- Sem `git add`, `git commit`, `git push`, `git apply` efetivo, publicação de branch, edição automática de PR ou merge. Alteração inesperada em `Solution/src` ou `Solution/tests` faz o job falhar.

Estas guardas de execução não são sandbox de segurança contra conteúdo malicioso no próprio código enviado ao provedor ou contra mudanças no workflow: exigir revisão das permissões, procedência e política institucional de envio de código ao serviço de IA antes de executar em dados não públicos.

## 3. Fluxo operacional permitido

1. Pessoa autorizada aciona `Jornada coding agent - GitHub Models diagnostic` com `front=v9` no GitHub Actions.
2. A Action lê os arquivos elegíveis; se não houver contexto/correção, encerra sem alteração e pode não produzir patch.
3. GitHub Models devolve uma única sugestão estruturada; a Action valida o formato, alvo e trecho exato, produz `proposed-correction.patch` e executa apenas `git apply --check`.
4. Se houver patch, a Action publica o artefato `v9-proposed-correction`; o SHA-256 do arquivo é registrado no log. **A existência do artefato não demonstra que a sugestão esteja correta.**
5. Uma pessoa revisa a pertinência, segurança, código, contrato, evidência e testes exigidos. Somente depois, mediante mudança consciente em uma nova branch/PR, a correção pode ser aplicada para revisão.
6. A PR de correção passa por CI, validação independente quando aplicável e gates técnicos do SHA final. Requer revisão humana e proteções de `master` antes de merge. Nunca autoaprovar, autosquash ou pular gates.
7. A promoção de modelo V9, autorização DT-15, DT-10, política SQL e release seguem seus fluxos **independentes**. Não usar o agente como prova de autorização.

## 4. Gates e resultado esperado

| Situação | Evidência exigida | Consequência |
| --- | --- | --- |
| Token de inferência sem acesso ou resposta inválida | Erro HTTP/parsing e log da Action | Falha fechada; sem patch aplicado |
| Não há arquivos elegíveis ou modelo se abstém | Log de ausência de sugestão | Nenhuma alteração nem autorização |
| Patch incorreto, trecho não único, diff inaplicável | Rejeição da entrada ou `git apply --check` falha | Sem mudança em código; job pode falhar |
| Patch válido sintaticamente | `proposed-correction.patch` e SHA-256 no log | Apenas proposta pendente de revisão humana |
| Necessidade de integrar correção | PR específica, aprovação, CI/testes do HEAD exato | Aplicar somente pela trilha normal de desenvolvimento |
| Exigência de atuar em #1199 | Fora da matriz; requer trabalho humano independente | Agente não opera DT-10 |

A execução em andamento do probe físico 30k e a situação de #1215/#1199 devem ser conferidas separadamente; sucessos de CI históricos não certificam novo HEAD nem habilitam V9.

## 5. Tratamento de incidentes, auditoria e rollback

Se a Action tentar escrever fonte, fazer push ou alterar branch, interromper o uso e inspecionar logs, permissões do token, diff do workflow e histórico Git. A reversão é feita por PR ou desabilitação administrativa do workflow, **não por modificações em Gold/REF/produção**. Registrar commit e SHA do workflow, ID e resultado do run, PR de origem, digest do patch, decisão da pessoa revisora e PR corretiva vinculada (se existir).

Controlar acesso aos artefatos GitHub Actions conforme a política institucional de retenção e informação: um patch pode revelar trechos de código sensíveis. Nenhum segredo ou dado pessoal deve ser enviado como contexto do modelo.

## 6. Integração com a Especificação v5.00

O item 28 da matriz técnica da [visão unificada v5.00](../../Documentos/Especificacao_Tecnica_Jornada_v5.00_Visao_Unificada_SQL_Fabric_Alternativo_2026-10-29.md) foi retificado para o **blocking multi-passe descoberto pelo Calibrador**, com avaliação de recall, cobertura, custo e taxas de ausência/falsos negativos por estrato. A antiga exigência de homologar previamente **blocking exato por data de nascimento** contradizia essa política.

As decisões de `calibração solicitada pelo operador`, `seleção automática pelo Calibrador` e `autorização de uso pela autoridade competente` permanecem **três atos separados**. O workflow de sugestão de patches **não participa** de nenhum dos três. A alternativa SQL Database in Microsoft Fabric para HML/Produção é opção de **hospedagem** da autoridade transacional única, independente deste agente e sujeita a homologação real.

**Integridade documental:** a edição do Markdown candidato não altera o DOCX de origem na Biblioteca nem a especificação publicada v3.62. A consolidação DOCX/PDF posterior deve incorporar as retificações de forma explícita e aprovada.

## 7. Estado de comprovação na elaboração

- **#1218:** workflow em proposta, sem autoescrita após o commit de endurecimento; ainda requer CI e eventual merge.
- **#1215:** frente V9 independente; gates de calibração/TEST/integração continuam próprios.
- **#1199:** frente DT-10 independente; o agente não atua nela; autorização positiva governada e digest permanecem gates antes de qualquer promoção.
- **Documento:** adendo candidato versionado, não publicação institucional.

O estado mais recente deve sempre ser confirmado por PR, SHA exato e GitHub Actions; este corte datado não é uma monitoria contínua.
