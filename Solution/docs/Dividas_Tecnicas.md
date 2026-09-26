# Dívidas técnicas — Jornada do Cidadão

**Revisão:** 2026-09-26 · **Natureza:** backlog técnico candidato, não normativo · **Base:** master `7d03e1bc040dd76d9c848a7267fdf84c1048c9d8` e avaliação das 12 ações propostas.  
**Regra de leitura:** este documento complementa as issues abertas; não substitui `Documentos/Anexo_Pendencias_Desenvolvimento_Jornada_v1.47` (snapshot histórico), não declara conclusão de tarefas nem aprovação institucional. Quantidades da proposta inicial são inventário a reconfirmar no HEAD antes de executar.

## Ordem proposta e critérios de aceite

| ID | Prioridade | Dívida / ação | Critério verificável de aceite | Situação |
|---|---|---|---|---|
| DT-01 | Imediata | Congelar `config/linkage/implementation-conference-tolerance.json` | Tolerância numérica definida **antes** de conferir resultados governados, justificada por análise independente, versionada e testada em cenários conformes/divergentes; decisão final exige equivalência exata. Nunca derivar a tolerância do primeiro resultado nem relaxá-la para passar no gate. | Pendente de definição técnica |
| DT-09 | Imediata | Unificar parâmetros e asserts de `VALIDATE`/`ACTIVATE` | Fonte única dos parâmetros obrigatórios; os dois gates conferem a mesma versão de tolerância, fingerprint do snapshot, evidência mais recente e estado `CONFORME`; mutação posterior e evidência divergente bloqueiam promoção. | Verificar duplicação e wiring no HEAD |
| DT-05 | Imediata | Gravar `identidade.linkage_resultado` somente quando muda | Definir assinatura semântica da decisão; reprocessamento idêntico não duplica resultado operacional; mudança de decisão ou de evidência relevante tem rastreabilidade; runs/modelos e histórico permanecem auditáveis; ensaio com três ondas e CPF tardio. | Implementação a verificar |
| DT-04 | Alta | Centralizar `X-Jornada-Access-Key` em `AuthenticationHandler` e policies | Remover parsing manual redundante, preservar escopos/credencial/auditoria, testar 401/403 e regressão DEV; HML/Produção continuam deny-by-default até identidade corporativa (#378). | Inventariar ocorrências |
| DT-10 | Alta | Publicação set-based | Substituir cursor quando demonstrada equivalência funcional; testes de concorrência, idempotência, conflitos, rollback, plano de execução e volumetria. | Inventariar caminho atual |
| DT-02 | Alta | Migrar solução para .NET 10 LTS e atualizar SqlClient | Inventariar projetos e dependências; CI, restore, build sem warnings, testes SQL/E2E, containers e deployment reproduzíveis; PR isolado e plano de rollback. | Quantidades 17/13 não revalidadas |
| DT-07 | Alta | Corrigir drift documental | Conferir `SECURITY.md`/Gitleaks, SQL citado na Especificação §18 e `LEIA-ME.txt` contra implementação e workflows; preservar distinção entre release selada, candidata e documentos históricos. | Auditoria pontual necessária |
| DT-03 | Média | Tipar respostas OpenAPI | Inventariar operações sem schema; declarar respostas de sucesso e erro, gerar spec e testes de contrato/compatibilidade. | Quantidade 20/21 não revalidada |
| DT-08 | Média | Mover arquivos do Linkage.Core para o projeto | Remover links físicos frágeis, manter fronteiras de dependência e executar testes de build/empacotamento. | Verificar estrutura atual |
| DT-11 | Média | Consolidar execução dos scripts PowerShell/Bash | Runner/orquestrador único com comandos claros, preservando scripts especializados; manter isolamento de `JornadaLocal`, `JornadaE2E` e `JornadaSyntheticDev`, sem reset implícito. | Quantidades 52/36 não revalidadas |
| DT-06 | Posterior | Baseline das migrations | Criar baseline para instalações **novas**, com upgrade testado a partir de bancos existentes; não reescrever/apagar histórico aplicado; backup e rollback documentados. | Quantidade 50 não revalidada |
| DT-12 | Posterior | Higiene de branches e notas | Inventariar referências, PRs, tags, proteção, links e evidências; arquivar notas históricas antes de remover; nunca apagar branches ativos ou referências necessárias à auditoria. | Quantidades 413/24 não revalidadas |
| DT-13 | Transversal | Matriz obrigatória de regressão | Cobrir identidade progressiva, CPF tardio, homônimos, conflito, reprocessamento, ledger, idempotência, concorrência, segurança e três ondas em banco sintético isolado; anexar evidências aos PRs. | Nova ação proposta |

## Fases de entrega

1. **Integridade operacional:** DT-01, DT-09 e DT-05. Não tratar a conferência de implementação como validação estatística representativa (#31).
2. **Segurança e publicação:** DT-04 e DT-10, com regressão de autorização e equivalência transacional.
3. **Modernização isolada:** DT-02, DT-03 e DT-08, preferencialmente em PRs separados.
4. **Consolidação:** DT-06, DT-07, DT-11 e DT-12; documentação DT-07 deve também acompanhar mudanças anteriores.
5. **Transversal:** DT-13 é critério de aceite de todas as fases, não uma atividade a adiar até o final.

## Restrições e evidências

- O arquivo de tolerância no master consultado permanece `UNFROZEN_REQUIRED_BEFORE_FIRST_EXECUTION`, `maxAbsolutePairLlrDifference: null` e `toleranceVersion: UNFROZEN`. A conferência atual é independente **somente para scorer/policy sobre estados pré-computados**; não valida comparadores nem o guard-input. Ver `Solution/docs/Linkage_Implementation_Conference.md`.
- Conferir no código a ligação efetiva de `VALIDATE/ACTIVATE`: a documentação da conferência diz que o wiring ainda não está ativo, enquanto a documentação de cluster descreve o fluxo governado completo. Corrigir a divergência documental após a verificação.
- O anexo de pendências v1.47 é histórico. As issues abertas continuam sendo a fonte de acompanhamento operacional; vincular cada DT à issue/PR correspondente antes de iniciar implementação.
- Para testes de três ondas, usar `JornadaSyntheticDev`; preservar `JornadaLocal` e a referência IBGE. Não apresentar recall/precisão temporal como comprovados sem avaliador e evidência adequados.
- Manter `codigoPessoaOrigem` opcional e `initial_uuid` como linhagem, nunca feature estatística.
- Para cada item: registrar SHA-base, owner, issue/PR, testes executados, evidências, riscos, rollback e status real. Nenhum item desta tabela está declarado concluído por mera inclusão neste documento.
