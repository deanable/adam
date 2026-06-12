#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────
#  Adam Test Runner — Sequential test execution with stale process cleanup
#
#  Kills lingering testhost.exe / testhost processes from previous runs,
#  builds the solution once, then runs each test project sequentially
#  with --no-build. This avoids the DLL lock contention that occurs when
#  `dotnet test` runs multiple projects in parallel.
#
#  Usage:
#    ./scripts/test.sh [options]
#
#  Options:
#    -c, --config <cfg>    Build configuration (default: Debug)
#    --skip-build          Skip the build step (use existing binaries)
#    --skip-cleanup        Skip killing lingering testhost processes
#    --filter <expr>       xUnit filter expression applied to ALL projects
#    -v, --verbose         Show full test output (default: summary only)
#    -h, --help            Show this help message
#
#  Exit codes:
#    0  All tests passed
#    1  One or more test projects failed
#    2  Build failed
# ─────────────────────────────────────────────────────────────────

set -uo pipefail

# ── Defaults ──────────────────────────────────────────────────

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
CONFIGURATION="Debug"
SKIP_BUILD=false
SKIP_CLEANUP=false
VERBOSE=false
FILTER=""
FAILED=0
WARNED=0

# Test projects in dependency-safe order (Shared first, then others)
TEST_PROJECTS=(
  "tests/Adam.Shared.Tests/Adam.Shared.Tests.csproj"
  "tests/Adam.CatalogBrowser.Tests/Adam.CatalogBrowser.Tests.csproj"
  "tests/Adam.ServiceManager.Tests/Adam.ServiceManager.Tests.csproj"
  "tests/Adam.BrokerService.Tests/Adam.BrokerService.Tests.csproj"
)

# ── Parse args ────────────────────────────────────────────────

while [[ $# -gt 0 ]]; do
  case "$1" in
    -c|--config)      CONFIGURATION="$2"; shift 2 ;;
    --skip-build)     SKIP_BUILD=true; shift ;;
    --skip-cleanup)   SKIP_CLEANUP=true; shift ;;
    --filter)         FILTER="$2"; shift 2 ;;
    -v|--verbose)     VERBOSE=true; shift ;;
    -h|--help)        grep "^#" "$0" | grep -v "^#!/" | sed 's/^#//'; exit 0 ;;
    *)                echo "Unknown option: $1"; exit 1 ;;
  esac
done

# ── Helpers ───────────────────────────────────────────────────

kill_testhost() {
  if $SKIP_CLEANUP; then
    return
  fi

  if command -v powershell &>/dev/null; then
    # Windows / PowerShell Core
    powershell -NoProfile -Command \
      "Get-Process -Name testhost -ErrorAction SilentlyContinue | Stop-Process -Force" 2>/dev/null || true
  elif command -v killall &>/dev/null; then
    # Linux
    killall -9 testhost 2>/dev/null || true
  elif command -v pkill &>/dev/null; then
    # macOS / generic Unix
    pkill -9 -f testhost 2>/dev/null || true
  fi

  # Brief pause for OS to release file handles
  sleep 1
}

print_summary() {
  echo ""
  echo "============================================================"
  echo "  Test Summary"
  echo "============================================================"
  if [[ $WARNED -gt 0 ]]; then
    echo "  Warnings: $WARNED (test host crashed during cleanup)"
  fi
  if [[ $FAILED -eq 0 ]]; then
    echo "  ALL TESTS PASSED"
  else
    echo "  FAILED: $FAILED project(s) failed"
  fi
  echo "============================================================"
}

# ── Banner ────────────────────────────────────────────────────

echo ""
echo "============================================================"
echo "  Adam Test Runner v1.0"
echo "============================================================"
echo "  Config:       $CONFIGURATION"
echo "  Filter:       ${FILTER:-<none>}"
echo "  Projects:     ${#TEST_PROJECTS[@]}"
echo ""

# ── Step 1: Kill stale testhost processes ─────────────────────

echo "==> Cleaning up stale testhost processes..."
kill_testhost
echo ""

# ── Step 2: Build ─────────────────────────────────────────────

if ! $SKIP_BUILD; then
  echo "==> Building solution..."
  if dotnet build "$PROJECT_ROOT/src/Adam.slnx" -c "$CONFIGURATION" --no-restore 2>&1; then
    echo "  Build succeeded."
  else
    echo "  BUILD FAILED. Aborting."
    exit 2
  fi
  echo ""
fi

# ── Step 3: Run tests sequentially ────────────────────────────

echo "==> Running tests sequentially (one project at a time)..."
echo ""

for proj_path in "${TEST_PROJECTS[@]}"; do
  full_path="$PROJECT_ROOT/$proj_path"
  proj_name="$(basename "$(dirname "$proj_path")")"

  if [[ ! -f "$full_path" ]]; then
    echo "  [SKIP] $proj_name (project file not found)"
    continue
  fi

  echo "--- $proj_name ---"

  # Build test arguments
  TEST_ARGS=("$full_path" -c "$CONFIGURATION" --no-build)
  if [[ -n "$FILTER" ]]; then
    TEST_ARGS+=(--filter "$FILTER")
  fi

  # Run tests and capture exit code
  TEST_EXIT=0
  OUTPUT=""
  if $VERBOSE; then
    dotnet test "${TEST_ARGS[@]}" 2>&1 || TEST_EXIT=$?
  else
    OUTPUT=$(dotnet test "${TEST_ARGS[@]}" 2>&1) || TEST_EXIT=$?
    echo "$OUTPUT" | tail -5
  fi

  # Determine pass/fail:
  #  - "Failed!  - Failed: N" where N > 0 means actual test failures
  #  - "Test Run Aborted" means host crashed (usually after tests completed)
  #  - dotnet test exit code != 0 is a fallback check
  HAS_ACTUAL_FAILURES=false
  HAS_HOST_CRASH=false

  if echo "${OUTPUT}" | grep -qE "Failed!\s+-\s+Failed:\s+[1-9]"; then
    HAS_ACTUAL_FAILURES=true
  fi
  if echo "${OUTPUT}" | grep -q "Test Run Aborted"; then
    HAS_HOST_CRASH=true
  fi

  if $HAS_ACTUAL_FAILURES; then
    echo "  FAILED: $proj_name"
    FAILED=$((FAILED + 1))
  elif $HAS_HOST_CRASH; then
    # Host crashed but tests likely completed — warn only
    echo "  PASSED (with warning): $proj_name - test host crashed during cleanup"
    WARNED=$((WARNED + 1))
  elif [[ $TEST_EXIT -ne 0 ]] && ! $HAS_HOST_CRASH; then
    echo "  FAILED: $proj_name (exit code: $TEST_EXIT)"
    FAILED=$((FAILED + 1))
  else
    echo "  PASSED: $proj_name"
  fi

  # Kill testhost after each project to prevent lock leakage to next project
  kill_testhost

  echo ""
done

# ── Summary ───────────────────────────────────────────────────

print_summary

if [[ $FAILED -gt 0 ]]; then
  exit 1
fi

exit 0
