static class Page
{
    public const string Html="""
<!doctype html>
<html lang="pt-BR">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width">
<title>Jornada DEV Console</title>
<style>
:root{font-family:system-ui,-apple-system,Segoe UI,sans-serif;color:#18212b;background:#f3f5f7}
*{box-sizing:border-box}
body{margin:0}
button,input,textarea{font:inherit}
button{cursor:pointer}
header{position:sticky;top:0;z-index:5;background:#fff;border-bottom:1px solid #d9dee5;padding:14px 22px;display:flex;align-items:center;justify-content:space-between}
header h1{font-size:20px;margin:0}
main{max-width:1180px;margin:0 auto;padding:22px}
.toolbar{display:flex;gap:8px;align-items:center}
.iconbtn{border:1px solid #cfd6df;background:#fff;border-radius:8px;padding:8px 12px}
.hero{margin-bottom:16px}
.hero p{color:#5d6875}
.card{background:#fff;border:1px solid #d9dee5;border-radius:10px;padding:15px;margin:10px 0}
.command-head{display:flex;justify-content:space-between;gap:16px;align-items:center}
.command-title{font-weight:700;font-size:16px}
.command-desc{margin:6px 0;color:#45515e}
.command-line{display:block;color:#6b7580;font:12px ui-monospace,SFMono-Regular,Consolas,monospace;overflow-wrap:anywhere}
.command-actions{display:flex;gap:10px;align-items:center;white-space:nowrap}
.count{color:#66717d;font-size:13px}
.primary{background:#1463d7;color:#fff;border:1px solid #1463d7;border-radius:7px;padding:8px 13px}
.secondary{background:#fff;border:1px solid #cfd6df;border-radius:7px;padding:8px 13px}
.danger{background:#fff2f0;color:#9b241c;border:1px solid #e8b3ad;border-radius:7px;padding:8px 13px}
.hidden{display:none!important}
.console-shell{background:#0b0f14;border-radius:10px;border:1px solid #202834;overflow:hidden;box-shadow:0 8px 24px rgba(0,0,0,.13)}
.console-top{background:#151b23;color:#eef4fb;padding:10px 14px;display:flex;align-items:center;justify-content:space-between;gap:12px}
.console-title{font-weight:700}
.status{font-size:12px;padding:4px 8px;border-radius:999px;background:#323c49}
.status.running{background:#6b4d00;color:#ffe9a9}
.status.success{background:#163f29;color:#a7f3c5}
.status.failure{background:#5a2020;color:#ffc5c5}
.terminal{height:58vh;min-height:360px;overflow:auto;padding:14px 16px;background:#05080c;color:#d7e0ea;font:13px/1.55 ui-monospace,SFMono-Regular,Consolas,monospace;white-space:pre-wrap}
.line{min-height:1.5em}
.line.command{color:#8ec8ff;font-weight:700}
.line.stderr{color:#ff9b9b}
.line.system{color:#aeb8c5}
.line.result{color:#a7f3c5}
.line.status{display:block;background:transparent;padding:0;border-radius:0;font-weight:700;color:#ffe08a}
.console-footer{background:#fff;padding:14px;border:1px solid #d9dee5;border-top:0;border-radius:0 0 10px 10px}
.result-box{margin-top:10px;padding:10px;border-radius:7px;background:#eef8ef;border:1px solid #b7dbbd;overflow-wrap:anywhere}
.result-actions{display:flex;flex-wrap:wrap;gap:8px;margin-top:10px}
.history-item{display:grid;grid-template-columns:1fr auto;gap:10px;padding:12px 0;border-bottom:1px solid #e5e8ec}
.history-title{font-weight:700}
.history-meta{color:#697481;font-size:13px}
.history-status{font-weight:700}
.history-status.SUCESSO{color:#1f7a43}
.history-status.FALHA{color:#a2332b}
.history-status.SEM-EXECUTOR{color:#9a6a00}
dialog{width:min(900px,94vw);border:1px solid #cad2dc;border-radius:10px;padding:0;box-shadow:0 18px 60px rgba(0,0,0,.28)}
dialog::backdrop{background:rgba(0,0,0,.45)}
.dialog-head{padding:14px 18px;border-bottom:1px solid #dde2e8;display:flex;justify-content:space-between;align-items:center}
.dialog-body{padding:16px 18px;max-height:70vh;overflow:auto}
.dialog-body label{display:block;font-weight:700;margin:12px 0 5px}
.dialog-body input,.dialog-body textarea{width:100%;padding:8px;border:1px solid #cbd3dc;border-radius:6px;font:13px ui-monospace,SFMono-Regular,Consolas,monospace}
.dialog-body textarea{min-height:110px}
.dialog-actions{display:flex;justify-content:flex-end;gap:8px;padding:12px 18px;border-top:1px solid #dde2e8}
table{border-collapse:collapse;width:100%;font-size:13px}
th,td{border-bottom:1px solid #ddd;padding:7px;text-align:left;vertical-align:top}
.back{margin-bottom:12px}
.small{font-size:12px;color:#697481}
@media(max-width:700px){main{padding:12px}.command-head{align-items:flex-start;flex-direction:column}.command-actions{width:100%;justify-content:space-between}.terminal{height:55vh}}
</style>
</head>
<body>
<header>
  <h1>Jornada · Console DEV</h1>
  <div class="toolbar">
    <button class="iconbtn" type="button" onclick="showHome()">⌂ Comandos</button>
    <button class="iconbtn" type="button" onclick="showHistory()">🕘 Execuções</button>
  </div>
</header>
<main>
  <section id="homeView">
    <div class="hero">
      <h2>Comandos</h2>
      <p>A tela mostra somente operações reais. A infraestrutura já inclui schema e referência IBGE; Bronze → Silver → identidade → Gold são etapas do Processor residente, acompanhadas pelo status da ingestão.</p>
    </div>
    <div id="commands">Carregando...</div>
  </section>

  <section id="consoleView" class="hidden">
    <button class="secondary back" type="button" onclick="showHome()">← Voltar aos comandos</button>
    <div class="console-shell">
      <div class="console-top">
        <span id="consoleTitle" class="console-title">Execução</span>
        <span id="consoleStatus" class="status running">RODANDO...</span>
      </div>
      <div id="terminal" class="terminal" role="log" aria-live="polite"></div>
    </div>
    <div id="consoleFooter" class="console-footer">
      <div id="runSummary">Aguardando conclusão...</div>
      <div id="resultPath"></div>
      <div id="resultActions" class="result-actions"></div>
      <div id="recordsPanel" class="hidden"></div>
    </div>
  </section>

  <section id="historyView" class="hidden">
    <button class="secondary back" type="button" onclick="showHome()">← Voltar aos comandos</button>
    <h2>Execuções anteriores</h2>
    <p class="small">O histórico é persistido entre reinicializações da Console DEV.</p>
    <div id="history">Carregando...</div>
  </section>
</main>

<dialog id="zipDialog">
  <div class="dialog-head"><strong>Entrada manual para o ZIP</strong><button class="secondary" type="button" onclick="zipDialog.close()">Fechar</button></div>
  <div class="dialog-body">
    <p>Edite os dados que irão para <code>manifest.json</code>, <code>pessoas.jsonl</code> e <code>registros.jsonl</code>. Depois a Console abre a tela de execução e mostra a geração do ZIP linha por linha.</p>
    <label for="zipGestor">Gestor</label>
    <input id="zipGestor" value="SEHAB">
    <label for="zipManifest">manifest.json</label>
    <textarea id="zipManifest">{"formatoVersao":2,"pessoaSchemaVersao":4,"codigoSistemaOrigem":"SEHAB","natureza":"BENEFICIO","codigoTipo":"AA01","tipoVersao":1,"dataReferencia":"2026-10-03T00:00:00-03:00"}</textarea>
    <label for="zipPessoas">pessoas.jsonl</label>
    <textarea id="zipPessoas">{"idPessoaEntrega":"PESSOA-MANUAL-001","cpf":null,"cpfAusenteMotivo":"NAO_INFORMADO_ORIGEM","nomeCompleto":"Maria Exemplo","dataNascimento":"1982-04-10","nomeMae":"Ana Exemplo","sourceTransactionId":"DEV-MANUAL-001","atributosTransversais":[]}</textarea>
    <label for="zipRegistros">registros.jsonl</label>
    <textarea id="zipRegistros">{"idPessoaEntrega":"PESSOA-MANUAL-001","codigoRegistroOrigem":"DEV-MANUAL-REG-001","operacao":"INCLUSAO","dataInicioConcessao":"2026-10-01","valorConcedido":600.0,"dataEventoConcessao":"2026-10-03","situacaoVigencia":"VIGENTE"}</textarea>
  </div>
  <div class="dialog-actions">
    <button class="secondary" type="button" onclick="zipDialog.close()">Cancelar</button>
    <button class="primary" type="button" onclick="startZip()">Gerar e executar</button>
  </div>
</dialog>

<script>
const esc=x=>String(x??'').replace(/[&<>"]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));
const views=[homeView,consoleView,historyView];
let commandsCache=[];
let currentRunId=null;
let currentCommandId=null;
let eventSource=null;

async function api(url,options){
  const response=await fetch(url,options);
  if(!response.ok)throw new Error(await response.text());
  const type=response.headers.get('content-type')||'';
  return type.includes('application/json')?response.json():response.text();
}

function switchView(view){
  for(const item of views)item.classList.toggle('hidden',item!==view);
  window.scrollTo({top:0,behavior:'smooth'});
}

async function showHome(){
  if(eventSource){eventSource.close();eventSource=null}
  switchView(homeView);
  await loadCommands();
}

async function showHistory(){
  if(eventSource){eventSource.close();eventSource=null}
  switchView(historyView);
  const runs=await api('/api/runs');
  history.innerHTML=runs.length?runs.map(r=>{
    const statusClass=String(r.status).replaceAll(' ','-');
    return '<div class="history-item"><div><a href="#" onclick="openHistoryRun(\''+r.id+'\');return false"><span class="history-title">'+esc(r.title)+'</span></a><div class="history-meta">'+esc(new Date(r.startedAt).toLocaleString())+' · '+esc(r.summary)+'</div></div><div class="history-status '+esc(statusClass)+'">'+esc(r.status)+'</div></div>'
  }).join(''):'Nenhuma execução registrada.';
}

async function loadCommands(){
  commandsCache=await api('/api/commands');
  commands.innerHTML=commandsCache.map(c=>{
    const label=c.id==='zip'?'Preencher dados':'Executar';
    const buttonClass=c.id==='finish'?'danger':'primary';
    return '<div class="card"><div class="command-head"><div><div class="command-title">'+esc(c.title)+'</div><div class="command-desc">'+esc(c.description)+'</div><small class="command-line">'+esc(c.displayCommand)+'</small></div><div class="command-actions"><span class="count">'+c.runCount+' execução(ões)</span><button class="'+buttonClass+'" type="button" onclick="'+(c.id==='zip'?'zipDialog.showModal()':"startCommand('"+c.id+"')")+'">'+label+'</button></div></div></div>'
  }).join('');
}

async function startCommand(id){
  const command=commandsCache.find(x=>x.id===id);
  const response=await api('/api/commands/'+encodeURIComponent(id)+'/start',{method:'POST'});
  openLiveRun(response.id,command?.title||id,id);
}

async function startZip(){
  zipDialog.close();
  const payload={gestor:zipGestor.value,manifestJson:zipManifest.value,pessoasJsonl:zipPessoas.value,registrosJsonl:zipRegistros.value};
  const response=await api('/api/zip/manual/start',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(payload)});
  openLiveRun(response.id,'Gerar ZIP de ingestão','zip');
}

function resetConsole(title){
  consoleTitle.textContent=title;
  consoleStatus.textContent='RODANDO...';
  consoleStatus.className='status running';
  terminal.innerHTML='';
  runSummary.textContent='Execução em andamento...';
  resultPath.innerHTML='';
  resultActions.innerHTML='';
  recordsPanel.className='hidden';
  recordsPanel.innerHTML='';
}

function appendConsole(item){
  const line=document.createElement('div');
  line.className='line '+esc(item.stream);
  const time=new Date(item.at).toLocaleTimeString();
  line.textContent='['+time+'] '+item.text;
  terminal.appendChild(line);
  terminal.scrollTop=terminal.scrollHeight;
}

function openLiveRun(id,title,commandId){
  if(eventSource)eventSource.close();
  currentRunId=id;
  currentCommandId=commandId;
  resetConsole(title);
  switchView(consoleView);
  eventSource=new EventSource('/api/runs/'+id+'/stream');
  eventSource.onmessage=async event=>{
    const item=JSON.parse(event.data);
    appendConsole(item);
    if(item.stream==='status'){
      eventSource.close();
      eventSource=null;
      await finishConsole(id);
    }
  };
  eventSource.onerror=async()=>{
    if(eventSource){eventSource.close();eventSource=null}
    try{await finishConsole(id)}catch(e){appendConsole({at:new Date(),stream:'stderr',text:'Falha no streaming: '+e.message})}
  };
}

async function finishConsole(id){
  let run=null;
  for(let i=0;i<20;i++){
    try{run=await api('/api/runs/'+id);break}catch{await new Promise(r=>setTimeout(r,100))}
  }
  if(!run)return;
  renderFinal(run);
}

function renderFinal(run){
  currentRunId=run.id;
  currentCommandId=run.command;
  consoleTitle.textContent=run.title;
  const cls=run.status==='SUCESSO'?'success':run.status==='FALHA'?'failure':'running';
  consoleStatus.textContent=run.status;
  consoleStatus.className='status '+cls;
  runSummary.innerHTML='<b>'+esc(run.status)+'</b> · '+esc(run.summary)+'<br><span class="small">Duração: '+(Number(run.step.durationMs||0)/1000).toFixed(2)+' s · Exit code: '+esc(run.step.exitCode)+' · Diretório: '+esc(run.step.workingDirectory)+'</span>';
  resultPath.innerHTML=run.step.resultPath?'<div class="result-box"><b>Resultado salvo em:</b><br>'+esc(run.step.resultPath)+'</div>':'';
  const actions=[];
  if(run.step.resultPath)actions.push('<button class="primary" type="button" onclick="window.open(\'/api/runs/'+run.id+'/result\',\'_blank\')">Abrir resultado</button>');
  if(run.records?.length)actions.push('<button class="secondary" type="button" onclick="toggleRecords()">Ver dados do resultado ('+run.records.length+')</button>');
  actions.push('<button class="secondary" type="button" onclick="rerun()">Executar novamente</button>');
  resultActions.innerHTML=actions.join('');
  if(run.records?.length){
    recordsPanel.innerHTML='<h3>Dados do resultado</h3><table><tr>'+Object.keys(run.records[0]).map(k=>'<th>'+esc(k)+'</th>').join('')+'</tr>'+run.records.map(row=>'<tr>'+Object.values(row).map(v=>'<td>'+esc(v)+'</td>').join('')+'</tr>').join('')+'</table>';
  }
}

function toggleRecords(){recordsPanel.classList.toggle('hidden')}

async function rerun(){
  if(currentCommandId==='zip'){zipDialog.showModal();return}
  await startCommand(currentCommandId);
}

async function openHistoryRun(id){
  const run=await api('/api/runs/'+id);
  if(eventSource){eventSource.close();eventSource=null}
  resetConsole(run.title);
  switchView(consoleView);
  appendConsole({at:run.startedAt,stream:'command',text:'> '+run.step.command});
  for(const line of String(run.step.output||'').split(/\r?\n/))if(line)appendConsole({at:run.startedAt,stream:'stdout',text:line});
  for(const line of String(run.step.error||'').split(/\r?\n/))if(line)appendConsole({at:run.finishedAt,stream:'stderr',text:line});
  appendConsole({at:run.finishedAt,stream:'status',text:run.status});
  renderFinal(run);
}

loadCommands().catch(e=>commands.textContent='Falha ao carregar comandos: '+e.message);
</script>
</body>
</html>
""";
}
