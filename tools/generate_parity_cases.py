#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Sinh bộ kịch bản để đối chiếu validator C# và Python ở mọi nhánh luật tầng 3 (và các lỗi tầng 1 hay gặp).

Gồm các ca nhắm sẵn (mỗi nhánh lỗi/cảnh báo của MapPositionRule, SpawnOverlapRule,
RouteFeasibilityRule, TriggerAtStartRule) và các ca ngẫu nhiên trên cả 12 map với seed cố định
(start/goal/actor là spawn point, toạ độ cạnh đường hoặc toạ độ tuỳ ý, waypoint, speed limit,
timeout, trigger). Dựng từ examples/S23_pedestrian_crossing_rain.json nên luôn qua tầng 1-2.

    .venv/bin/python tools/generate_parity_cases.py /tmp/asx-parity
    ASX_API_BASE=http://localhost:5080 ASX_METADATA_DIR=map_metadata \
        .venv/bin/python tools/compare_validators.py examples/*.json examples/*.yaml /tmp/asx-parity/*.json

Thư mục đích phải chưa tồn tại hoặc rỗng. Kết quả 2026-10-09: 16 ca nhắm + 60 ca ngẫu nhiên,
cùng 13 ví dụ: 89 file, 0 khác biệt. 2026-10-14 thêm 18 ca tầng 1 (s_*): 107 file, 0 khác biệt.
"""
import argparse
import copy
import glob
import json
import os
import random

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
parser.add_argument("out", help="Thư mục ghi file kịch bản (chưa tồn tại hoặc rỗng)")
parser.add_argument("--random", type=int, default=60, help="Số ca ngẫu nhiên (mặc định 60)")
parser.add_argument("--seed", type=int, default=20261009, help="Seed ngẫu nhiên (mặc định 20261009)")
args = parser.parse_args()
out = os.path.abspath(args.out)   # trước os.chdir để đường dẫn tương đối vẫn đúng
if os.path.isdir(out) and os.listdir(out):
    raise SystemExit("Thư mục đích không rỗng: %s" % out)
os.makedirs(out, exist_ok=True)
os.chdir(ROOT)
base = json.load(open('examples/S23_pedestrian_crossing_rain.json'))
maps = {}
for f in glob.glob('map_metadata/*.json'):
    d = json.load(open(f)); maps[d['map_name']] = d['spawn_points']

def save(name, doc):
    json.dump(doc, open(os.path.join(out, name + '.json'), 'w'), ensure_ascii=False, indent=1)

def pt(x, y, z=1.0): return {'x': x, 'y': y, 'z': z}
def idx(i): return {'spawn_point_index': i}
def after(ref, delay): return {'type': 'after_event', 'event_ref': ref, 'delay_s': delay}
def light(event_id, trigger):
    return {'id': event_id, 'type': 'traffic_light_override', 'trigger': trigger, 'params': {'state': 'red', 'duration_s': 10}}
def crossing(d, destination, actor='veh_cross', behavior='scripted', **params):
    """Thêm xe veh_cross (spawn #188 Town05) và event vehicle_crossing chạy tới destination."""
    d['actors'].append({'id': 'veh_cross', 'type': 'vehicle', 'blueprint': 'vehicle.audi.a2', 'spawn': idx(188), 'behavior': behavior})
    d['events'].append({'id': 'ev_cross', 'type': 'vehicle_crossing', 'actor_ref': actor,
                        'trigger': {'type': 'simulation_time', 'time_s': 5},
                        'params': dict({'destination': destination, 'speed_kmh': 30}, **params)})

# --- nhắm từng nhánh (giống ValidationRuleTests) ---
cases = {
 'off_map_actor': lambda d: d['actors'][0].__setitem__('spawn', pt(400, 0)),
 'far_actor': lambda d: d['actors'][0].__setitem__('spawn', pt(105, 58)),
 'trigger_location_off_map': lambda d: d['events'][0].__setitem__('trigger', {'type': 'ego_distance_to_location', 'location': pt(-400, 0), 'distance_m': 10}),
 'trigger_location_at_start': lambda d: d['events'][0].__setitem__('trigger', {'type': 'ego_distance_to_location', 'location': idx(11), 'distance_m': 5}),
 'overlap_index': lambda d: d['actors'].append({'id': 'veh_01', 'type': 'vehicle', 'blueprint': 'vehicle.audi.a2', 'spawn': idx(12), 'behavior': 'idle'}),
 'overlap_close': lambda d: d['actors'].append({'id': 'ped_02', 'type': 'pedestrian', 'blueprint': 'walker.pedestrian.0002', 'spawn': pt(-29.6, -80.9), 'behavior': 'idle'}),
 'overlap_coord_on_ego_spawn': lambda d: d['actors'].append({'id': 'veh_01', 'type': 'vehicle', 'blueprint': 'vehicle.audi.a2', 'spawn': pt(-44.3, -58.58, 0.45), 'behavior': 'idle'}),
 'goal_in_tolerance': lambda d: d['route'].__setitem__('goal', idx(11)),
 'loop_with_waypoint': lambda d: (d['route'].__setitem__('goal', idx(11)), d['route'].__setitem__('waypoints', [idx(87)])),
 'timeout_error': lambda d: (d['route'].__setitem__('goal', idx(234)), d['route'].__setitem__('speed_limit_kmh', 20), d['execution'].__setitem__('timeout_s', 45)),
 'timeout_warning': lambda d: (d['route'].__setitem__('goal', idx(234)), d['execution'].__setitem__('timeout_s', 40)),
 'timeout_waypoints': lambda d: (d['route'].__setitem__('waypoints', [idx(234), pt(150.5, 150.25)]), d['execution'].__setitem__('timeout_s', 60)),
 'no_speed_limit': lambda d: (d['route'].__setitem__('goal', idx(234)), d['route'].pop('speed_limit_kmh'), d['execution'].__setitem__('timeout_s', 10)),
 'trigger_at_start': lambda d: d['actors'][0].__setitem__('spawn', pt(-40, -65)),
 'coordinate_start_goal': lambda d: (d['route'].__setitem__('start', pt(-44.0, -60.0, 0.5)), d['route'].__setitem__('goal', pt(-44.0, -63.5, 0.5))),
 'waypoint_bad_index': lambda d: d['route'].__setitem__('waypoints', [idx(999)]),
 # trigger mở rộng 2026-10-17
 'after_event_ok': lambda d: d['events'].append(light('ev_light', after('ev_ped_cross', 3))),
 'after_event_missing': lambda d: d['events'].append(light('ev_light', after('ev_missing', 3))),
 'after_event_self': lambda d: d['events'].append(light('ev_light', after('ev_light', 3))),
 'after_event_loop': lambda d: (d['events'].append(light('ev_a', after('ev_b', 1))), d['events'].append(light('ev_b', after('ev_a', 1)))),
 'after_event_timeout': lambda d: (d['events'].append(light('ev_first', {'type': 'simulation_time', 'time_s': 100})), d['events'].append(light('ev_second', after('ev_first', 30.5)))),
 'after_event_param_delay': lambda d: (d['events'].append(light('ev_first', {'type': 'simulation_time', 'time_s': 110})), d['events'].append(light('ev_second', after('ev_first', {'$param': 'trigger_distance_m'})))),
 'ttc_ok': lambda d: d['events'][0].__setitem__('trigger', {'type': 'time_to_collision', 'actor_ref': 'ped_01', 'ttc_s': 2}),
 # tốc độ thử của ego 2026-10-20: TTC đã thoả lúc xuất phát, route theo target speed
 'ttc_at_start': lambda d: (d['actors'][0].__setitem__('spawn', pt(-40, -65)), d['ego_vehicle'].__setitem__('initial_speed_kmh', 36),
     d['events'][0].__setitem__('trigger', {'type': 'time_to_collision', 'actor_ref': 'ped_01', 'ttc_s': 2})),
 'target_speed_error': lambda d: (d['route'].__setitem__('goal', idx(234)), d['route'].__setitem__('speed_limit_kmh', 40), d['execution'].__setitem__('timeout_s', 45),
     d['ego_vehicle'].__setitem__('target_speed_kmh', {'$param': 'ego_speed_kmh'}),
     d['parameters'].__setitem__('ego_speed_kmh', {'type': 'number', 'value': 20, 'min': 10, 'max': 60, 'step': 5})),
 # pedestrian_crossing.sync 2026-10-21: offset mặc định 0, điểm ngoài map
 'pedestrian_sync_ok': lambda d: d['events'][0]['params'].__setitem__('sync', {'point': pt(-44.3, -73.6, 0.5)}),
 'pedestrian_sync_off_map': lambda d: d['events'][0]['params'].__setitem__('sync', {'point': pt(-400, 0), 'offset_s': 0.25}),
 # lead_vehicle_brake.time_gap_s 2026-10-21
 'lead_time_gap_param_range': lambda d: (d['parameters'].__setitem__('gap_s', {'type': 'number', 'value': 1, 'min': 0, 'max': 10}),
     d['actors'].append({'id': 'veh_lead', 'type': 'vehicle', 'blueprint': 'vehicle.audi.a2', 'spawn': idx(188), 'behavior': 'scripted'}),
     d['events'].append({'id': 'ev_brake', 'type': 'lead_vehicle_brake', 'actor_ref': 'veh_lead', 'trigger': {'type': 'simulation_time', 'time_s': 5},
                         'params': {'deceleration_ms2': 4, 'time_gap_s': {'$param': 'gap_s'}}})),
 'target_speed_above_limit': lambda d: (d['route'].__setitem__('goal', idx(234)), d['route'].__setitem__('speed_limit_kmh', 40), d['execution'].__setitem__('timeout_s', 45),
     d['ego_vehicle'].__setitem__('target_speed_kmh', 60)),
 'ttc_unknown_actor': lambda d: d['events'][0].__setitem__('trigger', {'type': 'time_to_collision', 'actor_ref': 'ped_99', 'ttc_s': 2}),
 # vehicle_crossing 2026-10-19
 'crossing_ok': lambda d: crossing(d, idx(128), ignore_traffic_lights=True),
 'crossing_pedestrian_actor': lambda d: crossing(d, idx(128), actor='ped_01'),
 'crossing_autopilot_actor': lambda d: crossing(d, idx(128), behavior='autopilot'),
 'crossing_destination_bad_index': lambda d: crossing(d, idx(999)),
 'crossing_destination_off_map': lambda d: crossing(d, pt(-400, 0)),
 'crossing_destination_far': lambda d: crossing(d, pt(105, 58)),
 'sync_ok': lambda d: crossing(d, idx(128), sync={'point': pt(-195.1, 6.6, 0.6), 'offset_s': 0.5}),
 'sync_point_off_map': lambda d: crossing(d, idx(128), sync={'point': pt(-400, 0)}),
 'sync_point_bad_index': lambda d: crossing(d, idx(128), sync={'point': idx(999)}),
 'sync_offset_param_out_of_domain': lambda d: crossing(d, idx(128), sync={'point': idx(128), 'offset_s': {'$param': 'trigger_distance_m'}}),
}
for name, change in cases.items():
    d = copy.deepcopy(base); change(d); save('t_' + name, d)

# --- tầng 1: path phải khớp; thông điệp C# viết lại cho dễ hiểu nên không so (giống SchemaMessageTests) ---
ev = lambda d: d['events'][0]
schema_cases = {
 'actor_ref_empty': lambda d: ev(d).__setitem__('actor_ref', ''),
 'trigger_actor_empty': lambda d: ev(d)['trigger'].__setitem__('actor_ref', ''),
 'speed_above_max': lambda d: ev(d)['params'].__setitem__('speed_ms', 10),
 'param_ref_empty': lambda d: ev(d)['params'].__setitem__('speed_ms', {'$param': ''}),
 'spawn_negative': lambda d: d['route'].__setitem__('start', idx(-1)),
 'coordinates_without_y': lambda d: d['route'].__setitem__('start', {'x': 1}),
 'unknown_setting': lambda d: d['route'].__setitem__('foo', 1),
 'missing_goal': lambda d: d['route'].pop('goal'),
 'direction_enum': lambda d: ev(d)['params'].__setitem__('direction', 'up'),
 'text_instead_of_number': lambda d: d['route'].__setitem__('speed_limit_kmh', 'fast'),
 'map_name_pattern': lambda d: d['map'].__setitem__('name', 'Paris'),
 'trigger_time_above_max': lambda d: ev(d).__setitem__('trigger', {'type': 'simulation_time', 'time_s': 5000}),
 'trigger_without_actor': lambda d: ev(d).__setitem__('trigger', {'type': 'ego_distance_to_actor', 'distance_m': 5}),
 'trigger_type_unknown': lambda d: ev(d)['trigger'].__setitem__('type', 'boom'),
 'ttc_above_max': lambda d: ev(d).__setitem__('trigger', {'type': 'time_to_collision', 'actor_ref': 'ped_01', 'ttc_s': 20}),
 'after_event_without_ref': lambda d: ev(d).__setitem__('trigger', {'type': 'after_event', 'delay_s': 2}),
 'text_in_param_field': lambda d: ev(d)['params'].__setitem__('speed_ms', 'fast'),
 'parameter_name_pattern': lambda d: d['parameters'].__setitem__('Bad Name', copy.deepcopy(d['parameters']['npc_count'])),
 'step_zero': lambda d: d['parameters']['npc_count'].__setitem__('step', 0),
 'crossing_without_destination': lambda d: crossing(d, idx(128)) or d['events'][1]['params'].pop('destination'),
 'crossing_speed_above_max': lambda d: crossing(d, idx(128), speed_kmh=150),
 'sync_without_point': lambda d: crossing(d, idx(128), sync={'offset_s': 1}),
 'sync_offset_above_max': lambda d: crossing(d, idx(128), sync={'point': idx(128), 'offset_s': 9}),
 'crossing_lights_not_bool': lambda d: crossing(d, idx(128), ignore_traffic_lights='yes'),
 'traffic_light_with_actor': lambda d: d['events'].__setitem__(0, {'id': 'ev_light', 'type': 'traffic_light_override', 'actor_ref': 'ped_01',
     'trigger': {'type': 'simulation_time', 'time_s': 5}, 'params': {'state': 'red', 'duration_s': 10}}),
}
for name, change in schema_cases.items():
    d = copy.deepcopy(base); change(d); save('s_' + name, d)

# --- ngẫu nhiên, seed cố định ---
rng = random.Random(args.seed)
for n in range(args.random):
    d = copy.deepcopy(base)
    mname = rng.choice(sorted(maps)); pts = maps[mname]; count = len(pts)
    d['map']['name'] = mname
    xs = [p['x'] for p in pts]; ys = [p['y'] for p in pts]
    def rand_loc():
        r = rng.random()
        if r < 0.45: return idx(rng.randrange(count + (3 if rng.random() < 0.1 else 0)))
        p = rng.choice(pts)
        if r < 0.75: return pt(round(p['x'] + rng.uniform(-6, 6), 2), round(p['y'] + rng.uniform(-6, 6), 2), round(p['z'] + rng.uniform(0, 1), 2))
        return pt(round(rng.uniform(min(xs) - 120, max(xs) + 120), 1), round(rng.uniform(min(ys) - 120, max(ys) + 120), 1))
    d['route']['start'] = rand_loc(); d['route']['goal'] = rand_loc()
    if d['route']['start'] == d['route']['goal']: d['route']['goal'] = idx((d['route']['start'].get('spawn_point_index', 0) + 1) % count)
    if rng.random() < 0.3: d['route']['waypoints'] = [rand_loc() for _ in range(rng.randint(1, 2))]
    d['route']['goal_tolerance_m'] = rng.choice([1, 2.5, 5, 10, 25, 50])
    if rng.random() < 0.8: d['route']['speed_limit_kmh'] = rng.choice([5, 10, 20, 30, 40, 50, 90, 130])
    else: d['route'].pop('speed_limit_kmh', None)
    d['execution']['timeout_s'] = rng.choice([10, 20, 30, 45, 60, 120, 300])
    start = d['route']['start']
    d['actors'][0]['spawn'] = rand_loc() if rng.random() < 0.7 else (copy.deepcopy(start) if 'x' in start else pt(-1, -1))
    for k in range(rng.randint(0, 2)):
        loc = rand_loc() if rng.random() < 0.6 else copy.deepcopy(rng.choice([start, d['actors'][0]['spawn']]))
        if rng.random() < 0.3 and 'x' in loc: loc = dict(loc, x=round(loc['x'] + rng.uniform(-0.9, 0.9), 2))
        d['actors'].append({'id': 'a%d' % k, 'type': rng.choice(['vehicle', 'pedestrian']), 'blueprint': '', 'spawn': loc, 'behavior': 'idle'})
        d['actors'][-1]['blueprint'] = 'vehicle.audi.a2' if d['actors'][-1]['type'] == 'vehicle' else 'walker.pedestrian.0003'
    d['parameters']['trigger_distance_m']['value'] = rng.choice([5, 10, 15, 30])
    if rng.random() < 0.25:
        d['events'][0]['trigger'] = {'type': 'ego_distance_to_location', 'location': rand_loc(), 'distance_m': rng.choice([5, 20, 60, 200])}
    d['traffic']['npc_vehicles']['count'] = 0
    d['parameters']['npc_count']['value'] = 0
    save('r_%02d' % n, d)
print(len(cases), 'targeted +', len(schema_cases), 'schema +', args.random, 'random files in', out)
