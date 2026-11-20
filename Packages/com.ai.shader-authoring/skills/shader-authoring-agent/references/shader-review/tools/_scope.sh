#!/usr/bin/env bash
# =============================================================================
# _scope.sh —— shader-review 运行时生效范围的唯一来源，供其它工具 source
# =============================================================================

SR_SCOPE_ROOTS=(
  "Shaders/ShaderTheseus"
  "Shaders/ShaderLibrary"
)
SR_SHADERLIB_ROOT="Shaders/ShaderLibrary"
SR_CHARACTER_ROOT="Shaders/ShaderTheseus/Character"

# 将路径解析为相对 Assets/ 的路径。
# - 已存在及尚未创建的相对路径都先基于当前目录转绝对路径
# - Windows 反斜杠、Git Bash /h/...、H:/... 均可处理
# - 找不到 Assets/ 锚点时保留规范化路径，随后由 scope 判断拒绝
sr_normalize() {
  local p="$1" abs
  p="${p//\\//}"
  p="${p%/}"

  # 路径本身已有 Assets 锚点时直接截取，避免批量扫描时为每个文件调用 realpath。
  case "$p" in
    Assets/*)     printf '%s' "${p#Assets/}"; return ;;
    *'/Assets/'*) printf '%s' "${p#*/Assets/}"; return ;;
    */Assets)     printf ''; return ;;
  esac

  # realpath -m 支持尚未创建的目标；没有 realpath 时使用 pwd 拼接。
  if command -v realpath >/dev/null 2>&1; then
    abs="$(realpath -m -- "$p" 2>/dev/null || printf '%s' "$p")"
  elif [[ "$p" = /* || "$p" =~ ^[A-Za-z]:/ ]]; then
    abs="$p"
  else
    abs="$PWD/$p"
  fi
  abs="${abs//\\//}"

  case "$abs" in
    *'/Assets/'*) printf '%s' "${abs#*/Assets/}" ;;
    */Assets)     printf '' ;;
    *)            printf '%s' "${abs#./}" ;;
  esac
}

sr_in_scope() {
  local rel root
  rel="$(sr_normalize "$1")"
  for root in "${SR_SCOPE_ROOTS[@]}"; do
    [ "$rel" = "$root" ] && return 0
    case "$rel" in "$root"/*) return 0 ;; esac
  done
  return 1
}

# 返回 0：目标位于指定根内，或是该根的祖先目录。
sr_target_relevant_to() {
  local rel root="$2"
  # 工程根（其下有 Assets/）也是受保护目录的祖先。
  [ -d "$1/Assets/$root" ] && return 0
  rel="$(sr_normalize "$1")"
  [ "$rel" = "$root" ] && return 0
  case "$rel" in "$root"/*) return 0 ;; esac
  [ -z "$rel" ] && return 0                    # Assets 根
  case "$root" in "$rel"/*) return 0 ;; esac # 目标是范围根祖先
  return 1
}

sr_target_relevant() {
  local root
  for root in "${SR_SCOPE_ROOTS[@]}"; do
    sr_target_relevant_to "$1" "$root" && return 0
  done
  return 1
}

sr_filter_in_scope() {
  local f
  while IFS= read -r f; do
    [ -z "$f" ] && continue
    sr_in_scope "$f" && printf '%s\n' "$f"
  done
}

sr_scope_desc() {
  printf 'Assets/Shaders/ShaderTheseus/  +  Assets/Shaders/ShaderLibrary/'
}
