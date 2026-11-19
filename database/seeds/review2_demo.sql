-- Repeatable application identities only: Supabase credentials must be created separately.
-- These example.invalid addresses are fixtures, never working accounts or passwords.
INSERT INTO asx.app_users(id,email,display_name,role,is_active) VALUES
('d0000000-0000-0000-0000-000000000011','admin@example.invalid','Review Admin','ADMIN',true),
('d0000000-0000-0000-0000-000000000012','scenario@example.invalid','Review Scenario Engineer','SCENARIO_ENGINEER',true),
('d0000000-0000-0000-0000-000000000013','tester@example.invalid','Review Test Engineer','TEST_ENGINEER',true),
('d0000000-0000-0000-0000-000000000014','reviewer@example.invalid','Review Test Reviewer','TEST_REVIEWER',true)
ON CONFLICT(id) DO NOTHING;

-- Scenario and validated READY version are loaded by migrations 100-104.
-- Campaigns/runs are intentionally created by the workflow demo or tests, not faked as PASS.
