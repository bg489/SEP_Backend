#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Sinh file SQL nạp asx.map_metadata từ các file JSON của export_map_metadata.py (bước B6).

Script kiểm tra từng file trước khi sinh SQL: tên file khớp map_name, map_name
đúng ràng buộc CHECK của bảng, spawn_point_count bằng số phần tử, index chạy
liên tục 0..n-1, toạ độ là số hữu hạn. Một file sai thì không sinh gì cả.

SQL sinh ra chạy lại nhiều lần không lỗi:
  * cùng (map_name, carla_version) -> ghi đè spawn_points và bật active;
  * bản metadata của phiên bản CARLA khác cùng tên map -> tắt active
    (mỗi map chỉ có 1 bản active, xem ux_map_metadata_active).
spawn_point_count là cột generated nên không ghi.

Cách dùng:
    python3 tools/build_map_metadata_migration.py map_metadata/ \
        --out database/migrations/104_load_map_metadata_carla_0916.sql
"""
from __future__ import annotations

import argparse
import json
import math
import os
import re
import sys

MAP_NAME_PATTERN = re.compile(r"^Town[0-9]{2}(HD)?(_Opt)?$")   # khớp CHECK trong 001
POINT_KEYS = {"index", "x", "y", "z", "yaw"}


def load_and_check(path):
    """Đọc một file metadata, trả (dict, danh sách lỗi)."""
    with open(path, "r", encoding="utf-8") as handle:
        data = json.load(handle)

    errors = []
    name = data.get("map_name")
    if not isinstance(name, str) or not MAP_NAME_PATTERN.match(name):
        errors.append("map_name '%s' không khớp ràng buộc của bảng" % name)
    elif os.path.basename(path) != name + ".json":
        errors.append("tên file khác map_name '%s'" % name)
    if not isinstance(data.get("carla_version"), str) or not data["carla_version"]:
        errors.append("thiếu carla_version")

    points = data.get("spawn_points")
    if not isinstance(points, list) or not points:
        errors.append("spawn_points rỗng hoặc không phải mảng")
        return data, errors
    if data.get("spawn_point_count") != len(points):
        errors.append("spawn_point_count=%s nhưng có %d điểm" % (data.get("spawn_point_count"), len(points)))
    for position, point in enumerate(points):
        if not isinstance(point, dict) or set(point) != POINT_KEYS:
            errors.append("điểm thứ %d sai khoá" % position)
            break
        if point["index"] != position:
            errors.append("index %s ở vị trí %d" % (point["index"], position))
            break
        if not all(isinstance(point[k], (int, float)) and not isinstance(point[k], bool)
                   and math.isfinite(point[k]) for k in ("x", "y", "z", "yaw")):
            errors.append("điểm %d có toạ độ không hợp lệ" % position)
            break
    return data, errors


def sql_literal(text):
    return "'" + text.replace("'", "''") + "'"


def build_sql(maps):
    versions = sorted({m["carla_version"] for m in maps})
    lines = [
        "-- =====================================================================",
        "-- AutoScenarioX - B6: nạp map metadata từ CARLA %s" % ", ".join(versions),
        "--",
        "-- SINH TỰ ĐỘNG bởi tools/build_map_metadata_migration.py từ thư mục map_metadata/",
        "-- (xuất bằng tools/export_map_metadata.py). Không sửa tay; muốn đổi thì xuất lại",
        "-- rồi sinh file migration mới.",
        "--",
        "-- Lưu ý: bản _Opt có cùng tập spawn point với bản gốc nhưng THỨ TỰ KHÁC, nên",
        "-- cùng spawn_point_index ở Town05 và Town05_Opt là hai vị trí khác nhau.",
        "-- Chạy lại nhiều lần không lỗi. spawn_point_count là cột generated, không ghi.",
        "--",
        "-- Maps (%d):" % len(maps),
    ]
    for m in maps:
        lines.append("--   %-14s %4d spawn point" % (m["map_name"], len(m["spawn_points"])))
    lines += ["-- =====================================================================", "", "BEGIN;", ""]

    for m in maps:
        name = sql_literal(m["map_name"])
        version = sql_literal(m["carla_version"])
        # Dollar quoting: JSON toạ độ không thể chứa chuỗi $json$.
        points = json.dumps(m["spawn_points"], ensure_ascii=False, separators=(",", ":"))
        lines += [
            "-- %s" % m["map_name"],
            "UPDATE asx.map_metadata SET is_active = false",
            "WHERE map_name = %s AND carla_version <> %s AND is_active;" % (name, version),
            "INSERT INTO asx.map_metadata (map_name, carla_version, spawn_points, is_active)",
            "VALUES (%s, %s, $json$%s$json$::jsonb, true)" % (name, version, points),
            "ON CONFLICT (map_name, carla_version) DO UPDATE",
            "SET spawn_points = EXCLUDED.spawn_points, is_active = true, imported_at = now();",
            "",
        ]

    lines += [
        "COMMIT;",
        "",
        "-- Kiểm tra sau khi chạy:",
        "-- SELECT map_name, carla_version, spawn_point_count, is_active",
        "-- FROM asx.map_metadata ORDER BY map_name;",
        "",
    ]
    return "\n".join(lines)


def main(argv=None):
    parser = argparse.ArgumentParser(description="Sinh SQL nạp asx.map_metadata từ JSON đã xuất.")
    parser.add_argument("metadata_dir", help="Thư mục chứa <MapName>.json")
    parser.add_argument("--out", required=True, help="File SQL sẽ ghi")
    args = parser.parse_args(argv)

    files = sorted(f for f in os.listdir(args.metadata_dir) if f.endswith(".json"))
    if not files:
        sys.stderr.write("Không có file .json trong %s\n" % args.metadata_dir)
        return 2

    maps, failed = [], False
    for name in files:
        data, errors = load_and_check(os.path.join(args.metadata_dir, name))
        if errors:
            failed = True
            sys.stderr.write("%s: %s\n" % (name, "; ".join(errors)))
        else:
            maps.append(data)
            print("  %-14s %s %4d spawn point" % (data["map_name"], data["carla_version"], len(data["spawn_points"])))
    if failed:
        sys.stderr.write("Có file lỗi, không sinh SQL.\n")
        return 1

    with open(args.out, "w", encoding="utf-8") as handle:
        handle.write(build_sql(maps))
    print("Đã ghi %d map -> %s" % (len(maps), args.out))
    return 0


if __name__ == "__main__":
    sys.exit(main())
