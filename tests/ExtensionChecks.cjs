'use strict';
const assert=require('node:assert/strict');const {Channel,siteFromUrl}=require('../EdgeExtension/state.js');let count=0;
function check(pass,name){assert.ok(pass,name);console.log('PASS:',name);count++}
function port(){const listeners=[],gone=[];return{sent:[],onMessage:{addListener:f=>listeners.push(f)},onDisconnect:{addListener:f=>gone.push(f)},postMessage(m){this.sent.push(m)},reply(m){listeners.forEach(f=>f(m))},disconnect(){gone.forEach(f=>f())}}}
(async()=>{
 check(siteFromUrl('https://video.Example.com/abc')==='video.example.com','Popup preserves exact subdomain scope');
 check(siteFromUrl('https://例子.中国')==='xn--fsqu00a.xn--fiqs8s','Popup normalizes international host without widening');
 check(!siteFromUrl('edge://settings')&&!siteFromUrl('file:///C:/x')&&!siteFromUrl('http://127.0.0.1')&&!siteFromUrl('http://[::1]'),'Unsupported and non-domain pages disabled');
 check(siteFromUrl('https://failed.example.com/')==='failed.example.com','Valid address independent from page loading success');
 const ports=[];const channel=new Channel(()=>{const p=port();ports.push(p);return p},()=>{},40);
 const promise=channel.request('state',{domain:'example.com'});const p=ports[0],r=p.sent[0];p.reply({version:1,id:'old',ok:true});p.reply({version:1,id:r.id,ok:true,data:{revision:1}});check((await promise).data.revision===1,'Unknown response identity ignored');
 const delayed=channel.request('state');const dr=p.sent.at(-1);await assert.rejects(delayed,/timeout/);p.reply({version:1,id:dr.id,ok:true,data:{revision:999}});check(channel.pending.size===0,'Timed-out response cannot update any caller');
 const disconnected=channel.request('state');p.disconnect();await assert.rejects(disconnected,/disconnected/);check(channel.pending.size===0,'Disconnect rejects pending requests without retrying writes');
 const reconnected=channel.request('state');const next=ports[1],nr=next.sent[0];p.reply({version:1,id:nr.id,ok:true,data:{revision:999}});next.reply({version:1,id:nr.id,ok:true,data:{revision:2}});check((await reconnected).data.revision===2,'Reconnect accepts only current native port response');
 const wrongVersion=channel.request('state');next.reply({version:9,id:next.sent.at(-1).id,ok:true});await assert.rejects(wrongVersion,/timeout/);check(true,'Protocol-version mismatch never becomes valid state');
 const pending=channel.request('setSite',{domain:'example.com',id:'forged',client:'forged',version:9});const msg=next.sent.at(-1);check(msg.version===1&&msg.id!=='forged'&&msg.client!=='forged','Channel owns request identity and protocol metadata');channel.close();await assert.rejects(pending,/closed/);check(channel.pending.size===0,'Popup close releases timers and pending listeners');
 const manifest=require('../EdgeExtension/manifest.json');check(manifest.permissions.length===2&&manifest.permissions.includes('activeTab')&&manifest.permissions.includes('nativeMessaging')&&!manifest.host_permissions&&!manifest.content_scripts,'Extension permissions contain no proxy, page injection or history access');
 const crypto=require('node:crypto');const hash=crypto.createHash('sha256').update(Buffer.from(manifest.key,'base64')).digest().subarray(0,16);let id='';for(const byte of hash)id+=String.fromCharCode(97+(byte>>4),97+(byte&15));check(id==='opbdfefdnighegepeiffmioofdkkphnk','Unpacked public key fixes approved extension identity');
 console.log(count+' extension checks passed.');
})().catch(e=>{console.error(e);process.exitCode=1});
