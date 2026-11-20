#!/usr/bin/env bash
# =============================================================================
#  shader-review 自动扫描（只筛候选，结论必须人工确认）
#
#  用法：
#     bash scan.sh <文件或目录...>          # 默认跳过白名单里的存量文件
#     bash scan.sh <目标> --legacy          # 连存量文件一起扫（做审计用）
#
#  白名单：whitelist/legacy-paths.txt
#     - 只做相对 Assets/ 的完整路径精确匹配；绝不按 basename 放行
#
#  ⚠️ 生效范围（大前提）：只对
#       Assets/Shaders/ShaderTheseus/**
#       Assets/Shaders/ShaderLibrary/**
#     下的 shader 起作用。范围外的 shader 一律不检查、不报告。
# =============================================================================
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SKILL_DIR="$(dirname "$SCRIPT_DIR")"
WL_FILE="$SKILL_DIR/whitelist/legacy-paths.txt"
# shellcheck source=_scope.sh
source "$SCRIPT_DIR/_scope.sh"

WITH_LEGACY=0
TARGETS=()
for a in "$@"; do
  case "$a" in
    --legacy) WITH_LEGACY=1 ;;
    -h|--help) sed -n '2,17p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) TARGETS+=("$a") ;;
  esac
done
[ ${#TARGETS[@]} -eq 0 ] && TARGETS=(".")

hr() { printf '%s\n' "--------------------------------------------------------------"; }

# ---------- 白名单 ----------
declare -A WL_PATH=()
WL_TOTAL=0
if [ -f "$WL_FILE" ]; then
  while IFS= read -r line; do
    line="${line%$'\r'}"
    case "$line" in ''|'#'*) continue ;; esac
    WL_TOTAL=$((WL_TOTAL+1))
    WL_PATH["$line"]=1
  done < "$WL_FILE"
fi

# 返回 0 = 白名单完整路径精确命中
is_legacy() {
  local rel
  rel="$(sr_normalize "$1")"
  [ -n "${WL_PATH[$rel]:-}" ]
}

# ---------- P001 目标范围预检 ----------
# collect() 只收 shader 文件，因此 P001 要在扩展名过滤之前按路径独立判断；
# 这样 ShaderLibrary 下的 .mat / .meta / 未来其他文件类型也不会漏掉。
P001_TARGETS=()
for t in "${TARGETS[@]}"; do
  # 同时覆盖 ShaderLibrary 本身、其内部文件，以及 Assets/、Shaders/ 等祖先目录。
  if sr_target_relevant_to "$t" "$SR_SHADERLIB_ROOT"; then
    P001_TARGETS+=("$t")
  fi
done

# ---------- 收集待做代码规则检查的 shader 文件 ----------
collect() {
  local t
  for t in "${TARGETS[@]}"; do
    if [ -d "$t" ]; then
      find "$t" -type f \( -name '*.shader' -o -name '*.hlsl' -o -name '*.cginc' \
                           -o -name '*.compute' -o -name '*.glsl' -o -name '*.hlslinc' \) 2>/dev/null
    elif [ -f "$t" ]; then
      printf '%s\n' "$t"
    fi
  done | sort -u
}

SCAN=(); LEGACY_FILES=(); OUT_SCOPE=()
while IFS= read -r f; do
  [ -z "$f" ] && continue
  # ① 生效范围（大前提）过滤
  if ! sr_in_scope "$f"; then
    OUT_SCOPE+=("$f")
    continue
  fi
  # ② 存量白名单
  if is_legacy "$f" && [ $WITH_LEGACY -eq 0 ]; then
    LEGACY_FILES+=("$f")
  else
    SCAN+=("$f")
  fi
done < <(collect)

echo "=============================================================="
echo " shader-review 扫描"
echo "   生效范围          : $(sr_scope_desc)"
echo "   范围外跳过        : ${#OUT_SCOPE[@]}"
echo "   白名单条目        : $WL_TOTAL"
echo "   目标文件（待检查）: ${#SCAN[@]}"
echo "   白名单跳过（存量）: ${#LEGACY_FILES[@]}"
[ $WITH_LEGACY -eq 1 ] && echo "   ⚠️  --legacy：存量文件也纳入扫描（仅审计，不要据此改动）"
echo "=============================================================="

if [ ${#OUT_SCOPE[@]} -gt 0 ] && [ ${#SCAN[@]} -eq 0 ] && [ ${#LEGACY_FILES[@]} -eq 0 ]; then
  echo
  echo "  ⛔ 目标文件全部不在生效范围内（大前提）："
  echo "       $(sr_scope_desc)"
  echo "     本 skill 对范围外的 shader 不检查、不报告、不给建议。"
  echo "     若认为该文件应在范围内，请先修改 tools/_scope.sh 的 SR_SCOPE_ROOTS。"
  exit 0
fi

# =============================================================================
# P001  Assets/Shaders/ShaderLibrary/** 有任何改动必须在群里 @朱瀚
#   —— 放在白名单空判断之前，因为受保护目录本身属于存量白名单。
# =============================================================================
if [ ${#P001_TARGETS[@]} -gt 0 ]; then
  echo
  echo "=============================================================="
  echo " ⛔ P001  本次范围含 Assets/Shaders/ShaderLibrary/"
  echo "   → 【若该目录内有任何新增/修改/删除/移动/重命名】"
  echo "      必须先在群里 @朱瀚(Kirk) 确认，才能修改 / 提交 / 合入"
  echo "   → open_id: ou_e2f1ba04362e32328c6b236b72351b10"
  echo "   → 详见 rules/P001-shaderlibrary-approval.md（含群消息模板）"
  echo "=============================================================="
  printf '  %s\n' "${P001_TARGETS[@]}" | sort -u
  echo
  echo "  ⚠️ 扫描范围命中不等于实际改动。请从 Git 根目录检查完整改动集："
  echo "     { git diff --name-only; git diff --cached --name-only;"
  echo "       git ls-files --others --exclude-standard; }"
  echo "       | tr '\\\\' '/' | grep -E '(^|/)Assets/Shaders/ShaderLibrary/'"
fi

if [ ${#SCAN[@]} -eq 0 ]; then
  echo
  echo "  全部命中白名单 —— 没有需要检查的新文件。"
  echo "  （确需审计存量，加 --legacy 参数）"
  exit 0
fi

# grep 封装：只扫 SCAN 里的文件
sgrep() { grep -nHE "$1" "${SCAN[@]}" 2>/dev/null; }

# =============================================================================
# R001  #define 宏值写了 0
# =============================================================================
echo
echo "=============================================================="
echo " R001  #define 宏值写了 0（0 仍被 defined() 视为开启）"
echo "=============================================================="
sgrep '^[[:space:]]*#[[:space:]]*define[[:space:]]+[A-Za-z_][A-Za-z0-9_]*[[:space:]]+0[[:space:]]*(//.*)?$' \
  || echo "  ✅ 无"
echo
echo "  ↑ 逐条判断：浮点参数(0.00)/层参数 → 放行；开关型宏 → 报 🔴"

echo
hr
echo " R001b #define 宏值写了 1（新文件建议改成裸定义）"
hr
cnt=$(sgrep '^[[:space:]]*#[[:space:]]*define[[:space:]]+[A-Za-z_][A-Za-z0-9_]*[[:space:]]+1[[:space:]]*(//.*)?$' | wc -l)
if [ "$cnt" -eq 0 ]; then echo "  ✅ 无"; else
  echo "  数量：$cnt"
  sgrep '^[[:space:]]*#[[:space:]]*define[[:space:]]+[A-Za-z_][A-Za-z0-9_]*[[:space:]]+1[[:space:]]*(//.*)?$' | sed 's/^/        /'
fi

# =============================================================================
# R002  切线符号被乘了两次
# =============================================================================
echo
hr
echo " R002  切线符号被乘了两次（负缩放矫正相互抵消）"
hr
echo "  特征（两行连招）："
echo "        float tangentSign = (unity_WorldTransformParams.w >= 0.0) ? 1.0 : -1.0;"
echo "        tangentSign = tangentSign * input.tangentWS.w;      // ← 第二次乘"
echo "  前提：顶点已把符号乘进 tangentWS.w（GetOddNegativeScale / GetVertexNormalInputs）"
echo

n2=0
for f in "${SCAN[@]}"; do
  premult=0
  grep -qE 'GetVertexNormalInputs' "$f" && premult=1
  if grep -qE 'GetOddNegativeScale' "$f" && \
     grep -qE '(tangentWS|tangent)[[:space:]]*=[[:space:]]*(half|float|real)?4[[:space:]]*\([^)]*(sign|sgn)' "$f"; then premult=1; fi
  double=0
  if grep -A3 -E 'tangentSign[[:space:]]*=[[:space:]]*\([[:space:]]*unity_WorldTransformParams\.w' "$f" \
     | grep -qE 'tangentSign[[:space:]]*=[[:space:]]*tangentSign[[:space:]]*\*[[:space:]]*[A-Za-z_][A-Za-z0-9_]*\.tangentWS\.w'; then double=1; fi
  if [ $double -eq 1 ] && [ $premult -eq 1 ]; then
    n2=$((n2+1))
    echo "  🔴 $f"
    grep -nE 'GetVertexNormalInputs|GetOddNegativeScale|unity_WorldTransformParams\.w[[:space:]]*>=[[:space:]]*0\.0|tangentSign[[:space:]]*=[[:space:]]*tangentSign' "$f" | sed 's/^/        /'
    echo
  fi
done
[ $n2 -eq 0 ] && echo "  ✅ 无" || echo "  命中文件数：$n2"

echo
hr
echo " R002b 低置信：同文件多次出现 GetOddNegativeScale（可能有别的双重乘形式）"
hr
nb=0
for f in "${SCAN[@]}"; do
  c=$(grep -c 'GetOddNegativeScale' "$f" 2>/dev/null || echo 0)
  if [ "$c" -ge 2 ]; then echo "  ⚠️  $f  (x$c)"; nb=$((nb+1)); fi
done
[ $nb -eq 0 ] && echo "  ✅ 无"

echo
hr
echo " R002c 漏乘检查：顶点原样传 tangent.w，且文件内没有任何符号矫正"
hr
nc=0
for f in "${SCAN[@]}"; do
  if grep -qE '(tangentWS|tangent)[[:space:]]*=[[:space:]]*(half|float|real)?4[[:space:]]*\([^)]*\.tangent\.w' "$f" \
     && ! grep -q 'GetOddNegativeScale' "$f" \
     && ! grep -q 'GetVertexNormalInputs' "$f" \
     && ! grep -qE 'unity_WorldTransformParams\.w[[:space:]]*>=[[:space:]]*0\.0' "$f" \
     && grep -qE 'cross\(' "$f" ; then
    echo "  ⚠️  $f"; nc=$((nc+1))
  fi
done
[ $nc -eq 0 ] && echo "  ✅ 无"

# =============================================================================
if [ ${#LEGACY_FILES[@]} -gt 0 ]; then
  echo
  hr
  echo " 白名单跳过（存量，既往不咎，不要改动）：${#LEGACY_FILES[@]} 个"
  hr
  printf '  %s\n' "${LEGACY_FILES[@]}" | head -20
  [ ${#LEGACY_FILES[@]} -gt 20 ] && echo "  ...（其余 $(( ${#LEGACY_FILES[@]} - 20 )) 个省略）"
fi

echo
hr
echo " 范围外（不检查，大前提）：${#OUT_SCOPE[@]} 个"
if [ ${#OUT_SCOPE[@]} -gt 0 ]; then
  hr
  printf '  %s\n' "${OUT_SCOPE[@]}" | head -10
  [ ${#OUT_SCOPE[@]} -gt 10 ] && echo "  ...（其余 $(( ${#OUT_SCOPE[@]} - 10 )) 个省略）"
fi

echo
echo "=============================================================="
echo " 扫描结束。候选必须人工读上下文确认，误报提示见各 rules/*.md"
echo "=============================================================="
