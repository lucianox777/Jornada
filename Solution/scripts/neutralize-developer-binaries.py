#!/usr/bin/env python3
"""Audit tracked DOCX/PDF and optionally neutralize editable DOCX in place.

PDF files are NEVER modified. The tree-wide final count must also include
git grep on plain text files. No personal data or office content is logged.
"""
from __future__ import annotations
import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path
from xml.dom import Node, minidom

LEGACY = "PRO" + "DAM"
NEW = "DESENVOLVEDOR"
PATTERN = re.compile(r"(?:(?<!\w)(pela|da|na|à|a)(\s+))?" + LEGACY + r"(?:-SP)?", re.I)
ARTICLES = {"pela":"pelo", "da":"do", "na":"no", "à":"ao", "a":"o"}
WORD = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
DRAWING = "http://schemas.openxmlformats.org/drawingml/2006/main"

def substitute(match):
    article, sep = match.group(1), match.group(2)
    if article is None:
        return NEW
    value = ARTICLES[article.lower()]
    if article.isupper(): value = value.upper()
    elif article.istitle(): value = value.title()
    return value + (sep or " ") + NEW

def closest_paragraph(node):
    parent = node.parentNode
    while parent is not None:
        if (parent.nodeType == Node.ELEMENT_NODE
                and parent.localName == "p"
                and parent.namespaceURI in (WORD, DRAWING)):
            return parent
        parent = parent.parentNode
    return node.parentNode

def groups(document):
    result = {}
    for element in document.getElementsByTagName("*"):
        if any(x.nodeType == Node.ELEMENT_NODE for x in element.childNodes):
            continue
        for node in element.childNodes:
            if node.nodeType != Node.TEXT_NODE or not node.data.strip():
                continue
            result.setdefault(id(closest_paragraph(node)), []).append(node)
    return list(result.values())

def replace_span(nodes, start, end, value):
    offset, first, last = 0, None, None
    for i, node in enumerate(nodes):
        next_offset = offset + len(node.data)
        if first is None and start < next_offset:
            first = (i, start - offset)
        if first is not None and end <= next_offset:
            last = (i, end - offset)
            break
        offset = next_offset
    if first is None or last is None:
        raise ValueError("OOXML node range is inconsistent; refusing replacement")
    a, start_at = first
    b, end_at = last
    if a == b:
        nodes[a].data = nodes[a].data[:start_at] + value + nodes[a].data[end_at:]
    else:
        nodes[a].data = nodes[a].data[:start_at] + value
        for i in range(a + 1, b): nodes[i].data = ""
        nodes[b].data = nodes[b].data[end_at:]

def transform_xml(payload, apply):
    document = minidom.parseString(payload)
    count, changed = 0, False
    for nodes in groups(document):
        matches = list(PATTERN.finditer("".join(node.data for node in nodes)))
        count += len(matches)
        if apply and matches:
            for match in reversed(matches):
                replace_span(nodes, match.start(), match.end(), substitute(match))
            changed = True
    for element in document.getElementsByTagName("*"):
        attrs = element.attributes
        for i in range(attrs.length):
            attr = attrs.item(i)
            if attr is None: continue
            matches = list(PATTERN.finditer(attr.value))
            count += len(matches)
            if apply and matches:
                attr.value = PATTERN.sub(substitute, attr.value)
                changed = True
    return document.toxml(encoding="UTF-8") if apply and changed else payload, count

def docx_mentions(path, apply=False):
    if not zipfile.is_zipfile(path):
        raise ValueError("DOCX not a valid ZIP")
    count, records = 0, []
    with zipfile.ZipFile(path) as source:
        for item in source.infolist():
            data = source.read(item)
            if item.filename.endswith((".xml",".rels")):
                data, n = transform_xml(data, apply)
                count += n
            records.append((item,data))
    if not apply or count == 0: return count, count
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(prefix="jornada-docx-",suffix=".docx",
                                         dir=path.parent,delete=False) as f:
            temporary = Path(f.name)
        with zipfile.ZipFile(temporary,"w") as target:
            for info,data in records: target.writestr(info,data)
        with zipfile.ZipFile(temporary) as check:
            if check.testzip() is not None:
                raise ValueError("Invalid rewritten DOCX ZIP")
        _, remaining = docx_mentions(temporary,apply=False)
        if remaining: raise ValueError("Residual legacy reference after DOCX edit")
        os.replace(temporary,path)
        temporary = None
        return count,0
    finally:
        if temporary is not None and temporary.exists(): temporary.unlink()

