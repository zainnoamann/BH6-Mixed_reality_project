"""Start the CreativeTwin AI server, setting everything up first if this is a new machine.

    python start.py          bootstrap if needed, then serve
    python start.py --setup  force a bootstrap pass (safe: it skips what is present)
    python start.py --check  status only

Unity talks to http://127.0.0.1:8765 by default (change AiClient.baseUrl if you host it
on another PC on the network).
"""
import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT))

import bootstrap  # noqa: E402


def main() -> int:
    args = sys.argv[1:]

    if "--check" in args:
        return bootstrap.main(["--check"])

    needs_setup = "--setup" in args or not bootstrap.STATUS_FILE.exists() \
        or not bootstrap.venv_python(".venv-server").exists()

    if needs_setup:
        code = bootstrap.main([a for a in args if a != "--setup"])
        if code not in (0, 1):
            return code

    server_python = bootstrap.venv_python(".venv-server")
    server = ROOT / "server" / "server.py"

    if not server_python.exists():
        print("server virtualenv missing; run: python bootstrap.py")
        return 1

    os.execv(str(server_python), [str(server_python), str(server)])
    return 0


if __name__ == "__main__":
    sys.exit(main())
