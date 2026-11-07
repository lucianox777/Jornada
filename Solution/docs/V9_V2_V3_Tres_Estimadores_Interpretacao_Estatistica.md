# Jornada V9 — três estimadores M/U e a divergência V2 × V3

**Estado (07/11/2026):** explicação técnica e resultados de TRAIN sintético, não certificação de FDR, TEST/Gold ou autorização DT-15. Referência: PR #1300, campanha física seed42, margens 0,5 (primária) e 0,25 (sensibilidade). Não confundir aprovação de CI com superioridade estatística.

## Contrato do plano: três métodos podem concorrer

O plano JSON do calibrador declara `MuEstimationMethods` (array). Cada método expande sua grade declarada em opções identificadas separadamente, combinadas com as demais opções do plano (TF, margem, blocking). A seleção posterior usa os mesmos critérios de elegibilidade, orçamento e risco; não é um concurso de máximo TP no TRAIN. Não há promoção automática, ativação ou escolha implícita de lambda.

- `DEFAULT`: estimador histórico V2; `Parameters` deve ser ausente.
- `CLASSWISE_SUPPORT_BACKOFF`: V3 original, com `Parameters` contendo `ClassMassCoverage`, `MinimumObservedMPerCell`, `MinimumObservedUPerCell` e `RetainSupportedTail` como arrays explícitos. Na ausência de pesos U, preserva o comportamento original da V3.
- `CLASSWISE_SUPPORT_BACKOFF_U_MIXTURE`: variante experimental da V3, com os mesmos quatro arrays e o quinto `UBackoffJointMixtureWeights`, uma lista **explícita** de valores decimais entre 0 e 1. Apenas os pesos declarados são expandidos. Lambda 0 reproduz numericamente a V3 original no estudo, mas mantém proveniência/identificador experimental próprio. Sem declaração desse método não há busca implícita de misturas.

Exemplo **esquemático** de `MuEstimationMethods` (inserir dentro de um plano válido já existente; os demais campos obrigatórios do plano não são mostrados):

```json
{
  "MuEstimationMethods": [
    {"Method": "DEFAULT"},
    {"Method": "CLASSWISE_SUPPORT_BACKOFF", "Parameters": {
      "ClassMassCoverage": [0.99],
      "MinimumObservedMPerCell": [5],
      "MinimumObservedUPerCell": [5],
      "RetainSupportedTail": [true]
    }},
    {"Method": "CLASSWISE_SUPPORT_BACKOFF_U_MIXTURE", "Parameters": {
      "ClassMassCoverage": [0.99],
      "MinimumObservedMPerCell": [5],
      "MinimumObservedUPerCell": [5],
      "RetainSupportedTail": [true],
      "UBackoffJointMixtureWeights": [0, 0.25, 0.5, 1]
    }}
  ]
}
```

O validador rejeita métodos duplicados/desconhecidos, pesos ausentes no método de mistura, valores fora de [0,1], grades vazias/duplicadas e expansão superior ao orçamento; não trunca opções silenciosamente. **Detalhe de implementação:** a sintaxe antiga de pesos embutidos diretamente no método `CLASSWISE_SUPPORT_BACKOFF` ainda é aceita para estudos existentes, mas a forma recomendada é o método de mistura explícito. Não declarar ambas as formas simultaneamente. Planos históricos sem `MuEstimationMethods` mantêm seus hashes/semântica históricos.

## Mecanismo matemático

No Fellegi–Sunter, a contribuição de uma célula de comparação `c` ao peso é proporcional a `log(M(c)/U(c))`. Logo, mudar `U(c)` muda o peso mesmo quando `M(c)`, as observações e os pares candidatos são idênticos.

Na V3, células conjuntas com suporte preservado mantêm sua estimativa; para células U raras fora desse suporte, o fallback usa produto de marginais condicionais (com normalização da massa residual). A mistura opt-in substitui **apenas nessas células U raras** a distribuição pelo interpolador:

```
U_mix(c) = (1 - lambda) * U_marginal_product(c) + lambda * U_joint_dirichlet(c)
```

O método não altera M nem as células U de suporte preservado. A alteração em U desloca log-odds, pode trocar top1/top2, modificar a distância entre os dois candidatos e alterar a aceitação/abstenção de Rank/Resolve sob a margem de conflito. TF nominal também modifica os pesos; por isso o mesmo estimador U pode melhorar com ONE_MF e piorar com TF0. Este é um **mecanismo matemático plausível e compatível com a evidência pareada**, não uma prova causal isolada de que toda perda decorreu de U.

