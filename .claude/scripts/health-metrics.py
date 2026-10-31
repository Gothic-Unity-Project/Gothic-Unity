#!/usr/bin/env python3
"""Deterministic health numbers of the whole Gothic-Unity codebase, for the codebase-health agent (and humans).

Cheap: git + regex, no LLM tokens. Measures the checked-out tree (tracked files incl. local edits) and its git history.
Heuristics, not a compiler: method bodies are found by brace matching, so treat counts as trends, not exact truths.

Usage:
    python3 .claude/scripts/health-metrics.py                 # text summary
    python3 .claude/scripts/health-metrics.py --json out.json # also all numbers as JSON, for the trend
    python3 .claude/scripts/health-metrics.py --days 60       # churn window (default 60 days)
"""
import argparse
import hashlib
import json
import os
import re
import statistics
import subprocess
import sys
from collections import Counter, defaultdict

CORE = 'Assets/Gothic-Core/Scripts/'
BASELINE = 'Assets/Gothic-Tests/EditMode/Architecture/ArchitectureBaseline.txt'
DISPATCHER = 'Assets/Gothic-Core/Scripts/GlobalEventDispatcher.cs'
EXCLUDE = ('Assets/HurricaneVR/', 'Assets/Plugins/', 'Assets/ThirdParty/')
HOT_METHODS = re.compile(r'\b(?:void|override\s+void)\s+(Update|LateUpdate|FixedUpdate|OnGUI)\s*\(\s*\)')
HOT_PATTERNS = {
    'Camera.main': re.compile(r'Camera\.main\b'),
    'GetComponent': re.compile(r'\bGetComponent(?:InChildren|InParent|s)?\s*[<(]'),
    'Find': re.compile(r'\b(?:GameObject\.Find|FindObjectOfType|FindObjectsOfType|FindObjectsByType|FindAnyObjectByType'
                       r'|FindFirstObjectByType|FindWithTag|FindGameObjectsWithTag)\b'),
    'LINQ': re.compile(r'\.(?:Where|Select|Any|All|First|FirstOrDefault|OrderBy|OrderByDescending|ToList|ToArray|Count'
                       r'|Sum|Max|Min|GroupBy|Distinct)\s*\('),
    'new collection': re.compile(r'\bnew\s+(?:List|Dictionary|HashSet|Queue|Stack)<'),
    'string building': re.compile(r'\$"|string\.Format\(|\+\s*"'),
    'Physics query': re.compile(r'\bPhysics\.(?:Raycast|RaycastAll|SphereCast|SphereCastAll|CapsuleCast|OverlapSphere'
                                r'|OverlapBox|OverlapCapsule|Linecast|BoxCast)\b'),
}
STATIC_RESOLVE = re.compile(r'\bDIContainer\.Resolve\b|ReflexProjectInstaller\.DIContainer|\.Resolve<')
VERSION_CHECK = re.compile(r'\bIsGothic[12]\s*\(\s*\)')
DEBUG_LOG = re.compile(r'\bDebug\.Log(?:Warning|Error|Exception)?\s*\(')
MARKERS = re.compile(r'\b(TODO|FIXME|HACK)\b')
INJECT = re.compile(r'\[Inject\]')
METHOD_SIG = re.compile(r'^\s*(?:\[[^\]]+\]\s*)*(?:(?:public|private|protected|internal|static|override|virtual|async'
                        r'|abstract|sealed|new|extern|unsafe)\s+)+[\w<>\[\],.?() ]+\s+\w+\s*(?:<[^>]*>)?\s*\(')
CLASS_DECL = re.compile(r'\b(?:class|struct|interface|record|enum)\s+(\w+)')


def git(*args):
    return subprocess.run(['git', *args], capture_output=True, text=True, check=False).stdout


def module_of(path):
    if path.startswith(CORE):
        layer = path[len(CORE):].split('/', 1)[0]
        return f'Core/{layer}' if layer in ('Models', 'Domain', 'Services', 'Adapters') else 'Core/(other)'
    if path.startswith('Assets/Gothic-Core/'):
        return 'Core/(editor)' if '/Editor/' in path else 'Core/(other)'
    parts = path.split('/')
    return parts[1].replace('Gothic-', '') if len(parts) > 1 else '(root)'


