# Decisão — precedência das evidências por data do documento (10/10/2026)

**Estado:** decisão de negócio para a candidata v5; adequação de contrato, persistência, seleção Gold e testes **a verificar/implementar**. Não declarar comportamento já implantado. Esta decisão não altera retroativamente a versão publicada.

## Regra de prioridade dos valores de um mesmo atributo

Entre evidências válidas e pertinentes que comprovem o mesmo atributo, a prioridade é:

1. **Documento comprobatório mais recente**, segundo a **data do próprio documento**.
2. **Documento comprobatório mais antigo**, segundo a **data do próprio documento**.
3. **Autodeclaração mais recente**, segundo a **data da autodeclaração**.
4. **Autodeclaração mais antiga**, segundo a **data da autodeclaração**.

Formalmente, ordenar primeiro pela classe de evidência (**documento válido > autodeclaração**) e **somente dentro da mesma classe** pela data própria da evidência, em ordem decrescente. Assim, um documento antigo prevalece sobre uma autodeclaração recente.

**A data do atendimento, da ingestão, da digitalização, do registro no sistema ou da última atualização cadastral não substitui a data própria do documento.** Essas datas são metadados de auditoria e não promovem a evidência na hierarquia.

A regra aplica-se **por atributo efetivamente comprovado**. Um documento não prova automaticamente campos que não contém. Preservar a proveniência, o valor original e as divergências entre fontes; a escolha de referência não apaga observações nem altera a identidade por si só. A Secretaria/Gestor não ganha prioridade por sua origem.

## Datas ausentes e desempates

A ausência ou incerteza da data própria **não autoriza usar a data de atendimento como fallback**. Marcar a evidência como sem data comprovada, preservá-la e encaminhar a seleção para critério de desempate/abstenção governado e auditável. Não inventar data nem assumir que o registro mais recente é o documento mais novo. Entre evidências de mesma classe e mesma data, exigir desempate determinístico documentado, sem promoção por atendimento.

## Cenários mínimos de aceite

- Documento emitido em 2020, atendido em 2026, contra documento emitido em 2024, atendido em 2025: **vence o documento de 2024**.
- Documento emitido em 2015 contra autodeclaração de 2026: **vence o documento de 2015**.
- Duas autodeclarações de 2022 e 2025: **vence a de 2025**.
- Documento antigo registrado hoje contra documento novo registrado ontem: **vence o documento novo**.
- Documento sem data própria: **não inferir data pelo atendimento**; registrar a lacuna e aplicar tratamento governado.
- Comprovação de apenas um atributo: **não promover outros atributos**.

## Pendências técnicas para o aceite

Verificar campos e semântica de **data do documento**, **data da autodeclaração**, **tipo de evidência** e **atributos comprovados** no contrato e no SQL; implementar ordenação por classe/data própria na seleção Gold; garantir histórico/auditoria; criar regressão SQL/E2E com datas de atendimento invertidas. Até esses testes passarem, **decisão documentada ≠ implementação certificada**.
