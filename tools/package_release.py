"""Build a clean, allowlisted portable release; never copy an installed user folder."""
from pathlib import Path
import argparse, hashlib, json, shutil, subprocess, zipfile

ROOT = Path(__file__).resolve().parents[1]
p = argparse.ArgumentParser()
p.add_argument('--desktop', type=Path, required=True)
p.add_argument('--bridge', type=Path, required=True)
p.add_argument('--binary-source-commit', default=None)
p.add_argument('--output', type=Path, default=ROOT / 'artifacts/release-v1.2.0')
args = p.parse_args()
out = args.output.resolve()
if not out.is_relative_to((ROOT / 'artifacts').resolve()):
    raise SystemExit('Release output must be below workspace artifacts/')
name = 'ClashDock-1.2.0-win-x64'
folder = out / name
if folder.exists():
    raise SystemExit('Refusing to mix with an existing package folder: choose a new output directory')
folder.mkdir(parents=True)
for source, target in [(args.desktop, 'ClashDock.exe'), (args.bridge, 'ClashDock.Bridge.exe')]:
    shutil.copy2(source, folder / target)
public_assets = {
    'EdgeExtension': ['manifest.json','popup.html','popup.css','popup.js','state.js','icon16.png','icon32.png','icon48.png','icon128.png'],
    'BridgeTools': ['Register-Bridge.ps1','Check-Bridge.ps1','Unregister-Bridge.ps1']
}
for directory, names in public_assets.items():
    (folder / directory).mkdir()
    for file in names:
        shutil.copy2(ROOT / directory / file, folder / directory / file)
for doc in ['README.md', '使用说明.md', 'EDGE_EXTENSION.md', 'DEVELOPMENT.md', 'VALIDATION.md', 'THIRD_PARTY_NOTICES.md', 'CHANGELOG.md']:
    shutil.copy2(ROOT / doc, folder / doc)
shutil.copy2(ROOT / 'docs/使用手册.html', folder / '使用手册.html')
(folder / 'docs/images').mkdir(parents=True)
shutil.copy2(ROOT / 'docs/images/edge-popup.png', folder / 'docs/images/edge-popup.png')
deps = json.loads((ROOT / 'obj/Release/net9.0-windows/win-x64/ClashDock.deps.json').read_text(encoding='utf-8'))
core_version = next(x.rsplit('/', 1)[1] for x in deps['libraries'] if x.startswith('runtimepack.Microsoft.NETCore.App.Runtime.win-x64/'))
desktop_version = next(x.rsplit('/', 1)[1] for x in deps['libraries'] if x.startswith('runtimepack.Microsoft.WindowsDesktop.App.Runtime.win-x64/'))
core_pack = ROOT / '.packages/microsoft.netcore.app.runtime.win-x64' / core_version
desktop_pack = ROOT / '.packages/microsoft.windowsdesktop.app.runtime.win-x64' / desktop_version
shutil.copy2(core_pack / 'LICENSE.TXT', folder / 'LICENSE.txt')
shutil.copy2(core_pack / 'THIRD-PARTY-NOTICES.TXT', folder / 'ThirdPartyNotices.txt')
(folder / 'licenses').mkdir()
shutil.copy2(desktop_pack / 'LICENSE', folder / 'licenses/WindowsDesktop-LICENSE.txt')
(folder / '开始使用.txt').write_text('''ClashDock v1.2.0 · Windows x64 便携版

1. 先将整个 ZIP 全部解压，不要在压缩包内运行。
2. 打开已配置节点／订阅的 Clash Verge / Verge Rev，自行确认规则模式。
3. 双击 ClashDock.exe；不需要安装 .NET。
4. 应用规则中搜索软件，或网站规则中添加域名；选择自动／直连／代理会自动保存应用。
5. 规则已加载仅说明内核核验，不保证公网出口已实测；必要时用“检查分流”。

完整说明：双击同目录“使用手册.html”。
Edge 可选：桌面 设置与说明 → Edge 通信桥检查／注册；再到 edge://extensions
加载解压缩的扩展，选择本目录 EdgeExtension 文件夹。
不要单独双击 ClashDock.Bridge.exe；它由 Edge 调用。

退出保留已经安装的规则，暂停才撤下规则。重新应用全部分流会先预览影响范围。
本包不含你的个人配置。更新时保留已有 settings.json、routing-intent.json（若存在）、
routing-intent.json.undo 与 ui-preferences.json，先从托盘退出旧程序再替换。
不自动修改系统代理、TUN、浏览器安全策略；没有商业代码签名，下载后先核对来源和校验值。

发布页：https://github.com/LeoLi006/ClashDock/releases/tag/v1.2.0
说明书：https://github.com/LeoLi006/ClashDock/blob/v1.2.0/使用说明.md
反馈：https://github.com/LeoLi006/ClashDock/issues
''', encoding='utf-8-sig')
commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
binary_commit = args.binary_source_commit or commit
changed = subprocess.check_output(['git','diff','--name-only',binary_commit,'HEAD','--','*.cs','*.csproj','app.manifest'],cwd=ROOT,text=True)
if changed.strip(): raise SystemExit('Runtime sources changed after binary build; rebuild before packaging')
(folder / 'BUILD_INFO.txt').write_text('ClashDock 1.2.0\nPlatform: win-x64, self-contained\nSource commit: ' + commit + '\nBinary source commit: ' + binary_commit + '\nTag: v1.2.0\nPersonal settings: not included\n', encoding='utf-8')
allowed_top = {'ClashDock.exe','ClashDock.Bridge.exe','README.md','使用说明.md','使用手册.html','EDGE_EXTENSION.md','DEVELOPMENT.md','VALIDATION.md','THIRD_PARTY_NOTICES.md','CHANGELOG.md','LICENSE.txt','ThirdPartyNotices.txt','开始使用.txt','BUILD_INFO.txt','SHA256SUMS.txt'}
allowed_children = {f'{directory}/{file}' for directory, names in public_assets.items() for file in names} | {'docs/images/edge-popup.png', 'licenses/WindowsDesktop-LICENSE.txt'}
files = sorted(x for x in folder.rglob('*') if x.is_file())
for file in files:
    rel = file.relative_to(folder).as_posix()
    if rel not in allowed_top | allowed_children:
        raise SystemExit('Unexpected package file: ' + rel)
    if any(part in {'.git','native-host','bin','obj'} for part in file.relative_to(folder).parts):
        raise SystemExit('Forbidden package directory')

def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest()
(folder / 'SHA256SUMS.txt').write_text(''.join(digest(f) + '  ' + f.relative_to(folder).as_posix() + '\n' for f in files), encoding='utf-8')
archive = out / (name + '.zip')
with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as z:
    for file in sorted(x for x in folder.rglob('*') if x.is_file()):
        z.write(file, name + '/' + file.relative_to(folder).as_posix())
with zipfile.ZipFile(archive) as z:
    if z.testzip() is not None: raise SystemExit('ZIP CRC failure')
guide = out / 'ClashDock-1.2.0-User-Guide.html'
shutil.copy2(folder / '使用手册.html', guide)
(out / 'SHA256SUMS.txt').write_text(''.join(digest(f) + '  ' + f.name + '\n' for f in [archive, guide]), encoding='utf-8')
report = {'archive':archive.name,'files':len(files)+1,'sourceCommit':commit,'archiveSHA256':digest(archive),'zipCRC':'passed','allowlist':'passed','personalConfiguration':'excluded by construction'}
(out / 'package-check.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report))
