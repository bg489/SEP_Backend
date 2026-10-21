# AutoScenarioX .NET API

ASP.NET Core 10 REST API for authentication, role authorization, and the five AutoScenarioX workflows. It uses PostgreSQL 18 database `autoscenariox`. Identity data is stored in schema `app_auth`; workflow JSONB records are stored in `app_workflow` and separated by resource area (scenarios, immutable scenario versions, campaigns, runs, failures, nodes, oracle rules, analytics settings, rankings, CARLA settings, and audit events).

## Local setup (English)

Requirements: .NET 10 SDK and PostgreSQL 18 with the `autoscenariox` database already created.

Configure local credentials once, then start the API:

```powershell
.\scripts\Set-LocalSecrets.ps1
dotnet tool restore
dotnet run
```

The script saves the connection string and JWT signing key in .NET User Secrets. The API verifies PostgreSQL 18 and applies EF migrations at startup. Do not put database credentials or JWT signing keys in `appsettings.json` or frontend `VITE_*` variables.

The API listens at `http://localhost:5080`. Start the frontend against it with `Frontend/.env.local`:

```dotenv
VITE_DATA_MODE=api
VITE_API_BASE_URL=http://localhost:5080
```

Restart Vite after changing the environment file. Authentication always uses this API in both mock and API data modes. In API mode, workflow data comes from PostgreSQL; the frontend does not silently substitute demo records when a workflow endpoint fails.

## Cài đặt cục bộ (Tiếng Việt)

Yêu cầu .NET 10 SDK và PostgreSQL 18 đã tạo database `autoscenariox`.

Cấu hình thông tin kết nối một lần rồi chạy API:

```powershell
.\scripts\Set-LocalSecrets.ps1
dotnet tool restore
dotnet run
```

Script lưu connection string và JWT signing key trong .NET User Secrets. API xác minh phiên bản PostgreSQL 18 và tự áp dụng EF migration khi khởi động. Không ghi thông tin database hoặc JWT signing key vào `appsettings.json` hay biến frontend `VITE_*`.

API chạy tại `http://localhost:5080`. Để frontend dùng API thật, tạo `Frontend/.env.local`:

```dotenv
VITE_DATA_MODE=api
VITE_API_BASE_URL=http://localhost:5080
```

Khởi động lại Vite sau khi sửa file môi trường. Xác thực luôn dùng API này ở cả chế độ mock và API. Khi bật API mode, dữ liệu luồng nghiệp vụ được đọc từ PostgreSQL; frontend sẽ báo lỗi thay vì âm thầm thay bằng dữ liệu mẫu.

## Roles and access

Supported roles are `Scenario Engineer`, `Test Engineer`, `Test Reviewer`, and `Administrator`. Registration allows users to select the first three roles; only an administrator can assign the Administrator role. Scenario authoring and campaign management endpoints enforce their matching roles. Root-cause confirmation is restricted to Test Reviewer and Administrator. Node, oracle, CARLA, and analytics configuration writes plus audit-log reads require Administrator.

Login and registration return a 20-minute JWT. Login, registration, and password-reset requests are rate-limited. Password reset creates Identity tokens, but production email delivery still needs a mail provider.

## API routes

| Method | Endpoint | Purpose / access |
| --- | --- | --- |
| `POST` | `/api/auth/register`, `/api/auth/login` | Public account creation and sign-in |
| `GET` | `/api/auth/me` | Current authenticated user |
| `POST` | `/api/auth/forgot-password`, `/api/auth/reset-password` | Identity password-reset flow |
| `GET`, `PUT` | `/api/admin/users`, `/api/admin/users/{id}/role` | Administrator user and role management |
| `GET` | `/api/dashboard/overview` | Counts and recent activity |
| `GET`, `POST` | `/api/scenarios` | List/filter and create scenarios |
| `POST` | `/api/scenarios/import` | Validate and persist an imported scenario and its original XML artifact |
| `POST` | `/api/scenarios/{id}/versions` | Save a new immutable scenario version |
| `POST` | `/api/scenarios/{id}/clone` | Clone a scenario into a draft |
| `GET`, `PUT` | `/api/scenarios/{id}/versions`, `/api/scenarios/{id}/status` | Version history and review state |
| `PATCH` | `/api/scenarios/{id}/execution-metrics` | Update execution summary metrics |
| `GET`, `POST` | `/api/campaigns` | List and create campaigns |
| `PATCH` | `/api/campaigns/{id}` | Update campaign settings or status |
| `GET`, `PUT` | `/api/campaigns/{id}/ranking` | Persist and retrieve priority order and evaluation metrics |
| `POST` | `/api/campaigns/{id}/runs` | Validate and queue campaign runs |
| `GET` | `/api/runs` | Read persisted run records |
| `POST` | `/api/campaigns/{id}/runs/cancel` | Cancel queued or active runs |
| `GET` | `/api/runs/{id}/artifacts/{fileName}` | Download a saved ScenarioRunner log |
| `GET`, `POST` | `/api/analytics/failure-clusters` | List or record failure clusters with oracle evidence |
| `POST` | `/api/analytics/failure-clusters/{id}/confirm` | Reviewer confirmation or correction |
| `GET`, `PUT` | `/api/simulation-nodes` | List nodes; Administrator registers or updates nodes |
| `GET`, `PUT` | `/api/oracles/rules` | Read or configure oracle thresholds |
| `GET`, `PUT`, `POST` | `/api/integration/carla`, `/api/integration/carla/test` | Read/save the CARLA profile and check the real executable, Python environment, ScenarioRunner and RPC port; quality is fixed to Low |
| `GET` | `/api/runs/{id}/artifacts/{fileName}` | Download the saved ScenarioRunner log |
| `POST` | `/api/worker-callbacks/runs/{id}` | Accept authenticated worker state/progress/result callbacks (`X-Worker-Key`) |
| `GET`, `PUT` | `/api/analytics/preferences` | Read or save analytics settings |
| `GET` | `/api/audit-log` | Administrator reads immutable server-generated audit events |