def pdf_mentions(path):
    try:
        from pypdf import PdfReader
        reader = PdfReader(str(path),strict=True)
        if reader.is_encrypted:
            raise ValueError("Encrypted PDF requires controlled manual review")
        text = "\n".join(page.extract_text() or "" for page in reader.pages)
    except ImportError:
        exe = shutil.which("pdftotext")
        if exe is None:
            raise RuntimeError("PDF not audited; install pypdf or pdftotext")
        text = subprocess.run([exe,"-layout",str(path),"-"],capture_output=True,
                              text=True,check=True).stdout
    return len(PATTERN.findall(text)),not bool(text.strip())

def scan(root,apply):
    files = subprocess.run(["git","-C",str(root),"ls-files","-z"],
                           capture_output=True,check=True).stdout.split(b"\x00")
    rows = []
    for raw in files:
        if not raw: continue
        path = root / os.fsdecode(raw)
        suffix = path.suffix.lower()
        if suffix not in {".docx",".pdf",".pptx",".xlsx"}: continue
        row = {"path":str(path.relative_to(root)), "type":suffix}
        try:
            if suffix == ".docx":
                row["before"],row["after"] = docx_mentions(path,apply)
            elif suffix == ".pdf":
                row["before"],row["text_empty"] = pdf_mentions(path)
                row["after"] = row["before"]
                row["action"] = "REGENERATE_FROM_SOURCE" if row["before"] else "NONE"
            else:
                row["error"] = "Editable format requires a separate editor"
        except Exception as exc:
            row["error"] = type(exc).__name__ + ": " + str(exc)
        rows.append(row)
    return {
        "schema":"NEUT_DEV_BINARY_AUDIT_V1", "apply_docx":apply,
        "total_files":len(rows), "files":rows,
        "verified_mentions_before":sum(r.get("before",0) for r in rows),
        "verified_mentions_after":sum(r.get("after",0) for r in rows),
        "pdfs_to_regenerate":[r["path"] for r in rows if r["type"]==".pdf" and r.get("before",0)],
        "not_audited":[r["path"] for r in rows if r.get("error") or r.get("text_empty")]
    }

def self_test():
    with tempfile.TemporaryDirectory() as directory:
        path = Path(directory)/"test.docx"
        part = '<w:p xmlns:w="'+WORD+'"><w:r><w:t>pela PRO</w:t></w:r><w:r><w:t>DAM e PRO'+'DAM-SP</w:t></w:r></w:p>'
        with zipfile.ZipFile(path,"w") as zipout:
            zipout.writestr("word/document.xml",part)
            zipout.writestr("[Content_Types].xml","<Types/>")
        assert docx_mentions(path) == (2,2)
        assert docx_mentions(path,apply=True) == (2,0)
        with zipfile.ZipFile(path) as zipin:
            document = minidom.parseString(zipin.read("word/document.xml"))
        assert "".join(n.data for group in groups(document) for n in group) == \
            "pelo DESENVOLVEDOR e DESENVOLVEDOR"
    print("SELF-TEST OK: split runs, grammatical articles and ZIP integrity")

def main():
    cli = argparse.ArgumentParser()
    cli.add_argument("--root",type=Path)
    cli.add_argument("--report",type=Path)
    cli.add_argument("--apply-docx",action="store_true")
    cli.add_argument("--self-test",action="store_true")
    args = cli.parse_args()
    if args.self_test:
        self_test()
        return
    if args.root is None or args.report is None:
        cli.error("--root and --report are required")
    report = scan(args.root.resolve(),args.apply_docx)
    args.report.write_text(json.dumps(report,ensure_ascii=False,indent=2)+"\n",
                           encoding="utf-8")
    print(json.dumps({
        "files":report["total_files"],
        "verified_mentions_before":report["verified_mentions_before"],
        "verified_mentions_after":report["verified_mentions_after"],
        "not_audited":len(report["not_audited"])
    },ensure_ascii=False))
    if report["not_audited"]: sys.exit(2)

if __name__=="__main__":
    main()
