# Fixtures de ingestão

Todos os exemplos executáveis ficam em `tests/fixtures` e seguem o envelope único da ingestão v2:

- `manifest.json`
- `pessoas.jsonl`
- `registros.jsonl`

`pessoas.jsonl` deve conter, no mínimo, as Pessoas incluídas ou alteradas no período e todas as Pessoas referenciadas pelos fatos da mesma Entrega. `registros.jsonl` pode ter zero bytes.

Fixtures:

- `CADASTRO_SMS_v2/` — atualização exclusivamente cadastral, sem contexto factual e com `registros.jsonl` vazio;
- `AA01_v2/` — Pessoas relacionadas + Benefício Concedido AA01;
- `AA01_SEM_FATOS_v2/` — contexto AA01 informado, Pessoas incluídas/alteradas no período e nenhum Benefício Concedido no período;
- `CRA1_v2/` — Pessoas relacionadas + Serviço Prestado CRA1.

O nome canônico do ZIP é:

`ENTREGA_<GESTOR>_<SISTEMA_ORIGEM>_v2_<SHA256_DO_ZIP>.zip`

As fixtures são insumos de teste, não interfaces alternativas de integração.
