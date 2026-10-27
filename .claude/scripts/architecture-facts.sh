#!/usr/bin/env bash
# Collects deterministic structural facts of a change for the architecture-reviewer agent (and humans).
# Cheap: only git + grep/awk, no LLM tokens spent on reading full diffs.
#
# Usage:
#   architecture-facts.sh            # committed branch changes: main...HEAD
#   architecture-facts.sh --wip      # merge-base(main) vs working tree (incl. uncommitted, excl. untracked)
#   architecture-facts.sh <range>    # any git diff range, e.g. HEAD~3..HEAD

set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

if [[ "${1:-}" == "--wip" ]]; then
    RANGE="$(git merge-base main HEAD)"
else
    RANGE="${1:-main...HEAD}"
fi

CORE="Assets/Gothic-Core/Scripts/"
BASELINE="Assets/Gothic-Tests/EditMode/Architecture/ArchitectureBaseline.txt"
# Exclude Unity meta files and third party code from every query.
PATHSPEC=(-- . ':(exclude)*.meta' ':(exclude)Assets/HurricaneVR/*' ':(exclude)Assets/Plugins/*')

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# One diff for everything. ADDED holds "path<TAB>added line" for every added line.
git diff --name-status --find-renames "$RANGE" "${PATHSPEC[@]}" > "$TMP/status"
git diff --numstat --find-renames=100% "$RANGE" "${PATHSPEC[@]}" > "$TMP/numstat"
git diff -U0 "$RANGE" "${PATHSPEC[@]}" > "$TMP/diff"
awk '/^\+\+\+ /{ f = substr($0, 7); next } /^\+/{ print f "\t" substr($0, 2) }' "$TMP/diff" > "$TMP/added"

