#!/usr/bin/env python3
"""Render docs/backlog.md to a single HTML page (the "Rogue Stars Backlog" artifact). Usage: backlog_page.py out.html"""
import html, re, sys, pathlib
src = pathlib.Path(__file__).resolve().parents[1].joinpath('docs/backlog.md').read_text().splitlines()
def inline(t):
    t = html.escape(t)
    t = re.sub(r'\*\*(.+?)\*\*', r'<b>\1</b>', t)
    return re.sub(r'`(.+?)`', r'<code>\1</code>', t)
out, i = [], 0
while i < len(src):
    l = src[i]
    if l.startswith('# '): out.append(f'<h1>{inline(l[2:])}</h1>')
    elif l.startswith('## '): out.append(f'<h2>{inline(l[3:])}</h2>')
    elif l.startswith('|'):
        rows = []
        while i < len(src) and src[i].startswith('|'):
            rows.append([c.strip() for c in src[i].strip().strip('|').split('|')]); i += 1
        head, body = rows[0], [r for r in rows[2:]]
        t = '<div class="ts"><table><tr>' + ''.join(f'<th>{inline(c)}</th>' for c in head) + '</tr>'
        for r in body:
            t += f'<tr id="{html.escape(r[0])}">' + ''.join(f'<td>{inline(c)}</td>' for c in r) + '</tr>'
        out.append(t + '</table></div>'); continue
    elif re.match(r'^\d+\. ', l) or l.startswith('- '):
        tag = 'ol' if l[0].isdigit() else 'ul'; items = []
        while i < len(src) and (re.match(r'^\d+\. ', src[i]) or src[i].startswith('- ')):
            items.append(re.sub(r'^(\d+\. |- )', '', src[i])); i += 1
        out.append(f'<{tag}>' + ''.join(f'<li>{inline(x)}</li>' for x in items) + f'</{tag}>'); continue
    elif l.strip(): out.append(f'<p>{inline(l)}</p>')
    i += 1
css = '''<title>Rogue Stars Backlog</title>
<meta name="claude:viewport-fit-sides" content="cover">
<style>
:root{--bg:#f5f3f8;--card:#fff;--fg:#1d1a26;--mut:#676377;--line:#e2deea;--soft:#efebf8}
@media (prefers-color-scheme:dark){:root:not([data-theme="light"]){--bg:#14121b;--card:#1d1a26;--fg:#ece9f3;--mut:#a29eb3;--line:#2e2a3a;--soft:#24203a;color-scheme:dark}}
:root[data-theme="dark"]{--bg:#14121b;--card:#1d1a26;--fg:#ece9f3;--mut:#a29eb3;--line:#2e2a3a;--soft:#24203a;color-scheme:dark}
body{background:var(--bg);color:var(--fg);font:15px/1.55 system-ui,sans-serif;padding-left:max(16px,env(safe-area-inset-left,0px));padding-right:max(16px,env(safe-area-inset-right,0px));padding-block:24px 48px}
main{max-width:1000px;margin:0 auto}h1{font-size:28px;margin:0 0 6px}h2{font-size:20px;margin:28px 0 8px}p{max-width:80ch}
.ts{overflow-x:auto;border:1px solid var(--line);border-radius:10px;background:var(--card);margin:10px 0}
table{border-collapse:collapse;width:100%;font-size:14px}th,td{padding:8px 10px;border-bottom:1px solid var(--line);text-align:left;vertical-align:top}th{background:var(--soft);color:var(--mut);font-size:13px}
tr:last-child td{border-bottom:0}td:first-child{font-weight:700;white-space:nowrap}code{background:var(--soft);padding:1px 5px;border-radius:5px;font-size:.9em}
</style>'''
pathlib.Path(sys.argv[1]).write_text(css + '<main>' + '\n'.join(out) + '</main>')
