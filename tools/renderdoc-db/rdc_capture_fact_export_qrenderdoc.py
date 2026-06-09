"""Export RenderDoc .rdc facts for rdc_capture_fact_import.py.

This script runs inside qrenderdoc.exe --python. Keep it compatible with the
older Python versions embedded by RenderDoc builds.
"""

import json
import os
import traceback

import renderdoc as rd


CONFIG_ENV = "RDC_FACT_IMPORT_CONFIG"
STAGES = [
    ("VS", rd.ShaderStage.Vertex),
    ("HS", rd.ShaderStage.Hull),
    ("DS", rd.ShaderStage.Domain),
    ("GS", rd.ShaderStage.Geometry),
    ("PS", rd.ShaderStage.Pixel),
    ("CS", rd.ShaderStage.Compute),
]


def write_json(path, data):
    payload = json.dumps(data, ensure_ascii=False, indent=2).encode("utf-8")
    parent = os.path.dirname(path)
    if parent and not os.path.isdir(parent):
        os.makedirs(parent)
    fd = os.open(path, os.O_CREAT | os.O_TRUNC | os.O_WRONLY)
    try:
        os.write(fd, payload)
    finally:
        os.close(fd)


def read_json(path):
    with open(path, "r") as handle:
        return json.load(handle)


def success_status(status):
    return "Success" in str(status)


def safe_int(value, default=None):
    try:
        return int(value)
    except Exception:
        return default


def safe_float(value, default=None):
    try:
        return float(value)
    except Exception:
        return default


def scalar(value):
    if value is None or isinstance(value, (str, int, float, bool)):
        return value
    try:
        return int(value)
    except Exception:
        return str(value)


def compact_enum(value):
    text = str(value or "")
    return text.rsplit(".", 1)[-1] if "." in text else text


def renderdoc_version():
    for name in ("GetVersionString", "GetVersion"):
        try:
            value = getattr(rd, name)()
            if value is not None:
                return str(value)
        except Exception:
            pass
    return "unknown"


def action_name(action, structured_file):
    try:
        return str(action.GetName(structured_file))
    except Exception:
        try:
            return str(action.customName)
        except Exception:
            return ""


def action_children(action):
    try:
        return list(action.children)
    except Exception:
        return []


def classify_action(action):
    flags = action.flags
    try:
        if flags & (rd.ActionFlags.PushMarker | rd.ActionFlags.SetMarker):
            return "marker"
        if flags & rd.ActionFlags.Drawcall:
            return "draw"
        if flags & rd.ActionFlags.Dispatch:
            return "dispatch"
        if flags & rd.ActionFlags.Clear:
            return "clear"
        if flags & rd.ActionFlags.Copy:
            return "copy"
        if flags & rd.ActionFlags.Present:
            return "present"
    except Exception:
        pass
    return "event"


def is_null_resource_id(value):
    if value is None:
        return True
    return str(value) in ("", "ResourceId::0")


def resource_id_text(value):
    if is_null_resource_id(value):
        return None
    return str(value)


def append_text(values, value):
    if value is None:
        return
    text = str(value).strip()
    if text:
        values.append(text)


def resource_name_candidates(controller, resource):
    values = []
    for attr in ("name", "customName"):
        try:
            append_text(values, getattr(resource, attr))
        except Exception:
            pass
    try:
        append_text(values, controller.GetResourceName(resource.resourceId))
    except Exception:
        pass
    return values


def first_meaningful_resource_name(values, resource_id):
    for value in values:
        name = meaningful_resource_name(value, resource_id)
        if name:
            return name
    return None


def build_resource_name_map(controller):
    resource_names = {}
    for getter_name in ("GetResources", "GetTextures", "GetBuffers"):
        try:
            resources = getattr(controller, getter_name)()
        except Exception:
            resources = []
        for resource in resources:
            try:
                resource_id = str(resource.resourceId)
            except Exception:
                continue
            if resource_names.get(resource_id):
                continue
            name = first_meaningful_resource_name(resource_name_candidates(controller, resource), resource_id)
            if name:
                resource_names[resource_id] = name
    return resource_names