## Evidência física controlada (TRAIN sintético, seed42)

Cada linha abaixo representa **TP resolvidos corretamente** entre 7.011 fontes elegíveis com três passes. Não são TP de TEST/Gold. As duas margens foram declaradas antes de comparar os resultados.

| Margem | TF | V2 DEFAULT | V3 original | Mix lambda 0 | Mix 0,25 | Mix 0,5 | Mix 1 |
|---|---|---:|---:|---:|---:|---:|---:|
| 0,5 (primária) | TF0 | 6758 | 6306 | 6306 | **6771** | 6758 | 6758 |
| 0,5 (primária) | ONE_MF | 6702 | **6771** | **6771** | 6715 | 6707 | 6702 |
| 0,25 (sensibilidade) | TF0 | 6759 | 6704 | 6704 | **6774** | 6767 | 6759 |
| 0,25 (sensibilidade) | ONE_MF | 6761 | 6772 | 6772 | 6718 | **6775** | 6761 |

Na comparação pareada primária TF0, a V3 perde 469 resoluções corretas da V2 e recupera 17: saldo **-452**. Foram observadas 450 mudanças de melhor candidato. Todas as 469 perdas envolveram células com fallback U, **mas 6.248 resoluções preservadas também** envolveram fallback: associação não é causalidade. Com ONE_MF, a V3 supera a V2 em 69 TP (6771 vs 6702). Na margem primária, lambda 0,25 recupera 465 TP frente à V3 TF0 (6771 vs 6306), porém perde 56 TP frente à V3 ONE_MF (6715 vs 6771). Nenhum lambda único melhora ambos os braços na margem primária. A margem 0,25 é sensibilidade e não pode substituir retrospectivamente a primária por apresentar resultado mais favorável.

Todos os braços de 19 passes foram rejeitados por fanout. Zero falsos vínculos aceitos observados no TRAIN elegível **não certifica FDR populacional <1%** nem independência dos pares. Não foi realizada seleção de modelo definitivo com esses resultados.

## O que está demonstrado e o que permanece aberto

**Demonstrado:** divergência reproduzível em mesmo corpus e quadro físico; paridade numérica V3 original ↔ lambda 0; influência material de U raro, TF e margem; rastreabilidade de opções e artefatos por hashes; diagnósticos pareados de rank e Resolve. Os workflows aprovados verificam execução e regressão, não certificação estatística.

**Não demonstrado:** que toda divergência seja exclusivamente causada pelo fallback U; que V3 ou qualquer mistura seja superior em dados independentes; FDR populacional certificado; causalidade isolada de M/U versus TF/Rank/Resolve; ativação em produção. Não interpretar perda de TP como prova de bug matemático, nem melhora de TP como prova de estimador correto.

## Procedimento de seleção

1. Declarar no plano somente métodos e valores que se deseja comparar, inclusive V2 se desejado.
2. Congelar corpus, partição, sementes, blocking, opções TF, margens, orçamento e política de risco; registrar hashes e recibos no bundle de artefatos no padrão existente da V2 (sem Terraform).
3. Executar TRAIN e examinar candidatos, rejeições, conflitos, TP/FP, recall, FDR e limites superiores, fanout e pendências; comparar no mesmo quadro.
4. Aplicar a política de elegibilidade e seleção do calibrador; não escolher pelo maior TP isolado.
5. Validar finalistas em holdout independente e cumprir os gates estatísticos/DT-15 antes de qualquer ativação.

Referências: `Solution/docs/V9_V3_U_Rare_Mixture_Experiment_20261106.md`, `Solution/src/Jornada.Linkage.Parameters.Worker/V9MuEstimationSearchSpace.cs`, `V9JointSupportEstimation.cs`, PR #1300 e workflow `v9-u-rare-mixture-physical-train`.

## Evidências auditáveis da exploração — SHA e artefatos

As tabelas anteriores são um **resumo interpretativo**; para reproduzir e auditar a conclusão, utilizar os relatórios e recibos gerados nas execuções físicas. As execuções abaixo são do commit imutável `e1b6a071d68eb0eaf16fc00325b68cafa46ec3b4`, anterior ao commit exclusivamente documental. Todos os runs indicados concluíram com `success`. A atualização posterior da documentação não altera retroativamente seus resultados.

### Regressão ampla 160 configurações

