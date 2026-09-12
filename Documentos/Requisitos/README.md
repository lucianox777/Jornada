# Família de Requisitos - Jornada do Cidadão - Fase 1

Esta pasta preserva os baselines de requisitos da Fase 1, mas existe **um único conjunto corrente por finalidade**.

## Baseline institucional corrente

Use `00_Indice_Mestre_Requisitos_Jornada_v1.1` como porta de entrada. A leitura institucional corrente é composta por:

1. `01_Requisitos_de_Negocio_Jornada_v1.1`;
2. `02_Requisitos_Funcionais_Jornada_v1.1`;
3. `03_Requisitos_Nao_Funcionais_Jornada_v1.1`;
4. `04_Requisitos_Tecnicos_Jornada_v1.1`;
5. `05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.1`.

Os arquivos v1.0 que permanecem nesta pasta são **baselines históricos**, preservados exclusivamente para rastreabilidade. Eles não são vigentes e não devem ser lidos cumulativamente com os documentos v1.1.

`06_Adendo_RF_RNF_Linkage_Calibracao_Avaliacao_v1.0.md` também é histórico: seu conteúdo foi incorporado ao baseline consolidado v1.1 e o mapa corrente o declara explicitamente como documento superseded. Os antigos aditivos de 02, 03 e 05 estão em `Historico/` apenas para auditoria da consolidação.

## Mapa legível por máquina

`requirements-map-v1.1.json` é o mapa corrente da família v1.1. O arquivo `requirements-map.json` é o **mapa baseline histórico** referenciado pelo mapa corrente para preservar a rastreabilidade anterior; ele não é uma segunda visão vigente da família de requisitos.

## Relação normativa e de release

O índice mestre v1.1 declara **Especificação Técnica Jornada v3.62** e SolutionSchema v3.70 para o candidato técnico à consolidação Solution Engenharia v5.00. A última release efetivamente selada continua sendo determinada por `RELEASE_INFO.txt`; a existência deste baseline candidato não cria tag/release v5.00.

A diferença entre a Base Normativa declarada pela release e a versão publicada da Especificação Técnica é uma lacuna normativa separada e deve ser resolvida explicitamente; este README não redefine essa hierarquia por inferência.