# Path -> "module" or "Core/<Layer>"
module_of() {
    case "$1" in
        ${CORE}Models/*)   echo "Core/Models" ;;
        ${CORE}Domain/*)   echo "Core/Domain" ;;
        ${CORE}Services/*) echo "Core/Services" ;;
        ${CORE}Adapters/*) echo "Core/Adapters" ;;
        Assets/Gothic-Core/*) echo "Core/(other)" ;;
        Assets/Gothic-VR/*)   echo "VR" ;;
        Assets/Gothic-Flat/*) echo "Flat" ;;
        Assets/Gothic1/*)     echo "Gothic1" ;;
        Assets/Gothic2/*)     echo "Gothic2" ;;
        Assets/Gothic-Lab/*)  echo "Lab" ;;
        Assets/Gothic-Tests/*) echo "Tests" ;;
        .claude/*|CLAUDE.md|Docs/*) echo "AI/Docs" ;;
        *) echo "Other" ;;
    esac
}

# Root namespace owned by a module.
namespace_of_module() {
    case "$1" in
        Core/*) echo "Gothic.Core" ;;
        VR) echo "Gothic.VR" ;;
        Flat) echo "Gothic.Flat" ;;
        Gothic1) echo "Gothic.G1" ;;
        Gothic2) echo "Gothic.G2" ;;
        Lab) echo "Gothic.Lab" ;;
        *) echo "" ;;
    esac
}

# Rank of a Core layer (by folder name or namespace part). -1 if not a layer.
layer_rank() {
    case "$1" in
        Models|Model) echo 0 ;;
        Domain) echo 1 ;;                     # Domain and Services share a rank
        Services|Manager|Creator) echo 1 ;;   # Manager/Creator are legacy namespaces inside Services/
        Adapters) echo 2 ;;
        *) echo -1 ;;
    esac
}

# Added C# lines matching a regex, as "path: line".
added_cs() {
    # Regex via ENVIRON, as -v would process its backslash escapes.
    RE="$1" awk -F'\t' '$1 ~ /\.cs$/ && $2 ~ ENVIRON["RE"] { line = $2; sub(/^[ \t]+/, "", line); print $1 ": " line }' \
        "$TMP/added"
}

echo "# Architecture facts for \`$RANGE\`"
echo

echo "## Changed files by module/layer"
echo '```'
# numstat shows renames as "old => new"; status file has the final path in the last column.
paste <(cut -f1,2 "$TMP/numstat") <(awk -F'\t' '{ print substr($1, 1, 1) "\t" $NF }' "$TMP/status") |
while IFS=$'\t' read -r added removed status path; do
    printf '%-14s %s +%s -%s %s\n' "$(module_of "$path")" "$status" "$added" "$removed" "$path"
done | sort
echo '```'
echo

echo "## New files (lines)"
echo '```'
awk -F'\t' '$1 == "A" { print $2 }' "$TMP/status" | while read -r path; do
    [[ -f "$path" ]] && printf '%5s %s\n' "$(wc -l < "$path")" "$path"
done
echo '```'
echo

echo "## Touched C# files now over 500 lines (god-class candidates)"
echo '```'
awk -F'\t' '$1 !~ /^D/ && $NF ~ /\.cs$/ { print $NF }' "$TMP/status" | while read -r path; do
    [[ -f "$path" ]] || continue
    lines=$(wc -l < "$path")
    if (( lines > 500 )); then printf '%5s %s\n' "$lines" "$path"; fi
done
echo '```'
echo

echo "## Added usings crossing layers/modules"
echo '```'
added_cs '^[ \t]*using[ \t]+(static[ \t]+)?Gothic\.' | sed -E 's/^([^:]+): using (static )?([A-Za-z0-9_.]+).*/\1 \3/' |
while read -r path ns; do
    module=$(module_of "$path")
    own=$(namespace_of_module "$module")

    # Upward dependency inside Core.
    if [[ "$module" == Core/* && "$ns" == Gothic.Core.* ]]; then
        file_rank=$(layer_rank "${module#Core/}")
        ns_rank=$(layer_rank "$(echo "$ns" | cut -d. -f3)")
        if (( file_rank >= 0 && ns_rank > file_rank )); then echo "UPWARD   $path ($module) -> $ns"; fi
    fi

    # Core must not know platform/version modules; those must not know each other. Tests may use anything.
    if [[ -n "$own" && "$ns" != Gothic.Core* && "$ns" != "$own"* ]]; then
        echo "LATERAL  $path ($module) -> $ns"
    fi
done | sort -u
echo '```'
echo

echo "## Reflex DI registrations (added/removed)"
echo '```'
awk '/^\+\+\+ /{ f = substr($0, 7) } /^[+-][^+-].*(AddSingleton|AddTransient|AddScoped|RegisterType|RegisterValue)/{ print f ": " $0 }' \
    "$TMP/diff"
echo '```'
echo

echo "## GlobalEventDispatcher"
echo "### Event definitions (added/removed lines)"
echo '```'
awk '/^\+\+\+ /{ f = substr($0, 7) } f ~ /GlobalEventDispatcher\.cs$/ && /^[+-][^+-]/' "$TMP/diff"
echo '```'
echo "### Raise/subscribe sites (added)"
echo '```'
awk -F'\t' '$1 ~ /\.cs$/ {
    line = $2
    while (match(line, /GlobalEventDispatcher\.[A-Za-z0-9_]+\.(AddListener|RemoveListener|Invoke)/)) {
        print $1 ": " substr(line, RSTART, RLENGTH)
        line = substr(line, RSTART + RLENGTH)
    }
}' "$TMP/added" | sort | uniq -c
echo '```'
echo

echo "## New public types"
echo '```'
added_cs '^[ \t]*public[ \t]+((static|abstract|sealed|partial)[ \t]+)*(class|interface|struct|enum|record)[ \t]'
echo '```'
echo

echo "## Added public members per file (count)"
echo '```'
added_cs '^[ \t]*public[ \t]' | cut -d: -f1 | sort | uniq -c | sort -rn
echo '```'
echo

echo "## Config, assemblies, AI instructions, architecture baseline"
echo '```'
grep -E '(DeveloperConfig[^/]*\.cs|GameConfigurations/.*\.asset|\.asmdef|ArchitectureBaseline\.txt|CLAUDE\.md|^\S+\s+\.claude/)' \
    "$TMP/status" || true
echo '```'
echo "### Baseline additions (new tolerated violations - each needs a justification)"
echo '```'
awk -F'\t' -v b="$BASELINE" '$1 == b && $2 !~ /^#/ && $2 != "" { print $2 }' "$TMP/added"
echo '```'