def strip_comments(text):
    text = re.sub(r'/\*.*?\*/', lambda m: '\n' * m.group(0).count('\n'), text, flags=re.S)
    return re.sub(r'//[^\n]*', '', text)


def method_body(code, start):
    """The text from the first '{' after start to its matching '}' (strings/chars are not special-cased)."""
    open_at = code.find('{', start)
    if open_at < 0:
        return ''
    depth = 0
    for i in range(open_at, len(code)):
        if code[i] == '{':
            depth += 1
        elif code[i] == '}':
            depth -= 1
            if depth == 0:
                return code[open_at:i + 1]
    return code[open_at:]


def percentile(values, p):
    if not values:
        return 0
    values = sorted(values)
    return values[min(len(values) - 1, int(round(p / 100 * (len(values) - 1))))]


def scan_files():
    files = [f for f in git('ls-files', 'Assets/Gothic-*').splitlines()
             if f.endswith('.cs') and not f.startswith(EXCLUDE)]
    result = {}
    for path in files:
        try:
            raw = open(path, encoding='utf-8-sig', errors='replace').read()
        except OSError:
            continue
        code = strip_comments(raw)
        lines = raw.splitlines()
        code_lines = [l for l in code.splitlines() if l.strip() and l.strip() not in ('{', '}')]
        hot = Counter()
        hot_methods = []
        for m in HOT_METHODS.finditer(code):
            body = method_body(code, m.end())
            hot_methods.append(m.group(1))
            for name, pattern in HOT_PATTERNS.items():
                hot[name] += len(pattern.findall(body))
        result[path] = {
            'module': module_of(path),
            'lines': len(lines),
            'code_lines': len(code_lines),
            'methods': sum(1 for l in code.splitlines() if METHOD_SIG.match(l)),
            'types': CLASS_DECL.findall(code),
            'injects': len(INJECT.findall(code)),
            'static_resolves': len(STATIC_RESOLVE.findall(code)),
            'version_checks': len(VERSION_CHECK.findall(code)),
            'debug_logs': len(DEBUG_LOG.findall(code)),
            'markers': Counter(MARKERS.findall(raw)),
            'long_lines': sum(1 for l in lines if len(l) > 120),
            'hot_methods': hot_methods,
            'hot': hot,
            'tests': len(re.findall(r'\[(?:Test|UnityTest|TestCase)\b', code)),
            'raw': code,
        }
    return result


def duplication(files, window=6):
    """Identical windows of `window` normalized code lines found in more than one place."""
    seen = defaultdict(set)
    for path, info in files.items():
        if info['module'] == 'Tests':
            continue
        norm = []
        for line in info['raw'].splitlines():
            s = re.sub(r'\s+', ' ', line.strip())
            if not s or s in ('{', '}', '};') or s.startswith(('using ', 'namespace ', '[')):
                continue
            norm.append(s)
        for i in range(len(norm) - window + 1):
            digest = hashlib.md5('\n'.join(norm[i:i + window]).encode()).hexdigest()
            seen[digest].add((path, i))
    duplicated = {h: locs for h, locs in seen.items() if len(locs) > 1}
    pairs = Counter()
    for locs in duplicated.values():
        paths = sorted({p for p, _ in locs})
        if len(paths) == 1:
            pairs[(paths[0], paths[0])] += 1
        for a in range(len(paths)):
            for b in range(a + 1, len(paths)):
                pairs[(paths[a], paths[b])] += 1
    return len(duplicated), pairs.most_common(10)


def events(files):
    text = open(DISPATCHER, encoding='utf-8-sig').read() if os.path.exists(DISPATCHER) else ''
    names = re.findall(r'public\s+static\s+readonly\s+UnityEvent(?:<[^>]*>)?\s+(\w+)', text)
    listeners, raisers = Counter(), Counter()
    for info in files.values():
        for name in names:
            listeners[name] += len(re.findall(rf'GlobalEventDispatcher\.{name}\.AddListener\b', info['raw']))
            raisers[name] += len(re.findall(rf'GlobalEventDispatcher\.{name}\.Invoke\b', info['raw']))
    return {
        'count': len(names),
        'no_listener': [n for n in names if listeners[n] == 0],
        'no_raiser': [n for n in names if raisers[n] == 0],
        'most_listened': listeners.most_common(5),
    }


