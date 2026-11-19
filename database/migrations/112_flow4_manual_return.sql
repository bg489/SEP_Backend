-- Flow 4: lỗi chưa giải thích được chỉ được đánh dấu.
-- Đưa về Flow 3 là một thao tác riêng (return-to-flow3).

ALTER TABLE asx.failure_analyses DROP CONSTRAINT IF EXISTS failure_analyses_status_check;
ALTER TABLE asx.failure_analyses ADD CONSTRAINT failure_analyses_status_check CHECK (status IN (
  'grouped', 'verified', 'similar_found', 'unexplained', 'awaiting_flow3',
  'explainable', 'report_draft', 'needs_more', 'evaluated'));

ALTER TABLE asx.failure_faults DROP CONSTRAINT IF EXISTS failure_faults_status_check;
ALTER TABLE asx.failure_faults ADD CONSTRAINT failure_faults_status_check CHECK (status IN (
  'grouped', 'verified', 'similar_found', 'unexplained', 'returned_to_flow3', 'explainable', 'in_report'));
