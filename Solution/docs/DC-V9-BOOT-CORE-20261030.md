# DC-V9-BOOT-CORE-20261030 — Investigação inicial do núcleo de identidade no bootstrap

**Estado:** decisão arquitetural confirmada pelo responsável do projeto em 30/10/2026; registro normativo para implementação e revisão.  
**Escopo:** Jornada V9, bootstrap sintético IBGE, calibrador adaptativo, catálogo de atributos/comparadores/blocking, seleção e governança.  
**Natureza:** especificação de comportamento; este documento não constitui evidência de execução, aprovação estatística, certificação ou ativação de modelo.

## 1. Decisão inequívoca

O bootstrap **não apenas cadastra** combinações arbitrárias. Ele prepara o catálogo inicial versionado de atributos, representações, comparadores e passes de blocking do **núcleo de identidade**, e a **calibração inicial investiga e compara efetivamente as alternativas elegíveis**. O calibrador estima os parâmetros, mede os resultados e recomenda configurações finalistas; não recebe uma combinação vencedora predefinida.

A investigação inicial das combinações do núcleo é uma **funcionalidade exigida no bootstrap**, não trabalho opcional para uma fase futura. A implementação não pode declarar a V9 funcionalmente completa apenas por possuir o catálogo, um scorer parametrizado ou um conjunto manual de opções.

## 2. Núcleo, evidência mínima e CPF

O núcleo é: **nome, nome da mãe, data de nascimento e CPF**.

- CPF válido segue o caminho determinístico e sua política própria; não é um atributo nominal cujo peso o calibrador possa livremente substituir.
- Na ausência de CPF determinístico, o caminho probabilístico deve respeitar a regra de evidência mínima: **nome + mãe ou nome + nascimento**. Nome isolado não autoriza vínculo.
- As alternativas podem variar a forma de comparar e combinar os campos do núcleo, mas não podem enfraquecer essas invariantes.
- A origem, a qualidade e a hierarquia de evidências documentais permanecem relevantes; data de expedição documental não deve ser confundida com data de atendimento.

## 3. Catálogo inicial e investigação

O bootstrap registra, com IDs e versões estáveis, os atributos e as alternativas tecnicamente suportadas, incluindo representações nominais (p.ex., nome completo, primeiro nome, último sobrenome significativo), comparadores, opções TF e passes de blocking. A lista exata de alternativas é um **catálogo versionado e auditável**, não uma enumeração fixa embutida como única verdade no calibrador.

O calibrador deve investigar, quando suportadas e estatisticamente elegíveis:
- alternativas com e sem ExactFirst e ExactLast, separadas ou combinadas;
- alternativas com e sem TF e seus parâmetros, sem impor previamente um peso vencedor;
- combinações de nome, mãe e nascimento que respeitem a evidência mínima;
- passes e composições de blocking, inclusive seus efeitos sobre geração de candidatos;
- parâmetros Fellegi–Sunter (m, u, prior), suporte, limiares de aceitação/abstenção/criação e políticas de decisão.

**Não** se presume que a melhor combinação seja a mais complexa, que TF deva estar ligado ou que ExactLast deva ser adotado. O calibrador pode rejeitar qualquer alternativa por desempenho, insuficiência de evidência, incompatibilidade estatística ou custo.

A busca pode empregar filtragem, fases ou poda justificadas para controlar o custo combinatório, desde que preserve a rastreabilidade do espaço elegível, dos descartes e dos critérios usados. Não é necessário executar exaustivamente um produto cartesiano inviável; tampouco é permitido fingir que opções não avaliadas foram testadas.

## 4. Método, separação e recomendação

1. Fixar versão do catálogo, referência IBGE, corpus, seed, frames, projeções, regras e fingerprints.
2. Construir as alternativas elegíveis do núcleo a partir do catálogo e registrar as que foram excluídas, com motivo.
3. Ajustar parâmetros **somente em TRAIN**, com separação por identidade-base, inclusive ao estimar m/u, prior e TF.
4. Avaliar o fit congelado em **VALIDATION**, sem refit e sem usar TEST para escolher alternativas.
5. Comparar qualidade, vínculos falsos, cobertura, fragmentação, fusão, abstenções, incerteza estatística e custo de geração/avaliação de candidatos.
6. Produzir fronteira de Pareto e candidatos justificados (p.ex. conservador, joelho, agressivo), com limites de evidência explícitos.
7. Exportar snapshot reproduzível e fingerprint do catálogo, da configuração, do fit e do frame; a execução deve reproduzir a mesma semântica.
8. Manter **seleção técnica, autorização de uso e ativação** como operações separadas.

