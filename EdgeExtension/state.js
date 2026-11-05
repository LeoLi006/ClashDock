/* No page injection, cookies, storage, proxy permission or browsing-history access. */
(function(root){
 const HOST='com.clashdock.bridge';
 function siteFromUrl(raw){try{const u=new URL(raw);return /^(https?:)$/.test(u.protocol)&&u.hostname.includes('.')&&!/^\[/.test(u.hostname)&&!/^\d+(\.\d+){3}$/.test(u.hostname)?u.hostname.toLowerCase().replace(/\.$/,''):null}catch{return null}}
 class Channel {
  constructor(connect,onDisconnect=()=>{},timeout=12000){this.connect=connect;this.onDisconnect=onDisconnect;this.timeout=timeout;this.pending=new Map();this.port=null;this.closed=false;this.client=crypto.randomUUID().replaceAll('-','');}
  ensure(){if(this.closed)throw Error('closed');if(this.port)return;const p=this.connect(HOST);this.port=p;p.onMessage.addListener(message=>{
    const pending=this.pending.get(message?.id);if(!pending||message.version!==1||pending.port!==p)return;
    this.pending.delete(message.id);clearTimeout(pending.timer);if(Date.now()>pending.expires)pending.reject(Error('timeout'));else pending.resolve(message);
   });p.onDisconnect.addListener(()=>{if(this.port!==p)return;this.port=null;const reason=this.onDisconnect()||'disconnected';for(const [id,r]of this.pending){if(r.port===p){clearTimeout(r.timer);r.reject(Error(reason));this.pending.delete(id)}}});}
  request(op,fields={}){try{this.ensure()}catch(e){return Promise.reject(e)}if(this.pending.size>=16)return Promise.reject(Error('too_many_requests'));
   const id=crypto.randomUUID().replaceAll('-',''),expires=Date.now()+this.timeout;
   const message={...fields,version:1,id,op,client:this.client,expiresAt:expires};const p=this.port;
   return new Promise((resolve,reject)=>{const timer=setTimeout(()=>{this.pending.delete(id);reject(Error('timeout'))},this.timeout);this.pending.set(id,{resolve,reject,timer,expires,port:p});try{p.postMessage(message)}catch(e){clearTimeout(timer);this.pending.delete(id);reject(e)}});
  }
  close(){this.closed=true;for(const r of this.pending.values()){clearTimeout(r.timer);r.reject(Error('closed'))}this.pending.clear();const p=this.port;this.port=null;p?.disconnect();}
 }
 const api={Channel,siteFromUrl};if(typeof module!=='undefined'&&module.exports)module.exports=api;else root.ClashDockUI=api;
})(globalThis);
