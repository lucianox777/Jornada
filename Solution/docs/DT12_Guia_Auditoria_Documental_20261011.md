# DT-12 — Guia de leitura e auditoria documental (11/10/2026)

**Natureza:** índice de navegação e controle de escopo, não é nova especificação nem altera decisões. **Estado:** proposta em PR; nenhuma exclusão, renomeação ou movimentação autorizada.

## Como identificar a regra aplicável

1. Começar pela [Especificação Técnica vigente e seus requisitos](../../Documentos/) e pela [Arquitetura de Identidade e Linkage](Arquitetura_Identidade_Linkage.md). Em caso de divergência, aplicar a hierarquia normativa documentada na arquitetura, não a data ou o nome de um arquivo avulso.
2. Consultar [Índice de Decisões Vigentes](Indice_Decisoes_Vigentes.md) e [Catálogo de Vigência Documental](Catalogo_Vigencia_Documental_20260929.md) para localizar normas aplicáveis e textos substituídos.
3. Para identidade/FS, consultar [Decisões Canônicas](Decisoes_Canonicas_Identidade_Linkage_20260929.md). Para diagnóstico de suficiência, consultar [DT-23](DT23_Diagnostico_Automatico_Suficiencia_FS.md): diagnóstico automático **não** aciona calibração, validação ou ativação.
4. Para comprovar aderência, relacionar requisito/decisão com implementação, PR, testes e evidências do HEAD e ambiente correspondente. **Documento de decisão não é prova de implementação**, e CI sintética não certifica operação populacional.

## Tecnologia vigente versus histórico

- O banco relacional operacional da candidata v5.00 é **Microsoft SQL Server**; PostgreSQL/Npgsql foi retirado do runtime candidato. Ver [ADR-004](../../Documentos/ADR/ADR-004-runtime-relacional-sql-server-unico.md), [Estado de Engenharia](../../Documentos/Estado_Engenharia_v5.00_Candidato.md) e [adaptador operacional](Operational_SQL_Adapter.md).
- Referências a PostgreSQL em inventários, histórico de branches, pareceres e relatórios antigos **não** constituem instruções operacionais vigentes. Não apagar essas referências para simular inexistência de decisões passadas.
- Instrução que **exija executar PostgreSQL hoje** deve ser classificada individualmente como possível conflito normativo/operacional, com caminho, trecho, versão, sucessor e correção proposta; uma simples ocorrência da palavra não basta para concluir erro.

## Arquivos de release e notas na raiz

O [inventário DT-12 de 26/09/2026](DT12_Inventario_Branches_NOTA_RELEASE_20260926.md) registrou **37 NOTA_*** e **24 RELEASE***, total **61 arquivos**, como fotografia datada, **não** contagem atual. A lista nominal e os hashes estão em [CSV de inventário](DT12_Root_NOTA_RELEASE_20260926.csv). Eles foram classificados para **preservação até aprovação**, por potencial vínculo com release e auditoria. Nenhum arquivo será excluído ou movido por este guia.

**Condição para arquivamento futuro:** obter autorização explícita para lista nominal, destino e retenção; atualizar inventário/SHAs e links; verificar tags, release assets, scripts e documentação; executar dry-run e backup recuperável; propor PR separada com mapa de caminhos antigos/novos e validação de links. Manter histórico Git intacto.

## Matriz mínima de auditoria por mudança

| Pergunta | Registro esperado |
| --- | --- |
| Qual regra vale? | Documento normativo, versão e seção |
| Por que foi escolhida? | Motivação, alternativas e riscos aceitos |
| O que foi implementado? | Código, migração e PR/commit |
| Como foi verificado? | Teste, CI no HEAD exato, corpus e ambiente |
| O que falta? | DT/issue aberta e critério objetivo de aceite |
| O que foi substituído? | Documento anterior, sucessor e vigência |

## Limites desta entrega

Este guia **não certifica** que todos os documentos estejam sincronizados, que não haja instrução obsoleta, nem que 61 arquivos ainda existam na raiz. Essas afirmações exigem inventário automatizado atualizado e conferência das referências. Próxima implementação: relatório reproduzível de links internos e instruções operacionais potencialmente obsoletas, com exceções explícitas para acervo histórico, sem bloqueio indiscriminado por palavras-chave.
