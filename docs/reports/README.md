# Current Report 5 copies

These three files are updated copies of the supplied Report 5 artifacts. The
originals under `Tai lieu/1` were not overwritten.

- Both workbooks retain all original sheet values/formulas and append
  `Week2 Current Evidence` with actual results from 19 November 2026. Original
  case statuses remain the historical baseline; they are not a statement that
  today's checks were skipped.
- The Word document retains the original content and appends the current
  verification and actual twelve-map evidence on pages 14–15.
- Current counts are 439 .NET, 70 Python, 108 frontend unit invocations and
  73 browser cases, plus 88 contract operations. All of these local checks passed.
  Browser fixtures and unit invocations are separate from real acceptance.
- Actual CARLA 0.9.16 Low runs completed full short routes on all twelve maps,
  with actor/event effects, video, telemetry and Oracle results. A separate
  Autopilot run, real Admin readback and browser-to-CARLA reload also passed.
- Hosted CI was blocked before startup by the GitHub account's billing lock.
  Supabase Auth/email/private bucket and shared staging acceptance remain pending
  configuration. No full release acceptance or release tag is claimed.

Validation: every original populated workbook value/formula was compared after
export (zero differences). New workbook ranges were rendered and inspected.
Original nonempty DOCX paragraphs were compared (preserved); the updated document
was rendered using installed Microsoft Word because LibreOffice was unavailable,
and the two appended pages were visually inspected. See `../week2-evidence.json`
and `../week2-task-status.md` for exact source revisions, run IDs and task limits.
