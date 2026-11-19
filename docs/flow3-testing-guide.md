# Hướng dẫn thử Flow 3 (Oracle) từ đầu tới cuối

Flow 3 chấm **simulation run** của Flow 2: runner chạy scenario trong CARLA, ghi Telemetry (`asx.telemetry/1`),
nộp cho API; Oracle chấm ngay và lưu Evaluation + Violations. Hợp đồng API: [oracle-api.md](oracle-api.md).
Định dạng Telemetry: [telemetry-format.md](telemetry-format.md). Lý do thiết kế: [ADR 0001](adr/0001-offline-versioned-oracle.md).

Có hai cách thử:

- **6A. Telemetry mẫu**: không cần CARLA, kiểm API + Oracle + giao diện trong 1 phút.
- **6B. CARLA thật**: chạy scenario thật, Oracle chấm bản ghi thật.

Các lệnh dưới là PowerShell trên Windows. `<REPO>` là thư mục repo, `<CARLA_ROOT>` là thư mục CARLA 0.9.16 đã giải nén
(ví dụ `D:\CARLA_0.9.16`). Đường dẫn CARLA nên chỉ có ký tự ASCII.

## 0. Chuẩn bị một lần

| Cần | Ghi chú |
|---|---|
| Migration `database/migrations/108_flow3_oracle.sql` đã chạy | DB chung đã chạy. `GET /api/health/db` phải trả `status: OK` và `missingTables` rỗng |
| CARLA 0.9.16 bản Windows | tải `CARLA_0.9.16.zip` từ GitHub releases của CARLA |
| **Python 3.12** | wheel `carla==0.9.16` không có cho 3.13+. Cài song song bản khác được: `py install 3.12` hoặc `winget install Python.Python.3.12` |

Tạo venv cho CARLA ở thư mục repo (`.venv/` đã gitignore):

```powershell
cd <REPO>
py -3.12 -m venv .venv
.venv\Scripts\activate
pip install carla==0.9.16 numpy networkx shapely
```

Gói `carla` trên pip **không** có `agents` (BehaviorAgent); script lấy nó từ `<CARLA_ROOT>\PythonAPI\carla` qua biến
`CARLA_ROOT`. `requirements.txt` trong bản CARLA tải về chỉ để build, nên cài thẳng `numpy networkx shapely` như trên.

## 1. Bật CARLA server (cửa sổ T1)

**Chỉ chạy một server.** Kiểm tra trước: `Get-Process CarlaUE4*` mà đã có `CarlaUE4-Win64-Shipping` thì không bật thêm.
Nhiều server cùng nghe cổng 2000 và chia nhau GPU sẽ làm CARLA crash (`EXCEPTION_ACCESS_VIOLATION`) khi nạp map.

```powershell
& "<CARLA_ROOT>\CarlaUE4.exe" -quality-level=Low -windowed -ResX=1280 -ResY=720
```

Cửa sổ CARLA hiện lên; khi chạy scenario (bước 6B) camera tự bám sau xe ego. Không cần xem thì thêm
`-RenderOffScreen` (không mở cửa sổ, nhẹ GPU hơn). GPU 4 GB chỉ chịu được `-quality-level=Low`. Cần tắt hết để bật lại: `Get-Process CarlaUE4* | Stop-Process -Force`.

## 2. Chạy API (T2)

```powershell
cd <REPO>\backend
$env:DevAuth__Role = "ADMIN"
dotnet run --project AutoScenarioX.Api --launch-profile http
```

Ở `Auth:Mode=Dev`, giao diện lấy role của user Dev trong DB (Scenario Engineer, nên vào được web), còn API phân quyền
theo `DevAuth__Role`. Nộp Telemetry cần Test Engineer hoặc Admin, vì vậy đặt `ADMIN`. Chỉ là mẹo cho chế độ Dev.

Kiểm tra: http://localhost:5080/api/health/db -> `"status":"OK"` và `missingTables` rỗng.

## 3. Chạy giao diện (T3)

```powershell
cd <REPO>\frontend
npm run dev
```

Mở http://127.0.0.1:5173 (Ctrl+F5 nếu kẹt "Loading…").

