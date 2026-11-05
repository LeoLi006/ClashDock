'use strict';
const fs=require('node:fs'),path=require('node:path'),{spawn}=require('node:child_process'),assert=require('node:assert/strict'),{pathToFileURL}=require('node:url');
const root=path.resolve(__dirname,'..'),profile=path.join(root,'artifacts','popup-cdp-profile'),chrome='C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';fs.mkdirSync(profile,{recursive:true});
const portFile=path.join(profile,'DevToolsActivePort');if(fs.existsSync(portFile))fs.unlinkSync(portFile);
const browser=spawn(chrome,['--headless=new','--disable-gpu','--no-first-run','--disable-background-networking','--disable-default-apps','--disable-sync','--remote-debugging-port=0',`--user-data-dir=${profile}`,pathToFileURL(path.join(__dirname,'ExtensionPreview.html')).href],{stdio:'ignore',windowsHide:true});
const sleep=ms=>new Promise(r=>setTimeout(r,ms));let ws;
(async()=>{
 for(let i=0;i<100&&!fs.existsSync(portFile);i++)await sleep(100);const port=fs.readFileSync(portFile,'utf8').split(/\r?\n/)[0];let tabs;
 for(let i=0;i<100;i++){tabs=await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();if(tabs.some(t=>t.type==='page'&&t.url.includes('ExtensionPreview')))break;await sleep(100)}
 const tab=tabs.find(t=>t.type==='page'&&t.url.includes('ExtensionPreview'));assert.ok(tab,'isolated test page exists');ws=new WebSocket(tab.webSocketDebuggerUrl);await new Promise((r,j)=>{ws.onopen=r;ws.onerror=j});let next=0,pending=new Map();ws.onmessage=e=>{const m=JSON.parse(e.data);if(m.id&&pending.has(m.id)){const r=pending.get(m.id);pending.delete(m.id);m.error?r.reject(Error(JSON.stringify(m.error))):r.resolve(m.result)}};
 const call=(method,params={})=>new Promise((resolve,reject)=>{const id=++next;pending.set(id,{resolve,reject});ws.send(JSON.stringify({id,method,params}))});
 await call('Page.enable');await call('Emulation.setDeviceMetricsOverride',{width:430,height:940,deviceScaleFactor:1,mobile:false});let report=null;
 for(let i=0;i<150;i++){const result=await call('Runtime.evaluate',{expression:'document.getElementById("harness-result")?.textContent',returnByValue:true});if(result.result.value){report=JSON.parse(result.result.value);break}await sleep(100)}
 assert.ok(report,'UI harness completed');fs.writeFileSync(path.join(root,'artifacts','popup-ui-checks.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
 const screenshot=await call('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});fs.writeFileSync(path.join(root,'artifacts','extension-popup.png'),Buffer.from(screenshot.data,'base64'));assert.equal(report.error,null);assert.ok(Object.values(report.checks).every(Boolean));
 ws.close();browser.kill();console.log(Object.keys(report.checks).length+' rendered popup checks passed.');
})().catch(e=>{console.error(e);ws?.close();browser.kill();process.exitCode=1});
