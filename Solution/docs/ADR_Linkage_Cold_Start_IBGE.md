# ADR — Cold start do linkage: prior de m, u por IBGE e transição para calibração empírica

**Status:** aceita; implementação iniciada na PR #162.

## Contexto

O Fellegi–Sunter precisa de probabilidades condicionais `m` e `u`. No início da operação da Jornada pode não existir quantidade suficiente de pares verdadeiros independentes para estimar `m`, embora a plataforma já precise comparar observações sem identificador determinístico.

A Jornada dispõe de uma referência versionada de frequências de nomes do IBGE e, progressivamente, produzirá pares verdadeiros independentes por convergência determinística de CPF. O CPF não é resultado do linkage: a relação CPF→UUID é uma âncora permanente já governada pela Jornada.

## Decisão

A Jornada adota três estágios explícitos de parâmetros:

```text
PRIOR_BOOTSTRAP -> ESTIMADO -> HOMOLOGADO
```

`PRIOR_BOOTSTRAP` existe para cold start. Ele pode produzir score/LS para diagnóstico e resolução provisória, mas não pode ser ativado como modelo probabilístico homologado.

`ESTIMADO` significa que `m` já foi estimado com corpus independente suficiente, especialmente pares entre observações de fontes distintas que convergiram pelo mesmo CPF/UUID determinístico.

`HOMOLOGADO` continua dependendo do fluxo explícito de validação/aceite do modelo. Não existe salto direto de `PRIOR_BOOTSTRAP` para `HOMOLOGADO`.

## O IBGE não estima m

Frequência populacional de nomes não mede taxa de erro de transcrição da mesma pessoa. Portanto o IBGE não deve ser apresentado como fonte empírica de `m`.

Enquanto não houver corpus suficiente, `m` vem de um prior institucional versionado e auditável. O prior inicial pode, por exemplo, atribuir 0,90 à concordância exata de nome, mas esse valor deve permanecer identificado como `PRIOR_INSTITUCIONAL`, nunca como parâmetro calibrado.

Quando o número mínimo de pares CPF independentes for atingido, uma nova versão do modelo substitui o prior por `m` empírico. Modelos antigos permanecem reproduzíveis.

## Uso do IBGE para u

Para concordância exata de nome, frequências marginais permitem estimar a probabilidade de colisão entre duas pessoas distintas:

```text
u_nome_exact = Σ p(nome)^2
```

A referência IBGE é congelada por `frequencia_nome_versao_id` e SHA-256 já existentes no projeto.

O ranking publicado pode omitir/suprimir nomes raros. A ausência de um nome da publicação **não é frequência zero** e nunca pode produzir `u=0`. No bootstrap, a colisão é calculada sobre a massa efetivamente publicada e a origem do parâmetro fica explicitamente registrada como referência conservadora.

Frequências marginais do IBGE não permitem inferir, sozinhas, as probabilidades de estados fuzzy (`HIGH`, `MEDIUM`, `LOW`) baseados em distância de strings. Esses estados usam priors conservadores no bootstrap e depois devem ser substituídos por estimativa/replay empírico.

A frequência nacional de nomes também não é uma calibração específica de `nome_mae`. Se usada provisoriamente como proxy para esse campo, a condição deve ficar marcada (`U_NOME_MAE_IBGE_PROXY`) e ser substituída assim que houver corpus adequado por perfil/fonte.

## Nascimento

No bootstrap, data de nascimento é uma única evidência probabilística versionada:

```text
DATA_NASCIMENTO_EXACT / DATA_NASCIMENTO_DIFF
```

Dia, mês e ano podem continuar existindo como atributos para blocking, índices e diagnóstico, mas não devem ser somados como três evidências probabilísticas independentes do mesmo evento de transcrição.

## Promoção

Um modelo com `estagio_parametro = PRIOR_BOOTSTRAP` deve ter `promocao_automatica_permitida = 0` e o banco deve rejeitar `status = ATIVO`.

O comportamento esperado é:

```text
poucos/zero pares CPF independentes
    -> m prior institucional
    -> u_nome_exact por IBGE
    -> score bootstrap
    -> somente PROVISORIA/CANDIDATA_PROBABILISTICA

pares CPF independentes >= mínimo governado
    -> nova versão
    -> m empírico
    -> estágio ESTIMADO
    -> validação
    -> HOMOLOGADO/ATIVO quando aprovado
```

O bootstrap nunca é retroativamente renomeado como calibrado. A transição gera nova versão para preservar replay.

## Relação com déficits metodológicos

- **P1:** pares CPF independentes são definidos fora do blocking probabilístico, evitando avaliar recall apenas sobre candidatos que o próprio blocking encontrou.
- **P2:** nascimento é uma única evidência probabilística; decomposição continua permitida para blocking.
- **P3:** a proveniência do corpus de `m` deve ser registrada; o bootstrap não mascara transportabilidade como calibração.
- **P6:** pares CPF devem preferir fontes realmente independentes; copiar o mesmo dado upstream não deve inflar artificialmente `m`.
- **P8:** ausência/supressão no IBGE não vira `u=0`.
- **P26:** observações sem identificador podem receber score bootstrap, mas sua consequência operacional é provisória até a política do perfil e o modelo estarem homologados.

## Auditabilidade

Cada modelo deve expor pelo menos:

- `estagio_parametro`;
- `m_origem`;
- `u_nome_origem`;
- `promocao_automatica_permitida`;
- `frequencia_nome_versao_id`;
- tamanho da amostra `m` e `u`;
- parâmetros efetivamente utilizados;
- versão do algoritmo/normalização.

Dessa forma, um LS produzido no cold start jamais é confundido com um LS produzido por um modelo municipal empiricamente calibrado e homologado.
