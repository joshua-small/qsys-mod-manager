"""Build an inert legacy-name portable ZIP for disposable installer tests."""
import argparse
from pathlib import Path
import subprocess
import zipfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("output", help="Fresh output directory; never a product release directory")
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
output = Path(args.output).resolve()
if output.exists(): raise SystemExit("Use a fresh fixture output directory")
output.mkdir(parents=True)
payload = output / "payload"
subprocess.run(["dotnet", "publish", "Installer/Fixtures/LegacyPortable/LegacyPortable.csproj",
    "-c", "Release", "--artifacts-path", str(output / "dotnet"), "-o", str(payload)], cwd=root, check=True)
names = ["QDS Mod Manager" + suffix for suffix in (".exe", ".dll", ".deps.json", ".runtimeconfig.json")]
if not all((payload / name).is_file() for name in names):
    raise SystemExit("Legacy fixture build did not produce the complete portable layout")
with zipfile.ZipFile(output / "legacy-portable-fixture.zip", "x", compression=zipfile.ZIP_DEFLATED) as archive:
    for name in names:
        info = zipfile.ZipInfo(name, date_time=(2000, 1, 1, 0, 0, 0))
        info.compress_type = zipfile.ZIP_DEFLATED
        archive.writestr(info, (payload / name).read_bytes())
print("Synthetic legacy-name fixture built; do not publish, install or execute it as an application.")
