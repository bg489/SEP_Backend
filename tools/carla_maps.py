"""The twelve packaged CARLA towns supported by the AutoScenarioX catalogue.

Keep the exact town identity: Town01 and Town01_Opt have different spawn indices.
This module does not import CARLA, start a process or contact the backend.
"""
from pathlib import Path
import re

SUPPORTED_MAPS = tuple(
    name for town in ("Town01", "Town02", "Town03", "Town04", "Town05", "Town10HD")
    for name in (town, town + "_Opt")
)
_CANONICAL = {name.casefold(): name for name in SUPPORTED_MAPS}


def normalize_map_name(value: str) -> str:
    """Resolve CARLA map/Unreal asset paths without substring matching or aliases."""
    if not isinstance(value, str) or not value.strip():
        raise ValueError("A scenario must name one of the twelve supported CARLA maps.")
    path = value.strip().replace("\\", "/")
    if any(part in (".", "..") for part in path.split("/")):
        raise ValueError(f"Invalid CARLA map path: {value!r}.")
    name = path.rsplit("/", 1)[-1]
    if "." in name:
        asset, extension = name.split(".", 1)
        if extension.casefold() not in (asset.casefold(), "umap"):
            raise ValueError(f"Invalid CARLA map asset: {value!r}.")
        name = asset
    if not re.fullmatch(r"Town\d{2}(?:HD)?(?:_Opt)?", name, re.IGNORECASE):
        raise ValueError(f"Unsupported CARLA map: {value!r}.")
    try:
        return _CANONICAL[name.casefold()]
    except KeyError as error:
        raise ValueError(f"Unsupported CARLA map {name!r}. Supported maps: {', '.join(SUPPORTED_MAPS)}.") from error


def discover_installed_maps(root: str) -> tuple[str, ...]:
    """Read the installation's map files; exclude template/OpenDrive/extra towns."""
    directory = Path(root) / "CarlaUE4" / "Content" / "Carla" / "Maps"
    if not directory.is_dir():
        raise ValueError(f"CARLA map directory was not found: {directory}")
    installed = {path.stem.casefold() for path in directory.glob("*.umap") if path.is_file()}
    return tuple(name for name in SUPPORTED_MAPS if name.casefold() in installed)


def require_installed_map(root: str, value: str) -> str:
    name = normalize_map_name(value)
    if name not in discover_installed_maps(root):
        raise ValueError(f"Map {name} is not installed under {root}. The worker will not substitute another town.")
    return name
