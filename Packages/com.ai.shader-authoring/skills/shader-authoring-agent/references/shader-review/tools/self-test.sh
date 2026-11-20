#!/usr/bin/env bash
# shader-review 工具回归测试；只使用临时目录，不改工程 shader。
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TMP="$(mktemp -d)"; trap 'rm -rf "$TMP"' EXIT
IN="$TMP/Assets/Shaders/ShaderTheseus/Test"; OUT="$TMP/Assets/Shaders/FTheseus/Test"
mkdir -p "$IN" "$OUT"
fail=0
ok(){ echo "✅ $1"; }
bad(){ echo "❌ $1"; fail=$((fail+1)); }

cat > "$IN/Bad.shader" <<'EOF'
Shader "T" { Properties { [HDR] _Rongjie_Fanwei("坏",Float)=0 [Toggle] _AlbedoColor("坏",Float)=0 } SubShader{} }
EOF
cat > "$IN/Bad.hlsl" <<'EOF'
TEXTURE2D(_Rongjie_Tex);
CBUFFER_START(UnityPerMaterial)
float4 _AlbedoColor;
CBUFFER_END
EOF
cp "$IN/Bad.shader" "$OUT/Bad.shader"

out="$(bash "$HERE/naming-lint.sh" "$IN" 2>&1)"; rc=$?
[ "$rc" -eq 1 ] && grep -q '_Rongjie_Fanwei' <<<"$out" && grep -q 'Bad.hlsl.*_AlbedoColor' <<<"$out" \
  && ok 'Attribute/内联 Properties + HLSL 声明可检出' || bad '命名解析漏检'
out="$(bash "$HERE/naming-lint.sh" "$OUT" 2>&1)"
grep -q '目标全部不在生效范围内' <<<"$out" && ok '范围外拒绝' || bad '范围外未拒绝'

# 新路径仅复用旧 basename 时不能命中白名单。
bn="$(grep -v '^#' "$HERE/../whitelist/legacy-paths.txt" | head -1 | awk -F/ '{print $NF}')"
printf 'Shader "T"{}\n' > "$IN/$bn"
out="$(bash "$HERE/scan.sh" "$IN/$bn" 2>&1)"
grep -q '目标文件（待检查）: 1' <<<"$out" && ok '白名单只按完整路径匹配' || bad 'basename 错误放行'

# 从 Assets/Shaders cwd 传相对路径。
mkdir -p "$TMP/Assets/Shaders/ShaderTheseus/Relative"
printf 'Shader "T"{}\n' > "$TMP/Assets/Shaders/ShaderTheseus/Relative/R.shader"
pushd "$TMP/Assets/Shaders" >/dev/null
out="$(bash "$HERE/scan.sh" ShaderTheseus/Relative/R.shader 2>&1)"
popd >/dev/null
grep -q '范围外跳过        : 0' <<<"$out" && ok '相对路径 scope 正常' || bad '相对路径被误判范围外'

[ "$fail" -eq 0 ] || exit 1
echo '全部回归测试通过。'