def meaningful_resource_name(value, resource_id):
    text = str(value or "").strip()
    if not text:
        return None
    if text == str(resource_id):
        return None
    if text.startswith("ResourceId::"):
        return None
    return text


def build_timing_map(controller):
    try:
        counters = controller.EnumerateCounters()
        target = int(rd.GPUCounter.EventGPUDuration)
        if target not in [int(counter) for counter in counters]:
            return {}
        timing_map = {}
        for result in controller.FetchCounters([rd.GPUCounter.EventGPUDuration]):
            if int(result.counter) != target:
                continue
            value = safe_float(result.value.d)
            if value is not None:
                timing_map[safe_int(result.eventId, 0)] = value * 1000000.0
        return timing_map
    except Exception:
        return {}


def usage_event_id(value):
    for attr in ("eventId", "eventID", "event"):
        try:
            event_id = safe_int(getattr(value, attr), 0)
        except Exception:
            continue
        if event_id and event_id > 0:
            return event_id
    return 0


def usage_event_ids(controller, resource):
    events = set()
    errors = []
    try:
        usages = controller.GetUsage(resource.resourceId) or []
    except Exception as exc:
        return events, [str(exc)]
    for usage in usages:
        event_id = usage_event_id(usage)
        if event_id > 0:
            events.add(event_id)
    return events, errors


def texture_row(controller, texture, resource_names):
    resource_id = resource_id_text(texture.resourceId)
    name = resource_names.get(resource_id)
    if not name:
        name = first_meaningful_resource_name(resource_name_candidates(controller, texture), resource_id)
    if not name:
        return None
    try:
        fmt = str(texture.format.Name())
    except Exception:
        fmt = None
    byte_size = None
    for attr in ("byteSize", "bytesize"):
        try:
            byte_size = safe_int(getattr(texture, attr), None)
            if byte_size is not None:
                break
        except Exception:
            pass
    return {
        "rdc_texture_id": resource_id,
        "texture_name": name,
        "byte_size": byte_size,
        "width": safe_int(getattr(texture, "width", None), None),
        "height": safe_int(getattr(texture, "height", None), None),
        "depth": safe_int(getattr(texture, "depth", None), None),
        "mip_count": safe_int(getattr(texture, "mips", None), None),
        "array_size": safe_int(getattr(texture, "arraysize", None), None),
        "format": fmt,
    }


def add_resource(resources, row):
    if row is None:
        return
    resource_id = row.get("rdc_texture_id")
    if not resource_id:
        return
    if not meaningful_resource_name(row.get("texture_name"), resource_id):
        return
    existing = resources.get(resource_id)
    if existing is None:
        resources[resource_id] = row
        return
    for key, value in row.items():
        if existing.get(key) in (None, "") and value not in (None, ""):
            existing[key] = value


def looks_like_generic_model_name(value):
    text = str(value or "").lower()
    return (
        text.startswith("buffer-")
        or text.startswith("buffer ")
        or text.startswith("resourceid::")
        or "scratchbuffer" in text
        or "d3d12scratchbufferinternal" in text
        or "constant" in text
        or "cbuffer" in text
        or "perframe" in text
        or "percamera" in text
        or "perdraw" in text
        or "material" in text
        or "upload" in text
    )


def looks_like_model_name(value):
    text = str(value or "").lower()
    if looks_like_generic_model_name(text):
        return False
    return (
        text.startswith("sm_")
        or text.startswith("sk_")
        or text.startswith("mesh")
        or "_lod" in text
        or " mesh" in text
    )


def buffer_vertex_count(buffer):
    length = safe_int(getattr(buffer, "length", None), None)
    stride = safe_int(getattr(buffer, "structureByteStride", None), None)
    if length is None or stride is None or stride <= 0:
        return None
    return length // stride


def model_row_from_buffer(buffer, resource_names):
    resource_id = resource_id_text(buffer.resourceId)
    model_name = resource_names.get(resource_id)
    if not model_name or not looks_like_model_name(model_name):
        return None
    return {
        "rdc_model_id": resource_id,
        "model_name": model_name,
        "vertex_buffer_ids": resource_id,
        "index_buffer_id": None,
        "index_count": None,
        "vertex_count": buffer_vertex_count(buffer),
        "instance_count": None,
        "primitive_count": None,
    }


