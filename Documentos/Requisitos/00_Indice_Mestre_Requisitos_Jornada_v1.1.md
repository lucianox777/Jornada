# Índice Mestre de Requisitos - Jornada do Cidadão - Fase 1

**Versão do documento:** 1.1  
**Data:** 10/09/2026  
**Base normativa:** Especificação Técnica Jornada v3.62  
**SolutionSchema:** SolutionSchema v3.70  
**Estado de incorporação:** candidato técnico à consolidação Solution Engenharia v5.00; release/tag ainda não cortada  
**Status:** PORTA DE ENTRADA DO BASELINE INSTITUCIONAL CONSOLIDADO

## 1. Regra de leitura institucional

Para tramitação, revisão pela PRODAM e juntada ao SEI, a leitura corrente é feita por **um único documento por número**, sempre na versão 1.1:

1. `01_Requisitos_de_Negocio_Jornada_v1.1`
2. `02_Requisitos_Funcionais_Jornada_v1.1`
3. `03_Requisitos_Nao_Funcionais_Jornada_v1.1`
4. `04_Requisitos_Tecnicos_Jornada_v1.1`
5. `05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.1`

Os arquivos v1.0 permanecem no repositório exclusivamente como baselines históricos e **não devem ser lidos cumulativamente** com os v1.1. Os antigos aditivos usados para formar 02, 03 e 05 foram preservados em `Documentos/Requisitos/Historico/` apenas para auditoria da consolidação.

## 2. Estrutura adotada

```text
RN - Requisitos de Negócio
        ↓
RF - Requisitos Funcionais ─────┐
        ↓                        │
RNF - Requisitos Não Funcionais │
        ↓                        │
RT - Requisitos Técnicos <──────┘
        ↓
Testes / Gates / Evidências
```

A seta representa rastreabilidade, não dependência de versão. Um RN pode produzir vários RF; um RNF pode afetar diversos RF; um RT pode realizar simultaneamente RF e RNF.

## 3. Baselines correntes da Fase 1

| Camada | Documento corrente | Versão | Escopo corrente | Regra de leitura |
|---|---|---:|---|---|
| RN | Requisitos de Negócio Jornada | 1.1 | 36 RN | Documento completo |
| RF | Requisitos Funcionais Jornada | 1.1 | 56 RF | Documento completo e consolidado |
| RNF | Requisitos Não Funcionais Jornada | 1.1 | 33 históricos + 4 aditivos | Documento completo e consolidado |
| RT | Requisitos Técnicos Jornada | 1.1 | baseline técnico vigente | Documento completo |
| Matriz | Matriz de Rastreabilidade | 1.1 | cadeias históricas + incorporações v1.1 | Documento completo e consolidado |

## 4. Política de identificadores RNF

A forma canônica dos 33 requisitos não funcionais históricos é `RNF-001` a `RNF-033`. Os arquivos v1.0 podem exibir a forma legada `RNF01` a `RNF33`; trata-se apenas de diferença de codificação, sem diferença semântica. Os documentos consolidados v1.1 normalizam essas referências.

Os identificadores `RNF34-A`, `RNF34-B`, `RNF34-C` e `RNF34-D` foram introduzidos como aditivos e são preservados nesta consolidação para não quebrar rastreabilidade já estabelecida. Eventual renumeração integral deve ocorrer apenas em rebaseline explícito, com atualização conjunta de requisitos, matriz, testes e documentação afetada.

## 5. Regra de governança

- **RN** muda por decisão de negócio/institucional.
- **RF** muda quando o comportamento esperado da solução muda.
- **RNF** muda quando qualidade, capacidade, segurança, compatibilidade ou restrição normativa muda.
- **RT** pode evoluir por arquitetura, plataforma, segurança, operação ou implementação sem alterar necessariamente RN/RF.
- Mudança material deve atualizar a Matriz de Rastreabilidade e indicar impacto nas camadas relacionadas.
- O baseline institucional corrente deve permanecer autossuficiente; aditivos de engenharia podem existir como histórico, mas não podem obrigar o destinatário institucional a montar manualmente o documento vigente.

## 6. Relação com os demais artefatos

A família de requisitos não substitui a Especificação Técnica v3.62, DDL, OpenAPI, JSON Schemas, documentação de arquitetura, runbooks ou evidências de teste. Ela organiza a intenção e a rastreabilidade entre esses artefatos. O estado técnico desta branch usa SolutionSchema v3.70, mas a release/tag v5.00 somente passa a existir quando for efetivamente cortada.

## 7. Controle de versão

| Versão | Data | Síntese | Incorporação |
|---|---|---|---|
| 1.0 | 03/09/2026 | Institui a hierarquia RN/RF/RNF/RT e a matriz única de rastreabilidade para a Fase 1. | Solution Engenharia v3.98 |
| 1.1 | 10/09/2026 | Consolida 02, 03 e 05 em artefatos autossuficientes, atualiza SolutionSchema para 3.70 e explicita a política de leitura e identificadores legados. | Candidato técnico à consolidação v5.00; release/tag ainda não cortada |
