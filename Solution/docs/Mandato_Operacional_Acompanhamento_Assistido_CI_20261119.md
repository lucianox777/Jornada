# MANDATO GERAL — Acompanhamento assistido de CI, investigação e correções rastreáveis

**Identificador:** GOV-MANDATO-CI-ASSISTIDA-20261119  
**Solicitante:** titular da solicitação / Product Owner no contexto de Jornada do Cidadão  
**Data original:** 19/11/2026  
**Revisão de escopo:** 19/11/2026 — generalização expressamente solicitada: **aplicável a qualquer projeto**, não exclusivo do Jornada.  
**Status documental:** registrado neste repositório como referência reutilizável; pendente de integração da PR #1450 à branch principal. **Não constitui política automaticamente adotada por outros repositórios ou organizações.**

## 1. Finalidade e aplicabilidade

Estabelecer um **modelo geral de trabalho** para acompanhamento assistido de integração contínua (CI), investigação de falhas, implementação de correções técnicas permitidas, validação e documentação auditável, **independentemente do projeto, linguagem, plataforma de CI ou provedor de repositório**.

O mandato pode orientar trabalhos em projetos novos ou existentes, públicos ou privados, sempre sujeito às permissões concedidas, às ferramentas disponíveis, à governança e às regras específicas de cada ambiente. Sua adoção em outro projeto exige contextualização das regras locais; salvar este documento no Git do Jornada não o instala nem o torna vinculante em outros projetos.

## 2. Natureza do acompanhamento

O assistente **não cria nem substitui uma CI paralela**: utiliza as interfaces e APIs autorizadas do provedor para consultar pipelines, commits, jobs, checks, logs e artefatos. Distingue evidência observada, diagnóstico provável e hipótese ainda não comprovada. Não há vigilância contínua entre interações sem automação explicitamente configurada e autorizada.

## 3. Mandato operacional

1. Identificar repositório, branch, PR/MR, commit exato, pipelines e requisitos de integração.
2. Consultar o estado da CI por revisão, distinguir sucesso, falha, cancelamento, pendência, fila e execução em andamento.
3. Priorizar verificações obrigatórias e bloqueios relevantes ao objetivo do trabalho.
4. Inspecionar logs, jobs, código, testes, configurações, dependências e proveniência de artefatos; separar erro primário de falhas consequentes.
5. Propor e, quando autorizado e tecnicamente seguro, aplicar correções **mínimas, reversíveis e rastreáveis** em branches apropriadas, sem alterar contratos ou políticas reservadas à aprovação humana.
6. Conferir novamente os resultados no novo commit, sem atribuir a ele aprovação obtida em revisão anterior.
7. Registrar decisões aprovadas, justificativas, impactos, precedência, evidências e histórico nos arquivos oficiais do projeto, conforme sua política de governança.
8. Comunicar estado real, evidências, incertezas, impedimentos e ações que dependam de autorização.

**Ciclo:** identificar → observar → investigar → corrigir → documentar → validar → informar → repetir conforme necessário.

## 4. Limites e controles universais

Este mandato **não concede por si só** autorização para:
- Efetuar merge, deploy, publicação, alteração de produção ou desabilitar gates, aprovações, proteção de branch e verificações de segurança.
- Expor dados pessoais, credenciais, segredos, artefatos restritos ou logs sensíveis.
- Modificar critérios de aceitação, resultados de testes, proveniência de dados ou controles de conformidade para produzir aprovação artificial.
- Executar atividades irreversíveis, de alto impacto, custosas ou reservadas ao responsável do projeto sem autorização pertinente.
- Afirmar que CI, PR/MR ou implantação foram aprovadas antes da confirmação dos requisitos correspondentes.
- Atribuir a si acesso, execução em segundo plano ou autoridade inexistentes.

Quando faltar um artefato crítico ou sua identidade não puder ser comprovada, registrar o bloqueio e **falhar de modo seguro**, sem substituição silenciosa. Havendo conflito, prevalecem as regras específicas do projeto, as permissões efetivas, os controles de segurança e a orientação expressa do responsável autorizado.

## 5. Critérios de rastreabilidade e conclusão

Cada intervenção deve identificar, quando disponíveis: projeto, PR/MR, branch, SHA antes/depois, run/job, erro observado, hipótese e confirmação, alteração realizada, testes executados, resultado, pendências, responsável por decisões e links de evidência.

**Correção implementada** não equivale a **correção validada**. **Pipeline verde** não equivale necessariamente a **merge autorizado**. A conclusão deve declarar os critérios efetivamente atendidos e os que permanecem pendentes.

## 6. Governança e adoção por projeto

O documento é **genérico**. Ao utilizá-lo, cada projeto deve definir ou identificar seus próprios responsáveis, regras de autorização, documentação oficial, critérios de CI, exigências de revisão, políticas de segurança e eventuais controles de dados, ciência ou operação.

No **Jornada do Cidadão**, a aplicação deste mandato continua subordinada à **GOV-DOC-01**, às decisões científicas e LOCKs V9, aos controles do Operador Master e às regras de proteção de branches. Não autoriza TRAIN/VALIDATION/TEST científicos, ativação de modelo ou merge sem as autorizações e condições locais.

O [procedimento operacional específico do Jornada](Procedimento_Operacional_Acompanhamento_CI_Externo_20261119.md) é um **exemplo de implementação**, não uma limitação de escopo deste mandato. A [PR #1450](https://github.com/lucianox777/Jornada/pull/1450), a [execução DT-05 #44605048704](https://github.com/lucianox777/Jornada/actions/runs/44605048704) e o [Memory Batch #44605048372](https://github.com/lucianox777/Jornada/actions/runs/44605048372) são evidências históricas **do caso Jornada**, não requisitos universais.

## 7. Decisão de revisão, justificativa, impacto e precedência

**Decisão do solicitante:** o mandato deve ser utilizável **com qualquer projeto**, em vez de restrito ao Jornada do Cidadão.  
**Motivo:** padronizar um método de trabalho reaproveitável entre repositórios e plataformas.  
**Impacto:** generalização do escopo e das atribuições; preservação das salvaguardas, sem ampliação automática de permissões ou dispensa de políticas locais.  
**Precedência:** esta revisão substitui o escopo exclusivamente Jornada da versão inicial deste mandato; as regras locais continuam prevalecendo quando aplicáveis.  
**Rastreabilidade:** revisão solicitada pelo PO na conversa de 19/11/2026 e registrada em commit na branch da PR #1450.
