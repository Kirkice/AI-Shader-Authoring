#!/usr/bin/env bash
# =============================================================================
# 重新生成生效范围内的存量白名单（相对 Assets/ 的完整路径）
#
# 用法：
#   bash tools/gen_whitelist.sh <Assets 目录> [--dry]
#
# 注意：正式写入会将白名单重建为当前两个生效范围内的全部现有 shader。
# 仅在新增 shader 已通过审查并正式上线后执行；日常先用 --dry。
# =============================================================================
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SKILL_DIR="$(dirname "$SCRIPT_DIR")"
OUT="$SKILL_DIR/whitelist/legacy-paths.txt"
# shellcheck source=_scope.sh
source "$SCRIPT_DIR/_scope.sh"

ASSETS="${1:-}"
DRY=0
[ "${2:-}" = "--dry" ] && DRY=1
if [ -z "$ASSETS" ] || [ ! -d "$ASSETS" ]; then
  echo "用法: bash tools/gen_whitelist.sh <Assets 目录> [--dry]" >&2
  exit 1
fi

mkdir -p "$SKILL_DIR/whitelist"
tmp="$(mktemp)"
old="$(mktemp)"
trap 'rm -f "$tmp" "$old"' EXIT

# 直接遍历两个范围根，避免先扫完整 Assets（工程很大）。
{
  for root in "${SR_SCOPE_ROOTS[@]}"; do
    dir="$ASSETS/$root"
    [ -d "$dir" ] || continue
    find "$dir" -type f \
      \( -name '*.shader' -o -name '*.hlsl' -o -name '*.cginc' \
         -o -name '*.compute' -o -name '*.glsl' -o -name '*.hlslinc' \) \
      ! -path '*/_DefineProbe/*'
  done
} | while IFS= read -r f; do
  sr_normalize "$f"
  printf '\n'
done | LC_ALL=C sort -u > "$tmp"

n="$(wc -l < "$tmp" | tr -d ' ')"
if [ "$DRY" -eq 1 ]; then
  echo "生效范围：$(sr_scope_desc)"
  echo "将写入 $n 条到 $OUT"
  if [ -f "$OUT" ]; then
    grep -vE '^#|^$' "$OUT" | tr -d '\r' | LC_ALL=C sort > "$old"
    echo "--- 新增（最多 50 条）---"
    comm -23 "$tmp" "$old" | head -50
    echo "--- 移除（含旧范围外条目，最多 50 条）---"
    comm -13 "$tmp" "$old" | head -50
  fi
  exit 0
fi

{
  echo "# ============================================================================"
  echo "# 存量 Shader 白名单（既往不咎）"
  echo "# 生效范围：Assets/Shaders/ShaderTheseus/** + Assets/Shaders/ShaderLibrary/**"
  echo "# 匹配方式：相对 Assets/ 的完整路径精确匹配；禁止按文件名放行。"
  echo "# 新增文件请勿手工加入。人工豁免必须写明原因和时间。"
  echo "# 重新生成：bash tools/gen_whitelist.sh <Assets 目录>"
  echo "# 生成时间：$(date '+%Y-%m-%d %H:%M:%S')"
  echo "# 条目数：$n"
  echo "# ============================================================================"
  cat "$tmp"
} > "$OUT"

echo "已写入 $n 条 → $OUT"