def add_model(models, row):
    if row is None:
        return
    model_id = row.get("rdc_model_id")
    if not model_id:
        return
    existing = models.get(model_id)
    if existing is None:
        models[model_id] = row
        return
    for key, value in row.items():
        if existing.get(key) in (None, "") and value not in (None, ""):
            existing[key] = value


def binding(event_id, texture_id, model_id):
    return {
        "event_id": event_id,
        "rdc_texture_id": texture_id,
        "rdc_model_id": model_id,
    }


def build_usage_bindings(textures, models, texture_usage_by_event, model_usage_by_event, allowed_event_ids):
    rows = []
    seen = set()
    for event_id in sorted(texture_usage_by_event):
        if allowed_event_ids is not None and event_id not in allowed_event_ids:
            continue
        texture_ids = sorted(texture_usage_by_event.get(event_id) or [])
        model_ids = sorted(model_usage_by_event.get(event_id) or [])
        if not model_ids:
            model_ids = [None]
        for texture_id in texture_ids:
            texture = textures.get(texture_id)
            if not texture:
                continue
            for model_id in model_ids:
                key = (event_id, texture_id, model_id)
                if key in seen:
                    continue
                seen.add(key)
                rows.append(binding(event_id, texture_id, model_id))
    return rows


def draw_counts(action, event_name):
    name = str(event_name or "").lower()
    count = safe_int(getattr(action, "numIndices", None), None)
    if "drawindexed" in name or "draw indexed" in name:
        return count, None
    return None, count


def primitive_count_from(action):
    count = safe_int(getattr(action, "numIndices", None), None)
    if count is None:
        return None
    return count // 3 if count >= 3 else None


