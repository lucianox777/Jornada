# ADR — Linkage multievidência orientado por catálogo

Status: DECISÃO DE PRODUTO APROVADA — implementação incremental pendente.
Data da decisão: 2026-09-06.
Complementa a ADR_Linkage_Sunter_Multievidencia_Qualidade_v4.06.md. Não altera a versão do modelo em produção nem substitui os gates de validação.

## Decisão

Todos os campos recebidos e preservados pela Jornada são evidências candidatas para resolução de identidade. O motor deverá ser multievidência e extensível, sem uma lista fechada de atributos limitada ao núcleo cadastral. Nenhum campo é descartado do catálogo apenas por não integrar o modelo inicial. Isso não significa que todos os campos devam receber peso positivo, participar de todos os modelos ou ser comparados indiscriminadamente. Sua habilitação depende de utilidade discriminativa demonstrada, semântica, qualidade, disponibilidade, finalidade legítima, segurança e calibração.

O catálogo deverá permitir incorporar novas evidências sem reescrever o motor ou alterar a estrutura física das tabelas para cada atributo. Cada feature habilitada terá identificação, origem, semântica, normalizador/comparador versionados, estados de qualidade e comparação, política de ausência, parâmetros calibrados, proveniência e evidência de validação. O modelo publicado deverá congelar o conjunto de features, suas versões, parâmetros e regras de dependência. A alteração dessas condições exige nova versão validada, nunca reinterpretar silenciosamente um modelo anterior.

## Evidências e dependências

São candidatas, entre outras: nome, nome social quando aplicável, nome da mãe, nascimento e seus componentes, CPF e outros documentos conforme política determinística, telefone, e-mail, identificadores estáveis da origem, endereço, distrito informado pelo Gestor, referência territorial, vínculos familiares e demais atributos disponíveis. Campos factuais, administrativos ou de serviço não são automaticamente identificadores pessoais; seu uso requer avaliação de pertinência, legitimidade, viés, risco de vazamento de informação e contribuição discriminativa. A mesma restrição vale para atributos sensíveis ou inferências de condição social.

A seleção e a ponderação deverão considerar dependências entre evidências. CEP, distrito, subprefeitura e endereço podem representar a mesma informação; telefone familiar, endereço e parentesco também podem ser correlacionados. O calibrador deverá avaliar o ganho conjunto e poderá agrupar features, usar contribuições condicionais ou outro tratamento estatístico validado. Não se deve somar pesos como independentes quando isso inflar a confiança. A ausência de correlação, isoladamente, não garante utilidade; a seleção deve ser orientada por desempenho e segurança em corpus representativo.

Telefone pessoal, familiar, institucional, reutilizado ou compartilhado não possui necessariamente a mesma força de evidência. O distrito de residência informado pelo Gestor não se confunde com distrito de atendimento, acolhimento ou referência territorial. A semântica, a fonte, a data de referência, a qualidade e a granularidade devem ser preservadas. Não se infere domicílio a partir da localização de uma unidade institucional. Mudança de residência, telefone ou outro atributo mutável não constitui, por si só, prova de pessoas distintas.

## Método e governança

O núcleo canônico permanece Fellegi–Sunter explicável, com calibração estatística separada de m/u e avaliação dos efeitos conjuntos das evidências. A decisão não autoriza IA generativa, um segundo modelo de ML, thresholds arbitrários, fusões automáticas ambíguas ou substituição da rota determinística por CPF. A eventual evolução do método de dependência exige versão, justificativa, validação e aprovação próprias.

Blocking poderá utilizar múltiplas evidências indexáveis e deverá preservar recall, evitando comparação cartesiana e truncamento silencioso. Uma chave de blocking não decide identidade. Dados ausentes, impossíveis, sentinelas ou de semântica incompatível não devem produzir concordância positiva artificial. Divergências fortes seguem a política governada; nenhuma evidência é corrigida ou apagada silenciosamente.

Os valores originais e a proveniência permanecem preservados. Novas evidências poderão desencadear reavaliação/replay de pendências, com histórico e versionamento. A publicação de vínculos exige os gates operacionais de coordenação, precedência determinística, auditoria, recuperação e recomposição, sem perder fatos válidos. Dados sensíveis devem ter acesso minimizado, proteção adequada e finalidade compatível com a LGPD.

## Sequenciamento

1. Concluir a fatia PostgreSQL de catálogo e scorer V1/V2 do PR #29, sem ampliar seu escopo funcional nem ativar modelos automaticamente.
2. Portar calibrador, ciclo de validação e ativação governada; depois runner, universo congelado, coordenação e publicação segura.
3. Evoluir o catálogo universal, a extração de evidências e o tratamento de dependências, incluindo telefone, e-mail e distrito informado pelo Gestor, mediante validação de modelo e contratos.
4. Medir precisão, recall, falsos vínculos, cobertura, calibração, impacto por fonte/grupo, custo de blocking e desempenho em corpus representativo antes de ativar novas features.

Esta decisão define a direção do produto, não declara que o runtime atual já compara todos os campos ou que o Linkage PostgreSQL possui paridade integral.