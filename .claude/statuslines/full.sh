#!/usr/bin/env bash
mapfile -t fields < <(
  jq -r '.model.display_name // "Unknown",
         (.context_window.used_percentage | round | tostring),
         (.effort as $e | if ($e|type) == "object" then ($e.level // "") elif ($e|type) == "string" then $e else "" end),
         (.thinking as $t | if ($t|type) == "boolean" then (if $t then "1" else "" end) elif ($t|type) == "object" then (if $t.enabled == true then "1" else "" end) else "" end),
         (.rate_limits.five_hour.used_percentage // "" | if type=="number" then round|tostring else . end),
         (.rate_limits.five_hour.resets_at // ""),
         (.rate_limits.seven_day.used_percentage // "" | if type=="number" then round|tostring else . end),
         (.rate_limits.seven_day.resets_at // "",
         (.workspace.current_dir // .cwd // ""))' <<< "$(cat)" | tr -d '\r'
)
model="${fields[0]}"
used="${fields[1]}"
effort="${fields[2]}"
thinking="${fields[3]}"
h5="${fields[4]}"; h5_reset="${fields[5]}"
d7="${fields[6]}"; d7_reset="${fields[7]}"
cwd="${fields[8]}"
now=$(date +%s)

esc() { printf '\e[%sm' "$1"; }
reset="$(esc 0)"

if [ -n "$used" ] && [ "$used" != "null" ]; then
  if [ "$used" -lt 50 ]; then
    ctx_str="$(esc 32)${used}%${reset}"
  elif [ "$used" -lt 75 ]; then
    ctx_str="$(esc 33)${used}%${reset}"
  else
    ctx_str="$(esc 31)${used}%${reset}"
  fi
else
  ctx_str="--"
fi

output="$ctx_str  $model"
[ "$effort" = "low" ]    && output="$output  $(esc 32)L${reset}"
[ "$effort" = "medium" ] && output="$output  $(esc 33)M${reset}"
[ "$effort" = "high" ]   && output="$output  $(esc 31)H${reset}"
[ "$effort" = "xhigh" ]  && output="$output  $(esc 35)XH${reset}"
[ "$thinking" = "1" ]    && output="$output  $(esc 36)T${reset}"

quota() { # label used reset
  [ -z "$2" ] && return
  local left=$(( ${3:-0} - now )) col=32
  [ $left -lt 0 ] && left=0
  [ "$2" -ge 50 ] && col=33
  [ "$2" -ge 75 ] && col=31
  local t
  if [ "$1" = "7d" ]; then t="$(date -d "@$3" +%a/%H:%M)"
  elif [ $left -ge 86400 ]; then t="$((left/86400))d$(((left%86400)/3600))h"
  else t="$((left/3600))h$(printf %02d $(((left%3600)/60)))m"; fi
  printf '  %s[%s: %s%%%s resets %s%s]%s' "$(esc 2)" "$1" "$(esc $col)$2" "$(esc 0)$(esc 2)" "$t" "" "$reset"
}
output="$output$(quota 5h "$h5" "$h5_reset")$(quota 7d "$d7" "$d7_reset")"

# Right-aligned git part: branch + commits to push (up) / pull (down). Only cheap ref lookups, no worktree scan.
git_part=""
if [ -n "$cwd" ] && branch=$(git -C "$cwd" rev-parse --abbrev-ref HEAD 2>/dev/null); then
  git_part="$(esc 35)${branch}${reset}"
  git_plain="$branch"
  if read -r ahead behind < <(git -C "$cwd" rev-list --left-right --count HEAD...@{u} 2>/dev/null); then
    [ "$ahead" -gt 0 ]  && { git_part="$git_part $(esc 32)↑${ahead}${reset}";  git_plain="$git_plain ↑${ahead}"; }
    [ "$behind" -gt 0 ] && { git_part="$git_part $(esc 33)↓${behind}${reset}"; git_plain="$git_plain ↓${behind}"; }
  fi
fi

if [ -n "$git_part" ]; then
  shopt -s extglob
  plain="${output//$'\e'\[+([0-9;])m/}"
  read -r _ cols < <({ stty size </dev/tty; } 2>/dev/null)
  cols="${cols:-${COLUMNS:-0}}"
  pad=$(( cols - 2 - ${#plain} - ${#git_plain} ))   # 2 = margin for harness padding
  if [ "$pad" -ge 2 ]; then
    printf "%s%*s%s" "$output" "$pad" "" "$git_part"
  else
    printf "%s  %s" "$output" "$git_part"
  fi
else
  printf "%s" "$output"
fi
