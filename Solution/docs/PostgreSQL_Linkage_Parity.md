# PostgreSQL — Linkage: paridade incremental

A implementação PostgreSQL é paralela. SQL Server e Fabric mantêm seus papéis canônicos. Os gates de CI não constituem homologação institucional, de escala ou de produção.

## Componentes

| Componente | Estado PostgreSQL | Próximo gate |
|---|---|---|
| Contratos, normalização e Fellegi–Sunter | Compartilhados com SQL Server; sem alteração de fórmula ou limiares | Homologação estatística em corpus representativo |
| Catálogo e scorer | Leitura versionada, V1 e cinco passes V2, limite explícito, política compartilhada | Recall, desempenho, distribuição de candidatos e qualidade dos parâmetros |
| Calibrador | Captura MVCC, amostras m/u limitadas, evidências e rascunho atômico; validação estrutural sintética | Amostra u representativa dos cinco passes e validação estatística independente |
| Ativação de modelos | Não implementada; método piloto bloqueado no banco | Fluxo governado com autorização e evidência de homologação |
| Runner e publicação | Não portados | Universo congelado, coordenação, checkpoints, recuperação e publicação lógica atômica |
| Correções governadas | Não portadas | Separação, fusão histórica, replay, auditoria e preservação dos fatos |

## Fronteira da calibração MVCC

O calibrador reutiliza `LinkageParameterEstimator` e preserva os parâmetros e comparadores canônicos. A amostra m usa pares inter-Gestores independentes com resolução determinística por CPF; a amostra u piloto usa UUIDs distintos com CPF, pareados por nascimento exato. A transação de captura é `REPEATABLE READ READ ONLY`. Evidências persistidas incluem fingerprints de IDs e parâmetros, snapshot e estatísticas agregadas, sem copiar nomes, CPF ou pares brutos para o catálogo.

A amostra u piloto **não representa a união dos cinco passes V2**. Em especial, a seleção por nascimento exato distorce a frequência de divergências de dia, mês e ano. A suavização não corrige esse viés de seleção. Por isso, modelos reais desse método permanecem em `RASCUNHO`: a validação SQL rejeita sua promoção e um trigger independente impede ativação, inclusive por atualização direta de status ou reclassificação do método. O corpus sintético descartável pode exercitar a validação estrutural, mas seus modelos não podem ser ativados.

A próxima metodologia deve capturar a população efetiva de candidatos do scorer, com os cinco passes, deduplicação e atribuição de pass de referência, amostragem probabilística documentada e probabilidades de inclusão conhecidas. Deve distinguir a distribuição u condicionada ao blocking da distribuição de pares não condicionados. O tratamento de dependências entre componentes de nascimento e entre atributos precisa ser calibrado e validado, sem presumir independência nem introduzir pesos arbitrários. Antes de habilitar uso real, exigir corpus de avaliação separado, recall, precisão, calibração de probabilidades, análise de falsos vínculos e revisão de T_LINKAGE e margem.

## Evolução multievidência

A ADR de multievidência universal permanece a direção arquitetural: todos os campos preservados são evidências candidatas, inclusive telefone, e-mail e distrito informado pelo Gestor. Nenhum campo entra automaticamente no score. Cada evidência exige semântica, qualidade, normalização, comparador, dependências e parâmetros versionados. Endereço residencial, endereço de residência, referência territorial e local de atendimento não são intercambiáveis. Evidências ausentes ou inválidas não devem ser convertidas em discordância. A ampliação não altera a precedência determinística do CPF nem autoriza fusões inseguras.

Esta entrega não registra o scorer no runner operacional, não cria UUID, não altera vínculos, não publica Gold/Serving e não ativa modelos. Uma futura ativação exige nova migração governada; não basta remover o bloqueio ou mudar o status manualmente.
