# Jornada — Solution de Referência (Fase 1) — consolidação candidata v5.00

> **Base normativa corrente: 3.62; SolutionSchema corrente: 3.70.** A consolidação v5.00 ainda não foi cortada como release/tag. O estado desta branch é candidato técnico e permanece sujeito aos gates de CI e às aprovações institucionais explicitamente marcadas como pendentes.

Stack principal: **C# 12 / .NET 8**, **Microsoft SQL Server como tecnologia relacional normativa**, Power BI Project (PBIP/TMDL/PBIR) para a camada analítica e **SQL Database in Microsoft Fabric como hospedagem relacional operacional preferencial de HML/Produção quando homologada para a release exata**. SQL Server 2022 Developer/Testcontainers permanece o baseline obrigatório de desenvolvimento local, CI, DDL e validação independente de ambiente; isso não transforma a edição Developer em tecnologia de Produção. Lakehouse e SQL Analytics Endpoint permanecem no escopo analítico/compatibilidade e não substituem implicitamente o banco relacional operacional.

## Estado técnico corrente

- `GET /health/ready` exige `Jornada.BaseNormativa=3.62` e `Jornada.SolutionSchema=3.70` e falha fechado quando os objetos essenciais não estão presentes.
- O instalador canônico editável é `database/Jornada_Fase1_v3.70.sql`.
- Para entrega a DBA/ferramenta de deploy, `scripts/materialize-sql-installer.py` gera um único `.sql` autocontido, sem diretivas `:r`, a partir da fonte canônica.
- O upgrade real reaproveita o backfill paginado/reentrante implementado por `ProgressiveIdentityOriginStore.BackfillPageAsync`; não existe uma segunda implementação T-SQL concorrente para esse backfill.
- O inventário automatizado do schema consolidado contém **66 tabelas**, das quais 13 ficam fora do baseline legado de 53.
- Microsoft SQL Server permanece a referência relacional normativa e o baseline independente de ambiente. A hospedagem em SQL Database in Microsoft Fabric, quando homologada, usa o mesmo contrato Microsoft SQL e não cria regra funcional ou DDL concorrente.

## Instalação e desenvolvimento local

Na raiz `Solution`, o caminho recomendado continua sendo Docker + SQL Server 2022 Developer:

```powershell
.\scripts\local-db.ps1
```

ou, em ambiente compatível com Bash:

```bash
./scripts/local-db.sh
```

Os scripts aplicam a instalação canônica 3.70 e os passos de desenvolvimento previstos. Para validação de release/local, use os runbooks e scripts sob `scripts/` e `docs/`.

## Fronteira funcional

A `Jornada.Api` é a borda externa oficial. O processamento Bronze → Silver → Gold/Serving e o Linkage são internos e auditáveis. A Solution não contém frontend central; interfaces de atendimento e atos transacionais permanecem nos sistemas dos Gestores finalísticos, que se integram à Jornada pelos contratos publicados.

A separação entre fato e identidade é invariável: fatos finalísticos válidos não são silenciosamente apagados ou reescritos porque a identidade ainda está pendente, indefinida ou em conflito. CPF válido/confiável segue a rota determinística e a âncora CPF→UUID é permanente; o Linkage probabilístico não transfere essa âncora.

## Documentação e UML

A documentação destinada à leitura/entrega deve ser publicada em **DOCX e PDF**. Diagramas de arquitetura devem usar notação **UML**, mas o leitor final não deve depender de PlantUML, Mermaid ou outro software específico.

Os diagramas de classes e de atividade da consolidação são incorporados visualmente aos DOCX/PDF. Fontes técnicas auxiliares eventualmente usadas para gerar figuras não são o formato documental exigido da PRODAM e não substituem os documentos legíveis.

O DER/modelo físico pode ser mantido como visão auxiliar de banco de dados, mas não é classificado como UML e não substitui diagramas UML de classes, atividade, componentes, implantação, estados ou sequência quando esses forem exigidos.

Artefato documental corrente da consolidação:

- `../Documentos/Anexo_Modelo_Fisico_Jornada_v1.40.md` — fonte textual do anexo durante a consolidação;
- a versão de entrega deve ser o correspondente `Anexo_Modelo_Fisico_Jornada_v1.40.docx` e `Anexo_Modelo_Fisico_Jornada_v1.40.pdf`, com os diagramas UML incorporados.

## Governança e release

Arquivos de governança podem permanecer com status `PENDENTE`; a consolidação técnica não fabrica aprovação institucional. O corte `jornada-solution-v5.00` somente deve ocorrer depois de:

1. CI integralmente verde no commit final;
2. documentos DOCX/PDF regenerados e validados;
3. metadados de release atualizados para o commit final;
4. aprovações exigidas pela promoção tratadas conforme os gates aplicáveis;
5. homologação Fabric adicional no HEAD exato candidato quando SQL Database in Microsoft Fabric for o alvo de HML/Produção;
6. verificação final do `master` antes da criação da tag.

## Histórico técnico

O README extenso anterior, com o histórico incremental das releases de engenharia, foi preservado em `README_Historico_Engenharia_v4.03.md`. Ele é histórico e não deve ser usado para inferir o `SolutionSchema` corrente quando houver divergência com este README, com a Especificação Técnica vigente ou com os gates atuais.

## Precedência

A precedência documental da consolidação é:

**Especificação Técnica vigente → requisitos normativos e documentos de arquitetura corrente subordinados → implementação e evidências executáveis.**

Divergência entre implementação e norma é defeito ou exige alteração normativa formal prévia; código verde por si só não redefine requisito arquitetural.