def inspect_capture(capture_path, config):
    capture = None
    controller = None
    try:
        capture = rd.OpenCaptureFile()
        status = capture.OpenFile(capture_path, "", None)
        if not success_status(status):
            raise RuntimeError("OpenFile failed: {0}".format(status))
        status, controller = capture.OpenCapture(rd.ReplayOptions(), None)
        if not success_status(status):
            raise RuntimeError("OpenCapture failed: {0}".format(status))

        structured_file = controller.GetStructuredFile()
        props = controller.GetAPIProperties()
        graphics_api = compact_enum(getattr(props, "pipelineType", "unknown"))
        timing_map = build_timing_map(controller)
        resource_names = build_resource_name_map(controller)

        textures = {}
        texture_resources = {}
        models = {}
        model_resources = {}
        texture_count = 0
        skipped_texture_count = 0
        skipped_texture_samples = []
        for texture in controller.GetTextures():
            texture_count += 1
            row = texture_row(controller, texture, resource_names)
            if row is None:
                skipped_texture_count += 1
                if len(skipped_texture_samples) < 12:
                    try:
                        resource_id = str(texture.resourceId)
                    except Exception:
                        resource_id = None
                    skipped_texture_samples.append(
                        {
                            "resourceId": resource_id,
                            "rawNames": resource_name_candidates(controller, texture),
                        }
                    )
            add_resource(textures, row)
            if row is not None:
                texture_resources[row["rdc_texture_id"]] = texture

        buffer_count = 0
        skipped_model_buffer_count = 0
        try:
            buffers = controller.GetBuffers()
        except Exception:
            buffers = []
        for buffer in buffers:
            buffer_count += 1
            row = model_row_from_buffer(buffer, resource_names)
            if row is None:
                skipped_model_buffer_count += 1
                continue
            add_model(models, row)
            model_resources[row["rdc_model_id"]] = buffer

        draw_events = []
        event_min = safe_int(config.get("event_min"), 0) or 0
        event_max = safe_int(config.get("event_max"), 0) or 0
        allowed_usage_event_ids = set()
        usage_errors = []

        def walk(actions, parent_event_id, marker_stack):
            for action in actions:
                event_id = safe_int(getattr(action, "eventId", 0), 0)
                event_name = action_name(action, structured_file)
                event_type = classify_action(action)
                current_marker_stack = marker_stack
                if event_type == "marker" and event_name:
                    current_marker_stack = marker_stack + [event_name]
                marker_path = "/".join(current_marker_stack) if current_marker_stack else None
                render_target = None
                depth_target = None
                in_range = (event_min <= 0 or event_id >= event_min) and (event_max <= 0 or event_id <= event_max)
                if event_type in ("draw", "dispatch") and in_range:
                    allowed_usage_event_ids.add(event_id)

                index_count, _vertex_count = draw_counts(action, event_name)
                draw_events.append(
                    {
                        "event_id": event_id,
                        "parent_event_id": parent_event_id,
                        "event_name": event_name,
                        "marker_path": marker_path,
                        "drawcall_type": event_type,
                        "index_count": index_count,
                        "instance_count": safe_int(getattr(action, "numInstances", None), None),
                        "primitive_count": primitive_count_from(action),
                        "render_target": render_target,
                        "depth_target": depth_target,
                        "gpu_us": round(timing_map[event_id], 6) if event_id in timing_map else None,
                    }
                )
                walk(action_children(action), event_id, current_marker_stack)

        walk(controller.GetRootActions(), None, [])

        texture_usage_by_event = {}
        for texture_id, texture in texture_resources.items():
            event_ids, errors = usage_event_ids(controller, texture)
            if errors and len(usage_errors) < 20:
                usage_errors.append({"resourceId": texture_id, "errors": errors[:3]})
            for event_id in event_ids:
                if event_id not in allowed_usage_event_ids:
                    continue
                texture_usage_by_event.setdefault(event_id, set()).add(texture_id)

        model_usage_by_event = {}
        for model_id, buffer in model_resources.items():
            event_ids, errors = usage_event_ids(controller, buffer)
            if errors and len(usage_errors) < 20:
                usage_errors.append({"resourceId": model_id, "errors": errors[:3]})
            for event_id in event_ids:
                if event_id not in allowed_usage_event_ids:
                    continue
                model_usage_by_event.setdefault(event_id, set()).add(model_id)

        bindings = build_usage_bindings(
            textures,
            models,
            texture_usage_by_event,
            model_usage_by_event,
            allowed_usage_event_ids,
        )

        return {
            "ok": True,
            "capture": {
                "path": capture_path,
                "graphics_api": graphics_api,
                "renderdoc_version": renderdoc_version(),
                "draw_event_count": len(draw_events),
                "texture_count": len(textures),
                "model_count": len(models),
                "binding_count": len(bindings),
            },
            "draw_events": draw_events,
            "textures": list(textures.values()),
            "models": list(models.values()),
            "bindings": bindings,
            "stats": {
                "draw_event_rows": len(draw_events),
                "texture_rows": len(textures),
                "model_rows": len(models),
                "buffer_rows_scanned": buffer_count,
                "model_buffer_rows_skipped": skipped_model_buffer_count,
                "texture_rows_scanned": texture_count,
                "texture_rows_skipped_without_name": skipped_texture_count,
                "texture_rows_skipped_samples": skipped_texture_samples,
                "texture_usage_event_count": len(texture_usage_by_event),
                "model_usage_event_count": len(model_usage_by_event),
                "binding_rows": len(bindings),
                "inspected_usage_events": len(allowed_usage_event_ids),
                "usage_errors": usage_errors,
            },
        }
    finally:
        for target in (controller, capture):
            if target is None:
                continue
            try:
                target.Shutdown()
            except Exception:
                pass


def main():
    config_path = os.environ.get(CONFIG_ENV)
    if not config_path:
        os._exit(2)
    config = read_json(config_path)
    output_path = config.get("output")
    if not output_path:
        os._exit(2)
    try:
        capture_path = config.get("capture")
        if not capture_path:
            raise RuntimeError("Missing capture path")
        write_json(output_path, inspect_capture(capture_path, config))
        os._exit(0)
    except Exception:
        write_json(output_path, {"ok": False, "error": traceback.format_exc()})
        os._exit(1)


if __name__ == "__main__":
    main()
