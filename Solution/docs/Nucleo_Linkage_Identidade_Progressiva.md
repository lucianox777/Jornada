# Núcleo prioritário — Linkage C# e identidade progressiva

**Estado:** direção de arquitetura e implementação proposta; não declarar como já implementados os mecanismos novos descritos aqui. **Prioridade:** núcleo de linkage e identidade progressiva antes de BI e melhorias opcionais do atendimento. Ensaio com uma ou várias Secretarias deve produzir evidência forte quando os dados e a verdade de referência sustentarem as conclusões.

## 1. Motor único C# com capacidades do Splink

Reutilizar SQL Server, candidate generation/blocking, parâmetros versionados, gates, Runner, Processor, ledger e Gold existentes. Traduzir para C# as capacidades úteis do Splink (comparadores, estimação, decomposição dos pesos e frequência de valores) sem introduzir outro motor operacional ou segundo caminho de decisão. Preservar resultados numéricos de referência em testes de paridade e conferir a implementação antes de promover modelos. O EM pode ser instrumento diagnóstico independente, não promoção automática.

A referência IBGE já integrada permanece como bootstrap das frequências de nomes; CIDACS-RL é referência metodológica/benchmark brasileiro, **não substituição automática da base de frequências do IBGE**. O motor é agnóstico à maturidade cadastral da Secretaria e aos atributos não identitários: não pressupõe qualidade superior ou inferior em registros com ou sem CPF, nem usa presença/ausência de CPF como proxy de qualidade. A qualidade dos dados de identidade é variável e não mensurável a priori; o processamento deve ser robusto aos estados de evidência efetivamente observados, dentro dos contratos versionados. Não criar programa de teste de hipóteses sobre qualidade por Secretaria, coorte ou estrato de CPF.

## 2. Comparação de modelos: aproveitar primeiro a literatura

É prudente comparar modelos de linkage, mas **não** implementar múltiplos motores completos apenas para refazer benchmarks publicados. Começar por revisão de estudos comparativos e avaliação de transferibilidade de seus resultados ao domínio da Jornada. O estudo de CIDACS-RL (2020, DOI 10.1186/s12911-020-01285-w) compara CIDACS-RL, AtyImo, Febrl, FRIL e RecLink sobre referência conhecida, além de avaliar escala; não compara diretamente Splink nem o motor C# da Jornada. Referência complementar: *Administrative Data Linkage in Brazil: Potentials for Health Technology Assessment* (2019, DOI 10.3389/fphar.2019.00984).

Documentar métodos de blocking/candidate retrieval, comparação de nomes brasileiros, uso de frequências, estimação, desempenho, explicabilidade, tratamento de atributos ausentes e limitações de cada estudo. Não transferir métricas publicadas de outra população como expectativa para as Secretarias.

Se a revisão revelar uma lacuna que afete decisão concreta de arquitetura, executar **experimento comparativo mínimo e controlado**, com mesmo conjunto de observações e mesma verdade de referência, isolando blocking de scoring e comparando precisão, recall, falsos vínculos e custo. Usar massas disponíveis e autorizadas, sem criar hipóteses prévias sobre maturidade de fontes ou diferença de qualidade por CPF. Comparar implementações externas em ambiente de pesquisa somente quando o ganho informacional justificar o custo. O motor operacional continua único em C#, reutilizando a infraestrutura; a escolha de métodos e ajustes permanece sujeita às evidências.

## 3. Sinais de identidade e documentos

O conjunto inicial de sinais inclui CPF válido quando disponível, nome civil, data de nascimento e filiação materna quando informada, com comparação de nomes brasileiros e seus agnomes. Considerar filiação paterna, local de nascimento e outros atributos compatíveis com certidões de nascimento **apenas quando presentes nos contratos e com qualidade comprovada**, sem exigir universalmente esses campos ou criar pesos antes de calibração. Nome social é referência de tratamento/exibição quando aplicável; não substituir nem descartar a evidência de nome civil. RG/CNH e outros documentos são evidências auxiliares com proveniência e regras próprias. Não incluir atributos de benefícios, serviços, endereço corrente ou outros fatos na similaridade de identidade por conveniência; qualquer sinal adicional exige justificativa, contrato, governança e avaliação de ganho/risco.

Manter `codigoPessoaOrigem` opcional e `initial_uuid` somente como âncora imutável de linhagem, nunca feature estatística.

## 4. Reprocessamento orientado por mudanças e dependências

