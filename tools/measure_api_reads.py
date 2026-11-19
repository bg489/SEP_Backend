"""Bounded read-only API timing sample; does not establish long-term stability."""
import argparse
from concurrent.futures import ThreadPoolExecutor
import json
import os
from pathlib import Path
import statistics
import time
import urllib.request


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--api', required=True)
    parser.add_argument('--requests', type=int, default=200)
    parser.add_argument('--concurrency', type=int, default=20)
    parser.add_argument('--output')
    args = parser.parse_args()
    if not 1 <= args.requests <= 2000 or not 1 <= args.concurrency <= 50:
        parser.error('Use 1-2000 requests and 1-50 clients')
    token = os.environ.get('ASX_ACCESS_TOKEN')
    headers = {'Authorization': 'Bearer ' + token} if token else {'X-Asx-Dev-Role': 'TEST_ENGINEER'}
    paths = ['/api/v1/scenarios?page=1&pageSize=12', '/api/v1/campaigns', '/api/v1/reference/maps']
    def one(index):
        start = time.perf_counter()
        request = urllib.request.Request(args.api.rstrip('/') + paths[index % len(paths)], headers=headers)
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                response.read()
                return (time.perf_counter() - start) * 1000, response.status, None
        except Exception as error:
            return (time.perf_counter() - start) * 1000, None, str(error)
    start = time.perf_counter()
    with ThreadPoolExecutor(max_workers=args.concurrency) as pool:
        observations = list(pool.map(one, range(args.requests)))
    elapsed = time.perf_counter() - start
    latencies = sorted(x[0] for x in observations)
    failures = [x[2] for x in observations if x[2] is not None]
    report = {'requests': args.requests, 'concurrency': args.concurrency, 'elapsedSeconds': round(elapsed, 3),
        'requestsPerSecond': round(args.requests / elapsed, 2), 'meanMs': round(statistics.mean(latencies), 2),
        'p95Ms': round(latencies[max(0, int(len(latencies) * .95) - 1)], 2), 'maxMs': round(max(latencies), 2),
        'errors': len(failures), 'errorSamples': failures[:3], 'paths': paths,
        'scope': 'Local read-only sample; no claim of shared staging or long-term availability'}
    if args.output: Path(args.output).write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))
    return 1 if failures else 0


if __name__ == '__main__':
    raise SystemExit(main())