- Workflow: https://github.com/lucianox777/Jornada/actions/runs/42380442728
- Artefatos publicados:
  - `v9-v3-broad-surname-seed57-42380442728-e1b6a071d68eb0eaf16fc00325b68cafa46ec3b4` — [artefato #13578310181](https://github.com/lucianox777/Jornada/actions/runs/42380442728/artifacts/13578310181); estado consultado: disponível.
  - `v9-v3-broad-first_name-seed42-42380442728-e1b6a071d68eb0eaf16fc00325b68cafa46ec3b4` — [artefato #13578235614](https://github.com/lucianox777/Jornada/actions/runs/42380442728/artifacts/13578235614); estado consultado: disponível.
  - `v9-v3-broad-surname-seed42-42380442728-e1b6a071d68eb0eaf16fc00325b68cafa46ec3b4` — [artefato #13578102048](https://github.com/lucianox777/Jornada/actions/runs/42380442728/artifacts/13578102048); estado consultado: disponível.
  - `v9-v3-broad-first_name-seed43-42380442728-e1b6a071d68eb0eaf16fc00325b68cafa46ec3b4` — [artefato #13578025579](https://github.com/lucianox777/Jornada/actions/runs/42380442728/artifacts/13578025579); estado consultado: disponível.
  - `v9-v3-broad-surname-seed43-42380442728-e1b6a071d68eb0eaf16fc00325b68cafa46ec3b4` — [artefato #13577784064](https://github.com/lucianox777/Jornada/actions/runs/42380442728/artifacts/13577784064); estado consultado: disponível.
  - `v9-v3-broad-first_name-seed57-42380442728-e1b6a071d68eb0eaf16fc00325b68cafa46ec3b4` — [artefato #13577278774](https://github.com/lucianox777/Jornada/actions/runs/42380442728/artifacts/13577278774); estado consultado: disponível.

### Mistura U — experimentos físicos pareados

- Workflow: https://github.com/lucianox777/Jornada/actions/runs/42380442603
- Artefatos publicados:
  - `v9-u-rare-mixture-m025-42380442603-e1b6a071d68eb0eaf16fc00325b68cafa46ec3b4` — [artefato #13577737647](https://github.com/lucianox777/Jornada/actions/runs/42380442603/artifacts/13577737647); estado consultado: disponível.
  - `v9-u-rare-mixture-m050-42380442603-e1b6a071d68eb0eaf16fc00325b68cafa46ec3b4` — [artefato #13577304978](https://github.com/lucianox777/Jornada/actions/runs/42380442603/artifacts/13577304978); estado consultado: disponível.

### V3 direta — comparação física

- Workflow: https://github.com/lucianox777/Jornada/actions/runs/42380442606
- Artefatos publicados:
  - `v9-v3-direct-train-42380442606-e1b6a071d68eb0eaf16fc00325b68cafa46ec3b4` — [artefato #13577383434](https://github.com/lucianox777/Jornada/actions/runs/42380442606/artifacts/13577383434); estado consultado: disponível.

### Como verificar a evidência

1. Abrir o workflow do SHA indicado e baixar o bundle original, sem reconstruir os números a partir desta página.
2. Conferir manifesto, recibos, plano congelado, configuração de TF/margem/seed, relatórios estatísticos e arquivos de hashes incluídos no próprio bundle. Verificar SHA-256 dos arquivos contra os digests registrados **no bundle**; IDs de artifact do GitHub não substituem hash de conteúdo.
3. Conferir a contagem de fontes elegíveis, as diferenças pareadas por fonte, TP/FP, rejeições por fanout e distinção entre margem primária e sensibilidade.
4. Distinguir o artefato experimental do modelo escolhido: a exploração é TRAIN sintético, não TEST/Gold, certificação de FDR nem autorização de produção.

**Retenção:** links de artifacts do GitHub Actions dependem da política de expiração do repositório. Os identificadores e o SHA permanecem registrados aqui, mas um link pode deixar de permitir download. Para preservação permanente, arquivar os bundles e respectivos hashes no repositório de evidências adotado pelo projeto, seguindo o padrão V2; não alegar preservação permanente somente por existir um link do Actions.

**Outros documentos de suporte:** `Solution/docs/V9_V3_U_Rare_Mixture_Experiment_20261106.md` descreve hipóteses, desenho, critérios de auditoria e limitações; `Solution/scripts/v9-u-rare-mixture-physical-audit.py` contém a auditoria reexecutável da campanha; `Solution/scripts/v9-v3-source-paired-audit.py` e `Solution/scripts/v9-v3-source-gap-audit.py` apoiam a atribuição pareada.
