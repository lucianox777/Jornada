# Plano de desenvolvimento — ponto único de priorização

**Status:** plano, não relatório de conclusão. Validar a situação de cada frente no código, PRs e Actions antes de afirmar que foi entregue.

1. Motor operacional único C# com comparadores, blocking, scoring, parâmetros e testes de paridade inspirados nas capacidades relevantes do Splink.
2. Reprocessamento por mudanças da origem **e** de referências candidatas, preservando auditoria e ledger; para o critério de persistência por mudança semântica, ver [DT-05](Dividas_Tecnicas.md#ordem-proposta-e-critérios-de-aceite).
3. Gold: conferir/implementar hierarquia por atributo — documento apresentado mais recente, documento anterior, autodeclaração mais recente, autodeclaração anterior; preservar divergências.
4. Conferência governada `VALIDATE`/`ACTIVATE`: executar [DT-01 e DT-09](Dividas_Tecnicas.md#ordem-proposta-e-critérios-de-aceite) como entregas técnicas relacionadas, mas distintas (definição independente da tolerância e integração dos gates); não ajustar parâmetros para passar na conferência.
5. Implementar busca síncrona tipada com o motor C# compartilhado, autenticação, autorização, auditoria, minimização e interface semicega de **até cinco** candidatos mais prováveis, sem ranking visível e com “Nenhum destes”.
6. **Gate antes do Ensaio:** integrar e testar **todos** os contratos, funcionalidades e controles previstos para HML. Não adiar implementação para HML.
7. Executar **um único Ensaio técnico e operacional**, com cargas em ondas, CPF tardio, referências novas, homônimos, replays, conflitos, recuperação, desempenho, busca ad hoc e evidência estatística independente quando possível.
8. Promover a HML com o **mesmo produto e contratos**; Secretarias enviam massa de testes preparada por cada Secretaria, com fidelidade às características reais, segurança e adequação estatística verificadas.
9. Melhorias complementares: ver [DT-02, DT-06, DT-07, DT-08, DT-10, DT-11 e DT-12](Dividas_Tecnicas.md#ordem-proposta-e-critérios-de-aceite). Antecipar qualquer requisito que se mostre indispensável à segurança, à integração ou ao gate do Ensaio; publicação set-based somente após equivalência e medição.

**Governança documental:** este plano define prioridades e dependências; [Dívidas técnicas](Dividas_Tecnicas.md) detalha critérios verificáveis, e as issues/PRs registram execução e evidências. IDs DT relacionados não são etapas adicionais nem comprovam implementação. A [matriz de regressão DT-13](Dividas_Tecnicas.md#ordem-proposta-e-critérios-de-aceite) é transversal a todas as entregas.

A Gold é a melhor representação revisável, não certificação civil. Não inferir qualidade a partir da Secretaria ou da presença de CPF. Ver [diretrizes](Diretrizes_Identidade_Progressiva_Apoio_Decisao.md), [Ensaio único](Ensaio_Unico_Paridade_HML.md) e [testes](Testes_Operacao_Indice.md).
