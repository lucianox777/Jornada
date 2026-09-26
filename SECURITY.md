# Security Policy

## Status da candidata

A Jornada está em **candidata técnica v5.00**, ainda sem publicação normativa v5.00 e sem implantação HML/Produção autorizada.

Fora de `Development`, a aplicação permanece **deny-by-default** enquanto a identidade corporativa/autenticação PRODAM e a separação ambiental da issue #378 não forem integradas e homologadas. CI verde, fixtures sintéticas e readiness local não equivalem a autorização de Produção.

## Versões suportadas para correção

Enquanto a v5.00 não for cortada, correções de segurança devem ser aplicadas ao `master` candidato. A última release selada continua sendo a indicada por `RELEASE_INFO.txt`.

Após cada release, este arquivo deve ser atualizado para declarar explicitamente quais linhas recebem correções de segurança.

## Como reportar uma vulnerabilidade

Não publique detalhes exploráveis, credenciais, dados pessoais ou segredos em issue pública.

1. Prefira o mecanismo privado **Report a vulnerability / Private vulnerability reporting** do GitHub, quando habilitado para o repositório.
2. Se o mecanismo privado não estiver disponível, abra uma issue pública **sem detalhes técnicos do exploit**, solicitando um canal privado ao mantenedor.
3. Inclua no relato privado: componente/commit afetado, pré-condições, impacto, passos mínimos de reprodução e proposta de mitigação, se conhecida.

Nunca inclua dados reais de cidadãos, tokens, access keys, certificados, senhas ou dumps de banco no relato.

## Fronteiras de segurança correntes

- fixtures de credencial existem somente para desenvolvimento/testes sintéticos;
- HML/Produção não devem reutilizar chaves de Development;
- CPF, endereço e demais dados pessoais não devem ser copiados para logs, artefatos de CI ou evidências de teste;
- `ENDERECO_CASA_ABRIGO_SIGILOSA` permanece fora de projeções compartilhadas e de features de blocking/linkage;
- decisões governadas de identidade são auditadas em ledger append-only; a identidade individual do operador continua dependente da integração corporativa #378;
- mudanças de finalidade, base legal, compartilhamento, retenção ou acesso permanecem decisões externas e não podem ser presumidas pelo código (#379).

## Controles automatizados já existentes

O CI corrente executa Gitleaks versionado com checksum sobre a árvore corrente, análise estática/CodeQL, auditoria de vulnerabilidades NuGet e o `source-sanity-gate.py`, que bloqueia classes conhecidas de material sensível versionado, incluindo private keys/tokens, `.env`, senhas literais fora da allowlist e SQL com TLS enfraquecido fora dos cenários autorizados.

**Após o corte de `v5.00-rc.1`:** Gitleaks já está integrado ao CI para varredura obrigatória da árvore corrente (`security-secret-scan.sh --current-tree-only`). A **auditoria completa do histórico alcançável** usa o workflow manual `jornada-secret-history-audit` (`workflow_dispatch`, checkout `fetch-depth: 0`); não é gate executado em cada commit. Publicar apenas contagens agregadas e redigidas; achados requerem triagem humana na issue #405, sem baseline automático, reescrita de histórico ou homologação implícita. Detalhes operacionais: [Security_Secret_Scanning.md](Solution/docs/Security_Secret_Scanning.md).

## Incidente ou exposição de dados

Se houver suspeita de segredo ou dado pessoal exposto:

1. interrompa o uso da credencial/artefato afetado;
2. revogue/rotacione o segredo no sistema de origem;
3. preserve evidência técnica mínima sem propagar o dado;
4. identifique commits, artifacts e releases potencialmente afetados;
5. trate remoção de histórico como operação excepcional, separada da simples exclusão no branch corrente;
6. siga o processo institucional aplicável de incidente e privacidade quando existir dado real.

Este documento descreve o estado técnico do repositório; não substitui política institucional de segurança, LGPD, resposta a incidentes ou autenticação corporativa.
