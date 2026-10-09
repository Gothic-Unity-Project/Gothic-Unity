#!/usr/bin/env bash
# PreToolUse hook: blocks Grep searches over the whole Unity project (repo root or Assets/) - they take forever.
input=$(cat)

normalize()
{
    printf '%s' "$1" | sed -e 's#\\\\#/#g' -e 's#\\#/#g' -e 's#/*$##' -e 's#^/\([a-zA-Z]\)/#\1:/#' \
        | tr '[:upper:]' '[:lower:]'
}

path=$(printf '%s' "$input" | sed -n 's/.*"path"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p')
root=$(normalize "${CLAUDE_PROJECT_DIR:-$PWD}")
target=$(normalize "$path")

case "$target" in
    ""|"."|"./"|"assets"|"./assets"|"$root"|"$root/assets")
        echo "Grep over the whole project is blocked (huge Unity repo). Scope 'path' to a subfolder, e.g. Assets/Gothic-Core/Scripts/Services/." >&2
        exit 2
        ;;
esac
exit 0