## 4. Môi trường Python cho runner (T4)

Mỗi lần mở cửa sổ mới:

```powershell
cd <REPO>
.venv\Scripts\activate
$env:CARLA_ROOT = "<CARLA_ROOT>"
$env:PYTHONIOENCODING = "utf-8"
```

`PYTHONIOENCODING` tránh `UnicodeEncodeError` khi console dùng bảng mã khác UTF-8 (ví dụ `cp932`) và đường dẫn repo có
ký tự tiếng Việt. Kiểm tra (CARLA phải đang chạy):

```powershell
python -c "import os, sys, carla; sys.path.append(os.path.join(os.environ['CARLA_ROOT'], 'PythonAPI', 'carla')); from agents.navigation.behavior_agent import BehaviorAgent; c = carla.Client('localhost', 2000); c.set_timeout(30); print('CARLA', c.get_server_version(), '- agents OK')"
```

Phải in `CARLA 0.9.16 - agents OK`.

## 5. Tạo simulation run để thử (Supabase SQL editor)

Khi Flow 2 có giao diện tạo/chạy campaign thì dùng run của Flow 2. Trước đó, tạo campaign thử bằng SQL. Thay
`<VERSION_ID>` và `<CONTENT_HASH>` bằng `scenarioVersionId` và `contentHash` của một version READY, lấy từ
`GET /api/v1/scenario-versions/ready?q=<mã scenario>`.

```sql
DO $$
DECLARE cid uuid;
BEGIN
  BEGIN
    INSERT INTO asx.campaigns (name, description, status, created_by, started_at)
    VALUES ('FLOW3 TEST', 'Oracle manual test', 'Running', 'd0000000-0000-0000-0000-000000000001', now())
    RETURNING id INTO cid;
  EXCEPTION WHEN not_null_violation THEN
    INSERT INTO asx.campaigns (seq_no, name, description, status, created_by, started_at)
    VALUES ((SELECT coalesce(max(seq_no), 0) + 1 FROM asx.campaigns),
            'FLOW3 TEST', 'Oracle manual test', 'Running', 'd0000000-0000-0000-0000-000000000001', now())
    RETURNING id INTO cid;
  END;
  INSERT INTO asx.campaign_versions (campaign_id, scenario_version_id) VALUES (cid, '<VERSION_ID>');
  INSERT INTO asx.simulation_runs (campaign_id, scenario_version_id, status, content_hash, seed, started_at)
  VALUES (cid, '<VERSION_ID>', 'Running', '<CONTENT_HASH>', 42, now());
END $$;

SELECT r.id AS run_id, r.status, r.verdict, r.created_at
FROM asx.simulation_runs r JOIN asx.campaigns c ON c.id = r.campaign_id
WHERE c.name = 'FLOW3 TEST'
ORDER BY r.created_at DESC;
```

Lấy `run_id` ở dòng đầu (mới nhất, `verdict` trống) và gán ở T4: `$id = "<run_id>"`.

**Mỗi run chỉ nộp Telemetry một lần** (lần hai -> 409). Mỗi lần thử: tạo run mới rồi gán lại `$id`.

## 6A. Thử bằng Telemetry mẫu (T4)

```powershell
$t = (Get-Content examples\telemetry\speeding.jsonl -Raw) -replace '"run_id":"[^"]*"', "`"run_id`":`"$id`""
Invoke-RestMethod -Method Put "http://localhost:5080/api/v1/runs/$id/telemetry" -ContentType "application/x-ndjson" -Body $t
```

Xem kết quả: `http://127.0.0.1:5173/runs/<run_id>`.

| File trong `examples/telemetry/` | Kết quả mong đợi |
|---|---|
| `clean_pass.jsonl` | PASS |
| `speeding.jsonl` | FAIL, `speeding` |
| `freezing.jsonl` | FAIL, `freezing` |
| `timed_out.jsonl` | FAIL, `route_incomplete` |
| `red_light.jsonl` | FAIL, `red_light` |
| `ttc_low.jsonl` | PASS, `ttc` mức WARNING |
| `lane_invasion_solid.jsonl` | PASS, 2 `lane_invasion` mức WARNING |
| `infra_failed.jsonl` | ERROR |

