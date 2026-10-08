# Plano de aceite da Console DEV — antes da Trilha 4

**Estado:** execução por etapas. **Trilha 4 suspensa expressamente pelo responsável pelo projeto**; não executar alterações de reprocessamento de resolvidos, fila de dependências ou replay incremental até nova autorização.

## Ambiente de teste obrigatório

- Somente **DEV isolado e descartável**, com banco e storage próprios. Confirmar identificador da instância e banco **diferentes de `JornadaLocal`**, nenhum volume do cluster comum, nenhuma integração com HML/PROD, e referência IBGE original montada somente em leitura ou fixture sintética separada. Não executar `local-db.ps1 -Action reset`, `local-cluster.ps1 -Action clean` nem `local-test-from-zero.ps1 -AllowDestructiveReset` no ambiente de trabalho habitual.
- Capturar o SHA do commit, a configuração do ambiente, SHA do ZIP, identificador da Entrega e os resultados de teste de cada etapa sem expor CPF ou dados pessoais nos logs.
- Usar exclusivamente dados sintéticos e contas DEV; não colocar segredos em evidências/prints.

## Checklist de aceite manual, com expectativa clara

| ID | Console / ação | Resultado esperado | Situação |
|---|---|---|---|
| TC-01 | Abrir a Console DEV; verificar catálogo e ordem das etapas | Infraestrutura → Ingestão → Bronze → Silver → Linkage → Gold; selo RunOnce em Silver | Cobertura HTTP automatizada na CI |
| TC-02 | Abrir “Gerar arquivo”; escolher contrato SEHAB/AA01 e preencher Pessoa sem registro | ZIP com exatamente `manifest.json`, `pessoas.jsonl` e `registros.jsonl` de 0 bytes; SHA-256 confere | Cobertura HTTP automatizada na PR #821 |
| TC-03 | Gerar ZIP com uma Pessoa e um registro sintético | Registro preservado, ZIP íntegro e download autorizado do artefato criado na sessão | Cobertura HTTP automatizada na PR #821 |
| TC-04 | Tentar JSONL sintaticamente inválido | Falha visível no histórico, nenhum ZIP para download; não enviar | Cobertura HTTP automatizada na PR #821 |
| TC-05 | Enviar ZIP sintético para API isolada; acompanhar recibo e Bronze | Entrega aceita uma vez, estado/ID preservados, conteúdo e hash físico verificáveis por Bronze.Verify | **Pendente teste isolado com API/SQL** |
| TC-06 | Reenviar com mesma chave de idempotência e depois com conteúdo inválido | Não duplicar Entrega nem aceitar integridade divergente | **Pendente teste isolado** |
| TC-07 | Processar Bronze → Silver em RunOnce, restrito à Entrega atual | Código 0 quando ocioso; 3 se incompleto por limite; nada de processar Entregas vizinhas | **Pendente teste isolado** |
| TC-08 | Executar Linkage somente onde necessário; consultar identidade | CPF determinístico pode ser no-op; SEM_CPF exige modelo/fluxo governado; pré-requisitos bloqueiam execução antecipada | Bloqueio HTTP automatizado; fluxo SQL **pendente** |
| TC-09 | Consultar Gold/Resultados e recibo da Entrega | Pessoa publicada, sem conversão de NULL em string e sem duplicidade | **Pendente teste isolado** |
| TC-10 | Reabrir histórico, baixar artefatos da sessão, inspecionar códigos de saída | Exibir status/exit code corretos; IDs inexistentes dão 404 | Cobertura parcial HTTP automatizada |
| TC-11 | Modo PROD: tentar pela API as ações destrutivas `finish` e `reset-environment` | Rejeição HTTP 409, sem processo destrutivo iniciado | Cobertura HTTP automatizada na CI |

## Evidências e critérios de encerramento

1. **Nível 1 — HTTP sem SQL:** `JORNADA_CONSOLE_ACCEPTANCE_ISOLATED=true python3 scripts/test-devconsole-http-acceptance.py`, depois de build Release. O script sobe a Console real no loopback, gera ZIPs sintéticos, baixa e verifica SHA/estrutura, e testa erros. Saída em `.local/test-evidence/unit/devconsole-http-acceptance.json`.
2. **Nível 2 — SQL isolado:** executar TC-05–TC-08 em database descartável; guardar evidência de RunOnce, Lease e Bronze físico. Proibido afirmar que essa etapa passou apenas porque a suíte Unit passou.
3. **Nível 3 — ponta a ponta:** executar TC-05–TC-09 no mesmo cenário com autenticação DEV, API, Silver, Linkage quando elegível e Gold; coletar os IDs e estados sem expor conteúdo pessoal. A CI `e2e` geral é complementar: verificar se efetivamente cobriu a **Console** antes de dar TC-09 como aceito.

**Bloqueio funcional conhecido antes de C3.1:** a geração manual atual valida sintaxe JSON/JSONL, mas não aplica o JSON Schema completo nem regras do Processor. Não confundir “ZIP gerado” com “Entrega contratualmente válida”. Registar e corrigir esta diferença antes de promover a Console a fluxo aceito.

Não executar a Trilha 4 antes de autorização específica, mesmo após todos os TC aprovados.
