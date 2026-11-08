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
