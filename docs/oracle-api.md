# Oracle API (hợp đồng Flow 2 -> Flow 3)

Flow 2 giữ Campaign, hàng đợi và runner (`asx.campaigns`, `asx.campaign_versions`, `asx.simulation_runs`).
Flow 3 chỉ chấm: nhận Telemetry của một simulation run, Oracle chấm offline (ADR 0001), lưu Evaluation + Violations.
Migration: `database/migrations/108_flow3_oracle.sql` (sau 106/107 campaigns + runs).

## Worker claim (hàng đợi)

`POST /api/v1/runs/claim?campaignId=` (campaignId optional), quyền Test Engineer hoặc Admin.

- Trả run Queued tiếp theo → status `Claimed` (204 nếu trống).
- Tiếp theo: `GET /api/v1/scenario-versions/{scenarioVersionId}/execution-package`, chạy CARLA
  (`tools/carla_recorder.py`), rồi nộp Telemetry bên dưới.

## Runner nộp Telemetry

`PUT /api/v1/runs/{simulationRunId}/telemetry`, body là file `asx.telemetry/1` (JSON Lines thô,
`Content-Type: application/x-ndjson`), quyền Test Engineer hoặc Admin.

- `header.run_id` phải bằng `simulationRunId` (khác -> 422 `TELEMETRY_RUN_MISMATCH`).
- Run còn `Queued` -> 409 `RUN_NOT_STARTED` (phải claim trước); mỗi run nộp một lần (lần hai -> 409 `TELEMETRY_ALREADY_RECEIVED`).
- Sau khi chấm: `simulation_runs.status` → Completed / TimedOut / Failed / Error; `verdict` được ghi bản sao.
- Run không khởi động được (nạp map, spawn lỗi) vẫn nên nộp file tối thiểu `header` + `end INFRA_FAILED`
  (`tools/carla_recorder.py: infra_failed_telemetry`), để run có Verdict ERROR.
- `tools/carla_recorder.py: record(pkg, profile, ..., run_id=<simulationRunId>, seed=<seed>)` ghi đúng định dạng.

API lưu file `telemetry-store/{campaignId}/{runId}.jsonl.gz` (cấu hình `Telemetry:Directory`), ghi `asx.run_telemetry`
(Run Outcome, thời lượng), chấm bằng Oracle Config campaign ghim (`campaigns.oracle_config_id`), rồi ghi bản sao
verdict vào `simulation_runs.verdict`. Flow 3 **không** đổi `simulation_runs.status`; việc đó của runner Flow 2.

## Đọc kết quả

- `GET /api/v1/runs/{simulationRunId}/result`: Run Outcome, `verdict` (theo Oracle Config campaign ghim, null khi chưa chấm),
  mọi Evaluation kèm Violations (mới nhất trước).
- `GET /api/v1/oracle-configs`: các version Oracle Config.
- Giao diện: `/runs/{simulationRunId}` (màn hình campaign của Flow 2 liên kết tới đây).
