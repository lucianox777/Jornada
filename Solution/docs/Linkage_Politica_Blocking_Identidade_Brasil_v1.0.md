# Linkage — Política de Blocking e Resolução de Identidade no Brasil

**Decisão arquitetural complementar e fonte canônica para índices de nome/IBGE:** [Blocking complementar com referência IBGE — 27/09/2026](Decisao_Arquitetural_Blocking_Complementar_IBGE_20260927.md). Nome completo, dinâmico e combinado são capacidades **complementares** do mesmo universo de candidatos; CPF confiável mantém sua rota determinística independente. Esta política não constitui autorização de ativação do protótipo combinado no Runner.

**Versão:** 1.0  
**Data:** 10/09/2026  
**Escopo:** Jornada do Cidadão — resolução de identidade e geração de candidatos

## Princípio central

No contexto brasileiro, CPF válido e estruturalmente confiável é a âncora determinística prioritária de identidade. Quando informado e válido, o CPF não participa do score probabilístico nem de uma combinação de blocking: ele é usado antes desse estágio para resolver a associação estável `CPF -> pessoa_uuid`.

O blocking probabilístico existe principalmente para registros em que o CPF está ausente por hipótese admitida. CPF informado porém estruturalmente inválido não deve ser convertido silenciosamente em ausência de CPF para ampliar candidatos; a inconsistência deve permanecer explícita.

## Hierarquia operacional

1. **CPF válido** — resolução determinística pela associação estável `CPF -> pessoa_uuid`. Não executar score probabilístico apenas para confirmar um CPF válido.
2. **CPF inválido** — conflito cadastral explícito; não degradar automaticamente para linkage probabilístico como se o CPF nunca tivesse sido informado.
3. **CPF ausente por motivo admitido** — executar geração de candidatos por blocking versionado e, depois, aplicar o modelo probabilístico somente sobre o universo candidato.

Essa hierarquia reduz custo e ambiguidade sem misturar a natureza de um identificador civil forte com evidências probabilísticas.

## Semântica temporal dos atributos

CPF e data de nascimento têm semântica estável. Em condições normais, são invariantes da Pessoa. Uma alteração nesses valores representa correção excepcional ou conflito de evidência e deve permanecer auditável; não deve ser tratada como simples alias equivalente.

Nome da pessoa e nome da mãe têm semântica versionável. Mudanças legítimas, correções ortográficas e formas anteriores podem permanecer úteis para reencontrar a mesma Pessoa. Por isso o blocking pode usar aliases históricos vindos das observações Silver vinculadas ao UUID, enquanto a Gold continua representando o valor corrente selecionado.

Um alias histórico serve apenas para recuperar candidatos. Encontrar uma Pessoa pelo nome anterior não significa declarar aquele nome como atual e não substitui a decisão posterior do score/modelo.

## Espaço mínimo do blocking sem CPF

O Calibrador deve avaliar combinações e múltiplos passes usando, quando disponíveis:

- nome completo normalizado, corrente e aliases históricos;
- primeiro nome/prenome;
- componentes internos derivados dos tokens posteriores ao primeiro nome;
- último token do nome como feature interna;
- nome completo da mãe, corrente e aliases históricos;
- primeiro nome da mãe;
- componentes internos derivados dos tokens posteriores ao primeiro nome da mãe;
- último token do nome da mãe como feature interna;
- dia de nascimento;
- mês de nascimento;
- ano de nascimento.

Os nomes físicos históricos `name_surnames`, `name_last`, `mother_name_surnames` e `mother_name_last` são mantidos por compatibilidade do vocabulário de blocking. Eles descrevem **features heurísticas internas derivadas por tokenização** e não declaram que esses tokens sejam sobrenomes estruturados ou equivalentes ao campo `SOBRENOME` publicado pelo IBGE.

Nenhuma combinação fixa é declarada universalmente ótima. O Calibrador deve medir cobertura dos vínculos verdadeiros, redução do universo candidato, tamanho dos blocos, custo e estabilidade. Uma política pode utilizar vários passes complementares para reduzir falsos negativos de blocking.

