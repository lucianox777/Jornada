# GOV-CI-C3.2d-DNS-BUILD-ONLY-20261111 — correção restrita de DNS no build do E2E privado

**Autoridade e contexto:** continuidade da determinação expressa do Product Owner em 11/11/2026: “Alguma coisa mudou? Continue e corrija”; após alteração de configuração externa, “Eu mudei uma configuração, tente novamente” e “Continua”. **GOV-DOC-01.** Esta autorização cobre remediação técnica por evidência, não mudança científica na calibração V9, nem permissão de TEST, Gold, implantação ou alteração de política operacional do serviço.

## Evidência e escopo do defeito

- `jornada-ci` no SHA de `master` `0d3134584923c534b4600161ba55770ea47b8c1b`, [run #43056390181](https://github.com/lucianox777/Jornada/actions/runs/43056390181), tentativa 1: job `e2e-private-sql` **FAIL**, artefato `e2e-private-sql-evidence` #13869844526. O SQL privado e seu bootstrap passam; no build de imagem da API, APT tenta HTTPS, mas não resolve `archive.ubuntu.com` e `security.ubuntu.com`, não instala `jq` e `curl`, exit 100. A API não chegou a iniciar.
- Após o usuário comunicar mudança de configuração, foi acionada **somente a repetição dos jobs reprovados** no SHA idêntico (`rerun_failed_workflow_run_jobs`): [mesmo run #43056390181, tentativa 2](https://github.com/lucianox777/Jornada/actions/runs/43056390181). O SQL privado passou novamente e o **mesmo erro DNS APT** voltou no artefato #13872118983: `api-image-build.log`, `Could not resolve 'archive.ubuntu.com'`, `Could not resolve 'security.ubuntu.com'`, `E: Unable to locate package jq/curl`, exit 100. Isso rejeita a hipótese de que a alteração externa, isoladamente, eliminou o defeito para o contêiner.
- A mesma tentativa 1 teve instalação de Chromium em `unit-runtime` e `e2e-console` prejudicada por `Temporary failure resolving 'azure.archive.ubuntu.com'`, evidenciando **risco adicional de DNS do runner host**. Esta remediação do build **não** afirma corrigir DNS externo do host nem certifica a CI integral. Não esconder FAIL nem pular o browser.

## Decisão técnica corretiva de menor alcance

O script CI-only `Solution/scripts/e2e-private-sql-bootstrap-runtime.sh` mantém integralmente `compose build api` como **primeira tentativa**. Se e somente se falhar e o novo classificador `e2e-private-sql-api-dns-retry-gate.py` constatar, no log real e limitado a 4 MB, a combinação exata de:
1. `Could not resolve 'archive.ubuntu.com'`;
2. `Could not resolve 'security.ubuntu.com'`;
3. `E: Unable to locate package jq`;
4. `E: Unable to locate package curl`;
5. `exit code: 100`;

então o executor permite **uma única** segunda tentativa de `docker compose build api` com **override Compose temporário** contendo apenas `services.api.build.network: host`. O override é gerado com `umask 077`, usado **exclusivamente no comando build**, e excluído após a tentativa. O `compose up -d --no-build --no-deps api resultado-api` continua usando **somente o Compose original**; os contêineres API, ResultadoApi, SQL e workers continuam na rede privada sem portas externas, sem volumes host, sem `network_mode: host`. Os testes de readiness, SQL, HTTPS, supervisor, auditoria e o gate `e2e` não são relaxados nem substituídos.

Qualquer outro erro de build, log inconclusivo, assinatura parcial, segunda tentativa fracassada ou inobservância do ambiente CI mantém o **FAIL fechado**. Não contornar assinatura GPG, não substituir pacote, não fazer `apt insecure`, não instalar em host produtivo. Rede do host **apenas durante o build em runner GitHub efêmero** não configura concessão de rede aos serviços. A viabilidade desse método depende das permissões do Docker BuildKit e da resolução DNS do host e **deve ser comprovada empiricamente**; não declarar correção final pelo código apenas.

## Impactos, testes e precedência

Arquivos alterados: script operacional do E2E privado, classificador puro de logs (sem ações externas), testes unitários offline, gate PR obrigatório; demais imagens, Dockerfiles, Compose canônico, scorer V9, `plan.json` 324×100k, FDR, P95, fanout, políticas TF, BUILD e TEST permanecem **inalterados**.

Os testes verificam que só a assinatura completa permite retry, que erros genéricos ou log ausente falham, e que `network: host` não aparece no Compose de runtime. A CI deve validar esta PR; após squash na `master`, verificar novo `jornada-ci` com **E2E físico real**. Se DNS externo do host persistir, reconhecer impedimento de infraestrutura; não declarar PASS fictício nem reexecutar calibração 100k para solucionar CI.

**Precedência:** complementa a [decisão CI/GOV existente](Decisao_GOV_OPS_Correcao_Erros_Squash_CI_Master_20261111.md) e as decisões anteriores de HTTPS + APT retries, preserva a [decisão da nova busca TF4 V3](Decisao_GOV_V9_V3_TF4_324_100k_20261111.md), a campanha antiga [24/24 PASS](Evidencia_V9_100k_TRAIN_Tecnico_PASS_Run_43035295017_20261111.md) e o LOCK científico. **Sem execução da campanha 324** por esta correção.

## Aditamento de implementação — BuildKit requer autorização explícita (11/11/2026)

A CI pós-merge da correção inicial, [run #43061411719](https://github.com/lucianox777/Jornada/actions/runs/43061411719), job `e2e-private-sql`, executou o SQL privado e o classificador DNS corretamente, mas a segunda tentativa de build falhou **antes de compilar**. O artefato imutável **#13874331488**, `api-image-build-host-network.log`, mostra literalmente: `additional privileges requested: pass "--allow=network.host" to grant requested privileges`. Portanto, **o código anterior não completou o retry**; não é regressão V9 nem melhora comprovada.

**Correção estritamente subordinada à mesma decisão e mesma assinatura DNS:** substituir **apenas** o comando da segunda tentativa `docker compose ... build api` por `docker buildx bake --allow=network.host --load -f "$COMPOSE" -f "$override" api`, que aceita o Compose original e o único override temporário, e concede explicitamente o entitlement exigido pelo BuildKit. `--load` requer exportação da mesma imagem para o daemon local, para a execução normal posterior `compose up --no-build`. O primeiro build permanece `compose build api` sem host networking; o retry segue condicional a cinco assinaturas exatas e limitado a uma única tentativa. O `compose up` posterior **não recebe** `--allow` nem `$override`; runtime API/SQL continua privado.

**Limites:** entitlement `network.host` é uma ampliação **temporária e explícita de alcance da rede durante a compilação Docker somente em CI efêmera**; não altera Dockerfile, banco, TEST, credenciais, isolamento de processos ou política científica. A correção não garante que o DNS do host esteja saudável nem que a nova imagem construa: o resultado exige CI física nova no SHA final, com E2E completo obrigatório. Evidência anterior `#43061411719` tem `unit-runtime` e `e2e-console` PASS, além de builds/segurança, mas `e2e-private-sql` FAIL; não considerar CI SUCCESS por causa do aditamento.

**Rastreabilidade:** esta é uma implementação de correção de comando necessário, sob o mandato existente do PO para continuar e corrigir. O documento e os dois índices continuam a registrar **CORREÇÃO EM VALIDAÇÃO** até a nova prova física. Sem qualquer execução da campanha TF4 324.
