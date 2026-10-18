"""Build synthetic packages and run offline contracts. .NET restore needs NuGet on first use."""
import argparse
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("output", help="New directory for generated fixtures and results")
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
output = Path(args.output).resolve()
if output.exists(): raise SystemExit("Use a fresh output directory")
output.mkdir(parents=True)
def run(*args): subprocess.run([str(a) for a in args], cwd=root, check=True)
modules = []
for name, assembly in (("SchematicOld", "Designer.Modules.SelectionProbe"), ("SchematicNew", "Designer.Modules.SelectionProbe"), ("Geometry", "Designer.Modules.AutomaticGeometry")):
    target = output/"modules"/name
    run("dotnet", "build", f"Tests/Fixtures/{name}/{name}.csproj", "-o", target)
    modules.append(target/(assembly + ".dll"))
test_output = output/"tests"
run("dotnet", "build", "Tests/QdsModManager.Tests.csproj", "-o", test_output)
tests = test_output/"QdsModManager.Tests.dll"
packages = output/"packages"
run("dotnet", tests, "--create-fixtures", packages, *modules)
run("dotnet", tests, packages, output/"lifecycle", "unit")
run("dotnet", "run", "--project", "CatalogTests/QdsModManager.CatalogTests.csproj", "--", packages, output/"publishing")
run("dotnet", "run", "--project", "CatalogTool/QdsModManager.CatalogTool.csproj", "--", "verify-feed", "Tests/Fixtures/catalog-signed.json")
print("Offline lifecycle, publishing and signed-feed checks passed. No release downloads were requested.")
