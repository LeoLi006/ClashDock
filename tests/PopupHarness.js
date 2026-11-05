'use strict';
const harness={checks:{},requests:[],url:'https://video.example.com/watch',revision:1,mode:'自动',phase:'applied',snapshot:0,ports:[],session:'abcdeabcdeabcdeabcdeabcdeabcdeabcde'};
function snapshot(){return{session:harness.session,revision:harness.revision,snapshot:++harness.snapshot,intentSaved:true,applicationCount:2,websiteCount:3,retryScope:"将重新应用全部分流：2 条应用规则、3 条网站规则。可能重载配置，不仅当前网站。",stateMessage:harness.phase==='retry'?'应用失败：未覆盖原配置':'规则与实际连接分别核验',next:'开始观察后手动刷新网页；旧连接可能仍使用原路线。',domain:new URL(harness.url).hostname,mode:harness.mode,inherited:null,paused:false,appliedPaused:false,state:harness.phase,controllerConnected:true,runningMode:'rule',rulesAccepted:true,target:'自动选择',targetDisplay:'自动选择 → 香港节点 A · 48 ms · 10:30',targetTime:new Date().toISOString()}}
globalThis.chrome={runtime:{lastError:null,connectNative(){let messages=[],disconnects=[];const p={onMessage:{addListener:f=>messages.push(f)},onDisconnect:{addListener:f=>disconnects.push(f)},disconnect(){disconnects.forEach(f=>f())},postMessage(r){harness.requests.push(r);if(r.op==='state'&&harness.holdNextState){harness.holdNextState=false;const held=snapshot();harness.releaseState=()=>messages.forEach(f=>f({version:1,id:r.id,ok:true,code:'ok',data:held}));return;}setTimeout(()=>{let data=harness.overrideNextState&&r.op==='state'?harness.overrideNextState:snapshot(),code='ok',message='';if(r.op==='state')harness.overrideNextState=null;if(r.op==='setSite'){harness.mode=r.mode;harness.revision++;harness.phase='applying';data=snapshot();setTimeout(()=>{harness.phase='applied'},50)}if(r.op==='templates')data=[{id:'bilibili',name:'哔哩哔哩',domains:['bilibili.com','bilivideo.com','hdslb.com']}];messages.forEach(f=>f({version:1,id:r.id,ok:true,code,message,data}))},10)}};harness.ports.push(p);return p}},tabs:{async query(){return[{id:1,url:harness.url}]},async reload(){harness.reloaded=true}}};
addEventListener('load',async()=>{
 const sleep=ms=>new Promise(r=>setTimeout(r,ms));const until=async test=>{for(let i=0;i<100;i++){if(test())return;await sleep(30)}throw Error('UI condition timed out')};const check=(key,value)=>{harness.checks[key]=Boolean(value);if(!value)throw Error(key)};
 try{
  await until(()=>document.getElementById('status').textContent.includes('规则已加载'));
  check('renders_target_delay',document.getElementById('target').textContent.includes('48 ms'));
  check('scope_preserved',document.getElementById('domain').textContent==='video.example.com');
  document.querySelector('[data-mode="直连"]').click();document.querySelector('[data-mode="代理"]').click();await until(()=>harness.mode==='代理');await sleep(120);
  check('rapid_choices_last_wins',harness.requests.filter(r=>r.op==='setSite').at(-1).mode==='代理');
  check('identity_not_display',harness.requests.filter(r=>r.op==='setSite').every(r=>!r.target&&!r.targetDisplay));
  document.getElementById('more').open=true;document.getElementById('templates').click();await until(()=>document.getElementById('templateDomains').children.length===3);
  check('template_review_visible',document.getElementById('templatePanel').open&&document.getElementById('templateDomains').textContent.includes('bilivideo.com'));
  document.getElementById('templatePanel').open=false;
  harness.ports.at(-1).disconnect();await sleep(30);check('disconnect_not_green',!document.getElementById('statusBox').classList.contains('good')&&document.querySelector('[data-mode="代理"]').disabled);
  document.getElementById('refresh').click();await until(()=>document.getElementById('status').textContent.includes('规则已加载'));check('reconnect_read_only',harness.requests.at(-1).op==='state');
  document.querySelector('[data-mode="直连"]').click();await until(()=>harness.mode==='直连');await sleep(100);document.getElementById('refresh').click();await sleep(100);
  check('direct_is_expected_not_observed',document.getElementById('expected').textContent==='预期出口：直连'&&document.getElementById('status').textContent.includes('未验证实际出口'));
  harness.phase='retry';document.getElementById('refresh').click();await until(()=>!document.getElementById('retry').hidden);
  const writesBefore=harness.requests.filter(r=>r.op==='retry').length;document.getElementById('retry').click();
  check('all_rules_preview_before_write',!document.getElementById('retryPanel').hidden&&document.getElementById('retryScope').textContent.includes('3 条网站')&&harness.requests.filter(r=>r.op==='retry').length===writesBefore);
  document.getElementById('cancelRetry').click();check('cancel_preview_no_write',document.getElementById('retryPanel').hidden&&harness.requests.filter(r=>r.op==='retry').length===writesBefore);
  document.getElementById('retry').click();document.getElementById('confirmRetry').click();await until(()=>harness.requests.some(r=>r.op==='retry'));
  check('explicit_all_rules_confirmation',harness.requests.find(r=>r.op==='retry').confirmAll===true);
  harness.phase='applied';document.getElementById('refresh').click();await sleep(100);
  harness.holdNextState=true;document.getElementById('refresh').click();await until(()=>typeof harness.releaseState==='function');
  harness.mode='代理';harness.revision++;document.getElementById('refresh').click();await until(()=>document.querySelector('[data-mode="代理"]').getAttribute('aria-pressed')==='true');
  harness.releaseState();await sleep(80);check('stale_read_cannot_replace_new_selection',document.querySelector('[data-mode="代理"]').getAttribute('aria-pressed')==='true');
  const oldSession=harness.session;harness.session='bbbbbabcdeabcdeabcdeabcdeabcdeabcd';harness.revision=0;harness.mode='直连';document.getElementById('refresh').click();await until(()=>document.querySelector('[data-mode="直连"]').getAttribute('aria-pressed')==='true');
  const writeCount=harness.requests.filter(r=>['retry','setSite'].includes(r.op)).length;harness.overrideNextState={...snapshot(),session:oldSession,revision:999,mode:'代理'};document.getElementById('refresh').click();await sleep(100);
  check('retired_session_cannot_restore_old_choice',document.querySelector('[data-mode="直连"]').getAttribute('aria-pressed')==='true'&&harness.requests.filter(r=>['retry','setSite'].includes(r.op)).length===writeCount);
  harness.overrideNextState={...snapshot(),snapshot:0,mode:'代理'};document.getElementById('refresh').click();await sleep(100);check('older_snapshot_ignored_at_equal_revision',document.querySelector('[data-mode="直连"]').getAttribute('aria-pressed')==='true');
  harness.url='edge://settings';document.getElementById('refresh').click();await until(()=>document.getElementById('status').textContent==='不适用');check('internal_page_disabled',document.querySelector('[data-mode="直连"]').disabled);
  harness.url='https://video.example.com/failed';document.getElementById('refresh').click();await until(()=>document.getElementById('status').textContent.includes('规则已加载'));check('failed_page_valid_domain_enabled',!document.querySelector('[data-mode="直连"]').disabled);
 }catch(e){harness.error=e.message}finally{const pre=document.createElement('pre');pre.id='harness-result';pre.hidden=true;pre.textContent=JSON.stringify({checks:harness.checks,error:harness.error??null});document.body.append(pre)}
});