## Nome da mãe

`nome_mae` é evidência de alta relevância prática no linkage brasileiro e já integra o score probabilístico da Jornada. Para blocking, sua decomposição segue a mesma normalização canônica usada para nomes da pessoa, preservando a semântica atual: normalização Unicode, remoção de diacríticos, caixa alta e normalização de espaços, sem remoção oculta de partículas, fonética ou correção ortográfica.

O Calibrador pode concluir que determinadas chaves isoladas — por exemplo partículas muito frequentes — não são seletivas. Essa decisão deve vir das métricas e não de uma lista manual não versionada.

## Uso do IBGE

Estatísticas oficiais de nomes só podem fornecer **frequências diretamente atribuídas** a componentes semanticamente compatíveis. Para **planejamento de índices por presença**, a marginal IBGE de sobrenomes em qualquer posição também pode ser usada como referência **auxiliar**, com proveniência e validação explícita no corpus; não é frequência direta de último token:

- primeiro nome da pessoa e primeiro nome da mãe podem usar estatística oficial de primeiro nome;
- as features internas derivadas por tokenização (`name_surnames`, `name_last`, `mother_name_surnames`, `mother_name_last`) **não** recebem a marginal oficial como se fosse a frequência observada exata da feature; podem usá-la **como referência auxiliar para desenho do índice por presença e análise de sensibilidade**;
- nome completo e nome completo da mãe não recebem frequência IBGE direta quando a fonte oficial não fornece essa mesma semântica;
- a classe estatística oficial de sobrenome permanece disponível na referência tipada para um futuro atributo cuja origem preserve uma fronteira estruturada e semanticamente compatível;
- data de nascimento e outros atributos só recebem enriquecimento externo quando houver fonte oficial explicitamente compatível.

A frequência IBGE é contexto estatístico agregado. Não substitui dados da Jornada, não decide identidade individual e não transforma localidade estatística em prova de residência. **A coleta de todos os sobrenomes, com último sobrenome como alternativa, e a publicação sem importar a posição são vantagens para o índice invertido por presença**: a mesma chave pode recuperar sobrenomes intermediários e finais. A proibição restante é **atribuir diretamente** `Surname` à frequência posicional de `name_last` ou presumir que toda tokenização de `nome_completo` identifica sobrenomes estruturados. O uso **auxiliar** da marginal por presença no blocking é permitido, sob ensaio/versionamento e correspondência semântica declarada.

## Projeção física

As chaves derivadas são materializadas em `identidade.blocking_chave` como projeção reconstruível da Gold, do histórico Silver e da versão de normalização. O índice estável sobre normalização, atributo, valor e vigência permite que diferentes versões do Calibrador experimentem combinações sem executar DDL por ruleset.

Para nomes, a projeção registra valor corrente e aliases históricos. Para nascimento, a projeção materializa somente o valor estável corrente; valores antigos corrigidos permanecem na trilha histórica da origem, mas não são mantidos automaticamente como aliases concorrentes do blocking.

O CPF não precisa ser duplicado nessa projeção para cumprir a política principal: sua rota determinística continua usando a associação CPF/UUID já protegida pelo modelo de identidade.

## Regra de qualidade do blocking

O objetivo do blocking não é decidir quem é a mesma pessoa; é evitar comparar cada registro contra toda a população sem excluir indevidamente o verdadeiro candidato. Portanto, a métrica prioritária é preservar alta cobertura/recall de vínculos verdadeiros com redução significativa do universo candidato. O score probabilístico posterior continua responsável pela decisão entre os candidatos encontrados.

Uma estratégia mais seletiva não deve ser promovida se o ganho de desempenho vier acompanhado de perda não aceitável de verdadeiros candidatos. O Calibrador deve comparar os passes individualmente e em união, e o Avaliador deve testar exatamente a versão publicada da política.
