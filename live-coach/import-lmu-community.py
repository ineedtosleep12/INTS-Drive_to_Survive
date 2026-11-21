"""Rebuild the bundled community references. See LMU-COMMUNITY-SOURCES.md."""

import argparse
import hashlib
import json
import math
import os
from pathlib import Path
from tempfile import NamedTemporaryFile

import duckdb


VERSION = "ee11c95b8c64047da55acfe7c22f5cfa7043368a"
SOURCE = f"https://github.com/vasco-duarte-oikosofy/lap-telemetry/blob/{VERSION}"
LICENCE_SHA256 = "15168c8cb518d6f1f982f8e236117c3a3d18dd6e4fdd61393c966b19be108e9b"
GINETTA = "DKR Engineering #4:ELMS25"
FERRARI = "Vista AF Corse 2026 #21:LM"
MODELS = {
    GINETTA: ("Ginetta G61-LT-P325-Evo (LMP3)", "https://lemansultimate.com/cars/ginetta-g61-lt-p325-evo/"),
    FERRARI: ("Ferrari 296 LMGT3 Evo", "https://assets.lemans.org/explorer/pdf/courses/2026/24-heures-du-mans/spotter-le-mans-2026.pdf"),
}


def verify(path, expected):
    if hashlib.sha256(path.read_bytes()).hexdigest() != expected:
        raise ValueError(f"Unexpected source revision: {path}")


def lap_duration(db, parquet, entry):
    number = entry["lap"]
    rows = db.execute("""
        WITH rows AS (SELECT *, file_row_number AS i FROM read_parquet(?, file_row_number=true)),
        bounds AS (SELECT min(i) AS a, max(i) AS b, count(*) AS n FROM rows WHERE lap_number=?)
        SELECT lap_number, b-a+1=n FROM rows, bounds WHERE i IN (a-1, a, b, b+1) ORDER BY i
    """, [str(parquet), number]).fetchall()
    if not all(contiguous for _, contiguous in rows):
        raise ValueError("Lap split across discontinuous session data")
    transitions = [lap for lap, _ in rows]
    if entry["vehicle"] == FERRARI:
        # This source is a sliced two-lap fixture. It starts just BEFORE the scoring
        # crossing; both timing lines are independently visible in the scoring counters.
        counted = db.execute("""
            SELECT scoring_total_laps, min(scoring_lap_start_et_s), min(scoring_last_lap_time_s)
            FROM read_parquet(?) GROUP BY scoring_total_laps ORDER BY scoring_total_laps
        """, [str(parquet)]).fetchall()
        if (transitions != [number, number, number+1] or [r[0] for r in counted] != [5, 6, 7]
                or abs(counted[-1][2] - entry["duration"]) > 0.00002):
            raise ValueError("Missing both native timing crossings of the sliced fixture")
    elif transitions != [number-1, number, number, number+1]:
        raise ValueError("Missing recorded lap transitions")

    # lap_time_s is scoring ET minus the telemetry lap start ET. The recorder's
    # count flag includes lap-only as well as timed laps; never promote to valid.
    epochs = db.execute("""
        SELECT lap_number, median(session_time_s-lap_time_s), bool_and(lap_valid)
        FROM read_parquet(?) WHERE lap_number IN (?, ?) GROUP BY lap_number ORDER BY lap_number
    """, [str(parquet), number, number+1]).fetchall()
    if len(epochs) != 2 or epochs[0][2] is not True:
        raise ValueError("Missing counted lap and next lap epoch")
    duration = epochs[1][1] - epochs[0][1]
    if abs(duration - entry["duration"]) > 0.00002:
        raise ValueError("Timing mismatch")
    return duration


def read_trace(db, parquet, entry):
    channel = entry["distanceChannel"]
    if channel not in {"raw_lap_distance_m", "lap_distance_m"}:
        raise ValueError("Unknown distance channel")
    native = channel == "raw_lap_distance_m"
    lateral = ",path_lateral_m AS pathLateralM" if native else ""
    # The recorder polls at 50 Hz. The scoring clock updates at 5 Hz. Use the
    # first recorded frame for each scoring timestamp, in original file order.
    query = db.execute(f"""
        SELECT lap_time_s AS time, {channel} AS distanceM,
               speed_kph AS speedKph, brake_norm AS brake, throttle_norm AS throttle,
               steering_norm AS steering, pos_x_m AS x, pos_z_m AS z {lateral}
        FROM read_parquet(?, file_row_number=true) WHERE lap_number=?
        QUALIFY row_number() OVER (PARTITION BY session_time_s ORDER BY file_row_number)=1
        ORDER BY session_time_s
    """, [str(parquet), entry["lap"]])
    fields = [col[0] for col in query.description]
    raw = [dict(zip(fields, row)) for row in query.fetchall()]
    # Asynchronous telemetry/scoring counters can straddle either line. Keep
    # only the single ordered progression between them; never fabricate endpoints.
    first = next((i for i, r in enumerate(raw[:5]) if r["time"] >= 0 and 0 <= r["distanceM"] < 35), None)
    if first is None:
        raise ValueError("No measured start of lap")
    points = raw[first:]
    if len(points) > 1 and points[-1]["distanceM"] < 35 and points[-2]["distanceM"] > 2500:
        points.pop()
    if len(points) != entry["samples"]:
        raise ValueError("Unexpected lap coverage")
    for i, point in enumerate(points):
        if not all(v is not None and math.isfinite(v) for v in point.values()):
            raise ValueError("Missing channel value")
        if not (0 < point["speedKph"] < 400 and 0 <= point["brake"] <= 1
                and 0 <= point["throttle"] <= 1 and abs(point["steering"]) <= 1):
            raise ValueError("Unexpected speed or controls")
        if i:
            prev = points[i-1]
            elapsed = point["time"] - prev["time"]
            distance = point["distanceM"] - prev["distanceM"]
            world_step = math.hypot(point["x"] - prev["x"], point["z"] - prev["z"])
            if not (0.19 < elapsed < 0.21 and 0 < distance < 20 and 0 < world_step < 20):
                raise ValueError("Discontinuous scoring or world trace")
    return [{k: round(v, 6) for k, v in s.items()} for s in points]


