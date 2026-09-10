# Linkage — Política de Blocking e Resolução de Identidade no Brasil

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

## Espaço mínimo do blocking sem CPF

O Calibrador deve avaliar combinações e múltiplos passes usando, quando disponíveis:

- nome completo normalizado;
- primeiro nome/prenome;
- sobrenomes;
- último nome;
- nome completo da mãe;
- primeiro nome da mãe;
- sobrenomes da mãe;
- último nome da mãe;
- dia de nascimento;
- mês de nascimento;
- ano de nascimento.

Nenhuma combinação fixa é declarada universalmente ótima. O Calibrador deve medir cobertura dos vínculos verdadeiros, redução do universo candidato, tamanho dos blocos, custo e estabilidade. Uma política pode utilizar vários passes complementares para reduzir falsos negativos de blocking.

## Nome da mãe

`nome_mae` é evidência de alta relevância prática no linkage brasileiro e já integra o score probabilístico da Jornada. Para blocking, sua decomposição segue a mesma normalização canônica usada para nomes da pessoa, preservando a semântica atual: normalização Unicode, remoção de diacríticos, caixa alta e normalização de espaços, sem remoção oculta de partículas, fonética ou correção ortográfica.

O Calibrador pode concluir que determinadas chaves isoladas — por exemplo partículas muito frequentes — não são seletivas. Essa decisão deve vir das métricas e não de uma lista manual não versionada.

## Uso do IBGE

Estatísticas oficiais de nomes podem enriquecer componentes semanticamente compatíveis:

- primeiro nome da pessoa e primeiro nome da mãe podem usar estatística de primeiro nome;
- sobrenomes/último sobrenome da pessoa e da mãe podem usar estatística de sobrenome;
- nome completo e nome completo da mãe não recebem frequência IBGE direta quando a fonte oficial não fornece essa mesma semântica;
- data de nascimento e outros atributos só recebem enriquecimento externo quando houver fonte oficial explicitamente compatível.

A frequência IBGE é contexto estatístico agregado. Não substitui dados da Jornada, não decide identidade individual e não transforma localidade estatística em prova de residência.

## Projeção física

As chaves derivadas são materializadas em `identidade.blocking_chave` como projeção reconstruível da Gold e da versão de normalização. O índice estável sobre `(normalizacao_versao, atributo, valor_normalizado, pessoa_uuid)` permite que diferentes versões do Calibrador experimentem combinações sem executar DDL por ruleset.

O CPF não precisa ser duplicado nessa projeção para cumprir a política principal: sua rota determinística continua usando a associação CPF/UUID já protegida pelo modelo de identidade.

## Regra de qualidade do blocking

O objetivo do blocking não é decidir quem é a mesma pessoa; é evitar comparar cada registro contra toda a população sem excluir indevidamente o verdadeiro candidato. Portanto, a métrica prioritária é preservar alta cobertura/recall de vínculos verdadeiros com redução significativa do universo candidato. O score probabilístico posterior continua responsável pela decisão entre os candidatos encontrados.

Uma estratégia mais seletiva não deve ser promovida se o ganho de desempenho vier acompanhado de perda não aceitável de verdadeiros candidatos. O Calibrador deve comparar os passes individualmente e em união, e o Avaliador deve testar exatamente a versão publicada da política.
