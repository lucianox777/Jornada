# Política de registro obrigatório das decisões de discussão — Jornada

**ID:** GOV-DOC-01  
**Data da decisão do PO:** 2026-11-07  
**Estado:** DECIDIDO — regra de processo documental; não é autorização de implantação, ativação nem publicação normativa de produto.  
**Autoridade:** Product Owner (PO).  
**Âmbito:** Todo o Projeto Jornada, incluindo discussões em chats, reuniões, comentários de PR/issue e decisões adotadas ao revisar alternativas técnicas.

## 1. Regra

**Toda decisão efetivamente tomada ou confirmada nas discussões do projeto deve ser registrada nos documentos versionados do GitHub.** Não basta que tenha aparecido em uma conversa, issue, PR, commit ou resposta de agente. O registro documental deve ser feito no ciclo em que a decisão foi tomada, preferencialmente na mesma PR da implementação, e sempre **antes de declarar a atividade concluída**.

A obrigação abrange decisões de produto/requisitos, arquitetura, identidade/linkage, calibrador e parâmetros, hipóteses estatísticas, dados/segurança, CI/gates, operação, testes, documentação, priorização, implantação e governança. Decisão sem código exige uma alteração documental própria.

## 2. Onde registrar

1. Atualizar primeiro **o documento temático canônico** aplicável (ou criar um documento de decisão em `Solution/docs/` quando nenhum existir), mantendo a terminologia e a precedência do acervo. Quando a matéria for de especificação de produto, também atualizar a candidata pertinente em `Documentos/` **se cabível**, sem declarar publicação formal automática.
2. Atualizar `Solution/docs/Indice_Decisoes_Vigentes.md` quando a decisão for nova, alterar diretriz, prevalecer sobre registro anterior ou mudar vigência. Vincular o documento temático.
3. Atualizar `Solution/docs/Indice_Acervo_Documental.md` quando for necessário expor nova fonte canônica ou rota de leitura. Descrições de PR/issue podem dar rastreabilidade, **não substituir** esses registros.
4. Manter `AGENTS.md` e `.github/copilot-instructions.md` como apontadores da política para agentes. Não duplicar toda a especificação nesses arquivos.

## 3. Conteúdo mínimo de cada decisão

- **ID ou título e data**, fonte/autoridade que decidiu (por exemplo, PO), e enunciado inequívoco do que foi aprovado;
- **Contexto, motivo e alternativas relevantes** — distinguir as rejeitadas das que continuam abertas, sem inventar fundamento;
- **Alcance/impacto e precedência**, incluindo qual texto anterior é substituído, complementado ou permanece histórico;
- **Estado real:** DECIDIDO, IMPLEMENTADO, MEDIDO, VALIDADO e/ou ATIVADO, claramente separados e sustentados por evidência; nunca inferir validação ou ativação por documentação ou merge;
- **Rastreabilidade:** links para issues, PRs, commits, gates, relatórios ou demais evidências quando disponíveis, e pendências identificadas.

Uma decisão **não** precisa ter código implementado para ser documentada. Se a posição do PO não estiver clara, registrar como **PROPOSTA / PENDENTE DE DECISÃO** e não inventar autorização.

## 4. Critério de conclusão e verificação

Em cada entrega decorrente de decisão, conferir:
- A decisão está no documento temático correto e, quando necessário, nos índices e especificações candidatas?
- O registro distingue decisão, implementação, medição, validação e ativação?
- A PR identifica os arquivos atualizados e eventuais decisões ainda não registradas?
- Foram preservados os registros históricos e os limites da aprovação do PO?

**Não declarar “documentação concluída” ou “decisão incorporada ao projeto” sem commit verificável no GitHub.** Se não houver acesso de escrita ou o merge estiver pendente, informar o bloqueio e apresentar o texto proposto, sem fingir persistência. Não contornar gates da PR.

## 5. Precedência e salvaguardas

Esta política disciplina o **registro**, não altera por si só os requisitos ou a autoridade de aprovação. Decisões novas do PO com precedência expressa devem ser registradas sem apagar a história. `RELEASE_INFO.txt`, especificações publicadas e releases seladas não são reescritas retroativamente. Referências a estado de implementação exigem conferir a `master`/SHA, Actions e PRs; comunicação em chat não comprova execução.

**Origem da regra:** orientação expressa do PO em discussão de 07/11/2026: todas as decisões tomadas nas discussões precisam ficar documentadas nos documentos do projeto no GitHub.
