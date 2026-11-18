"""Builds the three forwarder libraries of Build\\RunHybrid\\libs (libSceAgc, libSceAgcDriver, libSceVideoOut).

Each NID of these libraries resolves to kyty_gpu.dll (list: Kyty's src/hybrid/kytyGpuNids.inc); the two
AnyPS5-internal HDR calls of libSceAgcDriver go to akibot_host.dll. Same output as make_run_hybrid.py, without
its private paths.

Usage: python -I Tools/hybrid/make_shims.py <kytyGpuNids.inc> <output libs folder> [<LLVM bin folder>]
"""

import pathlib
import re
import subprocess
import sys
import tempfile

SHIMS = ("libSceAgc.prx", "libSceAgcDriver.prx", "libSceVideoOut.prx")
HOST_FORWARDS = {
    "libSceAgcDriver.prx": {
        "doCCHPPfxkI": "akibot_host.AkiBotHostHdr_nid_no_patch",
        "KMphJjTcNgk": "akibot_host.AkiBotNoteReportedToneMapPeak_nid_no_patch",
    },
}


def main():
    nids_file = pathlib.Path(sys.argv[1])
    out = pathlib.Path(sys.argv[2])
    llvm = pathlib.Path(sys.argv[3]) if len(sys.argv) > 3 else pathlib.Path(r"C:\Program Files\LLVM\bin")
    rows = [re.match(r'KYTY_GPU_NID\(\d+, "([^"]+)", "([^"]+)"', line) for line in nids_file.read_text(encoding="utf-8").splitlines()]
    out.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        work = pathlib.Path(tmp)
        empty = work / "empty.c"
        empty.write_text("int akibot_shim;\n", encoding="ascii")
        obj = work / "empty.obj"
        subprocess.run([str(llvm / "clang-cl.exe"), "/nologo", "/c", f"/Fo{obj}", str(empty)], check=True)
        for library in SHIMS:
            nids = [m.group(1) for m in rows if m and m.group(2) == library]
            lines = [f"LIBRARY {library}", "EXPORTS"] + [f'    "{nid}" = kyty_gpu.{nid}' for nid in nids]
            lines += [f'    "{nid}" = {target}' for nid, target in HOST_FORWARDS.get(library, {}).items()]
            definition = work / (library + ".def")
            definition.write_text("\n".join(lines) + "\n", encoding="ascii")
            subprocess.run([str(llvm / "lld-link.exe"), "/NOLOGO", "/DLL", "/NOENTRY", "/MACHINE:X64", f"/DEF:{definition}",
                            f"/OUT:{out / library}", f"/IMPLIB:{work / (library + '.lib')}", str(obj)], check=True)
            print(f"{library}: {len(lines) - 2} exports")


main()
