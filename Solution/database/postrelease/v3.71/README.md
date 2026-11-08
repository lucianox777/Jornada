# Pós-candidato — extensão territorial v3.71 (NEUT-DEV-20261107)

**Motivo e precedência:** o `CANDIDATE_INFO.json` sela o hash SHA-256 do `database/migrations/manifest.txt` do schema **3.70**. O run [42422118280](https://github.com/lucianox777/Jornada/actions/runs/42422118280) demonstrou que acrescentar a migração nova a esse manifesto **quebra a prova de proveniência**, apesar de os três caminhos SQL de instalação terem aplicado as migrations. **Não corrigir o erro alterando a evidência histórica**, a fingerprint selada ou enfraquecendo checks.

A extensão **3.71** fica fora de `migrations/manifest.txt` e não reescreve `Jornada_Fase1.sql`, `Jornada_Fase1_v3.70.sql`, `CANDIDATE_INFO.json` ou SQL legado. É uma **nova migração transacional, fail-closed e idempotente**, que troca o valor antigo de `origem_geografia` por `ENRIQUECIMENTO_DESENVOLVEDOR`, reconstrói CHECKs que dependem do nome antigo, valida ausência residual e preserva o conteúdo histórico no Git.

## Execução administrativa controlada

- Instalação em base SQL **nova**: executar `database/Jornada_Fase1_v3.71_fresh.sql` no `sqlcmd` a partir de `Solution/`, em SQL Server descartável primeiro.
- Atualização de uma base já no schema 3.70: executar `database/Jornada_Fase1_v3.71_upgrade.sql`; repetições do upgrade são idempotentes.
- **Não** executar o instalador inteiro 3.70 sobre uma base que já contém valores 3.71; o baseline selado antecedente ainda possui a restrição histórica.
- Verificar a migração em SQL real com registro antigo existente, estado final de CHECKs e replay idempotente; não gerar nem enviar dados reais às Actions.
- A designação nova não ativa integração com fornecedor externo nem altera responsabilidades.

**Estado:** candidato de implementação, **não aprovado para ativação**. O registro de schema ainda não foi promovido a release 3.71 e os instaladores operacionais Windows/CI oficiais continuam no schema 3.70 até aprovação posterior, reconciliação e gates. Não declarar a neutralização do valor de domínio finalizada apenas pela presença deste arquivo. Esta extensão evita adulterar hashes de versões seladas, mas ainda precisa de governança de adoção e migração verificável.

## Prova SQL real automatizada — candidata, NÃO homologada

A workflow `.github/workflows/neutralizacao-territorial-v371-smoke.yml` foi acrescentada **nesta mesma PR #1334**, para testar em SQL Server 2022 descartável: criação de schema 3.70 selado, seed DEV sintético, inserção do valor histórico em linha referencial existente, aplicação do upgrade 3.71 e replay, seguida de prova de dois CHECKs conhecidos confiáveis e ausência da origem antiga. Usa a rotina já existente de credencial aleatória mascarada e limpa o container ao fim. O script **não** toca em `JornadaLocal`, dados reais, `CANDIDATE_INFO.json`, manifests ou schema 3.70; não atesta promoção de versão, HML/PROD nem DT-15. O resultado só poderá ser declarado após o workflow e checks serem verificados no SHA exato. Caso falhe em SQL real, corrigir nesta mesma PR sem afrouxar constraints.
