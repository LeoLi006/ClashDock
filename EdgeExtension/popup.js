'use strict';
(()=>{
 const $=id=>document.getElementById(id),buttons=[...document.querySelectorAll('[data-mode]')];let domain=null,tabId=null,state=null,sequence=0,wanted=null,optimistic=null,sending=false,readGeneration=0,poll=null,closed=false,connected=false,templates=[];
 const channel=new ClashDockUI.Channel(name=>chrome.runtime.connectNative(name),()=>{void chrome.runtime.lastError;offline('通信桥断开，请检查安装后刷新；未重复提交修改。')});
 const codes={applied:'已生效 · 内核已核验',applying:'正在应用…',paused:'分流已暂停',retry:'应用失败 · 可重试',save_failed:'保存失败',overwritten:'配置被覆盖',controller_offline:'控制器未连接',unknown:'状态尚待核验'};
 function notice(text){$('notice').textContent=text}
 function status(text,detail='',kind=''){ $('status').textContent=text;$('statusDetail').textContent=detail;$('statusBox').className='status '+kind; }
 function offline(text){connected=false;clearTimeout(poll);status('未连接',text,'bad');buttons.forEach(b=>b.disabled=true);$('check').disabled=true;$('templates').disabled=true;$('start').hidden=false;}
 function render(){if(!state||!connected)return;const latest=optimistic??wanted?.mode??state.mode;buttons.forEach(b=>{b.disabled=!domain;b.setAttribute('aria-pressed',String(b.dataset.mode===latest))});$('target').textContent=state.target?state.targetDisplay:'请先在 ClashDock 选择全局代理目标';
  $('time').textContent=state.targetTime?'测试时间：'+new Date(state.targetTime).toLocaleString()+'；组延迟仅代表当前选中节点':'未测速或测试时间未知；没有历史不等于超时';
  const label=codes[state.state]??'状态未知';status(sending||wanted?'正在提交最新选择…':label,(state.paused?'暂停意图：是；恢复分流后才应用网站设置。 ':'')+'运行模式：'+state.runningMode+'。规则加载不等于观察到连接。'+(state.verifiedAt?' 最近核验 '+new Date(state.verifiedAt).toLocaleTimeString():''),!sending&&!wanted&&state.state==='applied'?'good':['retry','save_failed','overwritten','controller_offline'].includes(state.state)?'bad':'');
  $('inherit').textContent=state.inherited?'仍可能匹配父域 '+state.inherited+'（'+state.inheritedMode+'）。自动只撤销当前域名覆盖。':'自动沿用后续应用／订阅规则；不是标签页独立代理。';
  $('check').disabled=false;$('templates').disabled=false;$('applyTemplate').disabled=sending||Boolean(wanted);$('start').hidden=true;$('retry').hidden=!['retry','save_failed','overwritten'].includes(state.state);
 }
 function accept(next){if(!next)return false;if(state&&next.session===state.session&&next.revision<state.revision)return false;state=next;connected=true;return true}
 async function context(){const tabs=await chrome.tabs.query({active:true,currentWindow:true});const tab=tabs[0];const next=ClashDockUI.siteFromUrl(tab?.pendingUrl||tab?.url||'');tabId=tab?.id??null;
  if(next!==domain){domain=next;state=null;readGeneration++;wanted=null;notice('');buttons.forEach(b=>b.disabled=true);status('正在读取当前网站…');$('domain').textContent=domain??'此页面不支持网站分流';$('reloadTab').disabled=!domain;if(!domain){buttons.forEach(b=>b.disabled=true);$('templates').disabled=$('check').disabled=true;$('start').hidden=true;status('不适用','仅支持有有效域名的 HTTP / HTTPS 地址；内部页和文件页不操作规则。');}return true}return false;
 }
 function schedule(){clearTimeout(poll);if(!closed&&connected&&domain&&state)poll=setTimeout(()=>reload(),1500)}
 async function reload(){if(closed)return;if(sending||wanted){schedule();return}try{await context();if(!domain)return;const current=++readGeneration,scope=domain;const reply=await channel.request('state',{domain:scope});if(closed||current!==readGeneration||scope!==domain||sending)return;
   if(!reply.ok){offline(reply.message);return}if(accept(reply.data)){render();notice('');}schedule();
  }catch{if(!closed)offline('无法连接本机通信桥或请求超时。先按安装指南检查注册，再点击刷新。')}
 }
 async function ensureContext(){const changed=await context();if(changed){await reload();notice('页面地址已变化，请确认域名后重新操作。');return false}return Boolean(domain&&state)}
 async function drain(){if(sending||!wanted)return;sending=true;clearTimeout(poll);try{while(wanted&&!closed){const item=wanted;wanted=null;const scope=domain,fields={domain:scope,mode:item.mode,session:state.session,baseRevision:state.revision,sequence:item.sequence,...item.extra};let reply=await channel.request(item.op,fields);
    if(scope!==domain)continue; if(reply.data)accept(reply.data);
    // One bounded rebase for a concurrent desktop edit; a new request identity is used, never blind replay after timeout.
    if(reply.code==='conflict'&&item.sequence===sequence&&state){reply=await channel.request(item.op,{...fields,session:state.session,baseRevision:state.revision});if(reply.data)accept(reply.data)}
    if(item.sequence===sequence){optimistic=null;if(!reply.ok)notice(reply.message);else notice('修改已接收；请以核验状态为准，不会自动刷新网页。');render();}
   }}catch{wanted=null;notice('提交结果尚未确认。先刷新状态再决定是否重试，不自动重复操作。');offline('通信中断或超时；已被桌面端接收的修改不会撤销。')}
  finally{sending=false;if(!closed){render();schedule()}}
 }
 async function queue(mode,op='setSite',extra={}){if(!await ensureContext())return;sequence++;optimistic=op==='setSite'?mode:null;wanted={mode,op,extra,sequence};render();void drain()}
 buttons.forEach(b=>b.addEventListener('click',()=>queue(b.dataset.mode)));
 $('refresh').addEventListener('click',()=>{notice('');void reload()});
 $('start').addEventListener('click',async()=>{try{const r=await channel.request('start');notice(r.message);if(r.ok)setTimeout(()=>reload(),1800)}catch{notice('无法连接通信桥，请先按安装指南完成当前用户注册。')}});
 $('retry').addEventListener('click',()=>queue(state?.mode??'自动','retry'));
 $('check').addEventListener('click',async()=>{if(!await ensureContext())return;try{const r=await channel.request('check',{domain,session:state.session});notice(r.message)}catch{notice('检查窗口未确认打开，请刷新后重试。')}});
 $('reloadTab').addEventListener('click',async()=>{if(tabId!=null){await chrome.tabs.reload(tabId);notice('已按你的操作刷新网页。')}});
 function renderTemplate(){const t=templates.find(t=>t.id===$('templateSelect').value);$('templateDomains').replaceChildren();for(const host of t?.domains??[]){const label=document.createElement('label');label.className='domain-option';const cb=document.createElement('input');cb.type='checkbox';cb.checked=true;cb.value=host;const text=document.createElement('span');text.textContent=host;label.append(cb,text);$('templateDomains').append(label)}}
 $('templates').addEventListener('click',async()=>{try{const r=await channel.request('templates');if(!r.ok){notice(r.message);return}templates=r.data;$('templateSelect').replaceChildren();for(const t of templates){const o=document.createElement('option');o.value=t.id;o.textContent=t.name;$('templateSelect').append(o)}renderTemplate();$('templatePanel').open=true;}catch{notice('模板读取失败，请刷新通信状态。')}});
 $('templateSelect').addEventListener('change',renderTemplate);$('applyTemplate').addEventListener('click',()=>{const domains=[...$('templateDomains').querySelectorAll('input:checked')].map(i=>i.value);if(!domains.length){notice('请至少选择一个域名。');return}void queue($('templateMode').value,'applyTemplate',{template:$('templateSelect').value,domains})});
 addEventListener('unload',()=>{closed=true;clearTimeout(poll);channel.close()});void reload();
})();
