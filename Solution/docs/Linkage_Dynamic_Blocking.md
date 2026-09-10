# Linkage - blocking dinâmico compartilhado

## Regra normativa corrente

Calibrador e avaliador devem construir o universo de candidatos dinamicamente e consumir **a mesma política versionada**, sem manter regras paralelas. A política deve ser reproduzível, possuir fingerprint e registrar versões do método, normalização e fontes externas utilizadas.

O espaço de otimização passa a considerar, quando disponíveis e tecnicamente executáveis, **nome completo, prenome, sobrenome, último nome, dia, mês e ano do nascimento**, suas combinações, além de informação agregada oficial do IBGE como evidência auxiliar de seletividade/discriminação. A escolha não pode maximizar correlação isoladamente: recall de vínculos verdadeiros, redução do espaço de pares, custo, distribuição/tamanho máximo dos blocos, ganho incremental, dependência/redundância e risco de falso vínculo compõem a decisão.

A comparação de nomes deve evoluir sob normalização fonética pt-BR explicitamente versionada. Nenhuma transformação dependente de contexto, sotaque ou grafia ambígua é tratada como equivalência universal sem validação positiva/negativa e análise de colisões.

## Implementação entregue nesta fatia

Esta fatia fecha a primeira parte executável do contrato: **blocking dinâmico por observação sobre o `BirthBlockingPlan`**, compartilhado por calibrador e avaliador.

A versão corrente de nascimento é `BIRTH_BLOCKING_V2_20260907` e contém cinco passes:

1. `ExactDate`;
2. `MonthYearWithInitial`;
3. `DayYearWithInitial`;
4. `TransposedDayMonth`;
5. `NeighborYear`, limitado pela tolerância configurada.

Os passes aplicáveis dependem da evidência disponível na observação. Passes que exigem iniciais não são inventados quando nome/nome da mãe não fornecem inicial utilizável. Sobreposições entre passes representam um único par candidato.

`DynamicBlockingPolicy` introduz um artefato imutável e provider-independent contendo versão da política, versão do plano, passes habilitados, configuração, versão de normalização, versão do otimizador e, quando usada, proveniência/fingerprint da frequência externa de nomes. Seu fingerprint integra a evidência de reprodutibilidade.

### Calibrador

`PostgreSqlLinkageCalibrator` mantém `m` como evidência independente inter-Gestores e passa a formar `u` dentro da união dinâmica de blocking por observação. O método permanece piloto e fail-closed; validação estrutural continua restrita aos ambientes descartáveis de CI até homologação estatística/institucional.

### Avaliador

`Jornada.Linkage.Evaluation` aplica blocking dinâmico por observação e deve registrar a versão/fingerprint da política usada na avaliação. A configuração de blocking precisa permanecer versionada e compatível com a usada pelo calibrador.

## Próximo estágio obrigatório pelos RF-052 a RF-055

A implementação posterior deve ampliar o otimizador para materializar candidatos com componentes de nome (`nome completo`, `prenome`, `sobrenome`, `último nome`) e componentes de nascimento (`dia`, `mês`, `ano`), consumir snapshot IBGE versionado quando disponível e incorporar uma normalização fonética pt-BR governada. O resultado da seleção deverá gerar uma nova versão/fingerprint de política, usada sem divergência pelo calibrador e pelo avaliador.

Esses itens **não são declarados concluídos nesta fatia** apenas porque o contrato normativo existe. Cada ampliação exige regressões unitárias e de integração, atualização deste documento, da matriz de requisitos e do UML no mesmo change-set.

## Paralelismo

Calibrador e avaliador devem usar paralelismo apenas em etapas independentes quando benchmark/regressão demonstrar ganho no ambiente-alvo. A versão paralela deve ser semanticamente equivalente à serial: mesmos pares elegíveis, mesmos parâmetros/métricas e fingerprints determinísticos. Se houver degradação, contenção ou perda de reprodutibilidade, o caminho serial é preferido.

## Regressões obrigatórias

Conforme RNF12 e RNF34-A/B:

- **unitárias:** política/fingerprint, equivalência semântica dos predicados SQL Server/PostgreSQL, adaptação à evidência disponível, limites/configuração e fail-closed;
- **integração:** execução real dos caminhos de calibrador e avaliador nos providers suportados;
- **futuras regras de nome/IBGE/fonética:** vetores positivos e negativos, colisões, fallback sem IBGE, equivalência calibrador/avaliador e serial/paralelo quando aplicável.

Toda alteração futura no blocking deve atualizar código, regressões, contratos de evidência e documentação no mesmo change-set.

## UML

A sequência normativa desta arquitetura está em `docs/uml/Linkage_Dynamic_Blocking_Sequence.puml`, mantida em padrão UML e versionada junto com a implementação.

## Limite de governança

Blocking dinâmico tecnicamente correto não equivale a homologação estatística. A issue #31 continua aberta até existir corpus representativo/atestado, avaliação independente de recall, precisão, calibração e falsos vínculos, análise de subgrupos/dependências e aprovação institucional explícita. Nenhum resultado desta implementação autoriza criação/fusão automática de UUID ou publicação probabilística em Gold/Serving.
