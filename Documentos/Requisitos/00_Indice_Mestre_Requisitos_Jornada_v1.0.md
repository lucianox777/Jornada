# Índice Mestre de Requisitos - Jornada do Cidadão - Fase 1

**Versão do documento:** 1.0  
**Data:** 03/09/2026  
**Base normativa:** Especificação Técnica Jornada v3.62  
**SolutionSchema:** SolutionSchema v3.68  
**Release de incorporação:** Solution Engenharia v3.98  
**Status:** PORTA DE ENTRADA DA FAMÍLIA DE REQUISITOS

## 1. Estrutura adotada

A Fase 1 passa a organizar requisitos por nível de abstração:

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

## 2. Baselines da Fase 1

| Camada | Documento | Versão | Quantidade | Pergunta respondida |
|---|---|---:|---:|---|
| RN | Requisitos de Negócio Jornada | 1.1 | 36 | Por que / qual resultado institucional? |
| RF | Requisitos Funcionais Jornada | 1.0 | 50 | O que a solução deve fazer? |
| RNF | Requisitos Não Funcionais Jornada | 1.0 | 33 | Com quais qualidades/restrições? |
| RT | Requisitos Técnicos Jornada | 1.1 | 65 | Como a engenharia materializa RF/RNF? |
| Matriz | Matriz de Rastreabilidade | 1.0 | 36 cadeias RN | Como requisito e evidência se conectam? |

## 3. Regra de governança

- **RN** muda por decisão de negócio/institucional.
- **RF** muda quando o comportamento esperado da solução muda.
- **RNF** muda quando qualidade, capacidade, segurança, compatibilidade ou restrição normativa muda; os RNF canônicos da Fase 1 permanecem subordinados à Especificação Técnica v3.62.
- **RT** pode evoluir por arquitetura, plataforma, segurança, operação ou implementação sem alterar necessariamente RN/RF.
- Mudança material deve atualizar a Matriz de Rastreabilidade e indicar impacto nas camadas relacionadas.

## 4. Requisitos das partes interessadas

A Fase 1 não cria um quinto baseline separado de *stakeholder requirements*. Necessidades específicas de SGM/SEPE, PRODAM, Gestores, governança e consumidores autorizados permanecem registradas nos RN e especializadas nos RF. Se a complexidade institucional exigir, essa camada pode ser criada futuramente sem renumerar os baselines atuais.

## 5. Ordem recomendada de leitura

1. `01_Requisitos_de_Negocio_Jornada_v1.1`
2. `02_Requisitos_Funcionais_Jornada_v1.0`
3. `03_Requisitos_Nao_Funcionais_Jornada_v1.0`
4. `04_Requisitos_Tecnicos_Jornada_v1.1`
5. `05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.0`

## 6. Relação com os demais artefatos

A família de requisitos **não substitui** a Especificação Técnica v3.62, SolutionSchema v3.68, DDL, OpenAPI, JSON Schemas, ADRs, runbooks ou evidências de teste. Ela organiza a intenção e a rastreabilidade entre esses artefatos.

## 7. Controle de versão

| Versão | Data | Síntese | Incorporação |
|---|---|---|---|
| 1.0 | 03/09/2026 | Institui a hierarquia RN/RF/RNF/RT e a matriz única de rastreabilidade para a Fase 1. | Solution Engenharia v3.98 |
