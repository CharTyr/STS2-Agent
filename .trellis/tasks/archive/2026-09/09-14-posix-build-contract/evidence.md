# Goal 5 evidence — POSIX build staging/install contract

Date: 2026-09-15

## Change
scripts/build-mod.sh now copies mod_id.json into staging and install, supports --skip-install, and refuses a missing install parent instead of mkdir -p swallowing a typo. Help documents the flag.

## Files
- scripts/build-mod.sh
- mcp_server/tests/test_posix_script_portability.py

## Commands
uv run --locked python -m unittest mcp_server.tests.test_posix_script_portability -v
uv run --locked python -m unittest discover -s tests -v (mcp_server)
python scripts/check_verification_gates.py

## Results
POSIX contract tests OK. Full Python suite 201 tests OK. Nine verification gates OK. A Windows WSL bash fixture execution was attempted and rejected because WSL cannot see TEMP paths; coverage is source-contract plus sh-syntax.

## Limits
No live macOS/Linux game install in this session.