## Real CARLA/ScenarioRunner execution

The API hosts a local background worker. It claims persisted campaign runs, loads the exact `.xosc`/`.osc` artifact saved with the scenario version, starts the CARLA server if its RPC port is offline, invokes ScenarioRunner, and records `Assigned`, `Running`, `Passed`, `Failed`, or `Timed Out` callbacks plus the `scenario-run.log` artifact. The UI polls PostgreSQL-backed run state while a campaign is active. CARLA is always launched with `-quality-level=Low`, windowed at 800×600, and without sound.

Install the official ScenarioRunner `v0.9.16` source and its Python 3.12 environment against the matching CARLA wheel:

```powershell
.\scripts\Install-ScenarioRunner.ps1
```

This places ScenarioRunner at `C:\Users\admin\Downloads\CARLA_0.9.16\ScenarioRunner`. The CARLA root defaults to `C:\Users\admin\Downloads\CARLA_0.9.16` in Development; override `Carla:RootPath` or `CARLA_ROOT` if it is elsewhere. In the Administrator CARLA settings screen, save that ScenarioRunner path, run **Test installation**, then **Activate worker**. The test runs `scenario_runner.py --version` with the configured Python dependencies; it does not start CARLA. CARLA starts only when an actual run is claimed.

Import a real `.xosc`/`.osc` file in the Scenario Engineer screen. The API validates the XML structure and saves the original source by scenario version; scenario versions and clones retain that artifact. Only an Active/Approved scenario with a saved artifact can be queued. A missing file, wrong Python dependency, absent CARLA install, or failed run is reported in the run state and log instead of being marked as a pass.

Worker callbacks from a separate worker require a secret of at least 32 bytes. It is stored in .NET User Secrets as `Worker:CallbackKey`; create or rotate it with:

```powershell
.\scripts\Set-WorkerCallbackSecret.ps1
```

Send callbacks to `POST /api/worker-callbacks/runs/{id}` with the `X-Worker-Key` header and a state/progress/result JSON body. The embedded local worker writes through the same callback handler used to persist state transitions. Keep the callback key out of source control and browser-visible environment variables.

### Chạy CARLA/ScenarioRunner thật (Tiếng Việt)

Backend có worker chạy nền cục bộ: nhận run từ PostgreSQL, tải đúng tệp `.xosc`/`.osc` đã lưu theo phiên bản, mở CARLA nếu cổng RPC chưa chạy, gọi ScenarioRunner và lưu trạng thái/kết quả/log. CARLA luôn khởi chạy với `-quality-level=Low`, cửa sổ 800×600 và tắt âm thanh.

Cài bản ScenarioRunner `v0.9.16` cùng môi trường Python 3.12 và CARLA wheel tương ứng:

```powershell
.\scripts\Install-ScenarioRunner.ps1
```

Trong giao diện Administrator, nhập `C:\Users\admin\Downloads\CARLA_0.9.16\ScenarioRunner`, chọn **Save settings**, **Test installation**, rồi **Activate worker**. Kiểm tra bản cài chỉ chạy `scenario_runner.py --version`; CARLA chỉ được khởi động khi worker nhận run thật. Hãy import tệp `.xosc`/`.osc` rồi duyệt kịch bản sang Active/Approved trước khi thêm vào campaign.

Để cho phép worker ngoài backend gửi callback, tạo khóa riêng trong User Secrets:

```powershell
.\scripts\Set-WorkerCallbackSecret.ps1
```

Gửi `POST /api/worker-callbacks/runs/{id}` kèm `X-Worker-Key`; không đưa khóa này vào frontend hoặc Git.

## Database changes

EF Core migrations create the `app_auth` Identity tables and the `app_workflow.documents` JSONB store in the existing `autoscenariox` database. The store uses resource areas and stable IDs so scenarios, versions, campaigns, runs, failures, settings, and audit entries remain separate and queryable through their REST contracts. The first API launch applies any pending migrations automatically.

## Secrets and deployment

Keep PostgreSQL credentials and JWT signing keys in .NET User Secrets for local development or environment/secret-manager variables in deployment. Configure `Frontend:AllowedOrigins` for the deployed site and terminate HTTPS at the application host or reverse proxy. Never expose a database password, JWT key, or Supabase service-role key through a frontend `VITE_*` variable.
