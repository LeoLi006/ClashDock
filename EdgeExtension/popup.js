'use strict';
(()=>{
 const $=id=>document.getElementById(id),buttons=[...document.querySelectorAll('[data-mode]')];let domain=null,tabId=null,state=null,sequence=0,wanted=null,optimistic=null,sending=false,readGeneration=0,poll=null,closed=false,connected=false,templates=[],retryPreview=null;let contextEpoch=0,contextTab=null,contextRead=0,ownerWindow=null;const retiredSessions=new Set();
 const channel=new ClashDockUI.Channel(name=>chrome.runtime.connectNative(name),()=>{const error=chrome.runtime.lastError?.message||'Native host disconnected';offline(ClashDockUI.nativeError(error));return error});
 const codes={waiting:'等待应用',applying:'正在应用',saved:'已保存但未生效',applied:'规则已加载 · 未验证实际出口',paused:'已暂停',retry:'应用失败',save_failed:'配置保存失败',overwritten:'托管规则缺失或变化',controller_offline:'控制器未连接',unknown:'尚待核验',mode_bypass:'当前模式不按规则分流',target_missing:'代理目标失效',target_mismatch:'策略组选择变化'};
 function notice(text){$('notice').textContent=text}
 function status(text,detail='',kind=''){ $('status').textContent=text;$('statusDetail').textContent=detail;$('statusBox').className='status '+kind; }
 function offline(text){connected=false;clearTimeout(poll);status('未连接',text,'bad');buttons.forEach(b=>b.disabled=true);$('check').disabled=true;$('templates').disabled=true;$('start').hidden=!text.includes('ClashDock 未运行');$('retry').hidden=true;$('retryPanel').hidden=true;$('next').textContent=text.includes('ClashDock 未运行')?'点击启动固定的 ClashDock 程序，再只读刷新。':'在桌面「设置与说明」中打开通信桥检查；不必盲目重新注册或重启浏览器。';}
 function render(){if(!state||!connected)return;const latest=optimistic??wanted?.mode??state.mode;
  buttons.forEach(b=>{b.disabled=!domain;b.setAttribute('aria-pressed',String(b.dataset.mode===latest))});
  $('intent').textContent=(sending||wanted||optimistic?'待确认保存的选择：':state.intentSaved===false?'尚未保存的选择：':'已保存选择：')+latest;
  $('expected').textContent=latest==='直连'?'预期出口：直连':latest==='代理'?'预期出口：所选代理目标':'预期出口：沿用后续规则';
  $('target').textContent=state.target?state.targetDisplay:'尚未选择全局代理目标';
  $('time').textContent=state.targetTime?'测试时间：'+new Date(state.targetTime).toLocaleString()+'；组延迟仅代表当前选中节点':'未测速或测试时间未知；没有历史不等于超时';
  const concise={subscription_mismatch:'订阅核验不一致，本次修改未应用。',apply_failed:'应用失败，已保留选择；原始原因见详情。',save_failed:'选择尚未保存，请检查目录写入权限。',mode_bypass:'当前模式不会按这些规则分流。',target_missing:'所选代理目标已不存在，未自动替换。'}[state.reason];
  $('errorDetail').hidden=!state.reason;$('errorDetail').textContent='核验／失败详情：'+(state.stateMessage||'无');
  const error=['retry','save_failed','overwritten','controller_offline','target_missing','target_mismatch','mode_bypass'].includes(state.state);
  status(sending||wanted?'正在保存最新选择…':codes[state.state]??'尚待核验', concise||state.stateMessage||'按钮仅代表选择意图，不代表实际出口。',error?'bad':state.state==='applied'?'good':'');
  $('next').textContent=state.next||'需在检查分流中主动观察新连接；没有记录不等于未联网。';
  $('inherit').textContent='运行模式：'+state.runningMode+'；最近核验：'+(state.verifiedAt?new Date(state.verifiedAt).toLocaleTimeString():'未知')+'。'+(state.inherited?'仍可能匹配父域 '+state.inherited+'（'+state.inheritedMode+'）。':'自动沿用后续应用／订阅规则。');
  $('check').disabled=false;$('templates').disabled=false;$('applyTemplate').disabled=sending||Boolean(wanted);$('start').hidden=true;
  $('retry').hidden=!['retry','save_failed','overwritten','target_mismatch','saved'].includes(state.state);
  if(retryPreview&&(retryPreview.session!==state.session||retryPreview.revision!==state.revision)){$('retryPanel').hidden=true;retryPreview=null;notice('配置已变化，请重新预览全部分流。');}
 }
 function accept(next){
  if(!next||next.domain&&next.domain!==domain||retiredSessions.has(next.session))return false;
  if(state&&next.session===state.session&&(next.revision<state.revision||next.revision===state.revision&&(next.snapshot??0)<(state.snapshot??0)))return false;
  if(state&&next.session!==state.session){retiredSessions.add(state.session);if(retiredSessions.size>16)retiredSessions.delete(retiredSessions.values().next().value);wanted=null;optimistic=null;retryPreview=null;$('retryPanel').hidden=true;}
  state=next;connected=true;return true;
 }
 async function context(){const ticket=++contextRead;const tabs=await chrome.tabs.query(ownerWindow==null?{active:true,currentWindow:true}:{active:true,windowId:ownerWindow});if(closed||ticket!==contextRead)return true;const tab=tabs[0];ownerWindow??=tab?.windowId??null;const next=ClashDockUI.siteFromUrl(tab?.pendingUrl||tab?.url||'');tabId=tab?.id??null;
  if(next!==domain||contextTab!==tabId){contextTab=tabId;contextEpoch++;optimistic=null;domain=next;state=null;$('intent').textContent='正在读取已保存选择';$('expected').textContent='预期出口：尚未读取';retryPreview=null;$('retryPanel').hidden=true;$('templatePanel').open=false;readGeneration++;wanted=null;notice('');buttons.forEach(b=>b.disabled=true);status('正在读取当前网站…');$('domain').textContent=domain??'此页面不支持网站分流';$('reloadTab').disabled=!domain;if(!domain){buttons.forEach(b=>b.disabled=true);$('templates').disabled=$('check').disabled=true;$('start').hidden=true;status('不适用','仅支持有有效域名的 HTTP / HTTPS 地址；内部页和文件页不操作规则。');}return true}return false;
 }
 function schedule(){clearTimeout(poll);if(!closed&&connected&&domain&&state)poll=setTimeout(()=>reload(),1500)}
 async function reload(){let requestEpoch=contextEpoch;if(closed)return;if(sending||wanted){schedule();return}try{await context();requestEpoch=contextEpoch;if(!domain)return;const current=++readGeneration,scope=domain;const reply=await channel.request('state',{domain:scope});if(closed||current!==readGeneration||scope!==domain||sending)return;
   if(!reply.ok){offline(reply.message);return}if(accept(reply.data)){render();notice('');}schedule();
  }catch(error){if(!closed&&requestEpoch===contextEpoch)offline(ClashDockUI.nativeError(String(error?.message||error)))}
 }
 async function ensureContext(){const changed=await context();if(changed){await reload();notice('页面地址已变化，请确认域名后重新操作。');return false}return Boolean(domain&&state)}
 async function drain(){if(sending||!wanted)return;const flightEpoch=contextEpoch;sending=true;clearTimeout(poll);try{while(wanted&&!closed){const item=wanted;wanted=null;if(item.epoch!==contextEpoch||item.domain!==domain)continue;const scope=item.domain,epoch=item.epoch,fields={domain:scope,mode:item.mode,session:item.guard?.session??state.session,baseRevision:item.guard?.revision??state.revision,sequence:item.sequence,...item.extra};let reply=await channel.request(item.op,fields);
    if(scope!==domain||epoch!==contextEpoch)continue; if(reply.data)accept(reply.data);
    if(['session_changed','conflict','stale_sequence'].includes(reply.code)){
      wanted=null;optimistic=null;notice('桌面会话或配置已变化，未重放修改；请刷新并重新选择。');break;
    }
    if(item.sequence===sequence){optimistic=null;if(!reply.ok)notice(reply.message);else notice(reply.data?.intentSaved===false?'保存失败，选择仍在内存中；未确认应用。':'选择已接收；请以核验状态为准，不会自动刷新网页。');render();}
   }}catch{if(flightEpoch===contextEpoch){wanted=null;notice('提交结果尚未确认。先刷新状态再决定是否重试，不自动重复操作。');offline('通信中断或超时；已被桌面端接收的修改不会撤销。')}}
  finally{sending=false;if(!closed){render();if(!state)void reload();else schedule()}}
 }
 async function queue(mode,op='setSite',extra={},guard=null){if(!await ensureContext())return;if(guard&&(guard.session!==state.session||guard.revision!==state.revision)){notice('配置已变化，请重新预览全部分流。');return;}sequence++;optimistic=op==='setSite'?mode:null;wanted={mode,op,extra,sequence,guard,domain,epoch:contextEpoch};render();void drain()}
 buttons.forEach(b=>b.addEventListener('click',()=>queue(b.dataset.mode)));
 $('refresh').addEventListener('click',()=>{notice('');void reload()});
 $('start').addEventListener('click',async()=>{try{const r=await channel.request('start');notice(r.message);if(r.ok)setTimeout(()=>reload(),1800)}catch(error){notice(ClashDockUI.nativeError(String(error?.message||error)))}});
 $('retry').addEventListener('click',()=>{if(!state||sending||wanted)return;retryPreview={session:state.session,revision:state.revision};$('retryScope').textContent=state.retryScope||('将重新应用全部分流：'+(state.applicationCount??'未知')+' 条应用规则、'+(state.websiteCount??'未知')+' 条网站规则。不仅当前网站；可能重载配置，旧连接不自动断开。');$('retryPanel').hidden=false;});
 $('cancelRetry').addEventListener('click',()=>{retryPreview=null;$('retryPanel').hidden=true;});
 $('confirmRetry').addEventListener('click',()=>{if(!retryPreview||!state||retryPreview.session!==state.session||retryPreview.revision!==state.revision){notice('配置已变化，请重新预览。');return;}const guard=retryPreview;retryPreview=null;$('retryPanel').hidden=true;void queue(state.mode??'自动','retry',{confirmAll:true},guard);});
 $('check').addEventListener('click',async()=>{if(!await ensureContext())return;try{const epoch=contextEpoch;const r=await channel.request('check',{domain,session:state.session});if(epoch===contextEpoch)notice(r.message)}catch{notice('检查窗口未确认打开，请刷新后重试。')}});
 $('reloadTab').addEventListener('click',async()=>{if(!await ensureContext())return;if(tabId!=null){await chrome.tabs.reload(tabId);notice('已按你的操作刷新网页。')}});
 function renderTemplate(){const t=templates.find(t=>t.id===$('templateSelect').value);$('templateDomains').replaceChildren();for(const host of t?.domains??[]){const label=document.createElement('label');label.className='domain-option';const cb=document.createElement('input');cb.type='checkbox';cb.checked=true;cb.value=host;const text=document.createElement('span');text.textContent=host;label.append(cb,text);$('templateDomains').append(label)}}
 $('templates').addEventListener('click',async()=>{try{const epoch=contextEpoch;const r=await channel.request('templates');if(epoch!==contextEpoch||closed)return;if(!r.ok){notice(r.message);return}templates=r.data;$('templateSelect').replaceChildren();for(const t of templates){const o=document.createElement('option');o.value=t.id;o.textContent=t.name;$('templateSelect').append(o)}renderTemplate();$('templatePanel').open=true;}catch{notice('模板读取失败，请刷新通信状态。')}});
 $('templateSelect').addEventListener('change',renderTemplate);$('applyTemplate').addEventListener('click',()=>{const domains=[...$('templateDomains').querySelectorAll('input:checked')].map(i=>i.value);if(!domains.length){notice('请至少选择一个域名。');return}void queue($('templateMode').value,'applyTemplate',{template:$('templateSelect').value,domains})});
 function changed(){contextEpoch++;readGeneration++;contextRead++;wanted=null;optimistic=null;state=null;contextTab=null;buttons.forEach(b=>b.disabled=true);$('check').disabled=$('templates').disabled=$('reloadTab').disabled=true;status('正在切换网站…');void context().then(()=>reload()).catch(e=>offline(String(e)));}
 const activated=info=>{if(ownerWindow==null||info.windowId===ownerWindow)changed()};
 const updated=(id,change)=>{if(id===tabId&&(change.url||change.status==='loading'))changed()};
 chrome.tabs.onActivated?.addListener(activated);chrome.tabs.onUpdated?.addListener(updated);
 const sidebar=$('sidebar');if(sidebar){sidebar.hidden=document.body.classList.contains('sidepanel');sidebar.disabled=!chrome.sidePanel?.open;sidebar.addEventListener('click',()=>{if(ownerWindow==null){notice('请等待读取当前标签页后再打开侧边栏。');return}chrome.sidePanel.open({windowId:ownerWindow}).catch(e=>notice('无法打开侧边栏：'+e.message));});}
 buttons.forEach((b,i)=>b.addEventListener('keydown',e=>{let j=e.key==='ArrowRight'?(i+1)%buttons.length:e.key==='ArrowLeft'?(i+buttons.length-1)%buttons.length:e.key==='Home'?0:e.key==='End'?buttons.length-1:null;if(j!==null){e.preventDefault();buttons[j].focus()}}));
 addEventListener('unload',()=>{closed=true;contextRead++;readGeneration++;chrome.tabs.onActivated?.removeListener(activated);chrome.tabs.onUpdated?.removeListener(updated);clearTimeout(poll);channel.close()});void reload();
})();
