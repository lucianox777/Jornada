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
header h1{font-size:20px;margin:0}.brand{display:flex;flex-direction:column;gap:2px}.breadcrumb{font-size:12px;color:#6c7784;font-weight:600}
main{max-width:1180px;margin:0 auto;padding:22px}
.toolbar{display:flex;gap:8px;align-items:center}
.iconbtn{border:1px solid #cfd6df;background:#fff;border-radius:8px;padding:8px 12px}
.hero{margin-bottom:16px}
.hero p{color:#5d6875}
.card{background:#fff;border:1px solid #d9dee5;border-radius:10px;padding:15px;margin:10px 0}
.command-head{display:flex;justify-content:space-between;gap:16px;align-items:center}
.command-title{font-weight:700;font-size:16px}
.command-desc{margin:6px 0;color:#45515e}
.command-line{display:block;color:#6b7580;font:12px ui-monospace,SFMono-Regular,Consolas,monospace;overflow-wrap:anywhere}.dependency{margin-top:8px;padding:8px 10px;border-left:3px solid #d69b22;background:#fff8e6;color:#5f4a15;font-size:13px}.dependency code{font-size:12px}.dep-note{display:block;margin-top:3px;color:#746434}
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
.dialog-body textarea{min-height:110px}.tabs{display:flex;gap:8px;margin:10px 0}.form-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px}.form-grid label{margin:0}.form-grid input,.form-grid select{width:100%;padding:8px;border:1px solid #cbd3dc;border-radius:6px}.artifact-list{margin-top:10px;display:grid;gap:6px}.artifact-item{padding:8px 10px;border:1px solid #d9dee5;border-radius:6px;background:#fafafa;overflow-wrap:anywhere}@media(max-width:700px){.form-grid{grid-template-columns:1fr}}
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
  <div class="brand"><h1>Jornada · Console DEV</h1><div id="breadcrumb" class="breadcrumb">Console DEV / Comandos</div></div>
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
    <h2>Execuções desta sessão</h2>
    <p class="small">A lista começa vazia a cada inicialização da Console DEV e mostra somente as execuções da sessão atual.</p>
    <div id="history">Carregando...</div>
  </section>
</main>

<dialog id="zipDialog">
  <div class="dialog-head"><strong>Entrada manual para o ZIP</strong><button class="secondary" type="button" onclick="zipDialog.close()">Fechar</button></div>
  <div class="dialog-body">
    <p>Você pode preencher por formulário HTML ou editar diretamente JSON/JSONL. O exemplo é carregado da <code>gold.pessoa</code> para não distorcer nomes, nascimento e nome da mãe.</p>
    <div class="tabs">
      <button class="secondary" type="button" onclick="setZipMode('form')">Formulário HTML</button>
      <button class="secondary" type="button" onclick="setZipMode('json')">JSON / JSONL</button>
      <button class="secondary" type="button" onclick="loadGoldTemplate()">Atualizar exemplo da Gold</button>
    </div>
    <div id="zipTemplateSource" class="small"></div>

    <div id="zipFormMode">
      <div class="form-grid">
        <label>Gestor<input id="zipGestor" value="SEHAB"></label>
        <label>Sistema de origem<input id="zipSistema" value="SEHAB"></label>
        <label>Tipo<input id="zipTipo" value="AA01"></label>
        <label>ID pessoa na entrega<input id="zipPessoaId"></label>
        <label>Nome completo<input id="zipNome"></label>
        <label>Data de nascimento<input id="zipNascimento" type="date"></label>
        <label>Nome da mãe<input id="zipMae"></label>
        <label>Código do registro<input id="zipRegistroId"></label>
        <label>Valor concedido<input id="zipValor" type="number" step="0.01" value="600"></label>
        <label>Data do evento<input id="zipDataEvento" type="date"></label>
        <label>Situação<select id="zipSituacao"><option>VIGENTE</option><option>ENCERRADO</option></select></label>
      </div>
    </div>

    <div id="zipJsonMode" class="hidden">
      <label for="zipManifest">manifest.json</label>
      <textarea id="zipManifest"></textarea>
      <label for="zipPessoas">pessoas.jsonl</label>
      <textarea id="zipPessoas"></textarea>
      <label for="zipRegistros">registros.jsonl</label>
      <textarea id="zipRegistros"></textarea>
    </div>
  </div>
  <div class="dialog-actions">
    <button class="secondary" type="button" onclick="zipDialog.close()">Cancelar</button>
    <button class="primary" type="button" onclick="startZip()">Gerar e enviar</button>
  </div>
</dialog>

<dialog id="semiblindDialog">
  <div class="dialog-head"><strong>Consulta semicega · DEV sintético</strong><button class="secondary" type="button" onclick="semiblindDialog.close()">Fechar</button></div>
  <div class="dialog-body">
    <p>Consulta a API real <code>POST /api/v1/identidade/candidatos</code>. A resposta mostra no máximo cinco opções e não expõe CPF, UUID ou score.</p>
    <div class="tabs"><button class="secondary" type="button" onclick="loadSemiblindTemplate()">Usar exemplo da Gold sintética</button></div>
    <div id="semiblindSource" class="small"></div>
    <div class="form-grid">
      <label>Gestor<input id="semiblindGestor" value="SEHAB"></label>
      <label>Data de nascimento<input id="semiblindNascimento" type="date"></label>
      <label>Nome completo<input id="semiblindNome"></label>
      <label>Nome da mãe<input id="semiblindMae"></label>
    </div>
    <div id="semiblindResult" class="hidden">
      <h3>Resultado</h3>
      <pre id="semiblindJson" style="background:#0b0f14;color:#d7e0ea;padding:12px;border-radius:7px;white-space:pre-wrap;overflow:auto"></pre>
    </div>
  </div>
  <div class="dialog-actions">
    <button class="secondary" type="button" onclick="semiblindDialog.close()">Fechar</button>
    <button class="primary" type="button" onclick="runSemiblindSearch()">Consultar</button>
  </div>
</dialog>

<script>
const esc=x=>String(x??'').replace(/[&<>"]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));
const views=[homeView,consoleView,historyView];
const historyList=document.getElementById('history');
const breadcrumb=document.getElementById('breadcrumb');
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

function switchView(view,label){
  for(const item of views)item.classList.toggle('hidden',item!==view);
  breadcrumb.textContent='Console DEV / '+label;
  window.scrollTo({top:0,behavior:'smooth'});
}

async function showHome(){
  if(eventSource){eventSource.close();eventSource=null}
  switchView(homeView,'Comandos');
  await loadCommands();
}

async function showHistory(){
  if(eventSource){eventSource.close();eventSource=null}
  switchView(historyView,'Execuções');
  historyList.textContent='Carregando...';
  const controller=new AbortController();
  const timeout=setTimeout(()=>controller.abort(),7000);
  try{
    const runs=await api('/api/runs',{signal:controller.signal});
    historyList.innerHTML=runs.length?runs.map(r=>{
      const statusClass=String(r.status).replaceAll(' ','-');
      const started=r.startedAt?new Date(r.startedAt).toLocaleString():'data indisponível';
      return '<div class="history-item"><div><a href="#" onclick="openHistoryRun(\''+r.id+'\');return false"><span class="history-title">'+esc(r.title||r.command||'Execução')+'</span></a><div class="history-meta">'+esc(started)+' · '+esc(r.summary||'')+'</div></div><div class="history-status '+esc(statusClass)+'">'+esc(r.status||'')+'</div></div>'
    }).join(''):'Nenhuma execução registrada.';
  }catch(e){
    const detail=e.name==='AbortError'?'A API de histórico excedeu 7 segundos. Reinicie a Console DEV e tente novamente.':e.message;
    historyList.innerHTML='<div class="card"><b>Falha ao carregar o histórico.</b><div class="small">'+esc(detail)+'</div></div>';
  }finally{
    clearTimeout(timeout);
  }
}

async function loadCommands(){
  commandsCache=await api('/api/commands');
  const titleById=Object.fromEntries(commandsCache.map(x=>[x.id,x.title]));
  commands.innerHTML=commandsCache.map(c=>{
    const label=c.id==='zip'?'Preencher dados':c.id==='semiblind'?'Consultar':'Executar';
    const buttonClass=c.id==='finish'?'danger':'primary';
    const deps=(c.dependencies||[]).map(id=>titleById[id]||id);
    const dependency=deps.length||c.dependencyNote
      ?'<div class="dependency"><b>Pré-requisitos:</b> '+(deps.length?deps.map(esc).join(' → '):'nenhum obrigatório')+(c.dependencyNote?'<span class="dep-note">'+esc(c.dependencyNote)+'</span>':'')+'</div>'
      :'';
    const action=c.id==='zip'?'openZipDialog()':c.id==='semiblind'?'openSemiblindDialog()':"startCommand('"+c.id+"')";
    return '<div class="card"><div class="command-head"><div><div class="command-title">'+esc(c.title)+'</div><div class="command-desc">'+esc(c.description)+'</div><small class="command-line">'+esc(c.displayCommand)+'</small>'+dependency+'</div><div class="command-actions"><span class="count">'+c.runCount+' execução(ões)</span><button class="'+buttonClass+'" type="button" onclick="'+action+'">'+label+'</button></div></div></div>'
  }).join('');
}

async function startCommand(id){
  const command=commandsCache.find(x=>x.id===id);
  const response=await api('/api/commands/'+encodeURIComponent(id)+'/start',{method:'POST'});
  openLiveRun(response.id,command?.title||id,id);
}

async function openSemiblindDialog(){
  semiblindDialog.showModal();
  semiblindResult.classList.add('hidden');
  await loadSemiblindTemplate();
}

async function loadSemiblindTemplate(){
  semiblindSource.textContent='Carregando pessoa sintética da Gold...';
  try{
    const t=await api('/api/semiblind/template');
    semiblindGestor.value=t.gestor||'SEHAB';
    semiblindNome.value=t.nomeCompleto||'';
    semiblindNascimento.value=t.dataNascimento||'';
    semiblindMae.value=t.nomeMae||'';
    semiblindSource.textContent='Exemplo: '+t.source+' · pessoa sintética '+t.pessoaUuid;
  }catch(e){
    semiblindSource.textContent='Não foi possível carregar o exemplo: '+e.message;
  }
}

async function runSemiblindSearch(){
  semiblindJson.textContent='Consultando...';
  semiblindResult.classList.remove('hidden');
  try{
    const body={
      gestor:semiblindGestor.value,
      nomeCompleto:semiblindNome.value,
      dataNascimento:semiblindNascimento.value,
      nomeMae:semiblindMae.value
    };
    const result=await api('/api/semiblind/search',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});
    semiblindJson.textContent=JSON.stringify(result,null,2);
  }catch(e){
    semiblindJson.textContent='Falha: '+e.message;
  }
}

let zipMode='form';

function setZipMode(mode){
  zipMode=mode;
  if(mode==='json')syncFormToJson();
  else syncJsonToForm();
  zipFormMode.classList.toggle('hidden',mode!=='form');
  zipJsonMode.classList.toggle('hidden',mode!=='json');
}

async function openZipDialog(){
  zipDialog.showModal();
  await loadGoldTemplate();
  setZipMode('form');
}

async function loadGoldTemplate(){
  zipTemplateSource.textContent='Carregando exemplo da Gold...';
  try{
    const t=await api('/api/zip/template');
    zipGestor.value=t.gestor;
    zipSistema.value=t.codigoSistemaOrigem;
    zipTipo.value=t.codigoTipo;
    zipPessoaId.value=t.idPessoaEntrega;
    zipNome.value=t.nomeCompleto;
    zipNascimento.value=t.dataNascimento;
    zipMae.value=t.nomeMae;
    zipRegistroId.value=t.codigoRegistroOrigem;
    zipDataEvento.value=new Date().toISOString().slice(0,10);
    zipManifest.value=t.manifestJson;
    zipPessoas.value=t.pessoasJsonl;
    zipRegistros.value=t.registrosJsonl;
    zipTemplateSource.textContent='Exemplo obtido de '+t.source+' · pessoa '+t.pessoaUuid;
  }catch(e){
    zipTemplateSource.textContent='Não foi possível carregar exemplo da Gold: '+e.message;
  }
}

function syncFormToJson(){
  const now=new Date();
  const ref=now.toISOString();
  const manifest={
    formatoVersao:2,pessoaSchemaVersao:4,codigoSistemaOrigem:zipSistema.value||zipGestor.value,
    natureza:'BENEFICIO',codigoTipo:zipTipo.value||'AA01',tipoVersao:1,dataReferencia:ref
  };
  const pessoa={
    idPessoaEntrega:zipPessoaId.value,cpf:null,cpfAusenteMotivo:'NAO_INFORMADO_ORIGEM',
    nomeCompleto:zipNome.value,dataNascimento:zipNascimento.value,nomeMae:zipMae.value,
    sourceTransactionId:'DEV-'+(zipPessoaId.value||'MANUAL'),atributosTransversais:[]
  };
  const registro={
    idPessoaEntrega:zipPessoaId.value,codigoRegistroOrigem:zipRegistroId.value,operacao:'INCLUSAO',
    dataInicioConcessao:zipDataEvento.value,valorConcedido:Number(zipValor.value||0),
    dataEventoConcessao:zipDataEvento.value,situacaoVigencia:zipSituacao.value
  };
  zipManifest.value=JSON.stringify(manifest,null,2);
  zipPessoas.value=JSON.stringify(pessoa);
  zipRegistros.value=JSON.stringify(registro);
}

function syncJsonToForm(){
  try{
    const m=JSON.parse(zipManifest.value||'{}');
    const p=JSON.parse((zipPessoas.value||'{}').split(/\r?\n/).filter(Boolean)[0]||'{}');
    const r=JSON.parse((zipRegistros.value||'{}').split(/\r?\n/).filter(Boolean)[0]||'{}');
    zipGestor.value=zipGestor.value||m.codigoSistemaOrigem||'SEHAB';
    zipSistema.value=m.codigoSistemaOrigem||zipSistema.value;
    zipTipo.value=m.codigoTipo||zipTipo.value;
    zipPessoaId.value=p.idPessoaEntrega||zipPessoaId.value;
    zipNome.value=p.nomeCompleto||zipNome.value;
    zipNascimento.value=p.dataNascimento||zipNascimento.value;
    zipMae.value=p.nomeMae||zipMae.value;
    zipRegistroId.value=r.codigoRegistroOrigem||zipRegistroId.value;
    zipValor.value=r.valorConcedido??zipValor.value;
    zipDataEvento.value=r.dataEventoConcessao||zipDataEvento.value;
    zipSituacao.value=r.situacaoVigencia||zipSituacao.value;
  }catch{}
}

async function startZip(){
  if(zipMode==='form')syncFormToJson();
  zipDialog.close();
  const payload={gestor:zipGestor.value,manifestJson:zipManifest.value,pessoasJsonl:zipPessoas.value,registrosJsonl:zipRegistros.value};
  const response=await api('/api/zip/manual/start',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(payload)});
  openLiveRun(response.id,'Gerar e enviar ZIP de ingestão','zip');
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
  const stream=item.stream??item.Stream??'system';
  const at=item.at??item.At??new Date().toISOString();
  const text=item.text??item.Text??'';
  const line=document.createElement('div');
  line.className='line '+esc(stream);
  const parsed=new Date(at);
  const time=Number.isNaN(parsed.getTime())?'--:--:--':parsed.toLocaleTimeString();
  line.textContent='['+time+'] '+text;
  terminal.appendChild(line);
  terminal.scrollTop=terminal.scrollHeight;
}

function openLiveRun(id,title,commandId){
  if(eventSource)eventSource.close();
  currentRunId=id;
  currentCommandId=commandId;
  resetConsole(title);
  switchView(consoleView,'Execução / '+title);
  eventSource=new EventSource('/api/runs/'+id+'/stream');
  eventSource.onmessage=async event=>{
    const item=JSON.parse(event.data);
    appendConsole(item);
    if((item.stream??item.Stream)==='status'){
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
  if(run.step.resultPath){
    actions.push('<button class="primary" type="button" onclick="window.open(\'/api/runs/'+run.id+'/result\',\'_blank\')">Abrir resultado</button>');
    if(String(run.step.resultPath).toLowerCase().endsWith('.json'))actions.push('<button class="secondary" type="button" onclick="window.open(\'/api/runs/'+run.id+'/result/html\',\'_blank\')">Visualização amigável</button>');
  }
  if(run.records?.length)actions.push('<button class="secondary" type="button" onclick="toggleRecords()">Ver dados do resultado ('+run.records.length+')</button>');
  actions.push('<button class="secondary" type="button" onclick="rerun()">Executar novamente</button>');
  const artifacts=run.step.artifacts||[];
  if(artifacts.length){
    resultPath.innerHTML+=(resultPath.innerHTML?'':'')+'<div class="artifact-list"><b>Arquivos gerados:</b>'+artifacts.map((path,index)=>'<div class="artifact-item"><code>'+esc(path)+'</code><div class="result-actions"><button class="secondary" type="button" onclick="window.open(\'/api/runs/'+run.id+'/artifacts/'+index+'\',\'_blank\')">Abrir</button>'+(String(path).toLowerCase().endsWith('.json')?'<button class="secondary" type="button" onclick="window.open(\'/api/runs/'+run.id+'/artifacts/'+index+'/html\',\'_blank\')">Visualização amigável</button>':'')+'</div></div>').join('')+'</div>';
  }
  resultActions.innerHTML=actions.join('');
  if(run.records?.length){
    recordsPanel.innerHTML='<h3>Dados do resultado</h3><table><tr>'+Object.keys(run.records[0]).map(k=>'<th>'+esc(k)+'</th>').join('')+'</tr>'+run.records.map(row=>'<tr>'+Object.values(row).map(v=>'<td>'+esc(v)+'</td>').join('')+'</tr>').join('')+'</table>';
  }
}

function toggleRecords(){recordsPanel.classList.toggle('hidden')}

async function rerun(){
  if(currentCommandId==='zip'){await openZipDialog();return}
  await startCommand(currentCommandId);
}

async function openHistoryRun(id){
  const run=await api('/api/runs/'+id);
  if(eventSource){eventSource.close();eventSource=null}
  resetConsole(run.title);
  switchView(consoleView,'Execução / '+run.title);
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
