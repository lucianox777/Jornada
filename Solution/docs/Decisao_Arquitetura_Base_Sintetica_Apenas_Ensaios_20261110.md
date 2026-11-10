# GOV-V9-ARQ-BASE-SINTETICA-ENSaios-20261110 — separação entre referência sintética e base operacional

**Data:** 10/11/2026. **Autoridade:** Product Owner. **Estado:** decisão arquitetural e funcional vigente. **Rastreabilidade:** confirmação expressa do PO de que a base sintética é usada exclusivamente para teste e referência e não é a base inicial da Jornada; discussões sobre 24×100k, TF/IBGE e PRs #1409–#1410. GOV-DOC-01.

## Decisão
A **base sintética** da Jornada do Cidadão, inclusive os corpora seed42 de 30 mil e 100 mil pessoas, tem finalidade exclusiva de **teste, ensaio, regressão, avaliação estatística e referência técnica de reprodutibilidade**. **Não constitui a base inicial da Jornada**, nem a fonte autoritativa de cadastros de cidadãos, nem o conjunto de identidade de produção. A origem, ingresso e governança de dados reais da base inicial são assunto operacional separado e devem ser definidos/descritos pelos documentos específicos vigentes; esta decisão não inventa uma origem de dados real.

## Consequências
Os pins e artefatos sintéticos (manifestos, testemunhas V2, evidência seed42, métricas, SQL descartável) são provas de comportamento técnico em ambientes controlados, **não inicialização de base real**. Frequência publicada de nomes do IBGE é fonte estatística externa; não pode ser confundida com a distribuição da amostra sintética ou inferida somente do tamanho do corpus. Nenhum nome de pessoa real, documento pessoal ou dado de produção pode ser inferido a partir do seed42. A regra de divulgação IBGE e tratamento de nomes raros devem ser avaliados para cenários reais, mas um ensaio 100k não concede autorização operacional.

## Precedência, impactos e validação
Esta decisão prevalece sobre qualquer redação anterior que sugira que o corpus sintético é uma população inicial produtiva. Não modifica código, dados, infraestrutura, algoritmo de linking, calibração, 24/16/8, TF, FDR, Gold, TEST ou rotina de ativação. Documento para harmonização futura da especificação arquitetural; se houver afirmação contrária em documento oficial, corrigir por PR com rastreabilidade. Indexar em decisões vigentes e acervo documental. 
