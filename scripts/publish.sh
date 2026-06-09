#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────
#  Adam Publisher — Cross-platform self-contained build script
#  Usage:
#    ./scripts/publish.sh [options]
#
#  Options:
#    -r, --rid <rid>       Runtime identifier (default: auto-detect)
#                            win-x64 | osx-x64 | osx-arm64 | linux-x64
#    -c, --config <cfg>    Build configuration (default: Release)
#    -o, --output <dir>    Publish output directory (default: ./publish)
#    --no-single-file      Disable PublishSingleFile
#    --no-trim             Disable trimming
#    --trim-mode <mode>    Trimming level: CopyUsedAssemblies | Link (default: Link)
#    --ready-to-run        Enable ReadyToRun (crossgen)
#    --no-restore          Skip dotnet restore
#    --skip-tests          Skip running tests before publish
#    -q, --quiet           Suppress verbose output
#    -h, --help            Show this help message
#
#  Examples:
#    ./scripts/publish.sh -r win-x64                     # Windows x64
#    ./scripts/publish.sh -r osx-arm64 --ready-to-run    # Apple Silicon with R2R
#    ./scripts/publish.sh -r linux-x64 -o /opt/adam      # Linux to custom dir
# ─────────────────────────────────────────────────────────────────

set -euo pipefail

# ── Defaults ──────────────────────────────────────────────────

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
CONFIGURATION="Release"
PUBLISH_DIR="$PROJECT_ROOT/publish"
SINGLE_FILE=true
TRIM_MODE="Link"
READY_TO_RUN=false
NO_RESTORE=false
SKIP_TESTS=false
QUIET=false

# Auto-detect RID
case "$(uname -s)" in
  Linux*)   AUTO_RID="linux-x64" ;;
  Darwin*)  AUTO_RID="osx-x64" ;;
  MINGW*|MSYS*|CYGWIN*)  AUTO_RID="win-x64" ;;
  *)        AUTO_RID="linux-x64" ;;
esac
RID="$AUTO_RID"

# ── Parse args ────────────────────────────────────────────────

while [[ $# -gt 0 ]]; do
  case "$1" in
    -r|--rid)           RID="$2"; shift 2 ;;
    -c|--config)        CONFIGURATION="$2"; shift 2 ;;
    -o|--output)        PUBLISH_DIR="$2"; shift 2 ;;
    --no-single-file)   SINGLE_FILE=false; shift ;;
    --no-trim)          TRIM_MODE=""; shift ;;
    --trim-mode)        TRIM_MODE="$2"; shift 2 ;;
    --ready-to-run)     READY_TO_RUN=true; shift ;;
    --no-restore)       NO_RESTORE=true; shift ;;
    --skip-tests)       SKIP_TESTS=true; shift ;;
    -q|--quiet)         QUIET=true; shift ;;
    -h|--help)          grep "^#" "$0" | grep -v "^#!/" | sed 's/^#//'; exit 0 ;;
    *)                  echo "Unknown option: $1"; exit 1 ;;
  esac
done

# ── Build publish args ────────────────────────────────────────

PUBLISH_BASE=(
  -c "$CONFIGURATION"
  -r "$RID"
  --self-contained true
)

if $SINGLE_FILE; then
  PUBLISH_BASE+=(-p:PublishSingleFile=true)
fi

if [[ -n "$TRIM_MODE" ]]; then
  PUBLISH_BASE+=(-p:PublishTrimmed=true -p:TrimMode="$TRIM_MODE")
fi

if $READY_TO_RUN; then
  PUBLISH_BASE+=(-p:PublishReadyToRun=true)
fi

if $NO_RESTORE; then
  PUBLISH_BASE+=(--no-restore)
fi

# ── Print banner ──────────────────────────────────────────────

echo ""
echo "╔═══════════════════════════════════════════════════╗"
echo "║            Adam Publisher v1.0                   ║"
echo "╠═══════════════════════════════════════════════════╣"
echo "║  RID:           $RID"
echo "║  Config:        $CONFIGURATION"
echo "║  Output:        $PUBLISH_DIR"
echo "║  Single-file:   $SINGLE_FILE"
echo "║  Trim mode:     ${TRIM_MODE:-disabled}"
echo "║  ReadyToRun:    $READY_TO_RUN"
echo "╚═══════════════════════════════════════════════════╝"
echo ""

# ── Validation ────────────────────────────────────────────────

