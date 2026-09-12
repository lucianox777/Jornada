# Snapshot bruto — IBGE Nomes no Brasil / Censo 2022

Este diretório é um snapshot imutável incorporado ao projeto para calibração, enriquecimento e replay sem dependência de rede.

**Fonte original dos dados:** IBGE — Censo Demográfico 2022 — Nomes no Brasil, com data de referência de 1º de agosto de 2022.

**Origem técnica desta captura:** compilação Parquet `olob0/ibge-nomes-no-brasil-2022`, produzida diretamente a partir da API pública que alimenta o produto oficial do IBGE e da API oficial de localidades. A compilação declara não adicionar dados inferidos ou enriquecimentos externos. Os dados originais permanecem atribuídos ao IBGE; a estruturação da compilação é CC BY 4.0.

O arquivo `source-files.sha256` fixa o conteúdo exato capturado. Estes arquivos não devem ser substituídos no lugar. Uma revisão futura da fonte deve criar um novo diretório/versão.

Células de baixa frequência suprimidas pelo sigilo estatístico permanecem ausentes nos fatos detalhados. Ausência **não significa zero**.