A investigação de blocking deve medir o universo real de candidatos e não somente reponderar scores em um frame fixo se as alternativas mudarem a recuperação de pares. A identidade do frame e a cobertura devem integrar a comparação.

## 5. Semântica TF e ExactLast

ExactFirst e ExactLast são **representações de campos do núcleo**, não novos atributos fundamentais. O catálogo deve permitir sua investigação, não impor sua ativação.

A frequência de sobrenome publicada pelo IBGE em **qualquer posição** é apenas um **proxy** da distribuição de **último sobrenome significativo**. Não se pode convertê-la silenciosamente em frequência observada de ExactLast ou em estimativa TRAIN U. Qualquer alternativa TF positiva requer suporte estatístico, proveniência, unidade nominal coerente e estimação U adequada à partição TRAIN; ausência de suporte deve falhar fechado ou tornar a alternativa inelegível com motivo registrado, nunca criar um fallback oculto.

## 6. Campos adicionais e evolução

Depois da investigação inicial do núcleo, o calibrador também pode **investigar e sugerir** atributos ou combinações adicionais que tenham semântica, qualidade e proveniência definidas. A recomendação deve explicitar ganho incremental, suporte, incerteza, riscos, custo e efeito sobre os grupos avaliados.

**Sugestão não é incorporação automática.** A entrada de novos atributos/combinações no catálogo ativo depende de nova versão e autorização. Nenhuma recomendação pode alterar silenciosamente o núcleo, o caminho determinístico do CPF ou a regra de evidência mínima.

## 7. Estatuto do bootstrap sintético

A calibração inicial com corpus sintético IBGE (referência de ensaio: 30 mil identidades, seed 42) produz **candidatos técnicos e evidência sintética**, não certificação de desempenho em dados reais nem autorização de produção. Os testes físicos, validação independente, holdout e TEST selado exigidos pelo protocolo devem ser realizados e registrados nos SHAs exatos pertinentes. Não declarar verde/certificado quando a prova exigida estiver ausente.

## 8. Critérios de aceite verificáveis

A implementação deve provar, por testes e artefatos auditáveis:

- O bootstrap publica catálogo versionado do núcleo e a calibração inicial **efetivamente pesquisa** múltiplas alternativas elegíveis.
- Há alternativas com/sem ExactFirst, ExactLast e TF quando elegíveis; opções inelegíveis têm motivo, não desaparecem silenciosamente.
- A busca varia blocking quando isso altera o frame e mede cobertura/custo.
- TRAIN e VALIDATION permanecem separados por identidade-base; TEST não participa da seleção.
- O resultado e a fronteira de Pareto não dependem da ordem de enumeração das opções, para entradas fixadas.
- Nenhuma alternativa viola evidência mínima ou o caminho determinístico do CPF.
- Snapshots, fingerprints e runtime reproduzem a semântica da opção selecionada.
- Novos campos são **recomendados**, não automaticamente cadastrados/ativados.
- A ausência de evidência estatística ou de execução física obrigatória é tratada **fail-closed**.

## 9. Impacto de implementação e não objetivos

Revisar a integração entre catálogo/bootstrap, gerador de alternativas, adaptador Fellegi–Sunter, estimação TRAIN U, exportação de snapshot, runtime e seletor de Pareto. Não implementar apenas um toggle ad hoc no scorer como substituto da investigação inicial.

Este documento **não escolhe** previamente a melhor configuração, **não aprova** um modelo, **não determina** que ExactLast/TF sejam sempre usados e **não afirma** que os critérios acima já foram implementados ou testados.

**Precedência de intenção:** em caso de ambiguidade de documentação anterior, prevalece esta distinção funcional: *o bootstrap cadastra e investiga o núcleo; o calibrador recomenda evoluções adicionais; a governança autoriza versões e uso*.