if ! command -v dotnet &>/dev/null; then
  echo "ERROR: 'dotnet' CLI not found. Install .NET 10 SDK first."
  exit 1
fi

# ── Restore ───────────────────────────────────────────────────

if ! $NO_RESTORE; then
  echo "==> Restoring packages..."
  if $QUIET; then
    dotnet restore "$PROJECT_ROOT/src/Adam.CatalogBrowser/Adam.CatalogBrowser.csproj" 2>/dev/null
  else
    dotnet restore "$PROJECT_ROOT/src/Adam.CatalogBrowser/Adam.CatalogBrowser.csproj"
  fi
fi

# ── Tests ─────────────────────────────────────────────────────

if ! $SKIP_TESTS; then
  echo "==> Running tests..."

  TEST_PROJECTS=(
    "$PROJECT_ROOT/tests/Adam.Shared.Tests/Adam.Shared.Tests.csproj"
    "$PROJECT_ROOT/tests/Adam.CatalogBrowser.Tests/Adam.CatalogBrowser.Tests.csproj"
    "$PROJECT_ROOT/tests/Adam.ServiceManager.Tests/Adam.ServiceManager.Tests.csproj"
    "$PROJECT_ROOT/tests/Adam.BrokerService.Tests/Adam.BrokerService.Tests.csproj"
  )

  for proj in "${TEST_PROJECTS[@]}"; do
    if $QUIET; then
      dotnet test "$proj" -c "$CONFIGURATION" --no-restore 2>/dev/null | tail -2
    else
      echo "  Testing $(basename "$(dirname "$proj")")..."
      dotnet test "$proj" -c "$CONFIGURATION" --no-restore 2>&1 | tail -3
    fi
  done
  echo "  Tests complete."
  echo ""
fi

# ── Publish ───────────────────────────────────────────────────

echo "==> Publishing projects..."

PROJECT_SPECS=(
  "CatalogBrowser|$PROJECT_ROOT/src/Adam.CatalogBrowser/Adam.CatalogBrowser.csproj"
  "BrokerService|$PROJECT_ROOT/src/Adam.BrokerService/Adam.BrokerService.csproj"
  "ServiceManager|$PROJECT_ROOT/src/Adam.ServiceManager/Adam.ServiceManager.csproj"
)

for spec in "${PROJECT_SPECS[@]}"; do
  name="${spec%%|*}"
  csproj="${spec#*|}"
  outDir="$PUBLISH_DIR/$name"
  echo "  -> $name ($RID)"
  if $QUIET; then
    dotnet publish "$csproj" "${PUBLISH_BASE[@]}" -o "$outDir" 2>/dev/null
  else
    dotnet publish "$csproj" "${PUBLISH_BASE[@]}" -o "$outDir"
  fi
done

# Stage ServiceManager alongside CatalogBrowser (matching CI behavior)
echo "  -> Staging ServiceManager alongside CatalogBrowser..."
cp -r "$PUBLISH_DIR/ServiceManager/"* "$PUBLISH_DIR/CatalogBrowser/" 2>/dev/null || true

# ── Output summary ────────────────────────────────────────────

echo ""
echo "╔═══════════════════════════════════════════════════╗"
echo "║            Publish complete!                     ║"
echo "╠═══════════════════════════════════════════════════╣"
echo "║  Platform:   $RID"
echo "║  Output:     $PUBLISH_DIR"
echo "╠═══════════════════════════════════════════════════╣"

# Report sizes
if command -v du &>/dev/null; then
  for spec in "${PROJECT_SPECS[@]}"; do
    name="${spec%%|*}"
    outDir="$PUBLISH_DIR/$name"
    if [ -d "$outDir" ]; then
      size=$(du -sh "$outDir" 2>/dev/null | cut -f1)
      printf "║  %-15s %8s\n" "$name" "$size"
    fi
  done
fi

echo "╚═══════════════════════════════════════════════════╝"
echo ""
echo "Run the application:"
case "$RID" in
  win-x64)    echo "  $PUBLISH_DIR/CatalogBrowser/Adam.CatalogBrowser.exe" ;;
  osx-x64|osx-arm64) echo "  open $PUBLISH_DIR/CatalogBrowser/Adam.CatalogBrowser" ;;
  linux-x64)  echo "  $PUBLISH_DIR/CatalogBrowser/Adam.CatalogBrowser" ;;
esac
echo ""
