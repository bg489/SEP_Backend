"""One recorder process per simulation; no HTTP requests or queue operations.

CARLA's native Traffic Manager/sensor clients must not survive a server restart
between two different towns. The queue worker stays alive; this process exits.
"""
import json
import os
from pathlib import Path
import sys

import carla_recorder


def main():
    request_path, result_path = map(Path, sys.argv[1:3])
    job = json.loads(request_path.read_text(encoding="utf-8"))
    try:
        telemetry, video = carla_recorder.record(job["package"], **job["options"])
        result = {"telemetry": os.path.abspath(telemetry), "video": os.path.abspath(video) if video else None}
        code = 0
    except (Exception, SystemExit) as error:
        result = {"error": str(error)}
        code = 1
    result_path.write_text(json.dumps(result), encoding="utf-8")
    return code


if __name__ == "__main__":
    raise SystemExit(main())
