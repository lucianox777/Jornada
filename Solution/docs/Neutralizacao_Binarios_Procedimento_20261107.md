# Neutralização DOCX/PDF — ferramenta de auditoria (NEUT-DEV-20261107)

Execução a partir da raiz de um checkout de trabalho isolado, **nunca diretamente na master protegida**:

```bash
python Solution/scripts/neutralize-developer-binaries.py --self-test
python Solution/scripts/neutralize-developer-binaries.py --root . --report audit-before.json
python Solution/scripts/neutralize-developer-binaries.py --root . --apply-docx --report audit-after.json
```

O script utiliza somente bibliotecas padrão para DOCX; requer `pypdf` ou `pdftotext` para PDFs e falha de modo fechado se não puder extrair. A auditoria conta texto do conteúdo OOXML, inclusive palavras divididas entre runs, e atributos XML; imagens/scans sem texto exigem inspeção manual. Preserva elementos e estilos de runs, mas **não substitui** renderização visual de cada DOCX modificado e comparação das páginas. Os PDFs nunca são modificados: a lista `pdfs_to_regenerate` indica necessidade de regeneração controlada a partir da fonte. Em outros formatos editáveis (.pptx/.xlsx), ausência de suporte provoca erro explícito.

Somar o resultado binário à busca de todo texto indexado no checkout: `git grep -I -i -o 'p[r]odam' | wc -l`. Só reportar zero quando **não houver falhas de extração** e as contagens de texto e binários forem zero, salvo baselines SQL cujo hash histórico tenha sido documentado e aprovado pelo PO. Proibido alterar arquivo selado, descartar PDF com conteúdo histórico ou reescrever o Git.

Esta PR fornece a ferramenta e o teste sintético, **não** comprova que os 45 binários já foram inspecionados ou editados. A edição efetiva dos DOCX e a lista dos PDFs dependerão da execução e revisão visual em checkout real, com commit em PR própria. Não executar sobre dados reais.

## Verificação CI em PR

O workflow read-only `.github/workflows/neutralizacao-binarios-audit.yml` faz contagem no próprio runner e publica exclusivamente relatório agregado. Permissões `contents: read`, `persist-credentials: false`, actions SHA-pinned; nunca faz commit ou push. A inspeção de 45 documentos não implica autorização para distribuir arquivos, editar PDF ou publicar o conteúdo individualizado.

## Procedimento controlado de aplicação do patch binário F — 08/11/2026

**Evidência:** [run GitHub 42508046393](https://github.com/lucianox777/Jornada/actions/runs/42508046393), artifact ID **13627958839**, arquivo interno `binary-edits.zip`. Hash SHA-256 da cópia exportada e verificada pelo assistente: **`376bae3e06b34802e736f1b3c7d47e2e7e3b6ecc1b98a1822564b301c8d3f988`**. Contém os 14 arquivos aprovados no patch parcial (10 DOCX + 4 PDF), mais recibo JSON; não inclui os 6 PDFs excepcionados.

**Não equivale a merge:** a CI só gerou artifact read-only, sem `git push`. Para aplicar o patch a partir de uma cópia revisada, usar checkout isolado **da PR #1332**, nunca `master`:

```bash
git switch tools/neutralizacao-binarios-auditoria-ci-20261107
python3 Solution/scripts/apply-neutralized-binary-patch.py --zip /CAMINHO/binary-edits.zip
python3 Solution/scripts/apply-neutralized-binary-patch.py --zip /CAMINHO/binary-edits.zip --apply
python3 Solution/scripts/neutralize-developer-binaries.py --root . --report audit-final.json
git diff --stat -- Documentos/
# Revisar visualmente todos os DOCX alterados e os PDFs regenerados
git add -- Documentos/
git commit -m "docs(F): neutralizar 10 DOCX e regenerar 4 PDF de fonte validada"
git push
```

O aplicador local usa **dry-run por padrão** e exige branch F correta, checkout binário sem alterações prévias, ZIP cujo SHA corresponde exatamente ao artifact, 45 arquivos inventariados, contagens 120→49, lista de 14 caminhos sob `Documentos/` e tipos DOCX/PDF. **Nunca faz commit, push, altera migrations ou substitui PDF excepcional.** O comando de commit/push acima é uma **ação humana explícita após a revisão visual**, não ocorreu automaticamente. Após o push devem rodar os checks no HEAD exato da PR, seguido dos gates pós-merge da master antes de iniciar a campanha.

### PDFs de fonte existente com alteração de paginação

O workflow tem modo de **preview seguro**: `--preview-dir pdf-previews` gera **candidatos renderizados separados** dos PDFs com divergência de número de páginas e os inclui no artifact para conferência visual. Os previews não substituem PDFs publicados nem contam como neutralizados. Confirmar fontes/layout e autorizar adoção da versão regenerada ou deixar exceção documentada com justificativa. O PDF `Resumo_Executivo_v3.82.pdf` sem DOCX com mesmo basename continua `AGUARDA PO`/fonte editável.