## 6B. Chạy thật trên CARLA (T4)

Tạo run mới (bước 5), gán `$id`, rồi:

```powershell
# 1. Gói chạy của version (recordings/ đã gitignore)
curl.exe -s --create-dirs -o recordings\package.json http://localhost:5080/api/v1/scenario-versions/<VERSION_ID>/execution-package

# 2. Chạy trong CARLA; --run-id bắt buộc, header.run_id phải bằng id của run
python tools\carla_recorder.py recordings\package.json --profile normal --run-id $id --seed 42

# 3. Nộp bản ghi mới nhất
$file = Get-ChildItem recordings\*.jsonl | Sort-Object LastWriteTime | Select-Object -Last 1
Invoke-RestMethod -Method Put "http://localhost:5080/api/v1/runs/$id/telemetry" -ContentType "application/x-ndjson" -InFile $file.FullName
```

- Lần đầu nạp một map mất vài phút; script chỉ in `recording recordings\...jsonl` rồi chạy tới khi tới đích,
  hết `execution.timeout_s` hoặc va chạm.
- Event chưa hiện thực (`vehicle_cut_in`, `traffic_light_override`) bị bỏ qua kèm cảnh báo. Script cũng chưa phát
  event `red_light` và chưa hỗ trợ SUT Autopilot.
- Muốn nộp lại một bản ghi cho run khác: thay `run_id` trong file như lệnh ở 6A rồi nộp bằng `-Body`.

**Đọc kết quả cho đúng.** Ví dụ SCN-00002 v1 trên DB chung luôn ra `TIMED_OUT` + FAIL `route_incomplete`: route đi qua
một waypoint cách khoảng 1 km rồi quay về đích (khoảng 2 km), trong khi `timeout_s` chỉ 90 s. Đó là do thiết kế scenario,
không phải lỗi Oracle; muốn PASS thì publish version mới với timeout dài hơn hoặc route ngắn hơn.
`route.speed_limit_kmh` của scenario (nếu có) được ưu tiên hơn giới hạn của từng đoạn đường khi xét Speeding.

## 7. Dọn dẹp

DB là DB chung. Xoá campaign thử (kèm run, Telemetry đã nhận và Evaluation):

```sql
DELETE FROM asx.campaigns WHERE name = 'FLOW3 TEST';
```

## Lỗi thường gặp

| Lỗi | Nguyên nhân và cách xử lý |
|---|---|
| `403` khi nộp Telemetry | API không chạy với `DevAuth__Role=ADMIN` (bước 2) |
| `409 RUN_NOT_STARTED` | run còn `Queued`; dùng run tạo ở bước 5 |
| `409 TELEMETRY_ALREADY_RECEIVED` | run đã nhận Telemetry; tạo run mới |
| `422 TELEMETRY_RUN_MISMATCH` | file ghi bằng `--run-id` khác `$id` |
| `time-out of 60000ms while waiting for the simulator` | CARLA chưa bật hoặc chưa khởi động xong; chờ tới khi `Get-NetTCPConnection -LocalPort 2000 -State Listen` có kết quả |
| CARLA "Fatal error" / `EXCEPTION_ACCESS_VIOLATION` | nhiều server chạy cùng lúc hoặc hết VRAM; tắt hết, bật lại một server, giữ `-quality-level=Low` |
| `No module named 'numpy'` / `'agents'` | sai venv (dấu nhắc phải ở `<REPO>`) hoặc chưa đặt `CARLA_ROOT` |
| `No matching distribution found for carla==0.9.16` | venv không phải Python 3.12; xoá `.venv`, tạo lại bằng `py -3.12` |
| `UnicodeEncodeError: 'cp932'` | chưa đặt `PYTHONIOENCODING=utf-8` |
| Web kẹt "Loading…" | Ctrl+F5; vẫn kẹt thì chạy lại `npm run dev` |
| Trang báo "Unable to connect to the database" | xem log API; thường là chưa chạy migration 105 |
