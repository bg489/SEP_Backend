"""Read-only comparison of the supplied core DBML against a disposable PostgreSQL.

PG* variables configure psql; PGDATABASE must start with asx_ci_. No SQL mutation
is executed. Additional Week 2 columns/tables are allowed and documented.
"""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parent.parent


def parse_dbml(text):
    tables = {}
    for match in re.finditer(r"(?m)^Table (\w+) \{", text):
        start, depth, cursor = match.end(), 1, match.end()
        while depth and cursor < len(text):
            depth += (text[cursor] == '{') - (text[cursor] == '}')
            cursor += 1
        body = text[start:cursor - 1]
        columns, unique, primary, refs = {}, [], [], []
        for line in body.splitlines():
            column = re.match(r"\s+(\w+)\s+(uuid|text|bigint|integer|boolean|timestamptz|jsonb|numeric)(?:\s+\[(.*)\])?\s*$", line)
            if column:
                name, typ, attrs = column.groups()
                attrs = attrs or ''
                columns[name] = {'type': typ, 'nullable': 'not null' not in attrs and not re.search(r'\bpk\b', attrs)}
                if re.search(r'\bpk\b', attrs): primary.append(name)
                if re.search(r'\bunique\b', attrs): unique.append([name])
                ref = re.search(r'ref:\s*[>\-]\s*(\w+)\.(\w+)', attrs)
                if ref: refs.append((name, *ref.groups()))
            index = re.match(r'\s+\(([^)]+)\)\s+\[unique\]', line)
            if index: unique.append([x.strip() for x in index[1].split(',')])
        tables[match[1]] = {'columns': columns, 'unique': unique, 'primary': primary, 'refs': refs}
    return tables


SQL = """
SELECT json_build_object(
 'columns',(SELECT json_agg(json_build_object('table',table_name,'name',column_name,'type',udt_name,'nullable',is_nullable='YES')) FROM information_schema.columns WHERE table_schema='asx'),
 'constraints',(SELECT json_agg(json_build_object('table',c.relname,'kind',k.contype,'definition',pg_get_constraintdef(k.oid))) FROM pg_constraint k JOIN pg_class c ON c.oid=k.conrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='asx'),
 'indexes',(SELECT json_agg(json_build_object('table',tablename,'definition',indexdef)) FROM pg_indexes WHERE schemaname='asx'));
"""


def compare(expected, actual):
    aliases = {'bool': 'boolean', 'int4': 'integer', 'int8': 'bigint'}
    columns = {(x['table'], x['name']): x for x in actual['columns']}
    issues = []
    for table, spec in expected.items():
        for name, col in spec['columns'].items():
            found = columns.get((table, name))
            if found is None:
                issues.append(f'{table}.{name}: missing column')
                continue
            if aliases.get(found['type'], found['type']) != col['type']:
                issues.append(f"{table}.{name}: type {found['type']} differs from {col['type']}")
            if found['nullable'] != col['nullable']:
                issues.append(f'{table}.{name}: nullability differs')
        definitions = [x['definition'] for x in actual['constraints'] if x['table'] == table]
        pk = 'PRIMARY KEY (' + ', '.join(spec['primary']) + ')'
        if pk not in definitions: issues.append(f'{table}: expected {pk}')
        for column, target, target_column in spec['refs']:
            prefix = f'FOREIGN KEY ({column}) REFERENCES asx.{target}({target_column})'
            if not any(x.startswith(prefix) for x in definitions): issues.append(f'{table}.{column}: missing FK to {target}.{target_column}')
        for index in spec['unique']:
            # The old unique pair prohibits preserving old attempts. Migration116
            # extends it with attempt + a unique partial index on is_current.
            if table == 'simulation_runs' and index == ['campaign_id', 'scenario_version_id']:
                keys = '(campaign_id, scenario_version_id, attempt)'
                current = '(campaign_id, scenario_version_id) WHERE is_current'
                indexes = [x['definition'] for x in actual['indexes'] if x['table'] == table]
                if not all(any('UNIQUE INDEX' in x and key in x for x in indexes) for key in (keys, current)):
                    issues.append('simulation_runs: linked attempts/current uniqueness missing')
            else:
                keys = '(' + ', '.join(index) + ')'
                if not any('UNIQUE INDEX' in x['definition'] and keys in x['definition'] for x in actual['indexes'] if x['table'] == table):
                    issues.append(f'{table}: missing unique {keys}')
    return issues


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--psql', default='psql')
    parser.add_argument('--output')
    args = parser.parse_args()
    if not os.environ.get('PGDATABASE', '').startswith('asx_ci_'):
        parser.error('PGDATABASE must name an isolated asx_ci_ database')
    result = subprocess.run([args.psql, '-X', '-At', '-v', 'ON_ERROR_STOP=1', '-c', SQL], capture_output=True, text=True, encoding='utf-8', check=True)
    expected = parse_dbml((ROOT / 'docs/schema-core.dbml').read_text(encoding='utf-8-sig'))
    actual = json.loads(result.stdout)
    issues = compare(expected, actual)
    report = {'coreTables': len(expected), 'coreColumns': sum(len(t['columns']) for t in expected.values()), 'issues': issues,
              'extensions': ['simulation_workers', 'system_settings', 'similarity_analyses', 'linked run attempts/current uniqueness'], 'accepted': not issues}
    if args.output: Path(args.output).write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))
    return 0 if not issues else 1


if __name__ == '__main__':
    raise SystemExit(main())
