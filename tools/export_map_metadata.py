#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Xuất map metadata từ CARLA ra JSON để nạp vào asx.map_metadata (bước B6).

Validation tầng 3 cần biết mỗi map có bao nhiêu spawn point và chúng ở đâu.
Script này nối tới CARLA server đang chạy, duyệt từng map, lấy danh sách
spawn point rồi ghi ra một file JSON cho mỗi map.

Đã chạy thật 2026-10-08 trên máy cloud có CARLA 0.9.16: xuất 12 map (Town01–05,
Town10HD và bản _Opt), kết quả lưu ở map_metadata/. Bản _Opt có cùng tập spawn
point với bản gốc nhưng thứ tự khác, nên index không dùng lẫn được giữa hai bản.

Yêu cầu:
    - CARLA server đang chạy (./CarlaUE4.sh), mặc định localhost:2000
    - Python API của CARLA: pip install carla==<đúng phiên bản server>

Cách dùng:
    python tools/export_map_metadata.py --out map_metadata/
    python tools/export_map_metadata.py --out map_metadata/ --maps Town05 Town04
    python tools/export_map_metadata.py --out map_metadata/ --host 192.168.1.10 --port 2000

Sau khi có file JSON, sinh migration nạp DB bằng tools/build_map_metadata_migration.py
(cột spawn_points là jsonb; spawn_point_count là cột generated, KHÔNG ghi tay).

Định dạng file xuất ra khớp thứ mà tools/validate_scenario.py --metadata-dir đọc:
    {
      "map_name": "Town05",
      "carla_version": "0.9.16",
      "spawn_point_count": 302,
      "spawn_points": [{"index": 0, "x": 1.2, "y": 3.4, "z": 0.6, "yaw": 90.0}, ...]
    }

