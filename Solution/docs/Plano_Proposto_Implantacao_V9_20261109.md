# Trilha de implantação — proposta sujeita ao Product Owner

**ESTADO: PROPOSTA — AGUARDA DECISÃO DO PO.** Documento preparatório, sem execução, sem acesso a dados reais e sem liberação para HML/PRD.

1. **Autenticação institucional e segregação:** #378 depende da PRODAM/órgãos responsáveis; definir identidade corporativa, autorização por recurso, logs e separação de credenciais, SQL, objetos e NAS de DEV/HML/PRD.
2. **Topologia:** comparar instalação em uma máquina versus dois nós no cluster; diferenciar o contrato de instalador da prova de cluster DEV existente. Requisitos: failover, fila, bloqueio SQL, NAS compartilhado, observabilidade e recuperação. A decisão exige sizing real e aprovação.
3. **PDFs:** #1344, revisão editorial de 8 PDFs/51 menções, sem modificar fontes históricas seladas; aprovação editorial independente da V9.
4. **Massas sintéticas difíceis:** propor depois corpus novo de homônimos, famílias, erros correlacionados, registros próximos ao threshold e assimetria TF; **não gerar nesta rodada** nem consumir seed de TEST.
5. **FDR em dados reais sem gabarito:** propor desenho de amostragem estratificada de vínculos aceitos e casos abstidos, auditoria manual independente/cega e estimador com pesos de inclusão; declarar erro de classificação da revisão, cluster por identidade e dependência entre pares. Com observação apenas de aceitos não se identifica recall; requer desenho adicional e hipóteses explícitas, a serem aprovados.
6. **Credenciais:** conclusão residual da #1253 antes de expor infraestrutura institucional; rotação, segredo nativo, auditoria de logs e retenção aprovadas pelos responsáveis.
7. **Entrada e saída de HML:** gate contratual assinado; ambiente segregado, integridade dos artefatos e dos hashes, smoke, dados sintéticos, NAS e rollback. Saída exige métricas acordadas e aprovação expressa; nenhum PASS de CI substitui homologação.
8. **Separação de ambientes:** DEV com SQL descartável para provas; HML e Produção somente sob autorização e infraestrutura gerenciada; sem copiar corpus sensível para GitHub e sem promover automaticamente bundle científico.

**Pendências de decisão:** autenticação, topologia, orçamento, desenho de amostra manual, FDR real, limiares de aceite e owner de implantação. Manter #378 ABERTA. GOV-DOC-01.
