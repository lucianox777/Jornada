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
.stage{margin:18px 0 26px}.stage-head{display:flex;align-items:center;gap:10px;margin:0 0 8px}.stage-head h3{margin:0;font-size:17px}.stage-reset{margin-left:auto;white-space:nowrap}.stage-index{font:12px ui-monospace,SFMono-Regular,Consolas,monospace;color:#66717d;background:#e9edf2;border-radius:999px;padding:4px 8px}.flow-note{padding:10px 12px;border:1px solid #cfdceb;background:#f6f9fd;border-radius:8px;color:#44515f;font-size:13px;margin-bottom:14px}
.card{background:#fff;border:1px solid #d9dee5;border-radius:10px;padding:15px;margin:10px 0}
.command-head{display:block}
.command-title{font-weight:700;font-size:16px}
.command-desc{margin:6px 0;color:#45515e}
.run-meta{display:flex;flex-wrap:wrap;gap:6px;align-items:center;margin:8px 0}.exec-badge,.state-badge{font:12px ui-monospace,SFMono-Regular,Consolas,monospace;border-radius:999px;padding:3px 8px}.exec-badge{background:#edf2f7;color:#425466}.state-badge{font-weight:700}.state-badge.PRONTO{background:#e7f6ec;color:#1f6b3b}.state-badge.PENDENTE{background:#eef1f4;color:#66717d}.state-badge.DESATUALIZADO{background:#fff4d6;color:#795900}.state-badge.FALHA{background:#fde9e7;color:#9b241c}
.command-line{display:block;color:#6b7580;font:12px ui-monospace,SFMono-Regular,Consolas,monospace;overflow-wrap:anywhere}.dependency{margin-top:8px;padding:8px 10px;border-left:3px solid #d69b22;background:#fff8e6;color:#5f4a15;font-size:13px}.dependency code{font-size:12px}.dep-note{display:block;margin-top:3px;color:#746434}
.command-actions{display:grid;gap:0;margin-top:12px;white-space:normal;border-top:1px solid #e4e8ed}
.action-row{display:grid;grid-template-columns:minmax(0,1fr) auto;gap:16px;align-items:center;padding:12px 0;border-bottom:1px solid #e4e8ed}
.action-copy{min-width:0}.action-heading{display:flex;align-items:baseline;gap:8px;flex-wrap:wrap}.action-index{font:700 12px ui-monospace,SFMono-Regular,Consolas,monospace;color:#405166;background:#edf2f7;border-radius:999px;padding:3px 7px}.action-title{font-weight:700}.action-desc{margin-top:3px;color:#56616e;font-size:13px}.action-control{display:flex;align-items:center;justify-content:flex-end;gap:9px}.action-count{flex:0 0 auto;min-width:78px;text-align:right;color:#536170;font:700 12px ui-monospace,SFMono-Regular,Consolas,monospace;white-space:nowrap}.action-count.readonly{font-weight:600;color:#7a8490}.action-control button{min-width:190px}
.primary{background:#1463d7;color:#fff;border:1px solid #1463d7;border-radius:7px;padding:8px 13px}
.secondary{background:#fff;border:1px solid #cfd6df;border-radius:7px;padding:8px 13px}
.danger{background:#fff2f0;color:#9b241c;border:1px solid #e8b3ad;border-radius:7px;padding:8px 13px}
button:disabled{opacity:.5;cursor:not-allowed}
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
.history-status.PARCIAL{color:#9a6a00}
.history-status.SEM-EXECUTOR{color:#9a6a00}
dialog{width:min(900px,94vw);border:1px solid #cad2dc;border-radius:10px;padding:0;box-shadow:0 18px 60px rgba(0,0,0,.28)}
dialog::backdrop{background:rgba(0,0,0,.45)}
.dialog-head{padding:14px 18px;border-bottom:1px solid #dde2e8;display:flex;justify-content:space-between;align-items:center}
.dialog-body{padding:16px 18px;max-height:70vh;overflow:auto}
.dialog-body label{display:block;font-weight:700;margin:12px 0 5px}
.dialog-body input,.dialog-body textarea,.dialog-body select{width:100%;padding:8px;border:1px solid #cbd3dc;border-radius:6px;font:13px ui-monospace,SFMono-Regular,Consolas,monospace}
.dialog-body textarea{min-height:110px}.tabs{display:flex;gap:8px;margin:10px 0}.form-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px}.form-grid label{margin:0}.form-grid input,.form-grid select{width:100%;padding:8px;border:1px solid #cbd3dc;border-radius:6px}.artifact-list{margin-top:10px;display:grid;gap:6px}.artifact-item{padding:8px 10px;border:1px solid #d9dee5;border-radius:6px;background:#fafafa;overflow-wrap:anywhere}@media(max-width:700px){.form-grid{grid-template-columns:1fr}}
.dialog-actions{display:flex;justify-content:flex-end;gap:8px;padding:12px 18px;border-top:1px solid #dde2e8}
.layer-tools{display:flex;gap:8px;align-items:center;margin:10px 0}.layer-tools input{flex:1;padding:8px;border:1px solid #cbd3dc;border-radius:6px}.table-wrap{overflow:auto;max-height:50vh;border:1px solid #e0e5eb;border-radius:7px}.pager{display:flex;align-items:center;justify-content:space-between;gap:10px;margin-top:10px}
table{border-collapse:collapse;width:100%;font-size:13px}
th,td{border-bottom:1px solid #ddd;padding:7px;text-align:left;vertical-align:top}
.back{margin-bottom:12px}
.small{font-size:12px;color:#697481}
@media(max-width:700px){main{padding:12px}.action-row{grid-template-columns:1fr}.action-control{display:grid;grid-template-columns:auto minmax(0,1fr);justify-content:stretch}.action-count{text-align:left}.action-control button{width:100%;min-width:0}.terminal{height:55vh}}
</style>
</head>
<body>
<header>
  <div class="brand"><h1>Jornada · Console DEV</h1><div id="breadcrumb" class="breadcrumb">Console DEV / Fluxo do dado</div><div id="consoleRevision" class="small">revisão: carregando...</div></div>
  <div class="toolbar">
    <button class="iconbtn" type="button" onclick="showHome()">⌂ Fluxo</button>
    <button class="iconbtn" type="button" onclick="showTools()">🧰 Ferramentas</button>
    <button class="iconbtn" type="button" onclick="showHistory()">🕘 Execuções</button>
  </div>
</header>
<main>
  <section id="homeView">
    <div class="hero">
      <h2>Fluxo do dado</h2>
      <p>A Console acompanha a mesma jornada da aplicação: preparação do ambiente em etapas independentes → ingestão → Bronze → Silver → identidade/Linkage → Gold/Serving → encerramento.</p>
      <div class="flow-note">No modo didático da Console, o Processor residente é suspenso. Em <b>4.1 · Processar um lote</b>, cada clique é <b>One shot</b> e executa no máximo uma iteração do Processor para a Entrega atual. Repita 4.1 até concluir Silver. HML é o modo padrão; use <code>console.cmd --dev</code> apenas para habilitar o corpus adicional. Cada botão mostra, imediatamente à esquerda, quantas vezes sua ação foi executada nesta sessão. As contagens reiniciam ao abrir a Console e podem ser zeradas por seção.</div>
    </div>
    <div id="commands">Carregando...</div>
  </section>

  <section id="toolsView" class="hidden">
    <div class="hero">
      <h2>Ferramentas de verificação e administração</h2>
      <p>Diagnósticos, conferências, consulta semicega, contratos/configurações, calibração e massa sintética ficam separados do fluxo principal.</p>
    </div>
    <div id="toolsCommands">Carregando...</div>
  </section>

  <section id="consoleView" class="hidden">
    <button class="secondary back" type="button" onclick="returnFromConsole()">← Voltar</button>
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
    <button class="secondary back" type="button" onclick="showHome()">← Voltar ao fluxo</button>
    <h2>Execuções desta sessão</h2>
    <p class="small">A lista começa vazia a cada inicialização da Console DEV e mostra somente as execuções da sessão atual.</p>
    <div id="history">Carregando...</div>
  </section>
</main>

<dialog id="contractDialog">
  <div class="dialog-head"><strong>Contratos de ingestão</strong><button class="secondary" type="button" onclick="contractDialog.close()">Fechar</button></div>
  <div class="dialog-body">
    <p>Visualize ou altere os arquivos JSON reais de <code>config/contracts</code>. O conteúdo é validado como JSON antes de salvar.</p>
    <label>Arquivo<select id="contractPath" style="width:100%;padding:8px" onchange="loadContractFile()"></select></label>
    <label>Conteúdo JSON<textarea id="contractContent" style="min-height:45vh"></textarea></label>
    <div id="contractMessage" class="small"></div>
  </div>
  <div class="dialog-actions"><button class="secondary" type="button" onclick="contractDialog.close()">Cancelar</button><button class="primary" type="button" onclick="saveContractFile()">Validar e salvar</button></div>
</dialog>

<dialog id="activeConfigDialog">
  <div class="dialog-head"><strong>Configurações JSON ativas</strong><button class="secondary" type="button" onclick="activeConfigDialog.close()">Fechar</button></div>
  <div class="dialog-body">
    <p>Mostra o arquivo fonte e os caminhos efetivos usados pelo ambiente. Edite pela visão amigável ou pelo JSON bruto.</p>
    <label>Arquivo<select id="activeConfigPath" onchange="loadActiveConfig()"></select></label>
    <div id="activeConfigPaths" class="result-box"></div>
    <div class="tabs"><button class="secondary" type="button" onclick="setActiveConfigMode('friendly')">Visualização amigável</button><button class="secondary" type="button" onclick="setActiveConfigMode('raw')">JSON bruto</button></div>
    <div id="activeConfigFriendly"></div>
    <div id="activeConfigRaw" class="hidden"><label>JSON<textarea id="activeConfigContent" style="min-height:42vh"></textarea></label></div>
    <div id="activeConfigMessage" class="small"></div>
  </div>
  <div class="dialog-actions"><button class="secondary" type="button" onclick="activeConfigDialog.close()">Cancelar</button><button class="primary" type="button" onclick="saveActiveConfig()">Validar e salvar</button></div>
</dialog>

<dialog id="configurationDialog">
  <div class="dialog-head"><strong>Contratos e configurações</strong><button class="secondary" type="button" onclick="configurationDialog.close()">Fechar</button></div>
  <div class="dialog-body">
    <p>Escolha o que deseja administrar. Contratos de ingestão e configurações ativas continuam com validações e persistência independentes.</p>
    <div class="result-actions">
      <button class="primary" type="button" onclick="openContractsFromConfiguration()">Contratos de ingestão</button>
      <button class="primary" type="button" onclick="openActiveConfigFromConfiguration()">Configurações ativas</button>
    </div>
  </div>
  <div class="dialog-actions"><button class="secondary" type="button" onclick="configurationDialog.close()">Fechar</button></div>
</dialog>

<dialog id="zipDialog">
  <div class="dialog-head"><strong>Entrada manual para o ZIP</strong><button class="secondary" type="button" onclick="zipDialog.close()">Fechar</button></div>
  <div class="dialog-body">
    <p>Escolha primeiro o <b>contrato de ingestão</b>. A Console carrega do catálogo <code>ref.*</code> somente combinações utilizáveis de Gestor, sistema, contrato Pessoa e tipo/versão. Depois você pode preencher pelo formulário ou editar JSON/JSONL diretamente.</p>
    <label>Contrato para gerar o ZIP<select id="zipContract" onchange="changeZipContract()"></select></label>
    <div class="tabs">
      <button class="secondary" type="button" onclick="setZipMode('form')">Formulário HTML</button>
      <button class="secondary" type="button" onclick="setZipMode('json')">JSON / JSONL</button>
      <button class="secondary" type="button" onclick="refreshZipExample()">Substituir pelos dados da Gold</button>
    </div>
    <div id="zipTemplateSource" class="small"></div>

    <div id="zipFormMode">
      <div class="form-grid">
        <label>Gestor<input id="zipGestor" readonly></label>
        <label>Sistema de origem<input id="zipSistema" readonly></label>
        <label>Natureza<input id="zipNatureza" readonly></label>
        <label>Tipo<input id="zipTipo" readonly></label>
        <label>Versão do tipo<input id="zipTipoVersao" type="number" readonly></label>
        <label>Versão Pessoa utilizável<input id="zipPessoaSchemaVersao" type="number" readonly></label>
        <label>ID temporário nesta entrega<input id="zipPessoaId"><span class="small">Liga a Pessoa aos registros deste ZIP; não identifica a Pessoa entre entregas.</span></label>
        <label>Código da pessoa na origem (opcional)<input id="zipPessoaOrigem"><span class="small">Chave estável do sistema de origem. Use o mesmo valor em novas entregas da mesma Pessoa.</span></label>
        <label>CPF (opcional)<input id="zipCpf" inputmode="numeric" autocomplete="off"><span class="small">Quando informado, segue a rota determinística de CPF; vazio mantém o teste sem CPF.</span></label>
        <label>Nome completo<input id="zipNome"></label>
        <label>Data de nascimento<input id="zipNascimento" type="date"></label>
        <label>Nome da mãe<input id="zipMae"></label>
        <label>Código do registro<input id="zipRegistroId"></label>
        <label class="zip-benefit-field">Valor concedido<input id="zipValor" type="number" step="0.01" value="600"></label>
        <label class="zip-benefit-field">Data do evento<input id="zipDataEvento" type="date"></label>
        <label class="zip-benefit-field">Situação<select id="zipSituacao"><option>VIGENTE</option><option>ENCERRADO</option></select></label>
        <label class="zip-service-field hidden">Data/hora do serviço<input id="zipDataHoraServico" type="datetime-local"></label>
        <label class="zip-service-field hidden">Unidade do serviço<input id="zipUnidadeServico" value="UNIDADE DEV"></label>
        <label class="zip-service-field hidden">Situação do serviço<input id="zipSituacaoServico" value="REALIZADO"></label>
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
    <button class="primary" type="button" onclick="startZip()">Gerar arquivo</button>
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

<dialog id="layerDialog">
  <div class="dialog-head"><strong id="layerTitle">Camada</strong><button class="secondary" type="button" onclick="layerDialog.close()">Fechar</button></div>
  <div class="dialog-body">
    <p id="layerHelp">Visualização somente leitura.</p>
    <div class="layer-tools">
      <input id="layerSearch" type="search" placeholder="Buscar em qualquer coluna" onkeydown="if(event.key==='Enter'){event.preventDefault();applyLayerSearch()}">
      <button class="primary" type="button" onclick="applyLayerSearch()">Buscar</button>
      <button class="secondary" type="button" onclick="clearLayerSearch()">Limpar</button>
    </div>
    <div id="layerMeta" class="small">Carregando...</div>
    <div class="table-wrap">
      <table>
        <thead id="layerHead"></thead>
        <tbody id="layerBody"></tbody>
      </table>
    </div>
    <div class="pager">
      <button id="layerPrev" class="secondary" type="button" onclick="loadLayerPage(layerPage-1)">← Anterior</button>
      <span id="layerPageLabel" class="small"></span>
      <button id="layerNext" class="secondary" type="button" onclick="loadLayerPage(layerPage+1)">Próxima →</button>
    </div>
  </div>
  <div class="dialog-actions"><button class="secondary" type="button" onclick="layerDialog.close()">Fechar</button></div>
</dialog>

<script>
const esc=x=>String(x??'').replace(/[&<>"]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));
const views=[homeView,toolsView,consoleView,historyView];
const historyList=document.getElementById('history');
const breadcrumb=document.getElementById('breadcrumb');
let commandsCache=[];
let currentRunId=null;
let currentCommandId=null;
let eventSource=null;
let consoleOrigin=null;
let layerKind='gold';
let layerPage=1;
let layerAbortController=null;
const layerPageSize=50;
const layerRequestTimeoutMs=15000;

async function api(url,options){
  const response=await fetch(url,{cache:'no-store',...(options||{})});
  if(!response.ok)throw new Error(await response.text());
  const type=response.headers.get('content-type')||'';
  return type.includes('application/json')?response.json():response.text();
}

function switchView(view,label,scrollTop=true){
  for(const item of views)item.classList.toggle('hidden',item!==view);
  breadcrumb.textContent='Console DEV / '+label;
  if(scrollTop)window.scrollTo({top:0,behavior:'smooth'});
}

function captureActionOrigin(){
  const active=document.activeElement;
  const row=active instanceof Element?active.closest('.action-row'):null;
  if(!row)return null;
  const surface=row.closest('#toolsCommands')?'tools':row.closest('#commands')?'flow':null;
  if(!surface)return null;
  return {surface,actionKey:row.dataset.actionKey||'',scrollY:window.scrollY};
}

function restoreActionOrigin(origin){
  requestAnimationFrame(()=>{
    const row=origin?.actionKey
      ?Array.from(document.querySelectorAll('.action-row')).find(x=>x.dataset.actionKey===origin.actionKey)
      :null;
    if(row){
      row.scrollIntoView({block:'center',behavior:'auto'});
      const button=row.querySelector('button');
      if(button)button.focus({preventScroll:true});
      return;
    }
    window.scrollTo({top:Number(origin?.scrollY||0),behavior:'auto'});
  });
}

async function returnFromConsole(){
  if(eventSource){eventSource.close();eventSource=null}
  const origin=consoleOrigin;
  if(origin?.surface==='tools'){
    switchView(toolsView,'Ferramentas',false);
    await loadCommands('tools');
  }else if(origin?.surface==='history'){
    await showHistory();
    return;
  }else{
    switchView(homeView,'Fluxo do dado',false);
    await loadCommands('flow');
  }
  restoreActionOrigin(origin);
}

async function showHome(){
  if(eventSource){eventSource.close();eventSource=null}
  switchView(homeView,'Fluxo do dado');
  await loadCommands('flow');
}

async function showTools(){
  if(eventSource){eventSource.close();eventSource=null}
  switchView(toolsView,'Ferramentas');
  await loadCommands('tools');
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
      const number=Number(r.executionNumber||0)>0?' #'+r.executionNumber:'';
      const child=r.parentRunId?'↳ ':'';
      return '<div class="history-item"><div><a href="#" onclick="openHistoryRun(\''+r.id+'\');return false"><span class="history-title">'+child+esc(r.title||r.command||'Execução')+esc(number)+'</span></a><div class="history-meta">'+esc(started)+' · '+esc(r.summary||'')+'</div></div><div class="history-status '+esc(statusClass)+'">'+esc(r.status||'')+'</div></div>'
    }).join(''):'Nenhuma execução registrada.';
  }catch(e){
    const detail=e.name==='AbortError'?'A API de histórico excedeu 7 segundos. Reinicie a Console DEV e tente novamente.':e.message;
    historyList.innerHTML='<div class="card"><b>Falha ao carregar o histórico.</b><div class="small">'+esc(detail)+'</div></div>';
  }finally{
    clearTimeout(timeout);
  }
}

async function resetSectionCounts(button){
  const ids=JSON.parse(button.dataset.commandIds||'[]');
  if(!ids.length)return;
  const surface=button.closest('#toolsCommands')?'tools':'flow';
  button.disabled=true;
  try{
    await api('/api/session-counts/reset',{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify(ids)});
    await loadCommands(surface);
  }catch(e){
    button.disabled=false;
    alert('Falha ao zerar as contagens da seção: '+e.message);
  }
}

async function loadCommands(surface='flow'){
  commandsCache=await api('/api/commands');
  const commandById=Object.fromEntries(commandsCache.map(x=>[x.id,x]));
  const titleById=Object.fromEntries(commandsCache.map(x=>[x.id,x.title]));
  const target=surface==='tools'?toolsCommands:commands;
  const selected=commandsCache.filter(c=>c.visible!==false&&(c.surface||'flow')===surface);
  const groups=[];
  for(const c of selected){
    const stage=c.stage||'Outros';
    let group=groups.find(x=>x.stage===stage);
    if(!group){group={stage,items:[]};groups.push(group)}
    group.items.push(c);
  }

  const executionMeta=(commandId,readOnly=false)=>{
    if(readOnly)return {text:'somente leitura',title:'Esta ação não cria uma execução contabilizada.',readOnly:true};
    const item=commandById[commandId];
    const count=Number(item?.executionCount||0);
    const last=Number(item?.lastExecutionNumber||0);
    return {
      text:'Sessão '+count+'x',
      title:count===0?'Nenhuma execução nesta sessão. Próxima execução da sessão: #1.':'Nesta sessão: '+count+' · última #'+(last||count)+' · próxima #'+(count+1)+'.',
      readOnly:false
    };
  };

  const actionRow=(number,title,description,control,commandId,readOnly=false)=>{
    const index=number?'<span class="action-index">'+esc(number)+'</span>':'';
    const meta=executionMeta(commandId,readOnly);
    const count='<span class="action-count'+(meta.readOnly?' readonly':'')+'" title="'+esc(meta.title)+'">'+esc(meta.text)+'</span>';
    const actionKey=[number||'',commandId||'',title].join('|');
    return '<div class="action-row" data-action-key="'+esc(actionKey)+'"><div class="action-copy"><div class="action-heading">'+index+'<span class="action-title">'+esc(title)+'</span></div>'
      +'<div class="action-desc">'+esc(description)+'</div></div>'
      +'<div class="action-control">'+count+control+'</div></div>';
  };

  const countableCommandIds=c=>{
    if(c.id==='zip')return ['zip','ingestion','pipeline-status'];
    if(c.id==='bronze')return ['bronze-verify-latest'];
    if(c.id==='silver')return ['silver','pipeline-status'];
    if(c.id==='linkage')return ['linkage','replay'];
    if(c.id==='gold'||c.id==='semiblind')return [];
    if(c.id==='configuration')return ['contract-bundle'];
    if(c.id==='gold-synthetic')return ['gold-synthetic','blocking'];
    return [c.id];
  };

  const renderCard=c=>{
    const deps=(c.dependencies||[]).map(id=>titleById[id]||id);
    const state=String(c.executionState||'PENDENTE').toUpperCase();
    const lastTime=c.lastFinishedAt?new Date(c.lastFinishedAt).toLocaleString():'';
    const runMeta='<div class="run-meta"><span class="state-badge '+esc(state)+'">'+esc(state)+'</span>'+(lastTime?'<span class="small">última conclusão: '+esc(lastTime)+'</span>':'')+'</div>';
    const dependency=deps.length||c.dependencyNote
      ?'<div class="dependency"><b>Pré-requisitos:</b> '+(deps.length?deps.map(esc).join(' → '):'nenhum obrigatório')+(c.dependencyNote?'<span class="dep-note">'+esc(c.dependencyNote)+'</span>':'')+'</div>'
      :'';
    const blocker=c.disabled&&c.disabledReason
      ?'<div class="dependency"><b>Bloqueado:</b> '+esc(c.disabledReason)+'</div>'
      :'';
    const rawStage=String(c.stage||'').split(' · ')[0];
    const stageNumber=/^\d+(?:\.\d+)*$/.test(rawStage)?rawStage:'';
    const child=i=>stageNumber?stageNumber+'.'+i:'';

    let actions='';
    if(c.id==='zip'){
      actions=actionRow(child(1),'Gerar arquivo','Escolha o contrato de ingestão (Gestor, Pessoa e tipo/versão), preencha os dados e gere o ZIP local. Esta ação não envia dados para a API.','<button class="primary" type="button" onclick="openZipDialog()">Gerar arquivo</button>','zip')
        +actionRow(child(2),'Enviar arquivo','Envia explicitamente o último ZIP gerado para a API de ingestão em NODE1.','<button class="primary" type="button" onclick="startCommand(\'ingestion\',\'Enviar arquivo para ingestão\')">Enviar arquivo</button>','ingestion')
        +actionRow(child(3),'Consultar status','Consulta o recibo e o estado da última Entrega sem acionar processamento.','<button class="secondary" type="button" onclick="startCommand(\'pipeline-status\',\'Ver status da última ingestão\')">Consultar status</button>','pipeline-status');
    }else if(c.id==='bronze'){
      actions=actionRow(child(1),'Visualizar Bronze','Abre metadados e localização lógica dos objetos recebidos.','<button class="primary" type="button" onclick="openLayerDialog(\'bronze\')">Visualizar Bronze</button>',null,true)
        +actionRow(child(2),'Verificar integridade','Valida objeto, SHA-256 e tamanho físico da última Entrega sem processá-la.','<button class="secondary" type="button" onclick="startCommand(\'bronze-verify-latest\',\'Verificar integridade da última Entrega\')">Verificar integridade</button>','bronze-verify-latest');
    }else if(c.id==='silver'){
      actions=actionRow(child(1),'Processar um lote','One shot · executa uma única iteração do Processor e processa no máximo um lote da Entrega atual. Repita enquanto houver lotes pendentes.','<button class="primary" type="button" onclick="startCommand(\'silver\',\'4.1 · Processar um lote Bronze → Silver\')">Executar one shot</button>','silver')
        +actionRow(child(2),'Visualizar Silver','Inspeciona as observações já materializadas na Silver; não executa o Processor.','<button class="secondary" type="button" onclick="openLayerDialog(\'silver\')">Visualizar Silver</button>',null,true)
        +actionRow(child(3),'Consultar status','Consulta o estado da Entrega para confirmar se ainda há processamento pendente.','<button class="secondary" type="button" onclick="startCommand(\'pipeline-status\',\'Ver status da última ingestão\')">Consultar status</button>','pipeline-status');
    }else if(c.id==='linkage'){
      const disabled=c.disabled?' disabled aria-disabled="true" title="'+esc(c.disabledReason||'Execute 4.1 até concluir Silver')+'"':'';
      actions=actionRow(child(1),'Executar Linkage Runner','One shot · dispara uma única invocação do Runner para o universo elegível da última Entrega.','<button class="primary" type="button"'+disabled+' onclick="startCommand(\'linkage\',\'5.1 · Executar Linkage Runner\')">Executar one shot</button>','linkage')
        +actionRow(child(2),'Visualizar identidade','Abre os vínculos correntes e seus métodos de resolução sem executar novo linkage.','<button class="secondary" type="button" onclick="openLayerDialog(\'identity\')">Visualizar identidade</button>',null,true)
        +actionRow(child(3),'Replay do último run','One shot · repete a avaliação do último run publicado sem publicar um novo resultado.','<button class="secondary" type="button" onclick="startCommand(\'replay\',\'5.3 · Replay do último run\')">Executar replay</button>','replay');
    }else if(c.id==='gold'){
      actions=actionRow(child(1),'Visualizar Gold','Inspeciona o estado canônico publicado das Pessoas.','<button class="primary" type="button" onclick="openLayerDialog(\'gold\')">Visualizar Gold</button>',null,true);
    }else if(c.id==='semiblind'){
      actions=actionRow('','Consultar candidatos','Executa a consulta semicega interativa; a consulta não é registrada como execução de comando.','<button class="primary" type="button" onclick="openSemiblindDialog()">Consultar</button>',null,true);
    }else if(c.id==='configuration'){
      actions=actionRow('','Abrir contratos/configurações','Abre a administração dos JSONs governados; a abertura é somente interface.','<button class="primary" type="button" onclick="openConfigurationDialog()">Abrir</button>',null,true)
        +actionRow('','Gerar bundle','Gera o ZIP operacional de contratos e configurações.','<button class="secondary" type="button" onclick="startCommand(\'contract-bundle\',\'Gerar bundle de contratos e configurações\')">Gerar bundle</button>','contract-bundle');
    }else if(c.id==='gold-synthetic'){
      actions=actionRow('','Adicionar 5.000','Expande a massa sintética DEV em um bloco controlado.','<button class="primary" type="button" onclick="startCommand(\'gold-synthetic\')">Adicionar 5.000</button>','gold-synthetic')
        +actionRow('','Visualizar Gold','Inspeciona a Gold corrente sem executar alteração.','<button class="secondary" type="button" onclick="openLayerDialog(\'gold\')">Visualizar Gold</button>',null,true)
        +actionRow('','Reconstruir blocking','One shot · reconcilia uma vez a projeção local de blocking.','<button class="secondary" type="button" onclick="startCommand(\'blocking\',\'Reconstruir blocking\')">Reconstruir blocking</button>','blocking');
    }else{
      const buttonClass=c.id==='finish'||c.destructive?'danger':'primary';
      const disabled=c.disabled?' disabled aria-disabled="true" title="'+esc(c.disabledReason||'Operação indisponível')+'"':'';
      const label=c.id==='infrastructure'?'Executar sequência completa':'Executar';
      const description=c.id==='infrastructure'
        ?'Executa uma vez a sequência completa das etapas 1.1 a 1.7.'
        :'Executa esta ação uma vez por clique.';
      actions=actionRow(stageNumber,c.title,description,'<button class="'+buttonClass+'" type="button"'+disabled+' onclick="startCommand(\''+c.id+'\')">'+label+'</button>',c.id);
    }
    return '<div class="card"><div class="command-head"><div class="command-title">'+esc(c.title)+'</div><div class="command-desc">'+esc(c.description)+'</div>'+runMeta+'<small class="command-line">'+esc(c.displayCommand)+'</small>'+dependency+blocker+'<div class="command-actions">'+actions+'</div></div></div>';
  };

  target.innerHTML=groups.map(g=>{
    const split=g.stage.split(' · ');
    const badge=split.length>1?'<span class="stage-index">'+esc(split[0])+'</span>':'';
    const title=split.length>1?split.slice(1).join(' · '):g.stage;
    const ids=[...new Set(g.items.flatMap(countableCommandIds))];
    const reset=ids.length
      ?'<button class="secondary stage-reset" type="button" data-command-ids="'+esc(JSON.stringify(ids))+'" onclick="resetSectionCounts(this)">Zerar contagens da seção</button>'
      :'';
    return '<section class="stage"><div class="stage-head">'+badge+'<h3>'+esc(title)+'</h3>'+reset+'</div>'+g.items.map(renderCard).join('')+'</section>';
  }).join('');
}

async function startCommand(id,titleOverride){
  const command=commandsCache.find(x=>x.id===id);
  if(command?.disabled){
    alert(command.disabledReason||'Operação indisponível neste modo.');
    return;
  }
  const origin=captureActionOrigin()||consoleOrigin;
  const response=await api('/api/commands/'+encodeURIComponent(id)+'/start',{method:'POST'});
  const title=titleOverride||command?.title||id;
  openLiveRun(response.id,title+' #'+response.executionNumber,id,origin);
}

async function openLayerDialog(kind){
  layerKind=['bronze','silver','identity','gold'].includes(kind)?kind:'gold';
  layerPage=1;
  layerSearch.value='';
  const labels={
    bronze:['Camada Bronze · bronze.entrega_arquivo','Metadados e localização lógica dos objetos originais recebidos. Visualização somente leitura.'],
    silver:['Camada Silver · silver.pessoa_observacao','Observações normalizadas materializadas pelo Jornada.Processor.Worker. Visualização somente leitura.'],
    identity:['Identidade · identidade.v_vinculo_corrente','Estado corrente de resolução das observações, incluindo método e run de linkage quando aplicável.'],
    gold:['Camada Gold · gold.pessoa','Estado canônico publicado das Pessoas. O campo de busca procura em qualquer coluna exibida.']
  };
  layerTitle.textContent=labels[layerKind][0];
  layerHelp.textContent=labels[layerKind][1];
  layerDialog.showModal();
  await loadLayerPage(1);
}

layerDialog.addEventListener('close',()=>{
  if(layerAbortController){
    layerAbortController.abort();
    layerAbortController=null;
  }
});

async function applyLayerSearch(){
  await loadLayerPage(1);
}

async function clearLayerSearch(){
  layerSearch.value='';
  await loadLayerPage(1);
}

async function loadLayerPage(page){
  if(layerAbortController)layerAbortController.abort();
  const controller=new AbortController();
  layerAbortController=controller;
  const timeout=setTimeout(()=>controller.abort(),layerRequestTimeoutMs);
  layerPage=Math.max(1,page);
  layerMeta.textContent='Carregando...';
  layerHead.innerHTML='';
  layerBody.innerHTML='<tr><td>Carregando...</td></tr>';
  layerPageLabel.textContent='';
  layerPrev.disabled=true;
  layerNext.disabled=true;
  try{
    const params=new URLSearchParams({
      page:String(layerPage),
      pageSize:String(layerPageSize),
      search:layerSearch.value.trim()
    });
    const data=await api('/api/layers/'+encodeURIComponent(layerKind)+'?'+params.toString(),{signal:controller.signal});
    if(layerAbortController!==controller)return;
    layerPage=data.page;
    layerHead.innerHTML='<tr>'+data.columns.map(x=>'<th>'+esc(x)+'</th>').join('')+'</tr>';
    layerBody.innerHTML=data.rows.length
      ?data.rows.map(row=>'<tr>'+row.map(value=>'<td>'+esc(value??'')+'</td>').join('')+'</tr>').join('')
      :'<tr><td colspan="'+data.columns.length+'">Nenhum registro encontrado.</td></tr>';
    layerMeta.textContent=data.total+' registro(s)'+(data.search?' · filtro: "'+data.search+'"':'')+' · '+data.pageSize+' por página';
    layerPageLabel.textContent='Página '+data.page+' de '+data.totalPages;
    layerPrev.disabled=data.page<=1;
    layerNext.disabled=data.page>=data.totalPages;
  }catch(e){
    if(layerAbortController!==controller)return;
    const detail=e.name==='AbortError'
      ?'A consulta excedeu 15 segundos e foi cancelada. Feche operações em andamento e tente novamente.'
      :e.message;
    layerMeta.textContent='Falha: '+detail;
    layerBody.innerHTML='<tr><td>Não foi possível carregar a camada.</td></tr>';
    layerPageLabel.textContent='';
  }finally{
    clearTimeout(timeout);
    if(layerAbortController===controller)layerAbortController=null;
  }
}

function openConfigurationDialog(){
  configurationDialog.showModal();
}
function openContractsFromConfiguration(){
  configurationDialog.close();
  openContractDialog();
}
function openActiveConfigFromConfiguration(){
  configurationDialog.close();
  openActiveConfigDialog();
}


async function openContractDialog(){
  contractDialog.showModal();
  contractMessage.textContent='Carregando contratos...';
  try{
    const files=await api('/api/contracts');
    contractPath.innerHTML=files.map(p=>'<option value="'+esc(p)+'">'+esc(p)+'</option>').join('');
    if(files.length)await loadContractFile();else contractMessage.textContent='Nenhum contrato JSON encontrado.';
  }catch(e){contractMessage.textContent='Falha: '+e.message}
}
async function loadContractFile(){
  if(!contractPath.value)return;
  contractMessage.textContent='Carregando...';
  try{
    const file=await api('/api/contracts/file?path='+encodeURIComponent(contractPath.value));
    contractContent.value=file.content;
    contractMessage.textContent=file.path;
  }catch(e){contractMessage.textContent='Falha: '+e.message}
}
async function saveContractFile(){
  contractMessage.textContent='Validando e salvando...';
  try{
    JSON.parse(contractContent.value);
    const file=await api('/api/contracts/file',{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify({path:contractPath.value,content:contractContent.value})});
    contractMessage.textContent='Salvo: '+file.path;
  }catch(e){contractMessage.textContent='Não foi salvo: '+e.message}
}

let activeConfigObject=null;
let activeConfigMode='friendly';

async function openActiveConfigDialog(){
  activeConfigDialog.showModal();
  activeConfigMessage.textContent='Carregando configurações...';
  try{
    const files=await api('/api/config/active');
    activeConfigPath.innerHTML=files.map(f=>'<option value="'+esc(f.path)+'">'+esc(f.path)+'</option>').join('');
    if(files.length)await loadActiveConfig();else activeConfigMessage.textContent='Nenhuma configuração JSON ativa encontrada.';
  }catch(e){activeConfigMessage.textContent='Falha: '+e.message}
}

async function loadActiveConfig(){
  if(!activeConfigPath.value)return;
  activeConfigMessage.textContent='Carregando...';
  try{
    const file=await api('/api/config/active/file?path='+encodeURIComponent(activeConfigPath.value));
    activeConfigObject=JSON.parse(file.content);
    activeConfigContent.value=JSON.stringify(activeConfigObject,null,2);
    const runtime=(file.runtimePaths||[]).length?(file.runtimePaths||[]).map(esc).join('<br>'):'sem caminho de container; consumido do checkout/processo';
    activeConfigPaths.innerHTML='<b>Arquivo fonte:</b> <code>'+esc(file.fullPath)+'</code><br><b>Caminho relativo:</b> <code>'+esc(file.path)+'</code><br><b>Runtime:</b> <code>'+runtime+'</code><br><b>Aplicação:</b> '+(file.restartRequired?'exige reinício/recriação do serviço':'arquivo lido diretamente ou aplicado pelo fluxo correspondente');
    renderActiveConfigFriendly();
    setActiveConfigMode('friendly');
    activeConfigMessage.textContent='JSON válido.';
  }catch(e){activeConfigMessage.textContent='Falha: '+e.message}
}

function activeLeaves(value,path=[],out=[]){
  if(value!==null&&typeof value==='object'){
    if(Array.isArray(value))value.forEach((v,i)=>activeLeaves(v,path.concat(i),out));
    else Object.keys(value).forEach(k=>activeLeaves(value[k],path.concat(k),out));
  }else out.push({path,value});
  return out;
}

function renderActiveConfigFriendly(){
  const leaves=activeLeaves(activeConfigObject);
  activeConfigFriendly.innerHTML=leaves.length?'<table><thead><tr><th>Chave</th><th>Valor</th><th>Tipo</th></tr></thead><tbody>'+leaves.map((x,i)=>'<tr><td><code>'+esc(x.path.join('.'))+'</code></td><td><input data-config-index="'+i+'" value="'+esc(x.value===null?'null':x.value)+'" onchange="updateActiveConfigLeaf('+i+',this.value)"></td><td>'+esc(x.value===null?'null':typeof x.value)+'</td></tr>').join('')+'</tbody></table>':'<div class="small">JSON sem valores escalares.</div>';
}

function updateActiveConfigLeaf(index,text){
  const leaf=activeLeaves(activeConfigObject)[index];
  if(!leaf)return;
  let value=text;
  if(leaf.value===null)value=text==='null'?null:text;
  else if(typeof leaf.value==='number'){const n=Number(text);if(!Number.isNaN(n))value=n}
  else if(typeof leaf.value==='boolean')value=String(text).toLowerCase()==='true';
  let target=activeConfigObject;
  for(let i=0;i<leaf.path.length-1;i++)target=target[leaf.path[i]];
  target[leaf.path[leaf.path.length-1]]=value;
  activeConfigContent.value=JSON.stringify(activeConfigObject,null,2);
}

function setActiveConfigMode(mode){
  activeConfigMode=mode;
  if(mode==='friendly'){
    try{activeConfigObject=JSON.parse(activeConfigContent.value);renderActiveConfigFriendly()}catch(e){activeConfigMessage.textContent='JSON inválido: '+e.message;return}
  }
  activeConfigFriendly.classList.toggle('hidden',mode!=='friendly');
  activeConfigRaw.classList.toggle('hidden',mode!=='raw');
}

async function saveActiveConfig(){
  activeConfigMessage.textContent='Validando e salvando...';
  try{
    if(activeConfigMode==='raw')activeConfigObject=JSON.parse(activeConfigContent.value);
    const content=JSON.stringify(activeConfigObject,null,2);
    const file=await api('/api/config/active/file',{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify({path:activeConfigPath.value,content})});
    activeConfigContent.value=file.content;
    activeConfigObject=JSON.parse(file.content);
    renderActiveConfigFriendly();
    activeConfigMessage.textContent='Salvo: '+file.path+(file.restartRequired?' · reinicie/recrie o serviço para aplicar.':'');
  }catch(e){activeConfigMessage.textContent='Não foi salvo: '+e.message}
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
let zipContracts=[];
let zipOrigin=null;
let zipFormDirty=false;
let zipLoadedContract='';

function setZipMode(mode){
  if(mode==='json'&&!syncFormToJson())return;
  zipMode=mode;
  if(mode!=='json')syncJsonToForm();
  zipFormMode.classList.toggle('hidden',mode!=='form');
  zipJsonMode.classList.toggle('hidden',mode!=='json');
}

function setZipNatureFields(){
  const service=String(zipNatureza.value||'').toUpperCase()==='SERVICO';
  document.querySelectorAll('.zip-benefit-field').forEach(x=>x.classList.toggle('hidden',service));
  document.querySelectorAll('.zip-service-field').forEach(x=>x.classList.toggle('hidden',!service));
}

async function openZipDialog(){
  zipOrigin=captureActionOrigin()||consoleOrigin;
  zipFormDirty=false;
  await loadZipContracts();
  await loadGoldTemplate();
  setZipMode('form');
  zipDialog.showModal();
}

async function loadZipContracts(){
  zipTemplateSource.textContent='Carregando contratos utilizáveis...';
  const previous=zipContract.value;
  zipContracts=await api('/api/zip/contracts');
  zipContract.innerHTML=zipContracts.map(c=>'<option value="'+esc(c.key)+'">'+esc(c.label)+'</option>').join('');
  if(previous&&zipContracts.some(c=>c.key===previous))zipContract.value=previous;
  if(!zipContract.value&&zipContracts.length)zipContract.value=zipContracts[0].key;
}

async function changeZipContract(){
  const next=zipContract.value;
  if(zipFormDirty&&zipLoadedContract&&next!==zipLoadedContract&&!confirm('Trocar o contrato substituirá os dados preenchidos neste formulário. Continuar?')){
    zipContract.value=zipLoadedContract;
    return;
  }
  await loadGoldTemplate();
}

async function refreshZipExample(){
  if(zipFormDirty&&!confirm('Substituir os dados preenchidos pelos dados do exemplo da Gold?'))return;
  await loadGoldTemplate();
}

async function loadGoldTemplate(){
  if(!zipContracts.length){
    try{await loadZipContracts()}catch(e){zipTemplateSource.textContent='Não foi possível carregar contratos: '+e.message;return}
  }
  if(!zipContract.value){zipTemplateSource.textContent='Nenhum contrato utilizável disponível.';return}
  zipTemplateSource.textContent='Carregando exemplo da Gold para o contrato selecionado...';
  try{
    const t=await api('/api/zip/template?contract='+encodeURIComponent(zipContract.value));
    zipGestor.value=t.gestor;
    zipSistema.value=t.codigoSistemaOrigem;
    zipNatureza.value=t.natureza;
    zipTipo.value=t.codigoTipo;
    zipTipoVersao.value=t.tipoVersao;
    zipPessoaSchemaVersao.value=t.pessoaSchemaVersao;
    zipPessoaId.value=t.idPessoaEntrega;
    zipPessoaOrigem.value=t.codigoPessoaOrigem||'';
    zipCpf.value=t.cpf||'';
    zipNome.value=t.nomeCompleto;
    zipNascimento.value=t.dataNascimento;
    zipMae.value=t.nomeMae;
    zipRegistroId.value=t.codigoRegistroOrigem;
    zipDataEvento.value=new Date().toISOString().slice(0,10);
    const now=new Date();
    zipDataHoraServico.value=new Date(now.getTime()-now.getTimezoneOffset()*60000).toISOString().slice(0,16);
    zipManifest.value=t.manifestJson;
    zipPessoas.value=t.pessoasJsonl;
    zipRegistros.value=t.registrosJsonl;
    zipLoadedContract=zipContract.value;
    zipFormDirty=false;
    setZipNatureFields();
    zipTemplateSource.textContent='Contrato: '+t.contractLabel+' · exemplo: '+t.source+' · pessoa '+t.pessoaUuid;
  }catch(e){
    zipTemplateSource.textContent='Não foi possível carregar exemplo/contrato: '+e.message;
  }
}

for(const id of ['zipPessoaId','zipPessoaOrigem','zipCpf','zipNome','zipNascimento','zipMae','zipRegistroId','zipValor','zipDataEvento','zipSituacao','zipDataHoraServico','zipUnidadeServico','zipSituacaoServico']){
  document.getElementById(id)?.addEventListener('input',()=>{zipFormDirty=true});
  document.getElementById(id)?.addEventListener('change',()=>{zipFormDirty=true});
}

function syncFormToJson(){
  const now=new Date();
  const ref=now.toISOString();
  const natureza=String(zipNatureza.value||'BENEFICIO').toUpperCase();
  const manifest={
    formatoVersao:2,
    pessoaSchemaVersao:Number(zipPessoaSchemaVersao.value),
    codigoSistemaOrigem:zipSistema.value,
    natureza,
    codigoTipo:zipTipo.value,
    tipoVersao:Number(zipTipoVersao.value),
    dataReferencia:ref
  };
  const idPessoaEntrega=zipPessoaId.value.trim();
  const cpf=zipCpf.value.replace(/\D/g,'').trim();
  const codigoPessoaOrigem=zipPessoaOrigem.value.trim();
  const nomeCompleto=zipNome.value.trim();
  const dataNascimento=zipNascimento.value.trim();
  if(!idPessoaEntrega){alert('ID temporário nesta entrega é obrigatório.');zipPessoaId.focus();return false}
  if(cpf&&!/^\d{11}$/.test(cpf)){alert('CPF deve conter exatamente 11 dígitos.');zipCpf.focus();return false}
  if(!nomeCompleto){alert('Nome completo é obrigatório.');zipNome.focus();return false}
  if(!/^\d{4}-\d{2}-\d{2}$/.test(dataNascimento)){alert('Data de nascimento é obrigatória e deve ser uma data válida.');zipNascimento.focus();return false}
  const pessoa={
    idPessoaEntrega,
    cpf:cpf||null,
    cpfAusenteMotivo:cpf?null:'NAO_INFORMADO_ORIGEM',
    nomeCompleto,dataNascimento,nomeMae:zipMae.value.trim()||null,
    sourceTransactionId:'DEV-'+idPessoaEntrega,atributosTransversais:[]
  };
  if(codigoPessoaOrigem)pessoa.codigoPessoaOrigem=codigoPessoaOrigem;
  const registro=natureza==='SERVICO'
    ?{
      idPessoaEntrega:zipPessoaId.value,codigoRegistroOrigem:zipRegistroId.value,operacao:'INCLUSAO',
      dataHoraServico:zipDataHoraServico.value?new Date(zipDataHoraServico.value).toISOString():ref,
      unidadeServico:zipUnidadeServico.value||null,situacao:zipSituacaoServico.value||null
    }
    :{
      idPessoaEntrega:zipPessoaId.value,codigoRegistroOrigem:zipRegistroId.value,operacao:'INCLUSAO',
      dataInicioConcessao:zipDataEvento.value,valorConcedido:Number(zipValor.value||0),
      dataEventoConcessao:zipDataEvento.value,situacaoVigencia:zipSituacao.value
    };
  zipManifest.value=JSON.stringify(manifest,null,2);
  zipPessoas.value=JSON.stringify(pessoa);
  zipRegistros.value=JSON.stringify(registro);
  return true;
}

function syncJsonToForm(){
  try{
    const m=JSON.parse(zipManifest.value||'{}');
    const p=JSON.parse((zipPessoas.value||'{}').split(/\r?\n/).filter(Boolean)[0]||'{}');
    const r=JSON.parse((zipRegistros.value||'{}').split(/\r?\n/).filter(Boolean)[0]||'{}');
    const match=zipContracts.find(c=>
      c.gestor===zipGestor.value
      &&c.codigoSistemaOrigem===(m.codigoSistemaOrigem||zipSistema.value)
      &&Number(c.pessoaSchemaVersao)===Number(m.pessoaSchemaVersao)
      &&c.natureza===(m.natureza||zipNatureza.value)
      &&c.codigoTipo===(m.codigoTipo||zipTipo.value)
      &&Number(c.tipoVersao)===Number(m.tipoVersao));
    if(match)zipContract.value=match.key;
    zipSistema.value=m.codigoSistemaOrigem||zipSistema.value;
    zipNatureza.value=m.natureza||zipNatureza.value;
    zipTipo.value=m.codigoTipo||zipTipo.value;
    zipTipoVersao.value=m.tipoVersao||zipTipoVersao.value;
    zipPessoaSchemaVersao.value=m.pessoaSchemaVersao||zipPessoaSchemaVersao.value;
    zipPessoaId.value=p.idPessoaEntrega||zipPessoaId.value;
    zipPessoaOrigem.value=p.codigoPessoaOrigem??'';
    zipCpf.value=p.cpf??'';
    zipNome.value=p.nomeCompleto||zipNome.value;
    zipNascimento.value=p.dataNascimento||zipNascimento.value;
    zipMae.value=p.nomeMae||zipMae.value;
    zipRegistroId.value=r.codigoRegistroOrigem||zipRegistroId.value;
    zipValor.value=r.valorConcedido??zipValor.value;
    zipDataEvento.value=r.dataEventoConcessao||r.dataInicioConcessao||zipDataEvento.value;
    zipSituacao.value=r.situacaoVigencia||zipSituacao.value;
    if(r.dataHoraServico){
      const d=new Date(r.dataHoraServico);
      if(!Number.isNaN(d.getTime()))zipDataHoraServico.value=new Date(d.getTime()-d.getTimezoneOffset()*60000).toISOString().slice(0,16);
    }
    zipUnidadeServico.value=r.unidadeServico??zipUnidadeServico.value;
    zipSituacaoServico.value=r.situacao??zipSituacaoServico.value;
    setZipNatureFields();
  }catch{}
}

async function startZip(){
  if(zipMode==='form'&&!syncFormToJson())return;
  zipDialog.close();
  const payload={gestor:zipGestor.value,manifestJson:zipManifest.value,pessoasJsonl:zipPessoas.value,registrosJsonl:zipRegistros.value};
  const response=await api('/api/zip/manual/start',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(payload)});
  openLiveRun(response.id,'Gerar ZIP de ingestão #'+response.executionNumber,'zip',zipOrigin||consoleOrigin);
  zipOrigin=null;
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

function openLiveRun(id,title,commandId,origin=null){
  if(eventSource)eventSource.close();
  currentRunId=id;
  currentCommandId=commandId;
  consoleOrigin=origin||consoleOrigin;
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
  consoleTitle.textContent=run.title+(Number(run.executionNumber||0)>0?' #'+run.executionNumber:'');
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
  if(currentCommandId==='active-config-editor'){await openActiveConfigDialog();return}
  await startCommand(currentCommandId);
}

async function openHistoryRun(id){
  consoleOrigin={surface:'history',actionKey:'',scrollY:window.scrollY};
  const run=await api('/api/runs/'+id);
  if(eventSource){eventSource.close();eventSource=null}
  const historyTitle=run.title+(Number(run.executionNumber||0)>0?' #'+run.executionNumber:'');
  resetConsole(historyTitle);
  switchView(consoleView,'Execução / '+historyTitle);
  appendConsole({at:run.startedAt,stream:'command',text:'> '+run.step.command});
  for(const line of String(run.step.output||'').split(/\r?\n/))if(line)appendConsole({at:run.startedAt,stream:'stdout',text:line});
  for(const line of String(run.step.error||'').split(/\r?\n/))if(line)appendConsole({at:run.finishedAt,stream:'stderr',text:line});
  appendConsole({at:run.finishedAt,stream:'status',text:run.status});
  renderFinal(run);
}

async function loadConsoleRevision(){
  try{
    const version=await api('/api/version');
    consoleRevision.textContent='revisão: '+(version.revision||'desconhecida')+' · modo '+(version.mode||'HML')+' · processo '+(version.processId||'?');
  }catch{
    consoleRevision.textContent='revisão: indisponível';
  }
}

loadConsoleRevision();
loadCommands('flow').catch(e=>commands.textContent='Falha ao carregar o fluxo: '+e.message);
</script>
</body>
</html>
""";
}
