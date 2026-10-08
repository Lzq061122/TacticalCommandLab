import hashlib
import json
import subprocess
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[2]
out = Path(__file__).resolve().parent
runs = sorted((root / '_audit/_local-phase10-20261006').glob('run*'))
runs = [p for p in runs if p.is_dir() and p.name[3:5].isdigit() and 4 <= int(p.name[3:5]) <= 11]
result = {'review_runtime': 'NOT_RUN', 'runs': [], 'manifest_count': 0, 'manifest_errors': []}
def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()
def rows(p):
    found = []
    for i, line in enumerate(p.read_text(encoding='utf-8-sig').splitlines(), 1):
        if line.startswith('t='):
            r = dict(item.split('=', 1) for item in line.split('|'))
            r['line'] = i
            found.append(r)
    return found
for run in runs:
    a = json.loads((run / 'assessment.json').read_text(encoding='utf-8-sig'))
    manifest = json.loads((run / 'artifact-manifest.json').read_text(encoding='utf-8-sig'))
    for item in manifest:
        p = run / item['relative_path']
        result['manifest_count'] += 1
        if not p.exists() or p.stat().st_size != item['length'] or sha(p) != item['sha256']:
            result['manifest_errors'].append(str(p))
    telemetry = run / a['telemetry']
    script = (run / 'script-seeded.txt').read_text(encoding='utf-8-sig')
    commands = [line for line in script.splitlines() if line.strip() and not line.lstrip().startswith('#')]
    proc = subprocess.run([sys.executable, str(root / 'tools/recompute_telemetry.py'), str(telemetry)], capture_output=True)
    if proc.returncode:
        raise RuntimeError(proc.stderr.decode('utf-8', errors='replace'))
    (out / (run.name + '.recompute.json')).write_bytes(proc.stdout)
    fresh = json.loads(proc.stdout)
    previous = json.loads((root / '_audit/2026-10-07-phase11-evidence/recompute' / (run.name + '.recompute.json')).read_text(encoding='utf-8-sig'))
    normalized = json.loads(json.dumps(previous))
    normalized['input']['path'] = fresh['input']['path']
    all_rows = rows(telemetry)
    target = [r for r in all_rows if r['E'] == 'T1F0' and r['D'] == 'Movement']
    native = [r for r in target if r['W'] == 'Native']
    restore = [r for r in target if r['W'] == 'Lab' and float(r['t']) > 27]
    later = [r for r in native if float(r['t']) > 28]
    result['runs'].append({
        'run': run.name, 'game_pid': a['game_pid'], 'source_head': a['source_head'],
        'manifest_entries': len(manifest), 'metrics_hash_match': sha(run / a['metrics']) == a['metrics_sha256'],
        'telemetry_hash_match': sha(telemetry) == a['telemetry_sha256'],
        'script_hash_match': sha(run / 'script-seeded.txt') == a['script_sha256'],
        'script_commands': commands, 'verdicts': a['verdicts'],
        'fresh_recompute_exit': proc.returncode, 'fresh_recompute_equal_to_delivery': fresh == previous,
        'fresh_recompute_equal_except_input_path': fresh == normalized,
        'recompute_status': fresh['assessment_status'],
        'first_override_pairs': fresh['evidence']['first_override_latency']['pairs'],
        'target_native_rejected_all': sum(r['rej'] == '1' for r in native),
        'target_native_rejected_3_28': sum(r['rej'] == '1' and 3 <= float(r['t']) < 28 for r in native),
        'target_native_rejected_3_5_28': sum(r['rej'] == '1' and 3.5 <= float(r['t']) < 28 for r in native),
        'first_restore_lab': restore[:1], 'first_native_after_28': later[:1],
        'first_native_gap_after_restore': round(float(later[0]['t']) - float(restore[0]['t']), 4) if restore and later else None,
    })
result['reviewed_files'] = [{'path': str(p.relative_to(root)), 'sha256': sha(p), 'lines': len(p.read_text(encoding='utf-8-sig').splitlines())} for p in [root / 'docs/07-final-architecture.md', root / 'HANDOFF.phase11.md', root / '_audit/2026-10-07-phase11-evidence-review.md']]
freeze = root / 'docs/evidence/phase10-closure-20261007'
dev = root / '_audit/2026-10-07-phase11-evidence/g3-rebuild'
result['frozen_csv_comparison'] = [{'file': p.name, 'equal': sha(p) == sha(freeze / p.name)} for p in sorted(dev.glob('*.csv'))]
(out / 'independent-checks.json').write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding='utf-8')
print(json.dumps({'manifest_count': result['manifest_count'], 'manifest_errors': result['manifest_errors'], 'runs': [{k: r[k] for k in ('run', 'game_pid', 'fresh_recompute_equal_to_delivery', 'recompute_status', 'target_native_rejected_all', 'target_native_rejected_3_5_28', 'first_native_gap_after_restore')} for r in result['runs']], 'csv_match': all(r['equal'] for r in result['frozen_csv_comparison'])}, indent=2))
