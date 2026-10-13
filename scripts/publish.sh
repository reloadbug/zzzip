#!/usr/bin/env bash
# zzzip 发布脚本：产出可直接分发的绿色目录
#
#   ./scripts/publish.sh              # 自包含单文件（推荐，目标机无需装 .NET）
#   ./scripts/publish.sh -f           # 框架依赖（体积小，需目标机装 .NET 10 运行时）
#
# 产物位于 artifacts/zzzip-win-x64/
set -euo pipefail

cd "$(dirname "$0")/.."
ROOT="$(pwd)"
OUT="$ROOT/artifacts/zzzip-win-x64"

FRAMEWORK_DEPENDENT=false
[[ "${1:-}" == "-f" ]] && FRAMEWORK_DEPENDENT=true

rm -rf "$OUT"
mkdir -p "$OUT"

if $FRAMEWORK_DEPENDENT; then
  echo ">> 发布：框架依赖版本"
  dotnet publish src/zzzip.App/zzzip.App.csproj \
    -c Release -r win-x64 --self-contained false \
    -p:PublishSingleFile=true \
    -o "$OUT" --nologo
else
  echo ">> 发布：自包含单文件版本（目标机无需安装 .NET）"
  dotnet publish src/zzzip.App/zzzip.App.csproj \
    -c Release -r win-x64 --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -o "$OUT" --nologo
fi

# 随包附带 7-Zip 引擎（缺失时程序会自动降级为仅 ZIP，不会崩溃）
SEVENZIP=""
for c in "${ZZZIP_7Z:-}" \
         "/c/Program Files/7-Zip/7z.exe" \
         "/c/Program Files (x86)/7-Zip/7z.exe"; do
  if [[ -n "$c" && -f "$c" ]]; then SEVENZIP="$c"; break; fi
done

if [[ -n "$SEVENZIP" ]]; then
  cp "$SEVENZIP" "$OUT/7z.exe"
  echo ">> 已附带 7-Zip 引擎：$SEVENZIP"
  for sfx in "/c/Program Files/7-Zip/7z.sfx" "/c/Program Files (x86)/7-Zip/7z.sfx"; do
    [[ -f "$sfx" ]] && cp "$sfx" "$OUT/7z.sfx" && echo ">> 已附带自解压模块：$sfx" && break
  done
else
  echo "!! 未找到 7z.exe，产物将只能处理 ZIP 格式。"
  echo "   请将 7z.exe 放到 $OUT/ 或参考 native/7z/README.md"
fi

# 附带说明文件
[[ -f "$ROOT/README.md" ]] && cp "$ROOT/README.md" "$OUT/使用说明.md"
[[ -f "$ROOT/native/7z/README.md" ]] && cp "$ROOT/native/7z/README.md" "$OUT/7-Zip引擎说明.md"

echo
echo ">> 完成：$OUT"
ls -lh "$OUT"
