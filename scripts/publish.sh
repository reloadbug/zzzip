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
    -p:DebugType=none -p:DebugSymbols=false \
    -o "$OUT" --nologo
else
  echo ">> 发布：自包含单文件版本（目标机无需安装 .NET）"
  dotnet publish src/zzzip.App/zzzip.App.csproj \
    -c Release -r win-x64 --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -p:DebugType=none -p:DebugSymbols=false \
    -o "$OUT" --nologo
fi

# 双保险：个别 SDK 版本即使关了 DebugSymbols 也会把附属 pdb 拷进输出目录，
# 绿色版不需要调试符号，直接清掉（避免用户在目录里看到一堆无用文件）。
find "$OUT" -maxdepth 1 -name '*.pdb' -delete 2>/dev/null || true

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

# 打成可分发的 zip（解压即用）。用随包的 7z.exe 完成，不额外依赖 zip 命令。
VERSION=$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' "$ROOT/src/zzzip.App/zzzip.App.csproj" | head -1)
VERSION="${VERSION:-0.0.0}"
DIST_NAME="zzzip-${VERSION}-win-x64.zip"
DIST_PATH="$ROOT/artifacts/$DIST_NAME"

if [[ -n "$SEVENZIP" ]]; then
  rm -f "$DIST_PATH"
  # 在产物目录内以相对路径输出，避开 Git Bash 与 7z 之间的路径写法差异
  if ( cd "$OUT" && "$SEVENZIP" a -tzip "../$DIST_NAME" ./* > /dev/null ); then
    echo ">> 已打包分发文件：$DIST_PATH"
    echo "   $(du -h "$DIST_PATH" | cut -f1)  sha256=$(sha256sum "$DIST_PATH" | cut -c1-16)…"
  else
    echo "!! 打包失败（7z 返回非 0），发布目录本身仍可直接使用。"
  fi
else
  echo "!! 未找到 7z.exe，跳过 zip 打包。"
fi

echo
echo ">> 完成：$OUT"
ls -lh "$OUT"
