# Incidente de CI da master após a integração da PR #1920 — NuGet lock provenance

**Data:** 23/11/2026. **Autoridade:** ordem do Product Owner de corrigir todos os erros reais das PRs envolvidas em frentes paralelas; GOV-DOC-01. **Frente:** correção isolada a partir da master `597fef11a9ca73efbb2a14530f2ec5ec481d996a`, sem modificar a PR #1920 já mesclada e sem interferir na PR #1924 Draft.

## Erro real, causa e evidência pré-correção

O push da master após squash da #1920 acionou [jornada-ci #45250877383](https://github.com/lucianox777/Jornada/actions/runs/45250877383). No job `dependency-lock` `137960082572`, o restore dos 24 projetos concluiu com sucesso e o teste **existente** `scripts/nuget-lock-provenance-gate.py` falhou em `NUGET LOCK PROVENANCE GATE: FAIL: SHA candidato divergente: tests/Jornada.Tests/packages.lock.json`. A PR #1920 incluiu alterações no `Jornada.Tests.csproj` e no lock desse projeto; o manifesto de candidato v5.00, porém, conservou o SHA anterior. O problema é descompasso de evidência da candidata, não autorização para mudar dependências científicas ou relaxar integridade.

## Correção técnica estritamente limitada e regressão

A partir do conteúdo textual completo de `Solution/tests/Jornada.Tests/packages.lock.json` no SHA da master, foi calculado SHA-256 `0b393ed4ca0a34bb2d2c4d646237cda9f38ecfcbbd8a50293dcbed3f0fa3aba0`, em substituição ao SHA candidato anteriormente fixado `9c582069d0f5d081a47df2bd372db979569379b7d122bfcb62034bab00e2902c`. O calculador independente foi conferido com o vetor padrão SHA-256 de `abc` (`ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad`). Reconstruído o SHA-256 combinado a partir das 24 linhas de overrides candidatos em ordem lexical pelo caminho, cada linha no formato `<sha256>  <path>`, com junção por `\\n`: resultado `f392a29b52bd616edfeddb3b789733a9917f86d1d3569e8de0c186ef48477362` no lugar do combinado anterior `b351eb6d53ef73bc68e81256e76d93d412df42a68d6fd957d090bfdb3ed84afa`.

**Limite explícito de evidência:** os outros 23 arquivos não foram todos conferidos individualmente na mesma sessão por limitação de chamadas à API. O novo combinado é válido **condicionalmente** à permanência de seus SHA-256 anteriores; por isso **é obrigatório rodar o gate original na CI do HEAD desta PR** e corrigir qualquer outra divergência real com hashes calculados, nunca inventados. O teste de regressão já existente `nuget-lock-provenance-gate.py` reproduziu a falha no HEAD antigo e deve passar sem relaxamento no HEAD reparado. Nenhum workflow/teste hipotético externo criado.

## Impactos, governança e precedência

Mudança restrita a `Solution/config/release/nuget-lock-provenance.json` nas duas entradas da candidata v5.00; **o atestado histórico v4.05, os locks originais, hashes de vencedores/modelos V9, `global.json`, SDK e os mecanismos de verificação permanecem intactos**. Não alterar código de produção, treino/TEST científico, parâmetros, LOCKED, DT-15, ou publicação. O mandato superveniente do PO autoriza a frente paralela corretiva, sem conceder merge automático da PR #1924, ativação de V9 ou conclusão da etapa 3. A sincronização do índice de decisões vigentes e o checkpoint na issue #1851 são obrigatórios. A mergeabilidade da PR corretiva separada não substitui o resultado dos checks nem o aceite do PO, quando requerido pela proteção do repositório.
