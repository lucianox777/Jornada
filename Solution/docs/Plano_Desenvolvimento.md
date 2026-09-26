# Plano de desenvolvimento — ponto único de priorização

**Status:** plano, não relatório de conclusão. Validar a situação de cada frente no código, PRs e Actions antes de afirmar que foi entregue.

1. Motor operacional único C# com comparadores, blocking, scoring, parâmetros e testes de paridade inspirados nas capacidades relevantes do Splink.
2. Reprocessamento por mudanças da origem **e** de referências candidatas; gravação somente por mudança semântica, preservando auditoria e ledger.
3. Gold: conferir/implementar hierarquia por atributo — documento apresentado mais recente, documento anterior, autodeclaração mais recente, autodeclaração anterior; preservar divergências.
4. Congelar tolerância versionada de conferência para VALIDATE/ACTIVATE, sem contornar gates.
5. Implementar busca síncrona tipada com o motor C# compartilhado, autenticação, autorização, auditoria, minimização e interface semicega de **até cinco** candidatos mais prováveis, sem ranking visível e com “Nenhum destes”.
6. **Gate antes do Ensaio:** integrar e testar **todos** os contratos, funcionalidades e controles previstos para HML. Não adiar implementação para HML.
7. Executar **um único Ensaio técnico e operacional**, com cargas em ondas, CPF tardio, referências novas, homônimos, replays, conflitos, recuperação, desempenho, busca ad hoc e evidência estatística independente quando possível.
8. Promover a HML com o **mesmo produto e contratos**; Secretarias enviam massa de testes preparada por cada Secretaria, com fidelidade às características reais, segurança e adequação estatística verificadas.
9. Depois dos gates prioritários: .NET 10 LTS, baseline SQL para novas instalações (preservar upgrades), drift documental, Linkage.Core próprio, parâmetros de gates sem duplicação e publicação set-based apenas após medição.

A Gold é a melhor representação revisável, não certificação civil. Não inferir qualidade a partir da Secretaria ou da presença de CPF. Ver [diretrizes](Diretrizes_Identidade_Progressiva_Apoio_Decisao.md), [Ensaio único](Ensaio_Unico_Paridade_HML.md) e [testes](Testes_Operacao_Indice.md).
