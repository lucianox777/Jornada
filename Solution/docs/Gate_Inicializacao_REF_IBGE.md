# Gate de inicialização da REF IBGE — contrato de implementação

**Estado:** especificação de implementação; não comprova execução do gate em produção. **Escopo:** somente REF e derivados IBGE. A autorização humana de modelos é tratada pela DT-15 em PR independente.

## Contrato

A preparação da REF é uma etapa de provisionamento/readiness **antes da primeira Entrega**, não um cálculo residente no boot de cada API ou Processor. Cada subida verifica localmente o estado persistido da referência canônica e dos derivados exigidos, sem download ou Monte Carlo quando a chave de dependências coincide. Uma instância executa o preparo por coordenação SQL; as demais aguardam ou reutilizam a saída confirmada. Não usar apenas o status ATIVA como prova de integridade.

O snapshot atual é `data/reference/ibge-nomes-2022`, código `CENSO2022_NOMES_BRASIL_V1`. A identidade efetiva inclui código, SHA-256 do conteúdo verificado contra o manifesto e contrato de projeção. Derivados `TODOS` (nome) e `FEMININO` (nome da mãe) são separados; sua chave inclui hash de origem, método/versão, comparador, escopo, marginais, seed, PairCount e política de cauda. A V2 municipal 3550308 é somente candidata para nome/sobrenome civil, sem fallback geográfico; não entra no gate operacional V1.

## Estados e transições

1. **PRONTA:** referência ativa única, conteúdo/manifesto íntegros e ambos derivados compatíveis e íntegros: cache hit, sem carga e sem recalibração.
2. **AUSENTE:** no primeiro provisionamento, validar snapshot local, carregar por versão/hash, calcular derivados ausentes, registrar proveniência e só então liberar a primeira Entrega.
3. **INCOMPLETA:** concluir ou recuperar carga/derivação interrompida sob lock; nunca declarar readiness pela existência parcial de linhas.
4. **DIVERGENTE:** bloquear nova preparação/publicação dependente até verificar a causa. Não substituir silenciosamente a referência ativa nem apagar a anterior.
5. **NOVA VERSÃO VERIFICADA:** preservar a anterior e materializar a nova versão em staging; calcular apenas derivados com chave alterada e promover a REF atomicamente após validação. Modelos já ativos continuam fixados à sua referência.

Alterações de algoritmo dos derivados invalidam apenas as chaves afetadas, mesmo sem alteração do IBGE. Mudança apenas de timestamp do arquivo, sem alteração de conteúdo, não invalida cache. Falha de checksum ou ausência de snapshot esperado falha fechado para primeiro provisionamento. Se já existe modelo ativo, preservar a operação com a referência antiga enquanto a nova é preparada, sem trocar parâmetros silenciosamente.

## Relação com o calibrador

A preparação da REF pode sinalizar necessidade de `GENERATE_DRAFT`, mas não executa `VALIDATE` nem `ACTIVATE`. Um novo rascunho somente é gerado se as dependências efetivas mudaram e houver corpus suficiente; mudanças no corpus podem justificar calibração independentemente do IBGE. A decisão e ativação cabem exclusivamente ao operador Master, conforme DT-15 em outra PR. Não duplicar interfaces administrativas ou lógica de autorização nesta entrega.

## Implementação incremental

Reutilizar `ENSURE_NAME_FREQUENCY_SNAPSHOT`, `ENSURE_IBGE_NOMINAL_U_REFERENCE`, `NameFrequencyReferenceState`, `IbgeNominalUReferenceStore` e coordenação SQL existente. Antes de alterar os entrypoints, inventariar todos os caminhos de provisionamento e confirmar se o cache existente já confere checksum/versão integralmente; adicionar apenas as verificações ausentes. O monitor permanece somente leitura.

## Gates de regressão

- Banco limpo: carga e derivados completos antes da primeira Entrega.
- Duas instâncias simultâneas: apenas uma materialização por chave; segunda recebe cache hit.
- Dois boots com mesma REF: nenhuma nova linha de versão, nenhuma execução Monte Carlo e nenhum novo modelo.
- Snapshot com mesmo conteúdo e timestamp diferente: cache hit.
- Snapshot com hash diferente: staging, validação e reconstrução apenas de dependências afetadas.
- Mudança do comparador/método/seed/PairCount: invalidação seletiva dos derivados.
- Falha na metade da carga: recuperação sem versão ATIVA parcial e sem perda da versão anterior.
- Referência divergente: readiness bloqueada no primeiro provisionamento, diagnóstico explícito no monitor.
- Modelo ATIVO permanece idêntico após atualização da REF; nenhum `VALIDATE` ou `ACTIVATE` automático.

**Aceite:** anexar resultados de testes unitários, integração SQL, concorrência e inicialização limpa/repetida à PR de implementação; esta especificação isolada não equivale à implementação.
