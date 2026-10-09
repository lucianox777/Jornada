> **Decisão superveniente de 09/10/2026:** a orientação histórica
> “executar Trilha 4 por último” constante nesta fotografia de
> 08/10 foi substituída: **a Trilha 4 como serviço temporal
> contínuo foi encerrada como frente autônoma**. A revisão
> extraordinária de RESOLVIDOS é a
> [DT-22 — Reavaliação governada](DT22_Reavaliacao_Governada_Resolvidos.md),
> **aberta/postergada**, sem implementação e sem bloquear o
> fechamento da proposta arquitetural. A falta atual de
> reavaliação automática de RESOLVIDOS quando surgem candidatos
> de terceiros é **risco aceito/documentado**, não capacidade
> concluída; consulte o [Plano corrente](Plano_Desenvolvimento.md).
> O texto e as provas do corte de 08/10 abaixo continuam
> disponíveis para rastreabilidade.
>
# Jornada — Plano de continuidade v5 (base de execução)

**Data:** 08/10/2026  
**Base conferida:** `master` em `ad9bfe67308f4f7c8623207a4922fb81121b3ac7` (PR #808).  
**Precedência:** substitui a instrução v4 quanto à ordem, dependências e salvaguardas. Preserva suas decisões de negócio.  
**Estado:** planejamento técnico, não aprovação institucional, homologação nem autorização para reset.

> **Atualização de execução pós-PR #812 (08/10/2026):** K0.1 foi mesclada no `master` pelo [PR #812](https://github.com/lucianox777/Jornada/pull/812), commit `17073374570e2b87075adc0152a1def976ea7382`. O runtime, schemas e metadados Pessoa dos quatro Gestores consolidam toda a evolução funcional somente em **v1**; versões de tipos de Registro continuam independentes. O arquivo `config/governance/schema-approvals.json` agora contém **as oito entradas** v1 Pessoa (schema + metadados por Gestor) com SHA-256, todas `status=PENDENTE` e `approval=null`: inventário existente **não é aprovação institucional**. As descrições de estado anterior em §2 e P0.1 documentam a base `ad9bfe6`, **não** a situação atual. Os demais itens da tabela continuam requisitos a verificar individualmente por PR e CI. O banco `JornadaLocal` não foi resetado para esta consolidação. A **Trilha 4 será executada por último** na continuidade técnica, antes de considerar encerrado o conjunto de lacunas.

## 1. Decisões preservadas

- Antes da primeira Entrega real em HML, consolidar versões históricas dos contratos Pessoa em **v1**. A primeira Entrega em HML congela o v1; alterações posteriores exigem nova versão, migração, seed, hash e aprovação.
- Na **SMS**, `cns` será campo próprio e opcional; CNS é evidência auxiliar, não âncora nem vínculo determinístico. A âncora determinística permanece CPF.
- A **SMADS** terá registro v1 SA01 do Serviço Especializado de Abordagem Social, com `enderecoReferencia` representando **local da abordagem**, jamais a identidade da pessoa.
- O modo finito chama-se **RunOnce**, sem substituir o comportamento residente padrão dos workers em produção.
- Códigos de saída: `OK=0`, `FAILURE=1`, `VERIFICATION_FAILED=2`, `INCOMPLETE=3`, `INVALID_PRECONDITION=4`, `INVALID_ARGS=64`, `CANCELLED=130`.
- Cobertura de comportamento não é substituída por testes que só procuram trechos do código-fonte. A limpeza do staging segue na `Jornada.Api`, que mantém o heartbeat de API; o staging é local do nó.

## 2. Correções obrigatórias introduzidas pela v5

1. **Não renomear v6 para v1 cegamente.** O `master` contém `pessoa.json` em v1 e `pessoa.schema.json` em v6 para SEHAB, SMADS, SMDET e SMS; `v6/pessoa.json` não existe. Preservar metadados de v1 e incorporar conteúdo funcional do schema v6, revisando `$id`, referências, versões e hashes. O inventário deve incluir as três cópias SEHAB.
2. **Testes antes de alterações estruturais.** Criar uma rede mínima de testes comportamentais previamente a K0.1; retirar testes textuais só quando equivalentes exercitarem o comportamento.
3. **Proteger o banco existente.** `JornadaLocal` e referência IBGE não são descartáveis. Migração e E2E devem usar `JornadaE2E`/banco novo isolado. Qualquer cutover do banco original requer backup **com restauração comprovada**, inventário de dados, rollback e autorização operacional **separada**.
4. **Contratos e Console são frentes separadas.** K4.1/K4.2 não dependem da implementação C3.1 para existir, ser persistidos ou serem validados. C3.1 é necessário somente para os novos campos aparecerem automaticamente na Console.
5. **Reduzir tempo sem enfraquecer a CI.** Preservar todos os gates obrigatórios antes de merge; medir duração, reutilizar restore/build, rodar testes rápidos primeiro. Timeout, status ausente e job ainda rodando nunca contam como `SUCCESS`.
6. **Download Bronze seguro.** Além de SHA-256, exigir autenticação/autorização por Gestor/perfil, auditoria, validação de `objeto_chave`, proteção contra path traversal e restrição a DEV/HML; não interpolar caminhos não confiáveis no shell.
7. **Proteger identidade/linkage.** CNS igual ou endereço da abordagem coincidente nunca produz vínculo determinístico de CPFs distintos. Exigir regressão com base congelada e checagem de falsos vínculos; não repetir teste para contornar reprovação estatística.

## 3. Sequência, dependências e definição de pronto

Cada item entra em **PR próprio**, com código, teste e documentação. Exigir `dotnet build`, Unit/Integration e demais gates obrigatórios completos antes de merge. O PR deve explicar como verificar o resultado em até cinco passos.

| Frente | Item | Critério de aceitação | Dependências |
| --- | --- | --- | --- |
| Base | **P0.1** | Preflight somente leitura verifica os quatro Gestores, metadados v1, schemas v6, hashes de aprovação e reprova consolidação parcial; testes rápidos no CI | — |
| Testes | **T0.1a** | Proteção comportamental mínima de Console/ZIP e workers, CPF preservado, NULL SQL e NO_OP Linkage | P0.1 |
| Contratos | **K0.1** | v1 consolidado, fontes SEHAB únicas, seed/migrations/hashes/fixtures e consumidores atualizados, E2E e testes com SQL descartável | P0.1, T0.1a |
| Testes | **T0.1b** | Níveis 1/2 em cada PR e E2E da Console em estágio de CI/dispatch, sem perda de cobertura | K0.1, T0.1a |
| Execução | **F1.1** | `JornadaExitCodes` compartilhado; Runner cancelado com 130; testes verificam códigos reais | T0.1a |
| Execução | **F1.2** | `<Worker>:RunOnce` e `RunOnceMaxSeconds`; `Operation` apenas ação; alias antigo com warning | F1.1 |
| Execução | **F1.3** | `scheduler-jobs.json` v2 declara `supportsRunOnce`, `defaultMode` e limites, gate negativo | F1.2 |
| Console | **C2.1** | Blocking incremental vs integral com rótulos corretos e efeito único; Bronze.Verify sem duplicidade | T0.1a |
| Console | **C2.2** | Modo RunOnce no catálogo, selo visível, ordenação e códigos de saída legíveis | F1.2, F1.3 |
| Console | **C3.1** | ZIP por JSON Schema, arrays/objetos/`allOf`/`if`/`then`, mesma regra de validação do Processor | T0.1a, K0.1 |
| Console | **C3.2** | “Somente Pessoa”; ZIP com os três arquivos e `registros.jsonl` vazio; Entrega PROCESSADA | C3.1 |
| Console | **C3.3** | “Baixar ZIP” em Resultados e Bronze, integridade, autorização e auditoria | T0.1a |
| Contratos | **K4.1** | CNS SMS opcional e validado, serialização Bronze/Silver; sem impacto determinístico | K0.1 |
| Contratos | **K4.2** | SA01 SMADS, seed/hash, Ensaio, `enderecoReferencia`, validação real e teste de identidade | K0.1, confirmação SMADS antes de HML |
| Limpeza | **L5.x** | Refatoração e eliminação de órfãos com revisão de dependências e evidências históricas | Por item |

**Execução paralela:** contratos/persistência podem avançar em paralelo à Console após a estabilização dos testes essenciais; L5.2 e L5.7 dependem de K0.1. Não efetuar limpeza de modelos persistidos L5.8 sem inventário e rollback.

### P0.1 — Primeiro PR, pré-condição não destrutiva

Na raiz `Solution`, executar:

```sh
python scripts/tests/test_contract_v1_preflight.py -v
python scripts/contract-v1-preflight.py --root .
```

O preflight compara SHA-256 em bytes com `config/governance/schema-approvals.json` **quando a entrada existe**; em `ad9bfe6`, as entradas Pessoa da SEHAB não estão no inventário, portanto são reportadas como `missingApprovalEntries` e precisam ser cadastradas na K0.1. Em estado final consolidado, ausência de aprovação é erro. Verifica `v1/pessoa.json` mais `v6/pessoa.schema.json` antes da migração; depois aceita somente o estado atômico consolidado v1. Rejeita mistura de estados entre gestores, metadados inválidos e divergência de hashes. É **somente leitura**: não executa Git, Docker ou SQL; `OK_READ_ONLY` não aprova reset nem E2E. Os testes verificam inventário, preservação de bytes, ausência de metadados, drift de hash, estado misto e estado final.

### T0.1 — Testes em três níveis

1. **Nível 1 (rápido/todo PR):** `WebApplicationFactory` para endpoints da Console, geração de ZIP canônico, preservação do CPF digitado (#802), navegação de camadas, resultado `/api/runs/{id}/result`.
2. **Nível 2 (todo PR):** workers como processos sobre banco descartável, resultados `OK`, `INCOMPLETE`, `FAILURE`, idempotência, NO_OP Linkage (#804/#805) e NULL do SQL não convertido em texto “NULL”.
3. **Nível 3 (dispatch/estágio obrigatório antes de merge):** sequência completa Console → ZIP → API → Silver → Linkage → Gold, com `gold.pessoa` e recibo da Entrega.

O CI pode usar cache e artefatos imutáveis, mas não deve declarar PR apto se o estágio obrigatório não rodou.

### K0.1 — Consolidação v1 sem apagar ambiente original

1. Inventariar schemas/metadados `v1`–`v6`, consumidores, três cópias SEHAB, hashes, aprovações e fixtures; capturar resultados de P0.1.
2. Compor o novo `v1` com `pessoa.json` original de v1 **e schema funcional** de v6; revisar `$id`/`$ref`, manifests, parsers, seeds, migrations e testes. Histórico anterior deve permanecer acessível pelo Git e documentos, não como contratos ativos.
3. Testar ZIP válido e inválido por contrato; os gates de schema e hashes; integração e E2E num banco SQL Server isolado. Nenhum nó do cluster compartilhado pode apontar para o banco de ensaio.
4. Registrar backup restaurável, referência IBGE, rollback e evidências. **Não rodar** `local-db.ps1 -Action reset`, `local-cluster.ps1 -Action clean` ou `local-test-from-zero.ps1 -AllowDestructiveReset` em `JornadaLocal` como efeito do PR. A ação `clean` pode remover volumes compartilhados.
5. Cutover/reset do banco original só em operação separada, explicitamente aprovada, depois da validação. Congelar v1 na primeira Entrega real em HML.

### F1.1 a F1.3 — Execução finita

Usar códigos de saída nomeados em todos os executáveis; o Runner deixa de reutilizar 2 para cancelamento e passa a 130. `Processor:RunOnce=true` equivale a processar até ficar ocioso, respeitando `TargetEntregaId`; `PROCESS_ONE` fica restrito a Development. O `scheduler-jobs.json` declara modo suportado/padrão e duração; validar declarações inconsistentes.

### C3.1 e C3.3 — Limites de arquitetura e segurança

A Console deve suportar texto, números, datas, enums, booleanos, objetos, listas, campos obrigatórios, padrões e condicionais JSON Schema; para trecho não suportado, oferecer editor JSON, **sem ocultar campo**. Checar o gate `architecture-dependency-gate.py`: `Jornada.DevConsole` atualmente não referencia outro projeto. Não acrescentar `Jornada.Ingestion` sem decisão arquitetural; preferir extrair validador compartilhado se necessário. Validar que Processor aceita o ZIP gerado.

O download do ZIP Bronze só ocorre após autorização por Gestor/perfil, leitura controlada do objeto, verificação de hash/tamanho e auditoria; objeto corrompido ou ausente falha fechado. Logs não podem conter os dados do ZIP.

### K4.1 / K4.2 — Identidade

No contrato da SMS, `cns` é string opcional com 15 dígitos e dígito verificador validado; retirar CNS de `identificadores[].tipo` **apenas** na SMS para evitar duas fontes conflitantes. Outros Gestores mantêm suas opções até decisão D4. Não usar CNS como âncora nem evidência determinística.

No contrato SA01, campos obrigatórios: `idPessoaEntrega`, `codigoRegistroOrigem`, `operacao`, `dataHoraServico`. Incluir `modalidadeSeas` (ADULTO, MISTO, CRIANCA_ADOLESCENTE), `formaAcesso` (BUSCA_ATIVA, DEMANDA_ESPONTANEA, SP156), protocolo SP156 condicional, `resultado`, `termoRecusa` condicional, `encaminhamentos` e `enderecoReferencia` opcional (logradouro, ponto, tipoLocal, geografia, situacaoGeografia). Excluir saúde, substâncias, violência, motivo e GPS. Confirmar detalhes com SMADS **antes de congelar v1**. Testar CPF distinto + CNS/endereço comum sem falso vínculo.

### L5.x — Limpeza com retenção de evidência

L5.1 remove referências ao integrador antigo; L5.2 remove relatório legado após K0.1; L5.3 scripts órfãos; L5.4 gate de caminhos inexistentes; L5.5 instalador .NET 10; L5.6 histórico documental somente após inventário de referências/evidências; L5.7 fontes SEHAB junto de K0.1; L5.8 modelos V5/V6/V7 e guard demográfico somente após inventário persistido e rollback; L5.9 branches fechadas; L5.10 classificar `jornada-ibge-work` antes de mover.

## 4. Decisões abertas e limites

- **D4:** apenas SMS ganha `cns` próprio agora (recomendado).
- **D5:** `PROCESS_ONE` permanece modo de desenvolvimento (recomendado).
- **D7:** referência de local da abordagem fica no SA01, não duplica atributo Pessoa (recomendado).
- **D6:** confirmar campos do SA01 com SMADS antes da Entrega em HML (obrigatório).

Nenhuma ação destrutiva foi executada para produzir este plano. O preflight não substitui build .NET, testes Unit/Integration nem gates completos. **PR #810, que propõe manter exclusivamente Pessoa v6, conflita com a decisão v5 de consolidar em v1 e não deve ser unido sem resolver expressamente essa divergência.**
