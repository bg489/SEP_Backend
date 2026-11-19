-- Bỏ mô hình campaign cũ. Code hiện tại ghi campaigns, campaign_versions và simulation_runs.
-- Ba bảng này không có CREATE trong bộ migration. Database dev còn sót từ trước migration 110.
-- Chạy lại nhiều lần không lỗi. Database tạo mới không có ba bảng này thì lệnh không làm gì.

DROP TABLE IF EXISTS asx.test_runs;
DROP TABLE IF EXISTS asx.test_campaign_versions;
DROP TABLE IF EXISTS asx.test_campaigns;