Hình dạng đường (--roads, 2026-10-26): ghi thêm <out>/roads/<MapName>.json cho trình xem map và
kiểm tra "điểm đặt nằm trên làn". Thư mục con nên các công cụ đọc <out>/*.json không thấy file này.
    {
      "map_name": "Town05", "carla_version": "0.9.16", "sample_distance_m": 2.0,
      "lanes": [{"road_id": 1, "section_id": 0, "lane_id": -1, "type": "Driving", "width": 3.5,
                 "junction": false, "points": [[x, y], ...]}, ...],
      "crosswalks": [[[x, y], ...], ...]
    }
Làn xe chạy lấy từ Map.generate_waypoints (một điểm mỗi sample_distance_m), làn khác (vỉa hè, lề,
chỗ đỗ) lấy bằng cách đi ngang get_left_lane()/get_right_lane() từ mỗi điểm đó. Điểm của một làn
xếp theo s (quãng dọc đường). Bản _Opt dùng chung hình dạng với bản gốc, chỉ thứ tự spawn point khác.

    python tools/export_map_metadata.py --out map_metadata/ --roads
    python tools/export_map_metadata.py --self-test        # kiểm phần gom làn/vạch qua đường, không cần CARLA
"""
from __future__ import annotations

import argparse
import json
import os
import sys

carla = None  # import trong main() để --self-test chạy được trên máy không có CARLA


def round_coord(value, digits=3):
    """Làm tròn toạ độ: CARLA trả float rất dài, giữ nguyên làm hash/diff rối mắt."""
    return round(float(value), digits)


def build_lanes(samples, digits=2):
    """Gom điểm mẫu thành polyline từng làn.

    samples: dict road_id, section_id, lane_id, s, x, y, type, width, junction. Một điểm có thể
    được thu hai lần (vỉa hè giữa hai làn xe) nên khử trùng theo (làn, s làm tròn 0.1 m).
    """
    lanes = {}
    for p in samples:
        key = (p["road_id"], p["section_id"], p["lane_id"])
        lane = lanes.setdefault(key, {"type": p["type"], "width": p["width"],
                                      "junction": p["junction"], "points": {}})
        lane["points"].setdefault(round(p["s"], 1), (round(p["x"], digits), round(p["y"], digits)))

    result = []
    for (road_id, section_id, lane_id), lane in sorted(lanes.items()):
        points = [list(lane["points"][s]) for s in sorted(lane["points"])]
        if len(points) < 2:
            continue  # một điểm thì không vẽ được đoạn nào
        result.append({"road_id": road_id, "section_id": section_id, "lane_id": lane_id,
                       "type": lane["type"], "width": round(lane["width"], 2),
                       "junction": lane["junction"], "points": points})
    return result


def split_polygons(points, digits=2, tolerance=1e-3):
    """Map.get_crosswalks() trả các đa giác nối liền nhau, mỗi cái kết thúc bằng điểm đầu của nó."""
    polygons, current, start = [], [], None
    for x, y in points:
        start = start or (x, y)
        current.append([round(x, digits), round(y, digits)])
        if len(current) > 3 and abs(x - start[0]) < tolerance and abs(y - start[1]) < tolerance:
            polygons.append(current[:-1])  # bỏ điểm đóng trùng điểm đầu
            current, start = [], None
    if len(current) >= 3:
        polygons.append(current)  # đa giác cuối không lặp điểm đầu
    return polygons


def export_roads(world_map, map_name, carla_version, step):
    """Hình dạng làn đường + vạch qua đường của map đang nạp."""
    samples = []

    def add(wp):
        loc = wp.transform.location
        samples.append({"road_id": wp.road_id, "section_id": wp.section_id, "lane_id": wp.lane_id,
                        "s": wp.s, "x": loc.x, "y": loc.y, "type": str(wp.lane_type),
                        "width": wp.lane_width, "junction": wp.is_junction})

    for wp in world_map.generate_waypoints(step):
        add(wp)
        for side in ("get_left_lane", "get_right_lane"):
            other = getattr(wp, side)()
            # Đi ra phía ngoài tới hết làn; dừng khi gặp làn xe chạy (đã có từ generate_waypoints).
            for _ in range(6):
                if other is None or other.lane_type == carla.LaneType.Driving:
                    break
                add(other)
                other = getattr(other, side)()

    crosswalks = [(loc.x, loc.y) for loc in world_map.get_crosswalks()]
    return {
        "map_name": map_name,
        "carla_version": carla_version,
        "sample_distance_m": step,
        "lanes": build_lanes(samples),
        "crosswalks": split_polygons(crosswalks),
    }


def self_test():
    samples = [
        {"road_id": 1, "section_id": 0, "lane_id": -1, "s": s, "x": s, "y": 0.0,
         "type": "Driving", "width": 3.5, "junction": False} for s in (4.0, 0.0, 2.0)
    ]
    samples.append(dict(samples[0]))  # vỉa hè/làn thu hai lần không được nhân đôi điểm
    samples.append({"road_id": 2, "section_id": 0, "lane_id": 1, "s": 0.0, "x": 9.0, "y": 9.0,
                    "type": "Sidewalk", "width": 2.0, "junction": False})
    lanes = build_lanes(samples)
    assert len(lanes) == 1, lanes  # làn chỉ có một điểm bị bỏ
    assert lanes[0]["points"] == [[0.0, 0.0], [2.0, 0.0], [4.0, 0.0]], lanes

    square = [(0, 0), (1, 0), (1, 1), (0, 1), (0, 0)]
    tri = [(5, 5), (6, 5), (6, 6), (5, 5)]
    assert split_polygons(square + tri) == [[[0, 0], [1, 0], [1, 1], [0, 1]], [[5, 5], [6, 5], [6, 6]]]
    assert split_polygons([(0, 0), (1, 0), (1, 1)]) == [[[0, 0], [1, 0], [1, 1]]]
    print("self-test OK")


def export_map(client, map_name, carla_version, timeout_s):
    """Nạp một map rồi trả dict metadata của nó."""
    world = client.load_world(map_name)
    world.wait_for_tick(timeout_s)

    spawn_points = world.get_map().get_spawn_points()
    points = []
    for index, transform in enumerate(spawn_points):
        location = transform.location
        rotation = transform.rotation
        points.append({
            "index": index,
            "x": round_coord(location.x),
            "y": round_coord(location.y),
            "z": round_coord(location.z),
            "yaw": round_coord(rotation.yaw),
        })

    return {
        "map_name": map_name,
        "carla_version": carla_version,
        "spawn_point_count": len(points),
        "spawn_points": points,
    }


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Xuất spawn point của các map CARLA ra JSON cho validation tầng 3.")
    parser.add_argument("--out", help="Thư mục ghi file JSON")
    parser.add_argument("--roads", action="store_true",
                        help="Ghi thêm hình dạng làn đường + vạch qua đường vào <out>/roads/")
    parser.add_argument("--road-step", type=float, default=2.0,
                        help="Khoảng cách giữa hai điểm trên một làn (m), mặc định 2")
    parser.add_argument("--self-test", action="store_true", help="Kiểm phần xử lý dữ liệu, không cần CARLA")
    parser.add_argument("--host", default="localhost", help="CARLA server host")
    parser.add_argument("--port", type=int, default=2000, help="CARLA server port")
    parser.add_argument("--timeout", type=float, default=30.0,
                        help="Timeout khi nối và khi nạp map (giây)")
    parser.add_argument("--maps", nargs="*", default=None,
                        help="Chỉ xuất các map này; bỏ trống = mọi map server có")
    args = parser.parse_args(argv)

    if args.self_test:
        self_test()
        return 0
    if not args.out:
        parser.error("cần --out")

    global carla
    try:
        import carla as _carla
    except ImportError:  # pragma: no cover
        sys.stderr.write(
            "Không import được module 'carla'.\n"
            "Cần chạy trên máy đã cài CARLA Python API: pip install carla==<phiên bản server>\n")
        return 2
    carla = _carla

    os.makedirs(args.out, exist_ok=True)
    if args.roads:
        os.makedirs(os.path.join(args.out, "roads"), exist_ok=True)

    client = carla.Client(args.host, args.port)
    client.set_timeout(args.timeout)
    carla_version = client.get_server_version()
    print("CARLA server %s tại %s:%d" % (carla_version, args.host, args.port))

    # get_available_maps() trả đường dẫn kiểu /Game/Carla/Maps/Town05
    available = [path.split("/")[-1] for path in client.get_available_maps()]
    available = sorted(set(available))

    targets = args.maps if args.maps else available
    unknown = [name for name in targets if name not in available]
    if unknown:
        sys.stderr.write("Map không có trên server: %s\nCó sẵn: %s\n"
                         % (", ".join(unknown), ", ".join(available)))
        return 2

    for map_name in targets:
        print("  đang xuất %s ..." % map_name, end="", flush=True)
        data = export_map(client, map_name, carla_version, args.timeout)
        path = os.path.join(args.out, "%s.json" % map_name)
        with open(path, "w", encoding="utf-8") as handle:
            json.dump(data, handle, ensure_ascii=False, indent=2)
        print(" %d spawn point -> %s" % (data["spawn_point_count"], path))

        if args.roads:
            roads = export_roads(client.get_world().get_map(), map_name, carla_version, args.road_step)
            path = os.path.join(args.out, "roads", "%s.json" % map_name)
            with open(path, "w", encoding="utf-8") as handle:
                json.dump(roads, handle, ensure_ascii=False, separators=(",", ":"))  # file lớn: bỏ khoảng trắng
            print("    %d làn, %d vạch qua đường, %d KB -> %s" % (
                len(roads["lanes"]), len(roads["crosswalks"]), os.path.getsize(path) // 1024, path))

    print("Xong %d map." % len(targets))
    return 0


if __name__ == "__main__":
    sys.exit(main())
