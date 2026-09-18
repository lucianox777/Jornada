# ADR-004 — Runtime relacional único em Microsoft SQL Server

- **Status:** Aceita
- **Data:** 2026-09-18
- **Escopo:** candidata Solution Engenharia v5.00

## Contexto

A Jornada acumulou uma implementação operacional paralela em PostgreSQL ao lado do contrato Microsoft SQL canônico. Essa linha incluía adapter ADO.NET/Npgsql, persistência de identidade, coordenação, DDL, calibrador de Linkage, runner, testes e workflows próprios.

Embora SQL Server permanecesse declarado como tecnologia relacional normativa, manter duas persistências operacionais exigia paridade contínua de schema, locking, identidade, calibração e promoção. Isso aumentava a superfície de divergência e contrariava o fechamento arquitetural da candidata v5.00 em torno de uma única persistência operacional.

## Decisão

A candidata v5.00 da Jornada possui **um único runtime relacional suportado: Microsoft SQL Server**.

Consequentemente:

1. `Database:Provider` não seleciona PostgreSQL;
2. Npgsql não é dependência do produto corrente;
3. DDL, repositórios, coordenação, calibrador e runner PostgreSQL não pertencem ao runtime desta Jornada;
4. gates de paridade PostgreSQL não participam do CI/release da candidata;
5. SQL Server 2022 Developer/Testcontainers permanece baseline de desenvolvimento, CI e testes;
6. a edição/infraestrutura efetiva de HML/Produção deve preservar o mesmo contrato Microsoft SQL homologado para a release;
7. SQL Database in Microsoft Fabric não é alvo operacional nem gate da candidata v5.00; evidências anteriores são histórico de compatibilidade;
8. Lakehouse/SQL Analytics Endpoint permanecem analíticos/compatibilidade.

## Destino da implementação PostgreSQL anterior

A implementação anterior permanece recuperável pelo histórico Git. Se houver interesse em evoluí-la, ela deve seguir em projeto/repositório independente, com ciclo de versão, testes e decisões próprios.

A Jornada não mantém uma cópia “legacy” dentro do source tree corrente, pois isso recriaria a obrigação de paridade que esta decisão elimina.

## Consequências

- reduz-se a superfície de dependências, DDL e workflows do produto;
- desaparece a segunda persistência operacional de identidade;
- diferenças de dialeto deixam de ser requisito de novas features;
- documentação corrente deve falar em SQL Server único;
- referências PostgreSQL remanescentes só são aceitáveis em material explicitamente histórico, nunca como capacidade suportada corrente;
- remoção de ramificações mortas de dialeto em código compartilhado pode ocorrer incrementalmente, desde que nenhum entrypoint/factory permita executar PostgreSQL.

## Não decidido aqui

Esta ADR não escolhe edição/licenciamento de SQL Server para Produção, não altera regras de identidade/linkage e não antecipa aprovação institucional ou estatística da release.
