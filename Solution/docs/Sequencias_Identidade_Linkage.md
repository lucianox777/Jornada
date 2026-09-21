# Sequências correntes — fato, identidade, ledger e fila governada

**Baseline conferido:** `08ca8fc0effbbeba6f8cdb8ba6a671329eb01b0a`  
**Substitui como visão de revisão corrente:** os quatro fluxos de sequência originalmente mantidos em `docs/diagrams/sequence/01..04`. Os arquivos PlantUML/PNG/SVG anteriores permanecem no histórico até a limpeza controlada; não devem ser usados para inferir comportamento quando divergirem deste documento e dos contratos executáveis.

Os diagramas Mermaid são fonte de revisão no GitHub. DOCX/PDF devem incorporar imagens renderizadas.

## SQ-01 — fato com identidade resolvida

```mermaid
sequenceDiagram
    participant O as Sistema de origem
    participant A as Jornada.Api
    participant B as Bronze/Controle
    participant P as Processor
    participant S as Silver
    participant I as Identidade
    participant G as Gold/Serving

    O->>A: Entrega + ZIP + contrato
    A->>B: persiste metadados/objeto Bronze
    A-->>O: aceite de ingestão
    P->>B: reserva lote
    P->>S: persiste observação válida
    P->>I: resolve rota determinística/progressiva
    I-->>P: pessoa_uuid / estado
    P->>G: materializa fato + atribuição corrente
    P->>B: conclui lote
```

**Invariantes:** INV-API-001, INV-FATO-001, INV-GOLD-001.

## SQ-02 — CPF em conflito

```mermaid
sequenceDiagram
    participant P as Processor
    participant S as Silver
    participant C as Âncora CPF
    participant I as Identidade
    participant Q as Qualidade/Fila
    participant G as Gold/Serving

    P->>S: preserva observação e CPF declarado
    P->>C: consulta/reserva CPF governado
    C-->>P: conflito de autoridade/consistência
    P->>I: registra vínculo/estado de conflito
    P->>Q: sinaliza divergência governada
    P->>G: publica fato válido com estado de identidade apropriado
    Note over P,G: conflito de identidade não apaga o fato
```

**Invariantes:** INV-FATO-001, INV-CPF-001, INV-GOLD-001.

## SQ-03 — sem CPF, Linkage posterior

```mermaid
sequenceDiagram
    participant P as Processor
    participant S as Silver
    participant PG as Identidade progressiva
    participant R as Linkage.Runner
    participant B as Blocking
    participant M as Modelo FS
    participant PUB as Publicação
    participant G as Gold/Serving

    P->>S: persiste observação sem CPF
    P->>PG: assegura origem progressiva quando existe origem persistente
    P->>G: materializa fato como pendente/indefinido
    R->>B: gera união deduplicada de candidatos
    B-->>R: candidatos + proveniência do passe
    R->>M: compara/score com versão congelada
    M-->>R: resultados brutos
    R->>PUB: publica decisão versionada
    alt associação segura/homologada
        PUB->>PG: atualiza referência canônica
        PUB->>G: recompõe projeção
    else indefinida/incompleta
        PUB-->>G: preserva estado sem inventar vínculo
    end
```

**Invariantes:** INV-FATO-001, INV-ORIGEM-001, INV-LINK-001..004.

## SQ-04 — correção governada

```mermaid
sequenceDiagram
    participant U as Operador autenticado
    participant A as API/serviço de correção
    participant DB as SQL Server / transação
    participant I as Identidade
    participant L as Ledger de decisão
    participant G as Gold/Serving

    U->>A: ato governado + evidência permitida
    A->>DB: inicia transação
    DB->>I: valida autoridade/locks/estado corrente
    I->>I: aplica correção sem transferir âncora CPF
    I->>L: registra evento append-only na mesma transação
    I->>G: recompõe atribuição/projeção
    DB-->>A: commit
    A-->>U: recibo auditável
```

**Invariantes:** INV-CPF-001, INV-GOLD-001, INV-LEDGER-001, INV-REPLAY-001.

## SQ-05 — ledger de decisão de identidade

```mermaid
sequenceDiagram
    participant S as Serviço
    participant X as Mutação governada
    participant L as auditoria.sp_registrar_decisao_identidade
    participant T as Transação SQL

    S->>T: BEGIN
    S->>X: aplicar ato
    X-->>S: estado mutado
    S->>L: credencial + tipo de evento + evidência estruturada
    L->>L: valida atomicidade e taxonomia
    L-->>S: evento append-only
    S->>T: COMMIT
    Note over X,L: falha em qualquer lado reverte os dois
```

Confirmação humana usa `DOCUMENTO_VERIFICADO` ou `CONFIRMACAO_INSTITUCIONAL_SEM_DOCUMENTO`; evento sem confirmação usa evidência nula. Identidade individual definitiva do operador permanece dependente de #378.

## SQ-06 — fila governada de divergência

```mermaid
sequenceDiagram
    participant R as Linkage.Run
    participant Q as qualidade.sp_registrar_conflitos_linkage_publicados
    participant F as divergencia_gestor
    participant B as Balcão/serviço governado
    participant D as sp_registrar_desfecho_divergencia
    participant L as Ledger

    R->>Q: conflitos publicados
    Q->>F: cria/atualiza caso ABERTO
    B->>D: resolve ou descarta caso
    D->>F: ABERTA -> RESOLVIDA/DESCARTADA
    D->>L: registra desfecho governado
    Note over F,L: estado corrente encerra fila; #401 adicionará identidade causal/fingerprint para impedir renascimento idêntico
```

### Lacuna explicitamente aberta

O fluxo corrente encerra a fila, mas ainda não prova que a causa material desapareceu. A #401 é a fonte canônica para adicionar fingerprint/supressão versionada: mesma evidência não deve recriar infinitamente o caso; evidência materialmente nova poderá reabrir conforme regra explícita. O diagrama não antecipa essa implementação como se já existisse.
