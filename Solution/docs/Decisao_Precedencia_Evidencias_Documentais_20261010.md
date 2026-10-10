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


## Modelo de evidência: data no documento, não no atributo

**A data pertence à instância da evidência/documento**, identificada por seu tipo e identificador de ocorrência; **não criar uma coluna de data por atributo**. Cada tipo documental tem um conjunto próprio e explícito de atributos que pode comprovar, mantido em catálogo versionado de tipos e cobertura. A mesma instância pode comprovar vários atributos, todos associados à mesma data documental e à mesma proveniência.

Estrutura conceitual mínima:
- **TipoDocumento**: código estável, versão e conjunto de atributos admitidos; não presumir que todo tipo comprova nome, nascimento, filiação, endereço etc.
- **Documento/Evidência**: identificador, tipo, classe (DOCUMENTO ou AUTODECLARACAO), **data própria da evidência**, proveniência, validade e referência de auditoria; datas de atendimento/registro são metadados separados.
- **Valores comprovados**: relação da evidência com cada atributo que efetivamente comprova e seu valor; sem replicar a data na linha do atributo.
- **Seleção Gold**: para cada atributo, considerar somente evidências válidas cujo tipo admite aquele atributo e que de fato o contenham; ordenar as evidências por classe e data própria; preservar o vínculo ao documento vencedor.

**Tipo não é ocorrência**: duas certidões do mesmo tipo emitidas em datas diferentes são duas instâncias documentais, cada uma com sua data. Não manter uma única data global para todas as certidões de nascimento, por exemplo.

Exemplo: uma certidão de nascimento pode comprovar nascimento e filiação; um comprovante de residência pode comprovar endereço, mas não deve automaticamente substituir nome ou data de nascimento. O catálogo decide o escopo de cada tipo; estes exemplos não substituem a validação institucional dos campos aceitos.

O tratamento de documento sem data própria permanece governado: não usar data do atendimento como substituta. O contrato e o banco devem representar a associação documento → valores comprovados, com data documental única na instância, e não data repetida por campo.

## Pendências técnicas para o aceite

Verificar modelagem normalizada de **instância de documento com data própria única**, **tipo de evidência e catálogo versionado de atributos admissíveis** e **valores comprovados vinculados à instância** no contrato e no SQL; implementar ordenação por classe/data própria na seleção Gold; garantir histórico/auditoria; criar regressão SQL/E2E com datas de atendimento invertidas. Até esses testes passarem, **decisão documentada ≠ implementação certificada**.
