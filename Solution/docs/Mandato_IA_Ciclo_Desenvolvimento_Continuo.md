# Mandato operacional genérico para agentes de IA — desenvolvimento contínuo e saúde da integração

**ID:** MANDATO-IA-CICLO-CONTINUO-20261119  
**Escopo:** aplicável a qualquer repositório, linguagem, plataforma de CI/CD, equipe ou produto.  
**Autoridade:** responsável autorizado pelo produto ou repositório, conforme governança local.  
**Estado:** diretriz operacional solicitada pelo responsável; a adoção e a automação efetiva dependem de integração e validação no ambiente de destino.  
**Princípio:** a conversa não substitui evidência técnica nem documentação versionada.

## 1. Objetivo e comportamento esperado

Em cada interação operacional, o agente deve conciliar **(a) observabilidade cumulativa da integração contínua**, **(b) execução de trabalho técnico autorizado** e **(c) documentação rastreável**. Deve identificar riscos, executar verificações disponíveis, investigar falhas, corrigir causas e acompanhar resultados até comprovação no código integrado.

Pedidos como “continue” autorizam a continuidade de tarefas previamente permitidas, mas não concedem permissões novas para merges, publicações, acesso a segredos, alterações destrutivas ou testes especiais sujeitos a autorização.

O agente não trabalha autonomamente entre mensagens sem mecanismo de automação efetivamente instalado. Diferenciar sempre plano, implementação, teste local, execução remota, integração, observação e certificação.

## 2. Descoberta do ambiente e fontes de verdade

1. Identificar repositório, branch principal, SHA integral, sistema de versionamento, pipelines, linguagens, testes, ambientes, permissões, políticas e responsáveis.
2. Consultar documentação versionada, regras de contribuição, decisões vigentes, proteção de branch, issues, pull requests, execuções, logs e artefatos.
3. Descobrir os nomes **reais** dos workflows e seus gatilhos; não pressupor nomes, arquivos, infraestrutura ou suites de testes.
4. Distinguir documentos já integrados de propostas em branches ou PRs. Resolver conflitos de precedência segundo as regras locais.
5. Se uma ferramenta tiver cobertura parcial, registrar o limite e buscar outras evidências autorizadas. Uma busca que retorna zero itens **não demonstra ausência** quando a fonte não é exaustiva.

## 3. Procedimento por interação

### A — Sincronizar estado e autoridade

- Identificar solicitação, escopo, autorização, SHA da branch principal, PRs abertas, checks, conflitos, incidentes e alterações desde o último checkpoint.
- Não reutilizar resultados antigos para certificar um HEAD novo.
- Verificar se uma correção já está em andamento antes de criar trabalho duplicado.

### B — Inventariar CI/CD cumulativamente

- Consultar a API de execuções com paginação completa, janela UTC explícita e escopo declarado; incluir branch principal, PRs, pipelines relevantes e observadores.
- Usar **identificador único de execução** para deduplicar; preservar separadamente tentativas/reexecuções, workflow, evento, branch, SHA, timestamps, URL, jobs, steps e artefatos.
- Classificar cada execução em **aprovada**, **em andamento**, **aguardando**, **reprovada/bloqueada** ou **outra/indeterminada**. Não contar `skipped`, `cancelled` ou `neutral` como aprovação.
- Validar a identidade contábil do inventário. Comparar com checkpoint anterior apenas quando janelas, identidade e cobertura forem compatíveis; caso contrário, declarar linha de base ou comparação parcial.
- Investigar jobs críticos, falhos e pendentes, inclusive steps e artefatos. Separar resultados de workflows de testes de workflows de relatório/observabilidade.

### C — Investigar e desenvolver

- Priorizar regressões na branch principal, segurança, integridade, disponibilidade e bloqueios relevantes.
- Registrar evidência mínima reproduzível: execução, job, step, SHA, mensagem de erro segura, hipótese e confirmação causal.
- Corrigir a causa com a menor mudança compatível com a arquitetura, adicionar regressões e executar testes cabíveis.
- Continuar trabalho independente autorizado enquanto a CI roda, sem polling excessivo.
- Reexecutar jobs somente quando permitido por política e após classificar a falha; não mascarar falhas determinísticas como infraestrutura.

### D — Integrar e verificar pós-merge

- Antes de integrar, confirmar checks obrigatórios no SHA exato, revisões, conflitos, documentação e autorização de merge.
- Após integrar, obter o **SHA resultante da branch principal** e verificar os pipelines disparados naquele SHA e evento; checks da PR não substituem evidência pós-merge.
- Examinar conclusão global **e todos os jobs exigíveis**, diferenciando sucesso, falha, pendência, ausência e `skipped`. Um workflow de relatório bem-sucedido não prova que a suíte de testes executou.
- Classificar a saúde por escopo: **VERDE COMPROVADO** (gates aplicáveis aprovados e cobertura suficiente), **VERMELHO** (falha confirmada), **PENDENTE** (execução ativa), **NÃO VERIFICADO** (evidência ausente/incompleta) ou **COBERTURA PARCIAL** (opcionais/condicionais não executados). Informar o SHA e o conjunto de gates em cada conclusão.
- Quando um job for ignorado, examinar a condição que o dispensou; não tratar `skipped` automaticamente como falha nem como sucesso.
- Se não houver acesso para disparar testes, não afirmar que os executou. Preferir o workflow completo **já existente** e explicar a limitação exata; não criar pipeline redundante por reflexo.

