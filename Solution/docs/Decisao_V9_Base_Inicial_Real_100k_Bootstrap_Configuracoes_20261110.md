# GOV-V9-BASE-REAL-100K-BOOTSTRAP-20261110 — papel da coorte real inicial na parametrização

**Data:** 10/11/2026. **Autoridade:** Product Owner. **Estado:** decisão funcional/arquitetural de finalidade, sujeita aos gates de calibração e ativação vigentes. **Precedência:** [Decisao_Calibrador_Unico_Adaptativo_Bootstrap_20261014.md](Decisao_Calibrador_Unico_Adaptativo_Bootstrap_20261014.md); [Decisao_Arquitetura_Base_Sintetica_Apenas_Ensaios_20261110.md](Decisao_Arquitetura_Base_Sintetica_Apenas_Ensaios_20261110.md); GOV-DOC-01.

## Decisão e distinção entre bases

O PO definiu a **coorte inicial real de 100.000 registros** como referência de partida **para derivar, calibrar e propor as configurações iniciais do sistema**, e **não** como uma verdade estatística permanente, calibração universal ou licença de ativação automática. A coorte inicial real não se confunde com os **100k sintéticos seed42** da campanha física V9 nem com a referência de nomes publicada pelo IBGE. A decisão não define a fonte de ingresso, identidade autoritativa ou tratamento operacional dos dados reais: esses temas exigem os contratos de governança específicos.

Os **100k sintéticos** continuam apenas massa técnica de ensaio/regressão e referência de reprodutibilidade; **não** são o cadastro inicial da Jornada, não treinam em HML/PROD por boot automático e não devem ser misturados à coorte real para produzir parâmetros sem protocolo explícito.

## Contrato de bootstrap do calibrador único

1. Uma operação **manual e controlada** do Calibrador V9, em ambiente e dados autorizados, usa a coorte real inicial como insumo amostral para propor parâmetros com contexto e proveniência: bloqueio, 81 células nominais quando aplicáveis, M/U, TF, políticas de ausência, limiares, custo/fanout, FDR, cobertura, abstenção e métricas de resolução.
2. A amostra de partida **não é automaticamente representativa** de todos os futuros cidadãos, municípios, regiões, origens ou grafias. Medir independência de identidades, splits TRAIN/VALIDATION e TEST lacrado; verificar rareza, cobertura IBGE, eventos de não publicação e degradações, qualidade de rótulos e risco de vazamento. Se representatividade ou rótulos forem insuficientes, registrar incerteza e abster-se de certificar.
3. O resultado é uma **proposta de snapshot completo, versionado e imutável em `ref`**, com evidências, hashes e aprovação expressa. O bootstrap da aplicação apenas **consome/verifica o snapshot aprovado**: **não executa ajuste/treino, não escolhe configurações de forma implícita e não ativa linkage automaticamente**.
4. Novas massas reais e deriva de distribuição exigem monitoramento e, se necessário, **nova calibração manual, validação e autorização**, jamais extrapolar do 100k inicial como verdade permanente. Nenhuma decisão estatística da campanha sintética 24×100k substitui a validação sobre dados reais.
5. Para nomes não publicados no IBGE, preservar validade de nomes raros/únicos. Frequência censurada (<20 no recorte nacional, sob pressupostos verificados), abstenção ou estimativa são hipóteses distintas; **não há autorização para introduzir piso/frequência sintética automaticamente**. Uma política final exige ensaio específico de recall, FDR, cobertura, custo e abstenção.

## Implantação, limites e rastreabilidade

Esta decisão define a **finalidade da coorte real inicial**, não atesta existência, disponibilidade, autorização de acesso, qualidade, origem ou preparação dos 100.000 registros reais. Nenhuma carga real, publicação de modelo ou ativação em produção é autorizada por este registro isoladamente. Manter proteção de dados pessoais, segregação entre ambientes e fluxo das três operações (operador solicita, calibrador escolhe, autoridade aprova).

A próxima campanha técnica sintética 24×100k continua instrumento de diagnóstico da falha `FS_VALIDATION_EVALUATE`, agora com checkpoints detalhados integrados na PR #1409 e merge `8abb27fef8c41d1eaa83b40323dd680c4cde4186`. Não há certificação de 24/24 até executar e auditar novo run. Workflow manual owner-only com preflight real e SHA exato, sem alterar ciência.

**Impactos:** documentação e desenho de bootstrap real, sem mudança de código, TF, U/M, FDR, 81 células, 24/16/8, SQL ou referência IBGE. Rastrear eventuais implementações em PRs próprios e sincronizar índices GOV-DOC-01.
