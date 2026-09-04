#!/usr/bin/env bash
# Re-run only the tests that failed in a previous .trx.
#
# Usage (from repo root, in Git Bash):
#   tests/HR.Web.E2E.Tests/retest-fails.sh [--methods] [--list] [path/to/results.trx]
#
#   (default)   re-run every test CLASS that had a failure (shorter filter, most reliable)
#   --methods   re-run only the exact failed test methods (longer filter)
#   --list      just print what would be re-run, don't run it
#
# trx defaults to tests/HR.Web.E2E.Tests/TestResults/e2e-results.trx
# Fresh results go to tests/HR.Web.E2E.Tests/TestResults/retest.trx
#
# NOTE: stop the running dev app (Aspire AppHost / `dotnet run`) first — `dotnet test`
# rebuilds HR.Web / HR.Api and fails on locked DLLs otherwise.
set -euo pipefail

MODE=classes
LIST_ONLY=0
TRX=""
for arg in "$@"; do
  case "$arg" in
    --methods) MODE=methods ;;
    --list)    LIST_ONLY=1 ;;
    *)         TRX="$arg" ;;
  esac
done

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PROJ="$REPO_ROOT/tests/HR.Web.E2E.Tests/HR.Web.E2E.Tests.csproj"
RESULTS_DIR="$REPO_ROOT/tests/HR.Web.E2E.Tests/TestResults"
TRX="${TRX:-$RESULTS_DIR/e2e-results.trx}"

[[ -f "$TRX" ]] || { echo "trx not found: $TRX" >&2; exit 1; }

# Failed testNames, minus the "(params...)" theory-row suffix.
FAILED=$(LC_ALL=C grep -o 'testName="[^"]*" [^>]*outcome="Failed"' "$TRX" \
         | LC_ALL=C sed 's/.*testName="//; s/" .*//; s/(.*//')

if [[ "$MODE" == methods ]]; then
  mapfile -t KEYS < <(printf '%s\n' "$FAILED" | sort -u)
else
  mapfile -t KEYS < <(printf '%s\n' "$FAILED" | sed 's/\.[^.]*$//' | sort -u)
fi

[[ ${#KEYS[@]} -gt 0 ]] || { echo "No failed tests found in $(basename "$TRX")"; exit 0; }

echo "$MODE mode: ${#KEYS[@]} $( [[ $MODE == methods ]] && echo methods || echo classes ) from $(basename "$TRX")"

if [[ "$LIST_ONLY" == 1 ]]; then
  printf '  %s\n' "${KEYS[@]}"
  exit 0
fi

FILTER=""
for k in "${KEYS[@]}"; do FILTER+="${FILTER:+|}FullyQualifiedName~${k}"; done

exec dotnet test "$PROJ" \
  --filter "$FILTER" \
  --logger "trx;LogFileName=retest.trx" \
  --results-directory "$RESULTS_DIR"
