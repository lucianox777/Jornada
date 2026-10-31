> **Decisão V9, 13/10/2026:** não exigir comparação V8×V9
> como requisito para desenvolver, calibrar ou julgar se a V9
> funciona. A V8 só permanece para replay histórico.
> A V9 será avaliada diretamente por gabarito, TRAIN/VALIDATION/TEST,
> falsos vínculos, recall, suporte e modelo estatístico. A DT-15
> conserva exclusivamente o dossiê de substituição ATIVO × PROPOSTO
> quando houver uma decisão de ativação; esse dossiê não obriga
> superioridade da V9 em relação à versão histórica ativa.

# V9 — ligação read-only de TF de prenomes à referência IBGE publicada e fixada (13/10/2026)

## Entrega

O `FrozenIbgeNominalTfV9.LoadAsync` resolve **por ID explícito** a versão `ref.frequencia_nome_versao`, lê `codigo`, `status`, `conteudo_sha256` e rejeita `CARREGANDO`, `VALIDADA`, referência inexistente e hash ausente. Aceita `ATIVA` e `OBSOLETA` (esta somente para replay analítico reprodutível): não consulta a versão ativa do dia, não troca ID e não carrega novas frequências no hot path. Em seguida emprega `NominalTermFrequencyReferenceStore.PrepareAsync`, já restrito às marginais publicadas **pessoa = prenome TOTAL do município de São Paulo 3550308** e **mãe = prenome FEMININO Brasil**, com cobertura declarada. Reconstrói a tabela de entradas para detectar valores divergentes e fixa uma identidade SHA-256 própria do subconjunto materializado, distinta da hash de conteúdo da referência IBGE completa. A hash IBGE do catálogo segue como identidade da versão, não é recalculada a partir do subconjunto TF.

`CreatePreview` fornece ao scorer FS **apenas para `ExactFirst`** a frequência do prenome que foi realmente encontrada no snapshot fixado; o chamador **não informa nem substitui `TermFrequency`**. O contexto `PopulationFrameFingerprint` inclui código e hash publicados e o universo geográfico de cada lado; `SINTETICO_IBGE`, a identidade do corpus sintético 30 mil, seed, versão do gerador e hipóteses permanecem exigidos pelo guard canônico. Peso `w_TF`, `UnitReferenceU`, floor e suporte mínimo continuam argumentos **explícitos de calibração**; a fábrica **não escolhe** peso/limiar nem infere `u` do IBGE. A avaliação usa o scorer FS C# existente com **uma única contribuição nominal conjunta**.

O retorno `null` com `weight=0` tem sentido **único e intencional de BF=1**, sem exigir uma frequência desabilitada; já com peso positivo, prenome desconhecido/suprimido, comparações inconsistentes, parâmetros inválidos e nível não suportado **falham fechado**. Não inferir desconhecido como `0` ou `1`. Não aplicar marginal de prenome ao `ExactFull` ou `ExactFirstLast`, nem fingir marginal de último sobrenome: ainda é preciso uma fonte separada explícita de proxy de sobrenome em qualquer posição, não contida nesta materialização.

## Limites importantes

O caminho SQL `LoadAsync` é o único que **confere** status/digest contra a versão publicada em `ref`. O caminho puro `BindPublished` só combina dados **já autenticados**; ele pode receber entradas artificiais em testes e **não certifica** a procedência por si. A hash materializada não equivale a validação populacional, e metadados `SINTETICO_IBGE` não provam amostragem real.

**Escopo estritamente PREVIEW/read-only:** o caminho de `GENERATE_DRAFT`, pesos V8, Rank/Resolve, VALIDATE/ACTIVATE, tabelas de modelos, referências IBGE publicadas e `JornadaLocal` não foram alterados. A PR não promete vinculação operacional V9 completa. Para avançar são necessários integração explícita com calibração/score V9 congelado, `u` dos passes de blocking SQL reais, holdout comparável V8×V9 e DT-15. A matriz 30 mil da PR #993 mede somente suporte M/U em seleção proxy; não autoriza afirmar suficiência.
