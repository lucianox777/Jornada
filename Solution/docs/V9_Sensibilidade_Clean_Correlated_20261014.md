# V9 — comparação real do bloqueio SQL sob cenários clean e correlated

Amplia o diagnóstico de proveniência dos **18%** com experimento
de **sensibilidade** inteiramente sintético. Uma execução
do workflow `v9-synthetic-corruption-evidence` regenera no
mesmo runner C# dois conjuntos demográficos primários:
`--people 30000 --seed 42 --error-profile clean` e
`--people 30000 --seed 42 --error-profile correlated`.

Ambos utilizam **as mesmas referências IBGE**: pessoa NOME/
SOBRENOME município 3550308, prenome materno feminino Brasil,
sobrenome materno Brasil e a mesma referência diária SP
de nascimento, com verificação fail-closed de `manifest.json`.
Os manifests passam a registrar explicitamente erro hipotético,
nome do perfil, probabilidades de tentativas e hashes.

O script `v9-synthetic-clean-correlated-sensitivity.py` exige
mesmo `input_fingerprint_sha256`, seed, versão de regras,
PRNG, referência demográfica, e perfis **diferentes**.
Recusa comparação de manifestos sem rótulo de perfil.

Para cada cenário, a rotina executa, em SQL SQLite **indexado**
`:memory:`, os **mesmos 19 passes físicos diagnósticos**
com exportador C# V2 de nome normalizado, fonética e
sobrenomes estruturados. Aplica sobre TRAIN o mesmo algoritmo
de seleção min-U sob mínimo de 95% de recall global, e
congela sua escolha para VALIDATION. Não toca TEST.
Publica agregados `UNION_EXPANDED` e `greedy_pruned`:
M/U, recall sobre **todos os M sem CPF**, cobertura elegível,
grau U médio/P95/P99 com observações grau zero e limite inferior
unilateral bootstrap por identidade-base.

**Mesma seed NÃO garante as mesmas pessoas-base entre perfis**:
o gerador faz números distintos de draws em cada ramo
de corrupção. Trata-se de **comparação não pareada**, com
mesmo modelo de referência e semente, não experimento
contrafactual que só muda o erro mantendo pessoas fixas.
Para teste estritamente pareado será preciso versão de gerador
com substreams independentes por pessoa e atributo,
documentada e testada, sem reescrever evidência histórica.

Esse ensaio não pressupõe que `clean` represente São Paulo,
não altera parâmetros dos cenários para passar no recall 95%.
Uma melhora de recall no clean mede **sensibilidade à
hipótese**. A decisão do blocking continua automática
somente dentro da política de custo aprovada, em TRAIN,
com validação independente. Perfil de erro real demanda
rótulos de origem auditados por gestor e período e incerteza.

A execução não ativa Calibrador, Runner, V9/DT-15,
não atualiza Gold/V8, m/u, prior, TF, thresholds, IBGE
original, HML/PROD, JornadaLocal, NODE/NAS, nem usa
aceitação FS como prova de FDR. SQL Server físico e
custo em 11,45 milhões continuam sem medição.
