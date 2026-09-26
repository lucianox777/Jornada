# Estudo comparativo — motores de linkage e identidade progressiva

**Data:** 2026-09-26. **Natureza:** revisão documental de capacidades publicadas; não é benchmark executado, homologação de produto ou declaração de superioridade estatística. **Escopo:** identificação progressiva com novas fontes, correções, CPF tardio, decisões versionadas e reavaliação seletiva.

## Premissa da comparação

O objetivo da Jornada é manter a **melhor representação estatística disponível** da identidade, com incerteza explícita e revisão incremental, não alcançar certeza de vínculo nem decidir concessão de benefícios. Estados canônicos são hipóteses operacionais governadas; a responsabilidade pela decisão finalística e pela avaliação documental é da Secretaria competente. Comparar os sistemas também quanto à preservação de alternativas, incerteza, reversibilidade e explicabilidade, não somente à produção de clusters definitivos.

## Separar as duas camadas

1. **Scoring e recuperação de candidatos:** comparadores de identidade, bloqueio, probabilidades e explicação por campo. Splink é referência técnica para a tradução das capacidades Fellegi–Sunter para o motor C# único da Jornada, reutilizando a infraestrutura existente.
2. **Ciclo de vida da identidade:** chegada de novos registros e novas Secretarias, correção de registros antigos, alteração do conjunto de candidatos, possível fusão/separação de identidades, conflito, ledger, publicação e reprocessamento seletivo. A literatura de scoring não resolve automaticamente essa camada.

## Matriz de capacidades documentadas

| Sistema | Scoring / busca | Incremental / manutenção de entidades | Lição para a Jornada |
|---|---|---|---|
| Splink | FS, comparações e TF; `find_matches_to_new_records`; clustering por componentes conexos | Busca de novos registros suportada; mantenedores apontam ausência de clustering incremental completo sem recálculo de componentes históricos afetados | Referência do scorer C#, **não** substituir identidade progressiva por connected components |
| Senzing | Comparadores, regras de resolução e explicações | Reavalia registros e entidades, tem fila de redo e relata entidades afetadas; alterações de configuração podem exigir reavaliação explícita | Referência de invalidação por dependência, reavaliação seletiva e reporte de transições |
| Dedupe / Gazetteer | Aprendizado ativo e matching contra conjunto canônico | `index`, `unindex`, `search`; índice atualizável | Referência para índice de candidatos atualizável e busca no balcão, não ledger de identidade |
| AWS Entity Resolution | Matching ML incremental | Anunciou fluxos incrementais em maio/2026; documentação de lançamento enfatiza novos registros | Referência de processamento de deltas, sem inferir cobertura de correções históricas ou reversão de vínculos |

**Fontes:** Splink incremental https://github.com/moj-analytical-services/splink/discussions/2354 ; Splink clustering https://moj-analytical-services.github.io/splink/api_docs/clustering.html ; Splink busca em tempo real https://moj-analytical-services.github.io/splink/demos/examples/duckdb/real_time_record_linkage.html ; Senzing reprocessamento https://senzing.zendesk.com/hc/en-us/articles/360010709994--Advanced-Reprocessing-with-configuration-changes ; SDK Senzing https://garage.senzing.com/sz-sdk-python/senzing.html ; Dedupe https://docs.dedupe.io/en/latest/API-documentation.html ; AWS https://aws.amazon.com/about-aws/whats-new/2026/05/aws-entity-resolution-ml/ .

## Implicações de arquitetura

- Manter o motor operacional C# único com regras inspiradas no Splink, parâmetros e gates versionados e IBGE como bootstrap de frequências. CIDACS-RL continua referência metodológica para nomes brasileiros, não substituição da referência IBGE nem motor obrigatório.
- **Não adotar clustering transitivo ingênuo:** dois vínculos par-a-par acima do corte não bastam para autorizar uma fusão se houver conflito de CPF, homonímia ou restrição institucional. A representação canônica e a publicação de hipóteses permanecem governadas, sem transformar limiar estatístico em certeza civil.
- Manter hash semântico dos sinais **de identidade** aprovados; mudanças em benefícios, serviços ou atributos não identitários não disparam linkage. CPF presente/ausente não é proxy de qualidade; não presumir maturidade de fonte.
- Ao mudar uma observação ou uma referência, reavaliar o registro alterado **e** os pendentes potencialmente afetados nos blocos antigos e novos. Ao mudar modelo/guards/blocking, enfileirar reavaliação explicitamente versionada. Deduplicar trabalho.
- Distinguir **run realizado**, **decisão calculada** e **transição persistida**. Se decisão semântica não mudar, preservar o vínculo corrente sem duplicar a versão operacional; manter trilha de execução e auditoria. Se mudar, persistir transição e recompor Gold afetada com controles transacionais.
- Evitar varredura integral em toda entrega, mas manter mecanismo de reconciliação excepcional que detecte pendentes perdidos por índices de dependência incompletos. Provar correção com três ou mais ondas, chegada de CPF e nova Secretaria, conflito, candidato que sai de um bloco, atualização de modelo e concorrência.

## Decisão sobre benchmark próprio

A literatura documenta **capacidades**, não uma comparação controlada de precisão/recall, custo e correção do ciclo de vida entre Splink, Senzing, Dedupe e o motor da Jornada sobre a mesma população. Não alegar que tal estudo já foi feito. Primeiro reutilizar os resultados e a documentação existentes; só executar benchmark externo se houver decisão arquitetural que dependa de uma lacuna concreta. O Ensaio com dados fornecidos pelas Secretarias serve prioritariamente para comprovar as propriedades e o desempenho da implementação da Jornada, com verdade de referência quando disponível.

**Prioridade:** motor C# e identidade progressiva; contrato de busca FHIR no balcão posteriormente; BI depois do núcleo.
