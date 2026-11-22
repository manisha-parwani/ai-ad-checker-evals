#!/usr/bin/env bash
# Creates (or replaces) the brand-safety analyzer in your Azure AI Content Understanding resource,
# then waits until it is ready.
#
# Auth: a Microsoft Entra ID token from the Azure CLI. Run 'az login' first. No API keys.
#
# Usage:
#   export ContentUnderstanding__Endpoint="https://<your-resource-name>.services.ai.azure.com/"
#   export ContentUnderstanding__AnalyzerId="brandSafety"   # optional, this is the default
#   ./scripts/create-analyzer.sh
set -euo pipefail

endpoint="${ContentUnderstanding__Endpoint:?Set ContentUnderstanding__Endpoint to your resource endpoint}"
endpoint="${endpoint%/}"
analyzer_id="${ContentUnderstanding__AnalyzerId:-brandSafety}"
api_version="${ContentUnderstanding__ApiVersion:-2025-11-01}"
schema="$(cd "$(dirname "$0")/.." && pwd)/analyzer/brand-safety-analyzer.json"

workdir="$(mktemp -d)"
trap 'rm -rf "$workdir"' EXIT
chmod 700 "$workdir"

# Keep the token out of the process list: curl reads the header from a private file.
token="$(az account get-access-token --resource https://cognitiveservices.azure.com --query accessToken --output tsv)"
printf 'Authorization: Bearer %s\n' "$token" > "$workdir/auth-header"
unset token

echo "Creating analyzer '${analyzer_id}' (api-version ${api_version})..."
status="$(curl --silent --show-error \
  --request PUT "${endpoint}/contentunderstanding/analyzers/${analyzer_id}?api-version=${api_version}&allowReplace=true" \
  --header "@$workdir/auth-header" \
  --header "Content-Type: application/json" \
  --data-binary "@${schema}" \
  --dump-header "$workdir/headers" \
  --output "$workdir/body" \
  --write-out '%{http_code}')"

if [[ "$status" != 200 && "$status" != 201 ]]; then
  echo "Create failed with HTTP ${status}:" >&2
  cat "$workdir/body" >&2
  echo >&2
  exit 1
fi

operation="$(grep -i '^operation-location:' "$workdir/headers" | head -n 1 | cut -d' ' -f2- | tr -d '\r')"
if [[ -z "$operation" ]]; then
  echo "No Operation-Location header returned; check the analyzer in Content Understanding Studio." >&2
  exit 1
fi

for _ in $(seq 1 60); do
  result="$(curl --silent --show-error --header "@$workdir/auth-header" "$operation")"
  state="$(printf '%s' "$result" | grep -o '"status"[[:space:]]*:[[:space:]]*"[A-Za-z]*"' | head -n 1 | sed 's/.*"\([A-Za-z]*\)"$/\1/' | tr '[:upper:]' '[:lower:]')"
  case "$state" in
    succeeded)
      echo "Analyzer '${analyzer_id}' is ready."
      exit 0
      ;;
    failed|canceled)
      echo "Analyzer creation ${state}:" >&2
      printf '%s\n' "$result" >&2
      exit 1
      ;;
  esac
  sleep 2
done

echo "Timed out waiting for the analyzer. Check its status in Content Understanding Studio." >&2
exit 1
