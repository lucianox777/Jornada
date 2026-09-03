# Chaves e secrets — v3.43

Produção/HML deve usar o secret store/identidade corporativa. Nenhuma chave real deve ser versionada.

## Development local

O pacote contém `test-access-keys.json`, com credenciais **sintéticas, pré-geradas e regeneradas nesta release** para os Gestores e Tipos de exemplo. Não existe gerador de chaves dentro da API nem projeto `Jornada.DevKeys` na Solution.

`Jornada.Api` registra `DevelopmentAccessContextResolver` apenas quando `ASPNETCORE_ENVIRONMENT=Development`. Somente nesse ambiente o arquivo pode ser carregado. Em qualquer outro ambiente, a API usa o resolver deny-by-default até a integração corporativa.

Consequências:

- as chaves de `test-access-keys.json` são conveniência de teste, não segredo operacional;
- não devem ser copiadas para HML/Produção;
- uma configuração de Produção não pode “habilitar” esse arquivo, pois a implementação de Development nem é registrada fora de `Development`;
- rotação e emissão real de credenciais pertencem ao mecanismo corporativo, não à Jornada.Api.

`secrets.example.json` continua sendo apenas referência de configuração e não contém segredo real.


## Autorização e recursos

As fixtures de Development não carregam catálogo ou allowlist de finalidade. A autorização da Fase 1 usa credencial, scopes e recursos autorizados; o consumidor não precisa declarar motivo de consulta.

## Integridade da auditoria DEV

Os `credentialId` das nove fixtures são determinísticos e coincidem com as linhas sem segredo criadas por `database/Jornada_Seed_Dev.sql`. Isso é obrigatório porque `controle.api_evento.credencial_id` possui FK para `controle.credencial_api`; uma fixture com ID divergente faria a persistência da auditoria falhar. Há testes unitários e de seed para essa correspondência.

A v3.47 remove `ref.finalidade`, `ref.finalidade_versao`, `controle.credencial_finalidade` e o scope `jornada.primeiro_atendimento`. Restrições negativas de projeção permanecem governadas pelo Gestor responsável e não dependem de finalidade.

## CPF

Na Fase 1 o CPF não é cifrado/tokenizado pela aplicação, pois é utilizado na resolução determinística e na avaliação de qualidade. Seu acesso permanece restrito às camadas operacionais autorizadas. O CPF do munícipe não deve aparecer em logs, headers, auditoria HTTP ou modelo semântico de BI. TLS, criptografia de disco/backup e controles de acesso da infraestrutura permanecem obrigatórios.

## Credencial do armazenamento Bronze

Em HML/Produção, `BronzeStorage:RootPath` aponta para armazenamento compartilhado/durável. A identidade/credencial de acesso ao storage deve vir do mecanismo corporativo, com mínimo privilégio e fora do Git. `objeto_chave`/SHA-256 não é segredo nem autorização; a raiz não deve ser exposta publicamente. A API precisa criar/ler objetos no prefixo Bronze e o Processor precisa ler; permissão de deleção deve pertencer apenas ao processo operacional de retenção/expurgo quando aplicável. Em v3.38, somente `Jornada.Bronze.Maintenance.Worker` deve possuir delete físico; API/Processor não precisam dessa permissão.


## Backup/restore da Bronze

A propriedade de recuperação é referencial: toda `objeto_chave` citada por qualquer backup SQL ainda dentro da janela restaurável deve permanecer recuperável no storage. A infraestrutura pode usar backup, replicação, versionamento, soft delete ou imutabilidade conforme a tecnologia, mas deve testar restore SQL + disponibilidade de todos os objetos referenciados. Objetos extras no storage após um restore do banco não comprometem o replay.


## HMAC do CPF do agente

`X-Jornada-Agente-CPF` é um dado opcional declarado pelo sistema finalístico. A Jornada não autentica o agente e não verifica a associação entre usuário e CPF. Quando o header é informado, a API valida o CPF e persiste apenas HMAC-SHA-256 com separação de domínio e versão da chave. Em Development, `test-agent-hmac-key.txt` contém chave sintética; HML/Produção devem fornecer `AgentAudit:HmacKeyBase64` por secret store/variável protegida. O valor real nunca deve ser versionado.


## Integridade de identidade v3.43

O CPF do munícipe que estiver `EM_CONFLITO` no `identity_map` não resolve para UUID até correção governada. As operações de detalhe e correção exigem credencial institucional GESTOR e scopes específicos. Isso é independente do CPF opcional do agente: a Jornada continua sem autenticar o usuário humano do sistema finalístico.
