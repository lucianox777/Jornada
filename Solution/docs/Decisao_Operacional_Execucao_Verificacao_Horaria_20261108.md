# OPS-V9-VERIFICACAO-HORARIA-20261108 — Continuidade assistida e verificação horária

**Decisão do Product Owner:** 08/11/2026 (America/Sao_Paulo).  
**Autoridade:** Product Owner.  
**Estado:** DECIDIDO. Automação externa de acompanhamento horário criada e habilitada em 08/11/2026; execução futura, correções e integração exigem evidência por rodada. Não equivale a processo local permanente nem a monitoramento em tempo real.  
**Política relacionada:** [GOV-DOC-01](Politica_Registro_Decisoes_Discussoes.md).

## Contexto e decisão

O PO solicitou manter a continuidade da implementação do projeto Jornada/V9 com verificação **a cada hora**. A continuidade será executada por rodadas de agente agendadas, com consulta do estado real do repositório `lucianox777/Jornada`, em vez de presumir execução ininterrupta entre verificações. Cada rodada verifica a `master`, SHAs, PRs, conflitos, jobs da CI e regressão física 30k, efetua somente ações seguras e possíveis e fornece um balanço de ações verificáveis e bloqueios.

A prioridade segue os mandatos V9 em vigor (motor multiparâmetro, compatibilidade V2/V3, relatório analítico e documentação), preservando suas dependências e decisões de governança. A verificação periódica não substitui checks acionados por eventos da CI nem testes físicos locais.

## Limites e salvaguardas

- Implementações devem ocorrer em branches/PRs, **sem push direto na `master`**, sem agente de auto-commit privilegiado, e com merges individualizados somente quando elegíveis.
- Manter a decisão vigente: checks rápidos nas PRs; `jornada-ci` e gate técnico 30k no `push` da `master` no **SHA integrado exato**. A falha de gate obrigatório impede novos merges até correção ou reversão. Não afrouxar FDR ou mascarar reprovações.
- Não usar, liberar ou alterar TEST, Gold, DT-15, HML/produção nem campanhas científicas sem autorização específica. Não executar resets destrutivos em `JornadaLocal`, nem presumir acesso ao computador DEV ou aos bancos locais.
- Registrar qualquer decisão nova do PO nos documentos canônicos e atualizar este índice conforme GOV-DOC-01. Não tratar conversa, status de PR ou intenção como evidência de implementação, medição, validação ou ativação.
- Se a integração, o acesso, os checks ou a execução física estiverem indisponíveis, declarar claramente a limitação, sem inventar sucesso.

## Rastreabilidade, precedência e evidência

A orientação de 08/11/2026 complementa a operação e governança das decisões V9, **sem revogar** precedências científicas, estatísticas e de CI já documentadas. Referência principal do estado e histórico: [repositório Jornada](https://github.com/lucianox777/Jornada) e [índice de decisões vigentes](Indice_Decisoes_Vigentes.md). A automação horária foi habilitada no ambiente de tarefas do assistente, fora dos workflows do GitHub, não significando que GitHub Actions ou DEV tenham sido acionados nesse momento.

**Critério de acompanhamento:** a cada rodada, registrar quando for possível SHA da `master`, PRs e checks relevantes, ações realizadas, evidências e pendências. **Estado de execução do produto:** não inferido pela criação deste documento ou pelo agendamento. **Validação:** a verificar pela primeira e pelas sucessivas execuções reais.
