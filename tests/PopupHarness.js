'use strict';
const harness={checks:{},requests:[],url:'https://video.example.com/watch',revision:1,mode:'自动',phase:'applied',ports:[],session:'abcdeabcdeabcdeabcdeabcdeabcdeabcde'};
function snapshot(){return{session:harness.session,revision:harness.revision,domain:new URL(harness.url).hostname,mode:harness.mode,inherited:null,paused:false,appliedPaused:false,state:harness.phase,controllerConnected:true,runningMode:'rule',rulesAccepted:true,target:'自动选择',targetDisplay:'自动选择 → 香港节点 A · 48 ms · 10:30',targetTime:new Date().toISOString()}}
globalThis.chrome={runtime:{lastError:null,connectNative(){let messages=[],disconnects=[];const p={onMessage:{addListener:f=>messages.push(f)},onDisconnect:{addListener:f=>disconnects.push(f)},disconnect(){disconnects.forEach(f=>f())},postMessage(r){harness.requests.push(r);setTimeout(()=>{let data=snapshot(),code='ok',message='';if(r.op==='setSite'){harness.mode=r.mode;harness.revision++;harness.phase='applying';data=snapshot();setTimeout(()=>{harness.phase='applied'},50)}if(r.op==='templates')data=[{id:'bilibili',name:'哔哩哔哩',domains:['bilibili.com','bilivideo.com','hdslb.com']}];messages.forEach(f=>f({version:1,id:r.id,ok:true,code,message,data}))},10)}};harness.ports.push(p);return p}},tabs:{async query(){return[{id:1,url:harness.url}]},async reload(){harness.reloaded=true}}};
addEventListener('load',async()=>{
 const sleep=ms=>new Promise(r=>setTimeout(r,ms));const until=async test=>{for(let i=0;i<100;i++){if(test())return;await sleep(30)}throw Error('UI condition timed out')};const check=(key,value)=>{harness.checks[key]=Boolean(value);if(!value)throw Error(key)};
 try{
  await until(()=>document.getElementById('status').textContent.includes('已生效'));
  check('renders_target_delay',document.getElementById('target').textContent.includes('48 ms'));
  check('scope_preserved',document.getElementById('domain').textContent==='video.example.com');
  document.querySelector('[data-mode="直连"]').click();document.querySelector('[data-mode="代理"]').click();await until(()=>harness.mode==='代理');await sleep(120);
  check('rapid_choices_last_wins',harness.requests.filter(r=>r.op==='setSite').at(-1).mode==='代理');
  check('identity_not_display',harness.requests.filter(r=>r.op==='setSite').every(r=>!r.target&&!r.targetDisplay));
  document.getElementById('templates').click();await until(()=>document.getElementById('templateDomains').children.length===3);
  check('template_review_visible',document.getElementById('templatePanel').open&&document.getElementById('templateDomains').textContent.includes('bilivideo.com'));
  document.getElementById('templatePanel').open=false;
  harness.ports.at(-1).disconnect();await sleep(30);check('disconnect_not_green',!document.getElementById('statusBox').classList.contains('good')&&document.querySelector('[data-mode="代理"]').disabled);
  document.getElementById('refresh').click();await until(()=>document.getElementById('status').textContent.includes('已生效'));check('reconnect_read_only',harness.requests.at(-1).op==='state');
  harness.url='edge://settings';document.getElementById('refresh').click();await until(()=>document.getElementById('status').textContent==='不适用');check('internal_page_disabled',document.querySelector('[data-mode="直连"]').disabled);
  harness.url='https://video.example.com/failed';document.getElementById('refresh').click();await until(()=>document.getElementById('status').textContent.includes('已生效'));check('failed_page_valid_domain_enabled',!document.querySelector('[data-mode="直连"]').disabled);
 }catch(e){harness.error=e.message}finally{const pre=document.createElement('pre');pre.id='harness-result';pre.hidden=true;pre.textContent=JSON.stringify({checks:harness.checks,error:harness.error??null});document.body.append(pre)}
});
