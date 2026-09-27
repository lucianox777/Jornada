# Divergências na recomposição Gold Pessoa — inventário do comportamento existente

Conferência do `master` em 2026-09-27. Escopo: `database/migrations/20260919_Gold_Pessoa_Progressiva.sql`, procedure `identidade.sp_recompor_gold_pessoa`. Esta nota documenta o comportamento existente; não altera a precedência de escolha nem cria cópia das observações.

## Onde os valores são preservados

A Gold guarda somente o valor escolhido para `nome_completo`, `data_nascimento` e `nome_mae` e publica `estado_concordancia='DIVERGENTE'` quando há mais de um valor **não nulo distinto** de `nome_cmp`, data de nascimento ou `nome_mae_cmp` entre as observações selecionadas. A procedure não executa `UPDATE` nem `DELETE` sobre `silver.pessoa_observacao`: cada valor concorrente continua consultável na observação Silver que o forneceu, com `pessoa_observacao_id`, `gestor_id`, `source_as_of`, `pessoa_origem_id` e `versao_interna`. O vínculo corrente e a referência progressiva determinam quais observações compõem **esta** Gold; não se deve buscar somente por `initial_uuid`, que é linhagem, não evidência de composição.

A seleção de observações é a união deduplicada de: (1) `identidade.v_vinculo_corrente` resolvido para o UUID; (2) `identidade.pessoa_origem_progressiva` em REFERENCIA com `canonical_uuid` igual ao UUID; (3) cascas PROVISORIA/INDEFINIDA com `initial_uuid` igual ao UUID. Portanto, não existe descarte de valores pela escolha do vencedor **dentro da procedure**. Isso não é garantia de retenção eterna: exclusão ou expurgo de Silver e mudanças posteriores de atribuição podem alterar a consulta corrente. Histórico de atribuições deve ser analisado separadamente.

## Consulta das fontes discordantes atuais

A consulta abaixo reproduz o universo da recomposição. Ela retorna as três colunas por observação, inclusive os valores perdedores, sem modificar dados. Substituir o UUID pelo da pessoa sob análise; controlar o acesso porque os valores são dados pessoais.

```sql
DECLARE @pessoa_uuid UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';
;WITH obs_ids AS (
    SELECT po.pessoa_observacao_id
    FROM silver.pessoa_observacao po
    JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
    WHERE vc.pessoa_uuid=@pessoa_uuid AND vc.status='RESOLVIDO'
    UNION
    SELECT po.pessoa_observacao_id
    FROM silver.pessoa_observacao po
    JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=po.pessoa_origem_id
    WHERE p.estado='REFERENCIA' AND p.canonical_uuid=@pessoa_uuid
    UNION
    SELECT po.pessoa_observacao_id
    FROM silver.pessoa_observacao po
    JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=po.pessoa_origem_id
    WHERE p.initial_uuid=@pessoa_uuid AND p.estado IN ('PROVISORIA','INDEFINIDA')
)
SELECT po.pessoa_observacao_id, po.pessoa_origem_id, po.gestor_id,
       po.versao_interna, po.source_as_of,
       po.nome_completo, po.nome_cmp, po.data_nascimento,
       po.nome_mae, po.nome_mae_cmp,
       gp.nome_completo AS gold_nome, gp.data_nascimento AS gold_nascimento,
       gp.nome_mae AS gold_nome_mae, gp.estado_concordancia
FROM obs_ids i
JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=i.pessoa_observacao_id
LEFT JOIN gold.pessoa gp ON gp.pessoa_uuid=@pessoa_uuid
ORDER BY po.gestor_id,po.source_as_of DESC,po.pessoa_observacao_id DESC;
```

**Limites:** `divergente` não discrimina qual atributo divergiu; `COUNT(DISTINCT ...)` ignora NULL; divergências apenas ortográficas que normalizem para o mesmo `*_cmp` não acionam o bit. A consulta revela os valores brutos mesmo quando o bit não aciona. Não afirmar que um histórico de vinculações ou uma auditoria de cada recomposição está disponível sem teste específico.

## Aceite sintético de três fontes

Preparar em banco descartável três observações de três gestores, vinculadas ao mesmo UUID corrente e com `nome_cmp`, `data_nascimento` e `nome_mae_cmp` distintos; preencher os demais atributos de modo válido conforme o schema de ingestão vigente. Executar `identidade.sp_recompor_gold_pessoa` e a consulta acima. Aceitar somente se: (a) `gold.pessoa.estado_concordancia='DIVERGENTE'`; (b) a consulta retornar as três observações e os três valores originais distintos de cada atributo (nome, nascimento e nome da mãe) com seus `pessoa_observacao_id` e `gestor_id`; (c) a Gold continuar com o vencedor segundo a precedência existente; (d) uma segunda recomposição não apagar nenhuma observação. O teste transacional em `database/tests/Gold_Pessoa_Divergencia_Tres_Fontes.sql` cria três observações de três gestores e seus vínculos para um UUID sintético, confere os vencedores dos três atributos sem verificação documental e reexecuta a recomposição. Exige banco descartável com pelo menos três gestores distintos que já tenham sistema e base de origem associados, além de um lote preexistente; cria novas origens sintéticas e termina com `ROLLBACK`. Confere também que as três observações integram `identidade.v_vinculo_corrente`. O workflow `.github/workflows/ci.yml` executa esse SQL no banco `JornadaTest`, na mesma chamada `sqlcmd` e depois do smoke canônico. O merge exige que essa etapa passe no head do PR, comprovando a execução real em SQL Server e a compatibilidade com os triggers de identidade.

## Decisão

Não introduzir tabela de divergência duplicada enquanto as observações originais estiverem disponíveis e a consulta for suficiente. Uma view governada ou endpoint específico poderá ser proposta em outro escopo, após definir autorização, finalidade e tratamento de histórico de atribuição. Não confundir a trilha das observações com uma trilha imutável de cada resultado Gold passado.