def build(directory, entry, licence):
    name = entry["session"]
    parquet, metadata_path = directory / (name + ".parquet"), directory / (name + ".json")
    verify(parquet, entry["parquetSha256"])
    verify(metadata_path, entry["metadataSha256"])
    metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
    vehicle = metadata["vehicle_name"]
    if metadata["sim"] != "lmu" or vehicle != entry["vehicle"] or metadata["track"] != entry["track"]:
        raise ValueError("Session identity mismatch")
    with duckdb.connect() as db:
        duration = lap_duration(db, parquet, entry)
        trace = read_trace(db, parquet, entry)
    native = entry["distanceChannel"] == "raw_lap_distance_m"
    distance_note = ("Game scoring mLapDist and mPathLateral in metres." if native else
                     "Recorder lap_distance_m is anchored to scoring mLapDist and estimated between updates. "
                     "Raw anchors and path lateral were not saved in this earlier recording.")
    return {
        "schemaVersion": 1, "game": "LMU", "car": vehicle, "track": metadata["track"], "layout": entry["layout"],
        "gameVersion": "unknown", "trackLengthM": None, "timeBasis": "recorded-time-s",
        "distanceBasis": "lmu-scoring-distance-m" if native else "lmu-scoring-anchored-distance-m",
        "coordinateFrame": "lmu-world-xz-m", "lateralPositionBasis": "lmu-scoring-path-lateral-m" if native else "unknown",
        "sourceUrl": f"{SOURCE}/dev/sessions/{name}.parquet", "author": "Vasco Duarte",
        "recordedAt": metadata["started_utc"], "licence": licence,
        "provenance": (
            f"Recorded LMU session, source lap_number {entry['lap']}. {distance_note} "
            "First telemetry frame of each 5 Hz scoring timestamp. Native world mPos.x/z, unfiltered "
            "brake, throttle, steering and speed from local velocity. Lap start epochs establish the duration. "
            "No interpolated start/finish samples. The recorded count flag cannot establish track-limit validity."
        ),
        "validity": "unknown", "complete": False,
        "conditions": {
            "sourceCarLabel": vehicle, "sourceTrackLabel": metadata["track"], "sourceCarModel": MODELS[vehicle][0],
            "sourceCarUrl": MODELS[vehicle][1], "sourceCircuitUrl": entry["circuitUrl"],
            "sourceLapTimeSeconds": f"{duration:.6f}", "sourceLapExtent": "recorded-crossing-to-crossing",
            "sourceSha256": entry["parquetSha256"], "sourceRecordingMetadata": f"{SOURCE}/dev/sessions/{name}.json",
        },
        "samples": trace,
    }


def write_reference(destination, data):
    with NamedTemporaryFile(mode="w", encoding="utf-8", newline="\n", dir=destination.parent, delete=False) as output:
        temporary = Path(output.name)
    try:
        temporary.write_bytes((json.dumps(data, separators=(",", ":"), allow_nan=False) + "\n").encode("utf-8"))
        os.replace(temporary, destination)
    finally:
        temporary.unlink(missing_ok=True)


def main():
    manifest = json.loads(Path(__file__).with_name("lmu-community-sessions.json").read_text(encoding="utf-8"))
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source_directory", type=Path)
    parser.add_argument("output_directory", type=Path)
    selection = parser.add_mutually_exclusive_group()
    selection.add_argument("--only", choices=manifest)
    selection.add_argument("--review-example", choices=[key for key, entry in manifest.items() if "reviewLap" in entry],
                           help="Export the other recorded lap used in the two-lap review")
    args = parser.parse_args()
    licence_file = args.source_directory / "LICENSE"
    verify(licence_file, LICENCE_SHA256)
    licence = licence_file.read_text(encoding="utf-8").strip()
    args.output_directory.mkdir(parents=True, exist_ok=True)
    if args.review_example:
        source = manifest[args.review_example]
        entry = {**source, **source["reviewLap"]}
        destination = args.output_directory / f"{args.review_example}-comparison.json"
        write_reference(destination, build(args.source_directory, entry, licence))
        print(destination.name)
        return
    for key, entry in manifest.items():
        if not args.only or key == args.only:
            model = "ginetta-lmp3" if entry["vehicle"] == GINETTA else "ferrari-296-evo"
            destination = args.output_directory / f"{key}-{model}.reference.json"
            write_reference(destination, build(args.source_directory, entry, licence))
            print(destination.name)


if __name__ == "__main__":
    main()
