# PostgreSQL Linkage — universo de candidatos V2

Estado: primeira fatia da issue #31, sem habilitação de calibração real ou ativação. SQL Server permanece canônico e Fabric permanece analítico. Esta entrega não constitui homologação estatística, institucional, de escala ou de produção.

## Regra compartilhada

`BirthBlockingPlan` (`BIRTH_BLOCKING_V2_20260907`) descreve os cinco passes: nascimento exato; mês/ano com inicial do nome ou da mãe no respectivo campo; dia/ano com o mesmo filtro; transposição dia/mês válida; e mesmo dia/mês com tolerância configurada de ano. A tolerância é de 0 a 2, e o scorer conserva seus limites de configuração existentes. Datas impossíveis não são inventadas; ausência de iniciais desabilita os dois passes que dependem delas, sem desabilitar os demais. V1 mantém apenas a data exata.

`PostgreSqlBirthBlockingQuery` constrói os predicados parametrizados e uma máscara de pertencimento. A consulta do scorer usa esse componente, conserva ordenação por UUID, limite `max+1`, interrupção sem truncamento e política matemática compartilhada. Não altera m/u, prior, score, thresholds, margem, CPF determinístico, UUIDs ou publicação. Os critérios de iniciais preservam as expressões SQL anteriores; a equivalência Unicode/collation com a normalização .NET ainda exige testes representativos. O diagnóstico compara a máscara SQL com o plano .NET e interrompe a captura se houver divergência.

## Captura de diagnóstico

`PostgreSqlCandidateUniverse.CaptureAsync` recebe uma amostra de fontes definida externamente, com IDs estáveis, nascimento, nome, nome da mãe e UUID conhecido opcional. Não escolhe uma amostra populacional, não consulta CPF para criar rótulos e não infere que dois UUIDs diferentes representam pessoas distintas. A origem e a independência dos rótulos continuam responsabilidade de uma metodologia governada.

A unidade de análise é o par dirigido `(observação fonte, UUID candidato)`. Para cada fonte, a união dos passes é deduplicada pelo UUID. Uma máscara registra todos os passes de pertencimento, e um pass primário é atribuído pela ordem publicada apenas para contagens exclusivas. O mesmo candidato pode aparecer em fontes diferentes, sem que isso seja tratado como duplicação de pares. O UUID conhecido é mantido apenas como referência de concordância de identificadores, não como verdade estatística.

A captura usa uma transação PostgreSQL `REPEATABLE READ READ ONLY`, com limites explícitos de fontes, candidatos por fonte e pares totais. Qualquer excesso, UUID duplicado ou divergência entre predicados interrompe a operação, sem devolver uma amostra parcial. A saída contém contagens por pass (pertencimento e atribuição primária), sobreposições, blocos vazios, candidatos distintos, concordâncias com UUID conhecido, versão, configuração, snapshot e fingerprints SHA-256. Não retorna nomes, CPF ou pares brutos e não persiste modelo ou identidade. O snapshot identifica a leitura realizada, mas não é um snapshot exportado reutilizável; os fingerprints não substituem a preservação governada do corpus e da seleção de fontes. IDs e hashes podem continuar sendo dados pessoais pseudonimizados e exigem controle de acesso e retenção apropriados.

O componente é uma API interna de diagnóstico, não registrada no worker operacional. Não substitui `UnmatchedSql`, não chama `LinkageParameterEstimator` e não modifica a proteção de promoção do PR #30. O método piloto `M_INTERGESTOR_U_GOLD_MVCC_V2` permanece bloqueado para validação real e ativação.

## Próximos gates estatísticos

A população-alvo de treinamento deve ser definida como o universo efetivo de pares candidatos gerados para uma população de observações-fonte especificada, e não como todos os pares possíveis de cidadãos nem como uma amostra de pares adjacentes. A seleção probabilística de fontes precisa registrar quadro amostral, estratos, probabilidades de inclusão, perdas e versão do blocking. Se todos os candidatos de uma fonte selecionada forem enumerados, a inclusão de cada par decorre da inclusão da fonte, condicionada ao corpus congelado. Se houver subamostragem de candidatos ou passes, a probabilidade conjunta de inclusão e a deduplicação devem ser demonstradas; não se podem somar probabilidades de passes sobrepostos como se fossem independentes.

A metodologia deve separar pares m com evidência independente de Gestores distintos, pares u com rótulos negativos confiáveis e casos ambíguos. UUIDs diferentes, ausência de CPF e resultados probabilísticos anteriores não são automaticamente verdade-terreno negativa. Deve-se distinguir a distribuição u condicionada ao blocking da distribuição não condicionada, inclusive no cálculo do prior. A estimação ponderada, quando necessária, precisa ser implementada e validada explicitamente, sem introduzir pesos arbitrários nem alterar silenciosamente o estimador canônico. A dependência entre dia, mês e ano e entre evidências correlacionadas exige avaliação conjunta.

Antes de habilitar modelo real, exigir corpus de avaliação independente, recall do blocking, precisão, calibração probabilística, falsos vínculos, análise de subgrupos, revisão de T_LINKAGE e margem, proveniência e aprovação governada. A ADR de multievidência universal continua válida: todos os campos preservados são evidências candidatas, com semântica, qualidade, comparadores e dependências versionados; nenhum campo é ativado automaticamente. Endereço residencial, endereço de residência, referência territorial e local de atendimento não são intercambiáveis.

## Evidência técnica

O workflow dedicado usa banco descartável `JornadaPgUniverseTest`, opt-in explícito, restore locked, build Release com warnings como erros, instalação dupla dos DDLs e testes reais PostgreSQL. Os testes verificam cinco passes, sobreposição, deduplicação, campos de iniciais, datas-limite, blocos vazios, limites e ausência de escrita. Logs de texto, binlogs e TRX são preservados. A execução desses gates demonstra apenas a correção técnica coberta pelos testes, não a representatividade de uma amostra ou a qualidade de um modelo em dados reais.
