# Integração do histórico aplicado — registro histórico e estado atualizado

> **Registro anterior superado em 29/09/2026.** O diagnóstico antigo afirmava que `IdentityCompositionAuthoritativeReader.LoadClosedReadSetAsync` carregava `History` vazio após #52. A implementação atual já consulta `identidade.composicao_historico_aplicado` e fecha a leitura dos membros envolvidos; **não tratar aquela afirmação como tarefa em aberto**. Ver [arquitetura atual](Arquitetura_Identidade_Linkage.md), [decisão canônica DC-ID-02](Decisoes_Canonicas_Identidade_Linkage_20260929.md#dc-id-02--âncora-cpf-e-divisão-literal) e [continuidade publicada](Identidade_Composicao_Historico_Serving.md).

## Escopo preservado do inventário histórico

A persistência do histórico aplicado é distinta dos planos `PREPARADA`; somente atos `APLICADA` e publicação efetivada podem sustentar consultas de continuidade histórica. Hash de conteúdo não autentica autoria. Erros de hash, membros incompletos, replay divergente, versão obsoleta e publicação parcial são falhas fechadas. A prova de execução e cobertura de integração continua dependente da suíte SQL correspondente, não do presente texto.

**Nova pendência, não confundível com o defeito já resolvido:** após a decisão de 29/09/2026, restringir o **planejador de divisão sem CPF** a retirar o UUID antigo de todas as partes, atribuir destinos novos/admissíveis e registrar ambiguidade histórica 1→N, preservando CPF e `initial_uuid`. A eventual nova tabela temporal de sucessões é investigação futura [#613](https://github.com/lucianox777/Jornada/issues/613); não criar tabela agora.