### E — Documentar e reportar

- Registrar decisões aprovadas nos arquivos oficiais e índices exigidos pelo repositório, com autoridade, contexto, justificativa, impacto, precedência, evidências e estado de integração.
- Distinguir decisões de checkpoints operacionais; evitar transformar cada resultado de CI em decisão de produto.
- Relatar ações realmente executadas, resultados verificáveis, lacunas, falhas, mudanças, SHA, links e próximos passos. Não confundir uma PR aberta com mudança integrada.

## 4. Monitor automatizado de saúde da CI — requisito reutilizável

Se o repositório ainda não possuir supervisão **cumulativa e pós-merge**, especificar e implementar, mediante autorização e política local, um workflow de monitoramento de leitura, com **nome, caminho e periodicidade configuráveis**. Não assumir que o monitor já existe ou está ativo apenas porque foi documentado.

**Gatilhos recomendados:** agenda periódica, acionamento manual e, quando apropriado, eventos de conclusão de pipelines. Usar código confiável da branch protegida, token de escopo mínimo, concorrência controlada, timeout e retenção de artefatos compatível com a política local.

**Funções mínimas:**

1. Resolver SHA atual da branch principal e obter execuções por API paginada, com validação de cobertura e limites de consulta.
2. Persistir checkpoint versionado/artefato ou armazenamento apropriado, com identificadores únicos e tentativas separadas; conciliar sobreposição de janelas sem duplicidade.
3. Classificar estados, comparar transições, detectar falhas novas, recuperadas, execuções estagnadas, ausência de gates e mudanças de SHA.
4. Examinar jobs, steps e artefatos relevantes; registrar proveniência entre execução, job, artefato e commit.
5. Verificar saúde pós-merge para **pipelines exigíveis configurados por repositório**, sem confundir observadores com suítes de teste.
6. Publicar resumo legível e relatório estruturado, incluindo cobertura, janela, SHA, URLs, estados, skips e limitações.
7. Falhar fechado quando a coleta estiver incompleta; declarar **NÃO VERIFICADO** em vez de inferir aprovação.
8. Não disparar testes caros, reexecutar jobs, modificar código, abrir PRs ou publicar releases por padrão; qualquer ação corretiva automática requer política específica.

**Testes de aceite do monitor:** paginação, limites, duplicidade, tentativas, janela móvel, desaparecimento de execução, estados desconhecidos, erro de permissão, ausência de workflow, SHA divergente, jobs ignorados e distinção entre relatório e suíte. Comprovar a **primeira execução real**, o agendamento e a leitura dos artefatos antes de afirmar monitoramento contínuo.

O monitor é complementar a eventuais watchdogs de recuperação e aos pipelines de teste: **observar não é executar testes**.

## 5. Modelo de checkpoint reutilizável

```text
OBSERVAÇÃO: <instante UTC>
REPOSITÓRIO / BRANCH / SHA: <identificadores verificados>
COBERTURA: <global completa | parcial; janela e limitações>
RUNS ÚNICOS: <total; aprovadas, em andamento, aguardando, reprovadas, outras>
DELTA: <novos IDs, transições, tentativas, falhas resolvidas>
PIPELINES EXIGÍVEIS: <nome, evento, SHA, run, resultado, jobs, skips>
SAÚDE DA BRANCH PRINCIPAL: <verde comprovado | vermelho | pendente | não verificado | parcial>
EVIDÊNCIAS: <URLs de runs, jobs, artefatos, logs e checks>
TRABALHO REALIZADO: <commits, testes, PRs, documentos>
DECISÕES / GOVERNANÇA: <registro, índice, estado de integração>
BLOQUEIOS: <fato verificável, acesso e dependência>
PRÓXIMA AÇÃO: <passo objetivo>
```

Não divulgar credenciais, dados pessoais ou logs sensíveis. Evitar contagens históricas apresentadas como atuais.

## 6. Limites e precedência

Respeitar autorização humana, políticas de segurança, branch protection, requisitos regulatórios, gates de qualidade e restrições de custo. Testes destrutivos, cargas elevadas, produção, publicação, migrações irreversíveis ou experimentos restritos dependem de autorização específica. Este mandato não substitui procedimentos técnicos, critérios científicos ou regras de governança próprios de cada organização.

## 7. Critérios de conclusão

- Mudança técnica: código e regressões versionados, checks correspondentes verificados no HEAD, documentação atualizada.
- Integração: merge autorizado, SHA integrado identificado e verificações pós-merge aplicáveis comprovadas.
- Monitoramento: workflow implementado, integrado, agendado, testado e com primeira execução e artefatos confirmados.
- Governança: decisão registrada e indexada conforme regras locais; PR não integrada deve ser declarada como tal.
- Resposta: escopo, evidência, incertezas e limites apresentados sem extrapolação.

## 8. Histórico de revisão do modelo

**Revisão genérica — 20/11/2026:** removidas referências a produto, repositório, números de PR, execuções, tecnologias, campanhas e workflows específicos. Incorporados aprendizados reutilizáveis: zero resultados de consulta parcial não implica ausência de CI; evidência de relatório não equivale a teste; `skipped` exige análise de aplicabilidade; saúde é atestada por SHA e cobertura; monitor periódico precisa ser **efetivamente implementado**, não apenas descrito. Evidências concretas e decisões locais devem permanecer nos documentos e registros específicos de cada repositório, fora deste modelo genérico.
