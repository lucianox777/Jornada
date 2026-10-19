"""Read local GPU/runtime metadata only; never import optional GPU packages."""
import argparse
import ctypes
import importlib.metadata
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess

PACKAGES = (
    "numpy", "torch", "cupy", "cupy-cuda12x", "cupy-cuda13x", "numba",
    "llvmlite", "onnxruntime-gpu", "onnxruntime-directml", "tensorflow",
    "pycuda", "cuda-python",
)


def package_metadata(path=None):
    distributions = importlib.metadata.distributions(path=[path]) if path else importlib.metadata.distributions()
    found = {d.metadata.get("Name", "").lower(): d.version for d in distributions}
    return {name: found.get(name) for name in PACKAGES}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--site-packages", help="Optional existing venv metadata directory")
    args = parser.parse_args()
    inventory = {
        "version": "V9_LOCAL_GPU_INVENTORY_V1",
        "python": platform.python_version(),
        "logical_processors": os.cpu_count(),
        "system_packages_metadata_only": package_metadata(),
        "existing_venv_packages_metadata_only": package_metadata(args.site_packages) if args.site_packages else None,
        "nvcc_on_path": shutil.which("nvcc") is not None,
        "cuda_toolkit_default_directory_present": Path(os.environ.get("ProgramFiles", "C:/Program Files"),
            "NVIDIA GPU Computing Toolkit").exists(),
        "windows_sdk_x64_dxc_versions": [],
        "gpu_benchmark_executed": False,
        "gpu_speedup_measured": False,
    }
    sdk = Path(os.environ.get("ProgramFiles(x86)", "C:/Program Files (x86)"), "Windows Kits/10/bin")
    inventory["windows_sdk_x64_dxc_versions"] = sorted(p.parent.parent.name for p in sdk.glob("*/x64/dxc.exe"))
    if shutil.which("nvidia-smi"):
        result = subprocess.run(["nvidia-smi", "--query-gpu=name,driver_version,memory.total,compute_cap,pstate",
            "--format=csv"], capture_output=True, text=True, check=True)
        inventory["nvidia_smi"] = result.stdout.strip().splitlines()
    if platform.system() == "Windows":
        inventory["system_gpu_api_dlls_present"] = {
            name: (Path(os.environ.get("SystemRoot", "C:/Windows")) / "System32" / name).is_file()
            for name in ("nvcuda.dll", "DirectML.dll", "d3d12.dll", "d3dcompiler_47.dll")
        }
        try:
            cuda = ctypes.WinDLL("nvcuda.dll")
            count = ctypes.c_int()
            inventory["cuda_driver_init_status"] = cuda.cuInit(0)
            inventory["cuda_device_count_status"] = cuda.cuDeviceGetCount(ctypes.byref(count))
            inventory["cuda_device_count"] = count.value
        except OSError as error:
            inventory["cuda_driver_load_error_type"] = type(error).__name__
    print(json.dumps(inventory, indent=2))


if __name__ == "__main__":
    main()
