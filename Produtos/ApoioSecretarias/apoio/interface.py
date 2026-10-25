"""Interface local Windows. Não expõe servidor web, não processa arquivos automaticamente."""
from __future__ import annotations
import tkinter as tk
from tkinter import filedialog, messagebox, ttk
from pathlib import Path
from .core import executar


def main():
    root = tk.Tk()
    root.title("Jornada — Apoio às Secretarias")
    root.geometry("780x500")
    root.minsize(690, 420)
    path = tk.StringVar()
    profile = tk.StringVar()
    dest = tk.StringVar()
    sending = tk.BooleanVar(value=False)

    shell = ttk.Frame(root, padding=18)
    shell.pack(fill="both", expand=True)
    ttk.Label(shell, text="Preparação de arquivos da Secretaria", font=("Segoe UI", 16, "bold")).pack(anchor="w", pady=(0, 10))
    ttk.Label(shell, text="Selecione um arquivo ou pasta. O perfil determina os campos, contratos e versões.").pack(anchor="w")

    def row(label, variable, options):
        ttk.Label(shell, text=label).pack(anchor="w", pady=(12, 0))
        line = ttk.Frame(shell)
        line.pack(fill="x")
        ttk.Entry(line, textvariable=variable).pack(side="left", fill="x", expand=True)
        for text, fn in options:
            ttk.Button(line, text=text, command=lambda fn=fn: variable.set(fn() or variable.get())).pack(side="left", padx=(6, 0))

    row("Origem", path, [("Arquivo", lambda: filedialog.askopenfilename()),
                             ("Pasta", lambda: filedialog.askdirectory())])
    row("Perfil de integração (.json)", profile, [("Escolher", lambda: filedialog.askopenfilename(filetypes=[("JSON", "*.json")]))])
    row("Pasta de saída (fora da pasta de origem)", dest, [("Escolher", lambda: filedialog.askdirectory())])
    ttk.Checkbutton(shell, text="Enviar também à API remota (exige endpoint HTTPS e JORNADA_ACCESS_KEY)", variable=sending).pack(anchor="w", pady=14)
    status = tk.StringVar(value="Pronto para validar e gerar.")
    ttk.Label(shell, textvariable=status, wraplength=710).pack(anchor="w")
    log = tk.Text(shell, height=10, wrap="word", state="disabled")
    log.pack(fill="both", expand=True, pady=10)

    def execute():
        if not all((path.get(), profile.get(), dest.get())):
            messagebox.showwarning("Campos obrigatórios", "Escolha origem, perfil e saída.")
            return
        status.set("Validando a seleção e os contratos...")
        root.update_idletasks()
        report = executar(path.get(), profile.get(), dest.get(), enviar=sending.get())
        status.set("Situação: " + report["estado"])
        texts = [report["estado"]]
        for item in report.get("pacotes", []):
            texts.append("Pacote: " + item["arquivo"])
        for err in report.get("erros", []):
            texts.append(f"{err['codigo']}: {err['mensagem']} | {err['arquivo']} | linha {err['linha']} campo {err['campo']}")
        texts.append("Relatório HTML: " + report.get("relatorioHtml", "indisponível"))
        log.configure(state="normal")
        log.delete("1.0", "end")
        log.insert("end", "\n".join(texts))
        log.configure(state="disabled")
        if report["erros"]:
            messagebox.showerror("Operação não concluída", "Consulte o relatório de erros na pasta de saída.")
        else:
            messagebox.showinfo("Operação concluída", "ZIP salvo. " + ("Recebimento confirmado." if sending.get() else "Envio remoto desativado."))

    ttk.Button(shell, text="Validar, compactar e gerar ZIP", command=execute).pack(anchor="e")
    root.mainloop()

if __name__ == "__main__":
    main()