Separar três fatos: **entrega recebida**, **evidência de identidade alterada** e **decisão corrente alterada**. Entrega idempotente ou alteração de fato não identitário não exige nova pontuação. Uma alteração de evidência identitária dispara reavaliação da origem e, se mudar o índice/corpus de referências, invalida os pendentes potencialmente afetados pelos blocos antigos e novos. Uma referência candidata nova ou modificada também pode afetar observações inalteradas: não restringir a fila às origens que receberam novos bytes. Mudança de versão do modelo, comparadores, guards ou blocking exige reavaliação versionada do escopo afetado.

Proposta de fluxo:
1. Comparar versão/hash semântico dos **campos de identidade relevantes** da origem; preservar trilha de ingestão mesmo se idênticos.
2. Identificar alterações em CPF, nome, nascimento, filiação e nos demais sinais aprovados; distinguir mudanças de atributos não identitários.
3. Atualizar chaves de blocking afetadas; enfileirar a origem alterada e as observações pendentes que podem ganhar/perder candidatos nos blocos antigos e novos. Deduplicar a fila por observação, versão de evidência e versão de modelo. Registrar motivo da invalidação.
4. Executar scorer C# apenas no escopo invalidado, preservando a política de conflitos, homônimos e evidência insuficiente. A busca de afetados deve ter teste de completude para evitar falso negativo por índice de dependências incompleto.
5. Comparar o resultado com a **decisão semântica corrente**: status, UUID canônico, vínculo e motivo governado. Se idêntico, não criar nova versão operacional da decisão; registrar a execução e evidência necessária em trilha separada. Se diferente, persistir a transição e recompor somente as pessoas Gold afetadas, com atomicidade entre decisão, publicação e ledger quando exigida.
6. Tratar chegada tardia de CPF pela rota determinística, com regras explícitas de conflito e sem apagar a história de decisões anteriores.

O ledger append-only de atos de identidade não deve ser eliminado pela otimização de escrita. Separar histórico de execuções/diagnósticos, histórico de transições e estado corrente; não confundir ausência de nova transição com ausência de trabalho executado. Definir critérios de retenção institucional antes do expurgo.

**Regressões obrigatórias:** três ou mais ondas; nova Secretaria com referência candidata; origem inalterada afetada por candidato novo; CPF tardio; alteração de nome ou filiação; homônimos/parentes; candidato que sai de um bloco e entra em outro; modelo novo; replay idempotente; conflito; concorrência e falha entre publicação e ledger; crescimento de armazenamento por mudança real versus runs sem mudança.

## 5. Atendimento no balcão — etapa final

Preservar o fluxo atual de confirmação governada por enquanto. Uma busca opcional de semelhantes pode usar apenas sinais de identidade, inicialmente os mesmos comparadores e blocking do motor, para retornar candidatos com score/explicação e sem vínculo automático. Usar `FHIR Patient/$match` como referência de contrato de busca e retorno, com adaptador/perfil quando útil; não impor semântica clínica a todas as Secretarias. A seleção de candidato pelo atendente continua ato governado com autoria individual quando a identidade corporativa estiver integrada. Não usar confirmações de casos difíceis como amostra representativa para estimar `m` sem corrigir o desenho amostral.

## 6. Ensaio e ordem de entrega

1. Documentar invariantes, gatilhos e fronteiras de persistência; criar testes de regressão de reprocessamento por mudanças e dependências.
2. Revisar estudos comparativos existentes; experimentar alternativas apenas quando houver lacuna relevante à Jornada. Implementar e testar o motor único C# e sua explicabilidade, com IBGE como bootstrap e calibração sobre evidência apropriada.
3. Implementar a fila de invalidação e gravação de transições somente quando houver mudança semântica, preservando auditoria.
4. Executar Ensaio com uma ou várias Secretarias e cargas em ondas, verificando invariantes de identidade, precisão e recall quando existir verdade de referência adequada, cobertura de blocking, tempos e crescimento de escrita. Não presumir nem testar como premissa diferenças de qualidade por Secretaria ou presença de CPF; registrar limitações observadas da massa sem inferir maturidade institucional.
5. Tipar a API de identidade e preparar busca opcional de candidatos para o balcão, sem bloquear as entregas anteriores.
6. BI e transparência ampliada ficam posteriores ao núcleo, exceto métricas mínimas indispensáveis para verificar o Ensaio.

**Não presumir implementação:** o Runner atual pode registrar decisões por run e o fluxo de reavaliação já cobre certos pendentes; a fila de invalidação completa e a escrita operacional por mudança são propostas a validar contra o código e o DDL antes de alterar a persistência.
