# Jornada do Cidadão — manual técnico integrado do sistema

**Consolidação editorial:** 09/10/2026 · **base de conferência:** `master` em
`b4f74cb34e4346191ed58010b3d6b5cbb2db7e65`, após os merges
[#847–#859](https://github.com/lucianox777/Jornada/pulls?q=is%3Apr+is%3Amerged).
Este manual descreve a implementação **identificável no repositório** e
distingue expressamente aceites técnicos em CI de funcionalidades aprovadas
institucionalmente. Para mudanças posteriores, conferir `master`,
Actions e o backlog; esta data **não congela** a documentação futura.

> **Vigência:** este arquivo é uma **porta de entrada técnica corrente**, não
> substitui a [Especificação Técnica publicada](../../Documentos/README.md)
> nem converte a candidata v5.00 em norma final. Em conflito: documento
> normativo publicado → decisões canônicas aplicáveis → requisitos do
> domínio → código/evidências verificáveis para o estado implementado.
> Não reescrever artefatos de release selados ou evidências históricas
> como se fossem manuais correntes.

## 1. O que é o sistema

A Jornada é uma **plataforma de integração e apoio à decisão** para
Gestores públicos. Recebe entregas de fontes autorizadas, preserva a
proveniência das informações, organiza observações em Bronze/Silver,
constrói representações Gold/Serving e oferece consultas controladas por
finalidade e perfil de acesso. **Não** é cadastro civil certificado,
**não** decide sozinha concessão/negação de benefício e **não** altera
atos finalísticos nos sistemas das Secretarias.

- **Base normativa publicada:** `Jornada.BaseNormativa=3.62`.
- **Schema técnico da candidata:** `Jornada.SolutionSchema=3.70`.
- **Release:** verificar `RELEASE_INFO.txt`; v5.00 é **candidata**,
  e `v5.00-rc.1` é checkpoint técnico, sem promoção automática.
- **Tecnologia relacional de Produção prevista:** Microsoft SQL Server;
  SQL Server 2022 Developer é referência de DEV/CI, não edição de PROD.
  SQL Database em Fabric não é gate operacional da candidata. Power BI
  (PBIP/TMDL/PBIR) usa a camada analítica; Lakehouse/SQL Analytics
  Endpoint não substituem a base operacional.

Fontes: [README da Solution](../README.md),
[Escopo Fase 1](Escopo_Produto_Fase1.md),
[Arquitetura de identidade](Arquitetura_Identidade_Linkage.md) e
[decisões vigentes](Indice_Decisoes_Vigentes.md).

## 2. Mapa dos componentes e limites

| Componente | Responsabilidade | Fonte de implementação/documentação |
|---|---|---|
| `Jornada.Api` | Borda externa de ingestão, identidade e consultas com contratos v1 e controles de acesso | `src/Jornada.Api/`, [API](API.md), [integração de gestor](Manual_Integracao_Gestor.md) |
| `Jornada.Resultado.Api` | Serviço separado de apresentação/resultado; não compartilha o PID1 da API no E2E privado | `src/Jornada.Resultado.Api/`, [Console DEV isolada](../install/console-dev-e2e/README.md) |
| `Jornada.Processor.Worker` | Processa entregas/lotes; persiste Silver/Gold/Serving; mantém lease, heartbeat e mecanismo de reentrada | `src/Jornada.Processor.Worker/`, [lease](Processor_Lease_Heartbeat_Isolation.md), [Bronze](Bronze_Operacao.md) |
| `Jornada.Operations.Maintenance.Worker` | Rotinas de manutenção e watchdog observacional segundo configuração | `src/Jornada.Operations.Maintenance.Worker/`, [operação](Runbook_Operacao.md) |
| `Jornada.Bronze.Maintenance.Worker` | Ciclo de manutenção da Bronze e suas políticas específicas | `src/Jornada.Bronze.Maintenance.Worker/`, [Bronze](Bronze_Operacao.md) |
| `Jornada.Linkage.Parameters.Worker` | Preparação/calibração de parâmetros, com separação entre rascunho, validação e ativação | `src/`, [núcleo de linkage](Nucleo_Linkage_Identidade_Progressiva.md), [calibração](Calibrador_FS_Specification.md) |
| `Jornada.Linkage.Runner` | Resolução progressiva, replay e publicação governada | `src/Jornada.Linkage.Runner/`, [arquitetura](Arquitetura_Identidade_Linkage.md) |
| `Jornada.DevConsole` | Ferramenta de desenvolvimento/teste, diagnóstico e observação; **não** é a interface transacional oficial das Secretarias | `src/Jornada.DevConsole/`, [Console](Console_DEV_Supervisao_Atual.md) |
| `Jornada.Pipeline.Coordination` | Biblioteca de locks/coordenação e invariantes SQL, **não** scheduler autônomo | [Runbook operacional](Runbook_Operacao.md) |

**Scheduler:** no ambiente corporativo, cabe ao mecanismo de
orquestração aprovado; o controlador Docker do **E2E DEV descartável**
não constitui proposta de orquestração PROD. A borda API e o serviço
Resultado são independentes. A interface central de atendimento
continuará sob responsabilidade dos Gestores, segundo contratos.

## 3. Fluxo de dados e rastreabilidade

```text
Gestor autorizado
    │ contrato + autenticação + finalidade + chave idempotente
    ▼
Jornada.Api — recepção de Entrega/ZIP, registro de origem
    ▼
Bronze (pacote bruto e proveniência)
    ▼
Processor — Entrega / Lote / heartbeat / lease / retomada
    ▼
Silver — observações tipadas (pessoa, registros, atributos)
    ├──► Núcleo de identidade: CPF confiável, vínculos, UUIDs,
    │    ausência e conflito, ledger, linkage probabilístico governado
    ▼
Gold — representação conciliada e auditável
    ▼
Serving / Resultado.Api / consulta autorizada / consumo analítico
```

**Invariantes:** fatos finalísticos válidos não são eliminados
silenciosamente porque a identidade está pendente/conflitante. A
Jornada registra origem, idempotência, operações semânticas e
divergências. CPF confiável possui rota determinística; linkage
probabilístico é governado por modelo, estratos, evidência e escopo
de ativação. Consultas semicegas e as capacidades estatísticas
experimentais não são permissões institucionais automáticas.

Detalhes: [API](API.md),
[manual de integração](Manual_Integracao_Gestor.md),
[decisões identidade/linkage](Decisoes_Canonicas_Identidade_Linkage_20260929.md),
[fluxos de blocking](Fluxos_Blocking_Selecao_Registro.md) e
[catálogo de vigência](Catalogo_Vigencia_Documental_20260929.md)
(**fotografia datada**, não status corrente).

## 4. Ambientes, segurança e isolamento

| Ambiente | O que está comprovado | O que NÃO decorre da prova |
|---|---|---|
| DEV em cluster local ordinário | Scripts e Compose de desenvolvimento existem e exigem cuidados explícitos | Autorização para reiniciar, limpar ou migrar `JornadaLocal` e acervo IBGE |
| DEV CI descartável `JornadaE2E` | Serviços SQL, API, Resultado e workers isolados; recuperação e Console verificadas por testes e evidências na CI | Reutilização de PIDs, segredos e procedimentos de injeção de falha sobre hosts comuns |
| Ensaio com massa de Secretarias | Há procedimentos e contratos previstos | Aceite representativo já concedido |
| HML/PROD | Requisitos, runbooks e gates institucionais documentados | Implantação autorizada por CI sintética ou healthcheck DEV |

A supervisão/RunOnce/Parar processo da Console atual depende de
identidade de `GITHUB_RUN_ID`/`GITHUB_RUN_ATTEMPT`, projeto Compose
efêmero, labels de serviço, `JornadaE2E`, DEV e requisições
loopback. Fora desse perfil, operações são **negadas**. Na borda
de APIs, controles corporativos de IdP/RBAC/PRODAM e validação
de HML/Produção ainda exigem aceites próprios; ambiente não
`Development` deve falhar fechado quando a identidade corporativa
não estiver integrada.

**Nunca** executar scripts destrutivos de desenvolvimento para
“atualizar a documentação”. Não acessar, apagar, parar, recriar,
migrar ou testar diretamente `JornadaLocal`, dados IBGE originais,
NODE/Compose/volumes existentes, HML/PROD ou massas reais. As
provas mencionadas aqui são as já feitas em GitHub Actions isolado.

## 5. Console DEV: estado implementado vs desenho pendente

**Implementado e integrado ao código de `master` em 09/10/2026:**

- `GET /api/workers/supervisor`: modo efetivo a partir de Docker
  **e** `controle.runtime_componente`, PIDs, reinícios, identidade
  da instância, idade do heartbeat e disponibilidade das APIs.
- `POST /api/workers/supervisor`: controle **global** OFF ↔ ON
  com exclusão/validação, três workers e parada desarmando a política
  de restart antes do encerramento; repetição idempotente.
- `POST /api/workers/{worker}/run-once`: um ciclo finito dos três
  workers, permitido somente com supervisão OFF comprovada.
- `POST /api/workers/{worker}/stop`: operação individual do
  residente identificado, com verificação de identidade.
- Interface com controle global, estados automáticos e dois botões
  verticais **“Executar uma vez”** e **“Parar processo”** por trabalhador,
  sem botão extra para iniciar residente ou consultar status.
- Proteção contra desconexão HTTP durante RunOnce: a execução
  continua controlada pelo processo servidor, não pelo socket cliente.

**Limite aberto:** o contrato ideal de **cancelamento explícito e
confirmado** do RunOnce ativo antes de ativar ON, incluindo nonce/
identidade da execução, término cooperativo e prova de ausência de
one-off, **não está declarado como implementado** pelo conjunto
#853–#856. Não confundir proteção contra desconexão com confirmação
de cancelamento. [Contrato pendente](C3_3b3_Confirmacao_Cancelamento_RunOnce.md)
(se o documento ainda não estiver em `master`, consultar o backlog).

**Operação permitida:** somente perfil CI/DEV descartável com
`JornadaE2E`; a Console não é supervisor de processos em
PROD/HML. Ver [guia consolidado da Console](Console_DEV_Supervisao_Atual.md)
e [DT-18](DT18_Servicos_Independentes_Console_DEV.md),
[DT-19](DT19_Console_Acoes_Workers.md),
[DT-20](DT20_Supervisao_Opt_In_Workers.md),
[DT-21](DT21_Testes_Resiliencia_Workers.md).

## 6. Testes realmente realizados no projeto efêmero

| Aceite | Evidência técnica integrada | Limite |
|---|---|---|
| Infra privada / readiness | #845, #846 | Não prova restart nem lote recuperado |
| Restart independente de cada worker | #847 | PID/restart não provam recuperação de trabalho |
| Contrato de evidência e baseline ZIP | #848, #849 | Validador offline não é telemetria operacional |
| Rollback de transação / lease expirado / fencing / reentrada, idempotência | #850 | Banco e carga **sintéticos**, não HML |
| Estado OFF/ON observado | #851 | Inicialmente apenas GET, sem alternância |
| Toggle global real | #852 | Perfis DEV efêmeros |
| RunOnce de três workers | #853 | Exit code zero / OFF e retorno ON no sandbox |
| Desconexão HTTP sem liberar RunOnce | #854 | **Não** equivale a cancelamento confirmado |
| Parada individual | #855 | Injeção de falha somente no contêiner allowlisted |
| Painel da Console | #856 | Não confirma UI disponível ao usuário final |
| Proteção e extração inicial da CI DT-10 | #857, #858, #859 | Reuso **apenas** do gate DT10; outros builds continuam independentes |

**Como conferir:** procurar o número de PR no GitHub e o workflow da
**HEAD exata**; verificar jobs, logs reais e artefatos antes de afirmar
sucesso. Não transformar um arquivo JSON autodeclarado nem teste
estático em evidência de retomada de lote. Para o histórico de
execução do job DT-10, ver
[extração reutilizável](DT10_CI_Extracao_Reutilizavel.md).

## 7. CI, builds, segurança e política de merge

Arquivo chamador: `.github/workflows/ci.yml`.
O gate `impact` classifica docs-only vs código e determina `run_full`
e `run_dt10`; alterações do próprio workflow DT-10 exigem
executar o gate respectivo. Gates principais:

`impact`, `dependency-lock`, `ddl-upgrade`, `unit`,
`deterministic-build`, `security-analysis`, `integration-sql`,
`harness-smoke`, `e2e` e `dt10-evidence` (condicional conforme
`run_dt10`). O DT10 é chamado pelo reusable workflow
`.github/workflows/dt10-evidence.yml` com o mesmo run/artefato
de locks, SQL sintético e mínimo de quatro testes sem `skipped`.
Outros jobs de escala, restore, RC e publicação dependem do
tipo de evento e **não** são aceitos por omissão.

CI verde deve corresponder à **HEAD exata** da PR, às condições de
impacto aplicáveis, aos gates necessários completos, às revisões
liberadas e às evidências dos testes **efetivamente executados**.
Não concluir merge por “testes rodando há muito tempo”. O
desmembramento completo dos builds e reuso seguro de binários por
SHA **continuam pendentes**; a primeira extração DT10 não resolveu
o custo integral de recompilação.

Ver [testes técnicos](Runbook_Testes_Tecnicos.md),
[DT10 extração](DT10_CI_Extracao_Reutilizavel.md),
[CI gates](../tests/scripts/test_ci_required_gates.py) e
[release](Runbook_Git_Release.md).

## 8. Como desenvolver e operar com segurança

Para **compreender** o sistema, comece pelo mapa abaixo. Para
executar scripts de banco/Compose, siga os procedimentos oficiais
apenas em ambiente **explicitamente autorizado**. Este manual não
autoriza execução de `local-db.sh`, reset de containers ou
`docker compose down -v` em qualquer instalação preexistente.

- [Desenvolvimento local](Runbook_Desenvolvimento_Local.md):
  preflight/segredos, criação e validação em ambiente preparado.
- [Operação corporativa](Runbook_Operacao.md):
  responsável pelo scheduler, alertas, replay e segregação.
- [Monitoramento](Monitor_Operacional.md):
  indicadores e sinais, não apenas texto de log ou PID.
- [Testes técnicos](Runbook_Testes_Tecnicos.md):
  matrizes SQL/HTTP/integração, evidências e limites.
- [Bronze](Bronze_Operacao.md):
  retenção, replay e verificação.
- [HML e Ensaio](Ensaio_Unico_Paridade_HML.md):
  gates distintos de CI sintética.

## 9. Roteiro de leitura por função

| Leitor | Sequência recomendada |
|---|---|
| Gestor / instituição | [Escopo do produto](Escopo_Produto_Fase1.md) → [governança de acesso](Governanca_Finalidade_Acesso.md) → [manual de integração](Manual_Integracao_Gestor.md) |
| Analista funcional | [API](API.md) → [identidade](Arquitetura_Identidade_Linkage.md) → [Gold](Gold_Pessoa_Divergencias_Observacoes.md) |
| Desenvolvedor backend | `Solution/README.md` → [invariantes](Invariantes_Arquitetura.md) → [API](API.md) → [runbook de teste](Runbook_Testes_Tecnicos.md) |
| Operador DEV | [Console](Console_DEV_Supervisao_Atual.md) → [isolamento CI](../install/console-dev-e2e/README.md) → [estado atual](Estado_Atual_Projeto.md) |
| DBA/infra | [runbook operacional](Runbook_Operacao.md) → [upgrade](DT06_Aceite_Upgrade_Evidencia.md) → [esquema](Operational_SQL_Adapter.md) |
| Governança / QA | [índice vivo](Indice_Acervo_Documental.md) → [decisões vigentes](Indice_Decisoes_Vigentes.md) → [dívidas](Dividas_Tecnicas.md) → [testes](Runbook_Testes_Tecnicos.md) |

## 10. Decisão de encerramento da Trilha 4 como frente contínua

**Decisão superveniente de 09/10/2026:** a proposta do sistema
**não depende de criar um serviço contínuo que revisite todas as
identidades RESOLVIDAS pelo simples decurso do tempo**. Essa versão
da Trilha 4 foi **encerrada como frente arquitetural autônoma**.
Seu valor remanescente passa à
[**DT-22 — Reavaliação governada dos RESOLVIDOS**](DT22_Reavaliacao_Governada_Resolvidos.md),
**ABERTA/POSTERGADA** ([issue #861](https://github.com/lucianox777/Jornada/issues/861)) e independente do ciclo normal de ingestão.
Permitirá futura execução **explícita e autorizada**, inclusive
após mudança de modelo, com universo/versão definidos,
checkpoint, histórico e recomposição Gold/Serving consistentes.
Ativar um modelo **não** cria obrigação implícita de replay imediato
nem prova de que a população inteira já foi recalculada.

**Limitação atual comprovada no código:** `INCREMENTAL` seleciona
observações sem vínculo, `NAO_RESOLVIDO`, `CONFLITO` ou
`PENDENTE_PROBABILISTICO` e não captura de maneira geral
`RESOLVIDO` indiretamente afetado por um candidato novo de
outra fonte. A nova versão de origem no Processor e a
ancoragem CPF **não provam recomposição automática de todos os
outros membros de um agrupamento probabilístico anterior**.
O modo `REPLAY` existente exige run/modelo histórico imutável
e **não reavalia com novo modelo**; `FULL` existe como
modalidade mais ampla, não como DT-22 entregue. A Gold continua
a melhor representação disponível, **não a garantia de que
todas as evidências passadas foram reavaliadas**.

**Risco transparente:** associações probabilísticas de RESOLVIDOS
podem permanecer desatualizadas sem uma operação específica.
Registrar isso nos critérios de qualidade/risco antes de afirmar
automação ou encerrar o Ensaio. **Fechar a proposta arquitetural
não quita a DT-22 e não homologa a qualidade estatística.**
Preservar a regra de CPF-first, a impossibilidade de dois CPFs
no mesmo UUID, a origem de cada observação, o histórico e
todos os fatos finalísticos.

## 11. Pendências e critério de conclusão

**Débitos restantes prioritários:** DT-05 (replay histórico NAS),
DT-22 (reavaliação extraordinária dos RESOLVIDOS, **postergada**),
cancelamento **explicitamente confirmado** de RunOnce,
telemetria durável com Console fechada e otimização segura
dos builds repetidos da CI. Essas frentes são distintas e
não reabrem os aceites DEV já integrados.

**Ainda sem aceite comprovado:** IdP corporativo/HML/PROD, validação
estatística representativa do Ensaio com massa de Secretarias,
eventuais decisões de promoção do modelo de linkage, cancelamento
explícito confirmado de RunOnce e otimização completa da CI.
O status da issue/PR corrente deve prevalecer sobre documentos
antigos que usam “pendente” para funcionalidades já mergeadas ou
“concluído” para um escopo técnico mais restrito.

As referências versionadas e arquivos `archive/`,
`README_Historico_Engenharia_v4.03.md`, `evidence/` e releases
seladas preservam seus próprios tempos e **não** são reescritos
retroativamente por esta consolidação. Divergências precisam ser
registradas e classificadas, não apagadas.

---

**Próxima revisão:** após mudança em `master` que afete contratos,
ambientes, fluxos, gates de CI, permissões ou lifecycle dos workers.
Responsáveis devem atualizar este manual, o [estado atual](Estado_Atual_Projeto.md)
e a documentação do domínio no **mesmo PR** da implementação.
