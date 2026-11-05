"""Generate the offline user guide. Developer dependency: Python Markdown."""
from pathlib import Path
import base64
import re
import markdown

ROOT = Path(__file__).resolve().parents[1]
source = (ROOT / '使用说明.md').read_text(encoding='utf-8-sig')
body = markdown.markdown(source, extensions=['tables', 'fenced_code', 'toc'])
image = base64.b64encode((ROOT / 'docs/images/edge-popup.png').read_bytes()).decode('ascii')
body = body.replace('src="docs/images/edge-popup.png"', 'src="data:image/png;base64,' + image + '"')
for name in ['EDGE_EXTENSION.md', 'DEVELOPMENT.md', 'VALIDATION.md', 'THIRD_PARTY_NOTICES.md']:
    body = body.replace('href="' + name + '"', 'href="https://github.com/LeoLi006/ClashDock/blob/v1.2.0/' + name + '"')
css = '''
:root{color-scheme:light;--ink:#18243b;--muted:#586981;--line:#dce4ef;--blue:#2068df}
*{box-sizing:border-box}html{scroll-behavior:smooth}body{margin:0;color:var(--ink);background:#edf2f8;font:16px/1.8 "Segoe UI","Microsoft YaHei UI",sans-serif}
.hero{background:#111b2e;color:#fff;padding:42px max(24px,calc((100vw - 1040px)/2)) 32px}.hero small{color:#85b2ff;letter-spacing:.15em}.hero strong{display:block;font-size:36px;line-height:1.3;margin:8px 0}.hero p{color:#bccbe0;margin:8px 0}
main{max-width:1100px;margin:26px auto 60px;background:#fff;padding:32px 48px 48px;border-radius:16px;box-shadow:0 8px 35px #172e4d0b}h1{font-size:26px;line-height:1.4;margin-top:0}h2{font-size:24px;border-top:1px solid var(--line);padding-top:28px;margin-top:38px;line-height:1.5}h3{font-size:19px;margin-top:26px}a{color:var(--blue);text-underline-offset:3px;overflow-wrap:anywhere}p,li{overflow-wrap:anywhere}li{margin:6px 0}code{font:14px/1.7 Consolas,monospace;background:#f0f4fa;padding:2px 5px;border-radius:4px;overflow-wrap:anywhere}pre{background:#111b2e;color:#dce9ff;padding:16px;border-radius:8px;overflow:auto}pre code{background:none;padding:0}table{width:100%;border-collapse:collapse;font-size:15px;margin:18px 0}th,td{text-align:left;padding:11px 13px;border:1px solid var(--line);vertical-align:top}th{background:#eff5ff;color:#203a64}tr:nth-child(even){background:#fafcff}blockquote{border-left:4px solid var(--blue);margin:16px 0;padding:10px 18px;background:#eff5ff}details{padding:14px 16px;border:1px solid var(--line);border-radius:9px;margin:20px 0}summary{cursor:pointer;font-weight:600}.screenshot{height:420px;max-width:360px;overflow:hidden;margin:16px auto;border-radius:8px}.screenshot img{width:360px;max-width:100%;height:auto;display:block}.end{font-size:13px;color:var(--muted);border-top:1px solid var(--line);padding-top:20px;margin-top:40px}strong{font-weight:650}
@media(max-width:700px){.hero{padding:28px 18px}.hero strong{font-size:30px}main{margin:12px;padding:22px 18px;border-radius:10px}h2{font-size:21px}table{font-size:13px}th,td{padding:8px}}
@media print{body{background:#fff;font-size:11pt}.hero{background:#fff;color:#111;padding:12px}.hero p,.hero small{color:#333}main{box-shadow:none;padding:0;margin:0;max-width:none}h2{break-after:avoid}tr{break-inside:avoid}details{display:none}a{color:#111}.end{margin-top:20px}}
'''
html = '<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta http-equiv="Content-Security-Policy" content="default-src \'none\'; img-src data:; style-src \'unsafe-inline\'; base-uri \'none\'"><title>ClashDock v1.2.0 · 使用手册</title><style>' + css + '</style></head><body><header class="hero"><small>CLASHDOCK · USER GUIDE</small><strong>下载之后，照着用就行。</strong><p>v1.2.0 · Windows x64 · 从第一次打开到日常排查</p></header><main>' + body + '<div class="end">ClashDock v1.2.0 正式版使用手册 · 本文无需联网即可阅读。访问发布页或反馈链接时才会打开 GitHub。软件不会因为阅读本文自动修改网络。</div></main></body></html>'
(ROOT / 'docs/使用手册.html').write_text(html, encoding='utf-8')
anchors = set(re.findall(r'id="([^"]+)"', html))
broken = [x for x in re.findall(r'href="#([^"]+)"', html) if x not in anchors]
if broken:
    raise SystemExit('Broken manual anchors: ' + ', '.join(broken))
print('Offline guide generated; internal navigation checked; no external images, scripts or fonts.')
