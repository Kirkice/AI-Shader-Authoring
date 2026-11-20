#!/usr/bin/env bash
# =============================================================================
# scan-uniformes.sh —— 统计 Properties 命名；--update 生成“人工确认候选”
#
# 用法：
#   bash tools/scan-uniformes.sh <shader 根目录>
#   bash tools/scan-uniformes.sh <Character 根目录> --update
#
# 安全约束：
# - 普通统计可扫描任意目录，不写文件
# - --update 只接受 Assets/Shaders/ShaderTheseus/Character
# - --update 只写 variant-candidates.txt，不覆盖人工批准的 canonical-names.txt、
#   variant-blacklist.txt 和 typos.txt
# =============================================================================
set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SKILL="$(cd "$HERE/.." && pwd)"
REF="$SKILL/reference"
# shellcheck source=_scope.sh
source "$HERE/_scope.sh"

ROOT=""; UPDATE=0
for a in "$@"; do
  case "$a" in
    --update) UPDATE=1 ;;
    -h|--help) sed -n '2,15p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) ROOT="$a" ;;
  esac
done
if [ -z "$ROOT" ] || [ ! -d "$ROOT" ]; then
  echo "用法: bash tools/scan-uniformes.sh <shader 根目录> [--update]" >&2
  exit 2
fi
if [ "$UPDATE" -eq 1 ] && [ "$(sr_normalize "$ROOT")" != "$SR_CHARACTER_ROOT" ]; then
  echo "⛔ --update 只允许使用 Assets/$SR_CHARACTER_ROOT，避免范围外数据污染正式索引。" >&2
  echo "   当前: $(sr_normalize "$ROOT")" >&2
  exit 2
fi

PY=""
for c in python python3 py; do command -v "$c" >/dev/null 2>&1 && { PY="$c"; break; }; done
[ -z "$PY" ] && { echo "需要 python" >&2; exit 2; }

export SHADER_ROOT="$ROOT" REF_DIR="$REF" DO_UPDATE="$UPDATE" PYTHONIOENCODING=utf-8
"$PY" - <<'PYEOF'
import os, re, collections
root = os.environ['SHADER_ROOT'].replace('\\', '/')
ref = os.environ['REF_DIR'].replace('\\', '/')
do_update = os.environ.get('DO_UPDATE') == '1'
prop_re = re.compile(r'(?:\[[^\]]*\]\s*)*(_[A-Za-z][A-Za-z0-9_]*)\s*\(\s*"([^"]*)"\s*,\s*([^)]*(?:\([^)]*\))?[^)]*)\)')
rows=[]; files=0
for dirpath, _, names in os.walk(root):
    for fn in names:
        if not fn.endswith('.shader'): continue
        p=os.path.join(dirpath,fn); files += 1
        try: txt=open(p,encoding='utf-8-sig',errors='replace').read()
        except OSError: continue
        txt=re.sub(r'/\*.*?\*/|//[^\n]*','',txt,flags=re.S)
        m=re.search(r'\bProperties\s*\{',txt)
        if not m: continue
        i=m.end(); depth=1
        while i<len(txt) and depth:
            depth += (txt[i]=='{')-(txt[i]=='}'); i+=1
        block=txt[m.end():i-1] if depth==0 else txt[m.end():]
        rel=os.path.relpath(p,root).replace('\\','/')
        for mm in prop_re.finditer(block):
            rows.append(dict(file=rel,name=mm.group(1),display=mm.group(2),type=mm.group(3).strip()))

cnt=collections.Counter(r['name'] for r in rows)
norm=collections.defaultdict(list)
for n in cnt: norm[n.lstrip('_').lower()].append(n)
groups=sorted(norm.items(),key=lambda kv:(-max(cnt[n] for n in kv[1]),kv[0]))
print(f'shader 文件 : {files}')
print(f'属性声明    : {len(rows)}')
print(f'唯一 uniform: {len(cnt)}')
print('\n== 大小写候选冲突（前 30 组；不自动判定语义）==')
conflicts=[]
for key,names in groups:
    if len(names)<2: continue
    names=sorted(names,key=lambda n:(-cnt[n],n)); conflicts.append((key,names))
for _,names in conflicts[:30]: print('  '+', '.join(f'{n}({cnt[n]})' for n in names))
print(f'\n候选冲突组 {len(conflicts)} 个')
print('\n== 最高频 uniform（前 25）==')
for n,c in cnt.most_common(25):
    disp=sorted({r['display'] for r in rows if r['name']==n})
    print(f'  {c:4d}  {n:34s} {disp[:1]}')

if do_update:
    os.makedirs(ref,exist_ok=True)
    out=os.path.join(ref,'variant-candidates.txt')
    with open(out,'w',encoding='utf-8',newline='\n') as f:
        f.write('# 自动生成的大小写冲突候选；不是黑名单，必须人工确认语义后再更新批准文件。\n')
        f.write('# 格式：归一化键 TAB 名称:次数:DisplayName（同文件共存时标记 COEXIST）\n')
        byfile=collections.defaultdict(set)
        for r in rows: byfile[r['file']].add(r['name'])
        for key,names in conflicts:
            coexist=any(sum(n in vals for n in names)>1 for vals in byfile.values())
            parts=[]
            for n in names:
                displays='|'.join(sorted({r['display'] for r in rows if r['name']==n}))
                parts.append(f'{n}:{cnt[n]}:{displays}')
            f.write(key+'\t'+('COEXIST\t' if coexist else '')+'\t'.join(parts)+'\n')
    print(f'\n✅ 已刷新候选 {out}')
    print('ℹ️  未覆盖 canonical-names.txt / variant-blacklist.txt / typos.txt')
PYEOF
