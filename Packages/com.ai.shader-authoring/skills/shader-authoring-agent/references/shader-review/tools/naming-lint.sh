#!/usr/bin/env bash
# =============================================================================
# naming-lint.sh —— 检查 shader 的 Properties 命名是否符合 uniform 命名规范
#
#   规范表: reference/uniform-index.md
#   黑名单: reference/variant-blacklist.txt（非规范写法 -> 规范名）
#   白名单: reference/canonical-names.txt（已认可的规范名，抑制误报）
#   存量  : whitelist/legacy-paths.txt（默认跳过，存量既往不咎）
#
# ⚠️ 生效范围（大前提）：只对 Assets/Shaders/ShaderTheseus/** 和
#    Assets/Shaders/ShaderLibrary/** 下的 shader 起作用；范围外一律不检查。
#
# 用法：
#   bash tools/naming-lint.sh <shader 文件或目录> ...
#   bash tools/naming-lint.sh <目标> --legacy     # 连存量一起查（审计用，不阻断）
#
# 退出码：0 = 无错误 / 1 = 有错误
# =============================================================================
set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SKILL="$(cd "$HERE/.." && pwd)"
BLACKLIST="$SKILL/reference/variant-blacklist.txt"
CANONICAL="$SKILL/reference/canonical-names.txt"
TYPOS="$SKILL/reference/typos.txt"
WL_FILE="$SKILL/whitelist/legacy-paths.txt"

LEGACY=0
TARGETS=()
for a in "$@"; do
  case "$a" in
    --legacy) LEGACY=1 ;;
    -h|--help) sed -n '2,20p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) TARGETS+=("$a") ;;
  esac
done

if [ ${#TARGETS[@]} -eq 0 ]; then
  echo "用法: bash tools/naming-lint.sh <shader 文件或目录> [--legacy]" >&2
  exit 2
fi

short() { echo "${1#*Assets/Shaders/}"; }

# shellcheck source=_scope.sh
source "$HERE/_scope.sh"

# ---------------- 数据加载 ----------------
declare -A BLACK=()
if [ -f "$BLACKLIST" ]; then
  while IFS=$'\t' read -r bad good cnt; do
    bad="${bad%$'\r'}"; good="${good%$'\r'}"; cnt="${cnt%$'\r'}"
    case "$bad" in ''|'#'*) continue ;; esac
    BLACK["$bad"]="$good（$cnt 个文件在用）"
  done < "$BLACKLIST"
fi

declare -A CANON=()
if [ -f "$CANONICAL" ]; then
  while IFS= read -r n; do
    n="${n%$'\r'}"
    case "$n" in ''|'#'*) continue ;; esac
    CANON["$n"]=1
  done < "$CANONICAL"
fi

# 手工维护的拼写错误 / 同义名（优先级高于 BLACK）
if [ -f "$TYPOS" ]; then
  while IFS=$'\t' read -r bad good note; do
    bad="${bad%$'\r'}"; good="${good%$'\r'}"; note="${note%$'\r'}"
    case "$bad" in ''|'#'*) continue ;; esac
    BLACK["$bad"]="$good（${note:-同义/拼写}）"
  done < "$TYPOS"
fi

declare -A WL_PATH=()
if [ -f "$WL_FILE" ]; then
  while IFS= read -r line; do
    line="${line%$'\r'}"
    case "$line" in ''|'#'*) continue ;; esac
    WL_PATH["$line"]=1
  done < "$WL_FILE"
fi

is_legacy() {
  local rel
  rel="$(sr_normalize "$1")"
  [ -n "${WL_PATH[$rel]:-}" ]
}

# ---------------- 收集文件 ----------------
FILES=()
for t in "${TARGETS[@]}"; do
  if [ -d "$t" ]; then
    while IFS= read -r f; do FILES+=("$f"); done \
      < <(find "$t" -type f \( -name '*.shader' -o -name '*.hlsl' -o -name '*.cginc' -o -name '*.hlslinc' \) | sort)
  elif [ -f "$t" ]; then
    FILES+=("$t")
  else
    echo "⚠️  找不到: $t" >&2
  fi
done

