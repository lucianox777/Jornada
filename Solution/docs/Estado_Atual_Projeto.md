# Estado atual — fotografia documental de 26/09/2026

Este arquivo é um ponto de retomada, **não** auditoria do HEAD ou evidência de testes. Para estado atual de implementação conferir código, PRs, Actions e issues.

## Publicação e candidato

`../../RELEASE_INFO.txt` declara a última release selada de engenharia v4.05, Base Normativa declarada v3.64 e SolutionSchema 3.69. A última Especificação Técnica materializada na árvore é v3.62 (DOCX/PDF). A v5.00 é candidata, sem efeito de publicação. Ver `../../Documentos/README.md`.

## Estado técnico e decisões

A documentação descreve SQL Server, Processor, Runner em lote, motor C# de linkage, Gold e ledger. A aderência integral da procedure Gold à hierarquia de quatro níveis por atributo precisa ser verificada no código. `codigoPessoaOrigem` permanece opcional. A busca síncrona `Patient/$match` para o balcão **não está comprovada como implementada**; o requisito é até cinco candidatos internamente priorizados, mostrados sem ranking ou score e com “Nenhum destes”.

**Fases aprovadas:** DEV → **Ensaio único** → HML → Produção. O Ensaio é técnico e operacional, já com todas as funcionalidades, contratos, autenticação, auditoria e observabilidade de HML. A diferença planejada para HML é a massa de testes preparada pelas Secretarias, preservando as características relevantes das bases reais. Ver [contrato](Ensaio_Unico_Paridade_HML.md).

## Pendências de engenharia

Motor C# compartilhado, invalidação por mudanças de origem e do lado candidato, persistência por mudança semântica, hierarquia Gold por atributo, tolerância versionada VALIDATE/ACTIVATE, busca síncrona tipada e segura, evidência estatística da issue #31, .NET 10, baseline SQL para novas instalações, drift documental e avaliação da publicação set-based. **Decisão documental não significa implementação concluída.**