def history(files, days):
    churn = Counter()
    for path in git('log', f'--since={days}.days', '--no-merges', '--name-only', '--format=',
                    '--', 'Assets/Gothic-*').splitlines():
        if path in files:
            churn[path] += 1
    hotspots = sorted(((churn[p] * files[p]['code_lines'], churn[p], files[p]['code_lines'], p) for p in churn),
                      reverse=True)[:15]

    # Token cost of a change: how much code the touched files hold (what one reads to make that change).
    log = git('log', '--no-merges', '-300', '--format=@%s', '--name-only', '--', 'Assets/Gothic-*')
    commits, current = [], None
    for line in log.splitlines():
        if line.startswith('@'):
            current = {'subject': line[1:], 'files': []}
            commits.append(current)
        elif line.strip() and current is not None and line.endswith('.cs') and line in files:
            current['files'].append(line)
    commits = [c for c in commits if c['files']]
    touched = [len(c['files']) for c in commits]
    context = [sum(files[f]['code_lines'] for f in c['files']) for c in commits]
    fixes = [c for c in commits if c['subject'].lower().startswith('fix')]
    return {
        'churn_days': days,
        'hotspots': [{'path': p, 'churn': c, 'code_lines': n, 'score': s} for s, c, n, p in hotspots],
        'commits_sampled': len(commits),
        'files_per_commit_median': statistics.median(touched) if touched else 0,
        'files_per_commit_p90': percentile(touched, 90),
        'context_lines_per_commit_median': statistics.median(context) if context else 0,
        'context_lines_per_commit_p90': percentile(context, 90),
        'fix_commits_touching_5plus_files_pct': round(100 * sum(1 for c in fixes if len(c['files']) >= 5)
                                                      / len(fixes), 1) if fixes else 0,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--json')
    parser.add_argument('--days', type=int, default=60)
    args = parser.parse_args()
    os.chdir(git('rev-parse', '--show-toplevel').strip() or '.')

    files = scan_files()
    by_module = defaultdict(lambda: {'files': 0, 'code_lines': 0})
    for info in files.values():
        by_module[info['module']]['files'] += 1
        by_module[info['module']]['code_lines'] += info['code_lines']
    sizes = [i['code_lines'] for p, i in files.items() if i['module'] != 'Tests']
    hot_total = Counter()
    for info in files.values():
        hot_total.update(info['hot'])
    dup_windows, dup_pairs = duplication(files)
    markers = Counter()
    for info in files.values():
        markers.update(info['markers'])

    def top(key, n=12, module_filter=None):
        rows = [(i[key], p) for p, i in files.items() if i[key] and (module_filter is None or module_filter(i['module']))]
        return sorted(rows, reverse=True)[:n]

    data = {
        'ref': git('rev-parse', '--short', 'HEAD').strip(),
        'branch': git('rev-parse', '--abbrev-ref', 'HEAD').strip(),
        'modules': dict(sorted(by_module.items())),
        'files': len(files),
        'code_lines': sum(i['code_lines'] for i in files.values()),
        'file_size_median': statistics.median(sizes) if sizes else 0,
        'file_size_p90': percentile(sizes, 90),
        'files_over_500': sum(1 for s in sizes if s > 500),
        'files_over_1000': sum(1 for s in sizes if s > 1000),
        'largest_files': top('code_lines', 15, lambda m: m != 'Tests'),
        'most_methods': top('methods', 10, lambda m: m != 'Tests'),
        'most_injects': top('injects', 10),
        'static_resolves': sum(i['static_resolves'] for i in files.values()),
        'static_resolve_files': top('static_resolves', 10),
        'version_checks_outside_versions': sum(i['version_checks'] for i in files.values()
                                               if i['module'] not in ('Gothic1', 'Gothic2')),
        'version_check_files': top('version_checks', 10, lambda m: m not in ('Gothic1', 'Gothic2')),
        'debug_log_calls': sum(i['debug_logs'] for p, i in files.items() if '/Logging/' not in p),
        'markers': dict(markers),
        'long_lines': sum(i['long_lines'] for i in files.values()),
        'per_frame_methods': sum(len(i['hot_methods']) for i in files.values()),
        'per_frame_smells': dict(hot_total),
        'per_frame_smell_files': sorted(((sum(i['hot'].values()), p) for p, i in files.items() if i['hot']),
                                        reverse=True)[:12],
        'duplicated_6_line_windows': dup_windows,
        'duplicated_pairs': [[a, b, n] for (a, b), n in dup_pairs],
        'events': events(files),
        'architecture_baseline_lines': sum(1 for l in open(BASELINE, encoding='utf-8-sig')
                                           if l.strip() and not l.startswith('#')) if os.path.exists(BASELINE) else None,
        'tests': sum(i['tests'] for i in files.values()),
        'adrs': len([f for f in git('ls-files', 'Docs/adr').splitlines() if f.endswith('.md')]),
        'history': history(files, args.days),
    }

    out = sys.stdout
    print(f"# Health metrics {data['branch']} @ {data['ref']}", file=out)
    print(f"files {data['files']}, code lines {data['code_lines']}, file size median {data['file_size_median']} "
          f"p90 {data['file_size_p90']}, >500: {data['files_over_500']}, >1000: {data['files_over_1000']}")
    print('modules: ' + ', '.join(f"{m} {v['files']}f/{v['code_lines']}l" for m, v in data['modules'].items()))
    print('largest: ' + ', '.join(f'{os.path.basename(p)} {n}' for n, p in data['largest_files']))
    print('most [Inject]: ' + ', '.join(f'{os.path.basename(p)} {n}' for n, p in data['most_injects']))
    print(f"static resolves {data['static_resolves']}: " +
          ', '.join(f'{os.path.basename(p)} {n}' for n, p in data['static_resolve_files']))
    print(f"IsGothic1/2 checks outside version modules {data['version_checks_outside_versions']}: " +
          ', '.join(f'{os.path.basename(p)} {n}' for n, p in data['version_check_files']))
    print(f"per-frame methods {data['per_frame_methods']}, smells inside them {data['per_frame_smells']}")
    print('per-frame smell files: ' + ', '.join(f'{os.path.basename(p)} {n}' for n, p in data['per_frame_smell_files']))
    print(f"duplicated 6-line windows {data['duplicated_6_line_windows']}; top pairs: " +
          '; '.join(f'{os.path.basename(a)}~{os.path.basename(b)} {n}' for a, b, n in data['duplicated_pairs'][:6]))
    ev = data['events']
    print(f"events {ev['count']}, without listener {ev['no_listener']}, never raised {ev['no_raiser']}")
    print(f"Debug.Log outside Logger {data['debug_log_calls']}, markers {data['markers']}, "
          f"lines >120 {data['long_lines']}, baseline lines {data['architecture_baseline_lines']}, "
          f"tests {data['tests']}, ADRs {data['adrs']}")
    h = data['history']
    print(f"change cost (last {h['commits_sampled']} commits): files/commit median {h['files_per_commit_median']} "
          f"p90 {h['files_per_commit_p90']}, code lines in touched files median {h['context_lines_per_commit_median']} "
          f"p90 {h['context_lines_per_commit_p90']}, fix commits touching 5+ files "
          f"{h['fix_commits_touching_5plus_files_pct']}%")
    print(f"hotspots (churn x size, {h['churn_days']} days): " +
          ', '.join(f"{os.path.basename(x['path'])} {x['churn']}x{x['code_lines']}" for x in h['hotspots']))

    if args.json:
        os.makedirs(os.path.dirname(os.path.abspath(args.json)), exist_ok=True)
        with open(args.json, 'w') as f:
            json.dump(data, f, indent=1)


if __name__ == '__main__':
    main()