if [ ${#FILES[@]} -eq 0 ]; then
  echo "⚠️  没有可检查的 .shader/.hlsl/.cginc/.hlslinc 文件"
  exit 0
fi

# ---- 生效范围（大前提）----
OUT_SCOPE=()
KEPT=()
for f in "${FILES[@]}"; do  if sr_in_scope "$f"; then KEPT+=("$f"); else OUT_SCOPE+=("$f"); fi
done
FILES=()
[ ${#KEPT[@]} -gt 0 ] && FILES=("${KEPT[@]}")
OUT_SCOPE_N=${#OUT_SCOPE[@]}

if [ ${#FILES[@]} -eq 0 ]; then
  echo "⛔ 目标全部不在生效范围内（大前提）：$(sr_scope_desc)"
  echo "   本 skill 对范围外的 shader 不检查、不报告、不给建议。"
  exit 0
fi

if [ "$LEGACY" != "1" ]; then
  KEPT=(); SKIPPED=0
  for f in "${FILES[@]}"; do
    if is_legacy "$f"; then SKIPPED=$((SKIPPED+1)); else KEPT+=("$f"); fi
  done
  FILES=()
  [ ${#KEPT[@]} -gt 0 ] && FILES=("${KEPT[@]}")
  [ "$SKIPPED" -gt 0 ] && echo "ℹ️  跳过白名单存量 shader/HLSL 文件 $SKIPPED 个（加 --legacy 可强制包含）"
fi

if [ ${#FILES[@]} -eq 0 ]; then
  echo "✅ 没有需要检查的新 shader/HLSL 文件（全部属于白名单存量）"
  exit 0
fi

# ---------------- 提取 Properties 与 HLSL 声明 ----------------
# Python 解析支持 [HDR]/[Toggle]/多 Attribute、Properties 同行写法，及
# TEXTURE*/uniform/CBUFFER 成员。输出：<名字> TAB <来源>。
extract_names() {
  python - "$1" <<'PY'
import re, sys
p = sys.argv[1]
text = open(p, encoding='utf-8-sig', errors='replace').read()
found = []

def add(name, source):
    if name.startswith('_') and (name, source) not in found:
        found.append((name, source))

# 去注释后找 Properties 块；字符串中的花括号不影响项目现有声明格式。
clean = re.sub(r'/\*.*?\*/|//[^\n]*', '', text, flags=re.S)
m = re.search(r'\bProperties\s*\{', clean)
if m:
    start = m.end(); i = start; depth = 1
    while i < len(clean) and depth:
        depth += (clean[i] == '{') - (clean[i] == '}')
        i += 1
    block = clean[start:i-1] if depth == 0 else clean[start:]
    prop = re.compile(r'(?:\[[^\]]*\]\s*)*(_[A-Za-z][A-Za-z0-9_]*)\s*\(')
    for mm in prop.finditer(block): add(mm.group(1), 'Properties')

# HLSL 资源宏。
for mm in re.finditer(r'\b(?:TEXTURE(?:1D|2D|3D|CUBE|2D_ARRAY|CUBE_ARRAY)?|RW_TEXTURE(?:1D|2D|3D)?|SAMPLER(?:_CMP)?)\s*\(\s*(_[A-Za-z][A-Za-z0-9_]*)', clean):
    add(mm.group(1), 'HLSL')
# 普通 uniform 与 CBUFFER/全局参数声明；限定常见 HLSL 类型，避免函数/struct 误判。
type_re = r'(?:half|float|real|fixed|int|uint|bool)(?:[1-4](?:x[1-4])?)?'
for mm in re.finditer(r'(?m)^\s*(?:uniform\s+)?' + type_re + r'\s+(_[A-Za-z][A-Za-z0-9_]*)\b', clean):
    add(mm.group(1), 'HLSL')
for name, source in found:
    print(name + '\t' + source)
PY
}

# ---------------- 检测规则 ----------------
PINYIN_RE='_(rongjie|tiaojie|liangdu|yanse|qiangdu|fanwei|waiyan|neiyan|bianyuan|zhongxin|guangzhao|saoguang|liangbian|sudu|weizhi|juli|jiaodu|fudu|pinlv|yanzheng|shunxu|sanshe)'
PINYIN_OK_RE='^_(Sanshe|Sanshe2)_'
UNDERSCORE_RE='^_[A-Za-z0-9]+_[A-Za-z0-9]+'
LOWER_HINT_UNDERSCORE_WORDS='TiSP|TiSp|_Tiling_Speed|_Tiling_Offset|_Transform|_param|_params|_Dissolve_Tex|_Rim_|_Cube_|_Metal_|_Dis_|_Normal_TiSp|_Light_|_Ambient_|_Base_|_Diff_'
UNDERSCORE_EXCEPT='^(_.*_ST|_.*_low|_Sanshe_.*|_Sanshe2_.*|_LG_.*|_AO_.*)$'
UPPER_EXCEPT='^(_DISSOLVE_ON|_USE_DISSOLVE|_LASER_ON|_ALPHA_CLIP|_EMISSIVE_BREATHE|_INVERT_DISSOLVE_DIR|_SWITCH_DISSOLVE_MODE_ON|_USE_GLITTERY|_RENDER_QUALITY_LOW|_SKIN_SSSS_ON|_DIRECT_SANSHE|_USE_.*|_ENABLE_.*|.*_ON)$'

TOTAL_ERR=0
TOTAL_WARN=0
REPORT=()
err()  { TOTAL_ERR=$((TOTAL_ERR+1));  REPORT+=("  ❌ [$2] $1"); }
warn() { TOTAL_WARN=$((TOTAL_WARN+1)); REPORT+=("  ⚠️  [$2] $1"); }

for f in "${FILES[@]}"; do
  rel="$(short "$f")"
  props="$(extract_names "$f")"
  [ -z "$props" ] && continue

  declare -A SEEN=()
  while IFS=$'\t' read -r name _source; do
    [ -z "${name:-}" ] && continue

    # R1 黑名单拼写（大小写变体）
    if [ -n "${BLACK[$name]:-}" ]; then
      err "$name  →  规范名 ${BLACK[$name]}" "$rel"
    fi

    # R2 拼音（已被黑名单报过的同义名不重复报）
    if [ -z "${BLACK[$name]:-}" ] \
       && echo "$name" | grep -qiE "$PINYIN_RE" && ! echo "$name" | grep -qE "$PINYIN_OK_RE"; then
      err "$name  →  禁止拼音命名" "$rel"
    fi

    # R3 词中下划线
    if echo "$name" | grep -qE "$UNDERSCORE_RE" \
       && ! echo "$name" | grep -qE "$UNDERSCORE_EXCEPT" \
       && [ -z "${CANON[$name]:-}" ] \
       && echo "$name" | grep -qE "$LOWER_HINT_UNDERSCORE_WORDS"; then
      warn "$name  →  词中下划线，建议写成一个词（_dissolve_DirSpeed → _dissolveDirSpeed）" "$rel"
    fi

    # R4 属性名全大写（Toggle keyword 名除外）
    if echo "$name" | grep -qE '^_[A-Z0-9_]+$' \
       && ! echo "$name" | grep -qE "$UPPER_EXCEPT" \
       && [ -z "${CANON[$name]:-}" ]; then
      warn "$name  →  属性名全大写（除非它就是 Toggle 的 keyword 名）" "$rel"
    fi

    # R5 Pascal 开头（非规范名、非黑名单）
    base="${name#_}"
    first="${base:0:1}"
    if [ "$first" != "$(echo "$first" | tr 'A-Z' 'a-z')" ] \
       && echo "$base" | grep -qE '^[A-Z][a-z]' \
       && [ -z "${BLACK[$name]:-}" ] && [ -z "${CANON[$name]:-}" ]; then
      warn "$name  →  Pascal 开头，规范为 lowerCamel: _$(echo "${base:0:1}" | tr 'A-Z' 'a-z')${base:1}" "$rel"
    fi

    # R6 同文件内同一功能出现两种写法
    key="$(echo "$base" | tr 'A-Z' 'a-z')"
    if [ -n "${SEEN[$key]:-}" ] && [ "${SEEN[$key]}" != "$name" ]; then
      err "同文件内同一功能两种写法: ${SEEN[$key]}  vs  $name" "$rel"
    else
      SEEN["$key"]="$name"
    fi
  done <<< "$props"
  unset SEEN

  # R7 keyword 命名
  while IFS= read -r kw; do
    [ -z "${kw:-}" ] && continue
    case "$kw" in
      _ENABLE_*|_SWITCH_*)
        err "keyword $kw  →  禁用前缀，改用 _USE_XXX 或 _XXX_ON" "$rel" ;;
      _USE_FLOWLIGHT|_USE_FLOW_LIGHT_ON|_USE_FLOW_LIGHT2)
        warn "keyword $kw  →  规范为 _USE_FLOW_LIGHT" "$rel" ;;
      _USE_GLITTER|_LG_ON|_USE_GLITTER_ON)
        warn "keyword $kw  →  规范为 _USE_GLITTERY" "$rel" ;;
      _USE_LASER_ON)
        warn "keyword $kw  →  规范为 _LASER_ON" "$rel" ;;
      _USE_DISSOLVE)
        warn "keyword $kw  →  规范为 _DISSOLVE_ON" "$rel" ;;
    esac
  done < <(grep -hoE '#pragma[[:space:]]+(shader_feature|multi_compile)[A-Za-z_]*(_local)?[[:space:]]+[A-Za-z0-9_ ]+' "$f" 2>/dev/null \
           | sed -E 's/#pragma[[:space:]]+[A-Za-z_]+[[:space:]]+//' | tr ' ' '\n' \
           | grep -vE '^$|^_$' || true)
done

# ---------------- 输出 ----------------
echo "=============================================="
echo " Uniform 命名检查"
echo " 生效范围: $(sr_scope_desc)"
[ "${OUT_SCOPE_N:-0}" -gt 0 ] && echo " 范围外跳过: ${OUT_SCOPE_N} 个（大前提，不检查）"
echo " 目标 : ${#FILES[@]} 个 shader/HLSL 文件"
[ "$LEGACY" = "1" ] && echo " 模式 : --legacy（存量审计，不阻断）"
echo "=============================================="

if [ ${#REPORT[@]} -eq 0 ]; then
  echo " ✅ 未发现问题"
  exit 0
fi

printf '%s\n' "${REPORT[@]}"
echo "----------------------------------------------"
echo " ❌ 错误 $TOTAL_ERR   ⚠️  提示 $TOTAL_WARN"
echo " 规范表 : $SKILL/reference/uniform-index.md"

[ "$LEGACY" = "1" ] && exit 0
[ "$TOTAL_ERR" -gt 0 ] && exit 1
exit 0
