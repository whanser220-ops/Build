"""
GPU pass extraction service for RenderDoc.
"""

import renderdoc as rd

from ..utils import Helpers, Serializers


class GpuPassService:
    """Extract compact GPU pass information from RenderDoc actions."""

    def __init__(self, ctx, invoke_fn):
        self.ctx = ctx
        self._invoke = invoke_fn

    def get_gpu_passes(
        self,
        marker_filter=None,
        exclude_markers=None,
        include_pipeline_state=True,
        include_resources=True,
        include_timings=True,
        include_draws=False,
        event_id_min=None,
        event_id_max=None,
        max_passes=256,
    ):
        """Extract dispatch/draw actions as compact GPU pass records."""
        if not self.ctx.IsCaptureLoaded():
            raise ValueError("No capture loaded")

        result = {"data": None, "error": None}

        def callback(controller):
            structured_file = controller.GetStructuredFile()
            root_actions = controller.GetRootActions()
            api = controller.GetAPIProperties().pipelineType
            texture_map, buffer_map = self._build_resource_maps(controller)
            timings_available, timing_map, timing_error = self._build_timing_map(
                controller, include_timings
            )
            pass_limit = self._normalize_limit(max_passes)
            passes = []
            limit_reached = [False]

            def walk(actions, marker_stack):
                if limit_reached[0]:
                    return

                for action in actions:
                    if limit_reached[0]:
                        return

                    action_name = self._get_action_name(action, structured_file)
                    flags = action.flags
                    is_marker = bool(flags & (rd.ActionFlags.PushMarker | rd.ActionFlags.SetMarker))
                    current_markers = marker_stack[:]
                    if is_marker:
                        current_markers.append(action_name)

                    is_dispatch = bool(flags & rd.ActionFlags.Dispatch)
                    is_draw = bool(flags & rd.ActionFlags.Drawcall)
                    if is_dispatch or (include_draws and is_draw):
                        pass_name = self._resolve_pass_name(action_name, current_markers)
                        if self._passes_filters(
                            action,
                            action_name,
                            pass_name,
                            current_markers,
                            marker_filter,
                            exclude_markers,
                            event_id_min,
                            event_id_max,
                        ):
                            pass_info = self._build_action_pass_info(
                                action,
                                action_name,
                                pass_name,
                                current_markers,
                                is_dispatch,
                                timing_map,
                            )

                            if include_pipeline_state:
                                self._attach_pipeline_state(
                                    controller,
                                    action,
                                    pass_info,
                                    is_dispatch,
                                    include_resources,
                                    texture_map,
                                    buffer_map,
                                )

                            passes.append(pass_info)
                            if pass_limit is not None and len(passes) >= pass_limit:
                                limit_reached[0] = True
                                return

                    if action.children:
                        walk(action.children, current_markers)

            walk(root_actions, [])

            result["data"] = {
                "version": 1,
                "gpu": {
                    "api": str(api),
                    "passes": passes,
                    "metadata": {
                        "pass_count": len(passes),
                        "truncated": limit_reached[0],
                        "max_passes": pass_limit,
                        "marker_filter": marker_filter,
                        "exclude_markers": exclude_markers or [],
                        "include_draws": include_draws,
                        "pipeline_state_included": include_pipeline_state,
                        "resources_included": include_resources and include_pipeline_state,
                        "timings_requested": include_timings,
                        "timings_available": timings_available,
                        "timing_error": timing_error,
                    },
                },
            }

        self._invoke(callback)

        if result["error"]:
            raise ValueError(result["error"])
        return result["data"]

    def _build_action_pass_info(
        self,
        action,
        action_name,
        pass_name,
        marker_stack,
        is_dispatch,
        timing_map,
    ):
        flags = action.flags
        pass_info = {
            "name": pass_name,
            "event_name": action_name,
            "type": "compute" if is_dispatch else "draw",
            "event_id": action.eventId,
            "action_id": action.actionId,
            "flags": Serializers.serialize_flags(flags),
            "marker_path": marker_stack,
            "gpu_ms": timing_map.get(action.eventId),
            "shader": None,
            "shader_entry": None,
            "resources": {
                "srv": [],
                "uav": [],
                "cbv": [],
            },
            "barriers": [],
        }

        if is_dispatch:
            pass_info["dispatch"] = self._get_dispatch_dimensions(action)
            pass_info["indirect"] = bool(flags & rd.ActionFlags.Indirect)
        else:
            pass_info["draw"] = {
                "num_indices": action.numIndices,
                "num_instances": action.numInstances,
                "base_vertex": action.baseVertex,
                "vertex_offset": action.vertexOffset,
                "instance_offset": action.instanceOffset,
                "index_offset": action.indexOffset,
            }

        return pass_info

    def _attach_pipeline_state(
        self,
        controller,
        action,
        pass_info,
        is_dispatch,
        include_resources,
        texture_map,
        buffer_map,
    ):
        try:
            controller.SetFrameEvent(action.eventId, True)
            pipe = controller.GetPipelineState()

            if is_dispatch:
                stage = rd.ShaderStage.Compute
                shader_info = self._get_shader_summary(pipe, stage)
                if shader_info:
                    pass_info["shader"] = shader_info.get("resource_name") or shader_info.get("resource_id")
                    pass_info["shader_entry"] = shader_info.get("entry_point")
                    pass_info["shader_stage"] = "compute"
                    pass_info["shader_resource_id"] = shader_info.get("resource_id")
                    pass_info["shader_name"] = shader_info.get("resource_name")

                    if include_resources:
                        reflection = pipe.GetShaderReflection(stage)
                        pass_info["resources"] = self._get_stage_bindings(
                            controller, pipe, stage, reflection, texture_map, buffer_map
                        )
                return

            shaders = {}
            merged_resources = {"srv": [], "uav": [], "cbv": []}
            for stage in Helpers.get_all_shader_stages():
                shader_info = self._get_shader_summary(pipe, stage)
                if not shader_info:
                    continue

                stage_name = self._stage_name(stage)
                shaders[stage_name] = shader_info
                if include_resources:
                    reflection = pipe.GetShaderReflection(stage)
                    bindings = self._get_stage_bindings(
                        controller, pipe, stage, reflection, texture_map, buffer_map
                    )
                    for key in ("srv", "uav", "cbv"):
                        for binding in bindings[key]:
                            binding["stage"] = stage_name
                            merged_resources[key].append(binding)

            if shaders:
                pass_info["shaders"] = shaders
                pass_info["resources"] = merged_resources
        except Exception as exc:
            pass_info["pipeline_error"] = str(exc)

    def _get_shader_summary(self, pipe, stage):
        shader = pipe.GetShader(stage)
        if shader == rd.ResourceId.Null():
            return None

        shader_info = {
            "resource_id": str(shader),
            "entry_point": pipe.GetShaderEntryPoint(stage),
        }

        try:
            resource_name = self.ctx.GetResourceName(shader)
            if resource_name:
                shader_info["resource_name"] = resource_name
        except Exception:
            pass

        return shader_info

    def _get_stage_bindings(self, controller, pipe, stage, reflection, texture_map, buffer_map):
        return {
            "srv": self._get_stage_srvs(controller, pipe, stage, reflection, texture_map, buffer_map),
            "uav": self._get_stage_uavs(controller, pipe, stage, reflection, texture_map, buffer_map),
            "cbv": self._get_stage_cbuffers(pipe, stage, reflection, texture_map, buffer_map),
        }

    def _get_stage_srvs(self, controller, pipe, stage, reflection, texture_map, buffer_map):
        resources = []
        try:
            srvs = pipe.GetReadOnlyResources(stage, False)
            name_map = self._build_resource_name_map(reflection, "readOnlyResources")

            for srv in srvs:
                resource_id = self._get_descriptor_resource(srv.descriptor)
                if resource_id == rd.ResourceId.Null():
                    continue

                slot = srv.access.index
                info = {
                    "slot": slot,
                    "name": name_map.get(slot, ""),
                    "resource_id": str(resource_id),
                }
                info.update(self._get_resource_details(resource_id, texture_map, buffer_map))

                descriptor = srv.descriptor
                self._try_copy_attr(info, descriptor, "first_mip", "firstMip")
                self._try_copy_attr(info, descriptor, "num_mips", "numMips")
                self._try_copy_attr(info, descriptor, "first_slice", "firstSlice")
                self._try_copy_attr(info, descriptor, "num_slices", "numSlices")
                resources.append(info)
        except Exception as exc:
            resources.append({"error": str(exc)})

        return resources

    def _get_stage_uavs(self, controller, pipe, stage, reflection, texture_map, buffer_map):
        resources = []
        try:
            uavs = pipe.GetReadWriteResources(stage, False)
            name_map = self._build_resource_name_map(reflection, "readWriteResources")

            for uav in uavs:
                resource_id = self._get_descriptor_resource(uav.descriptor)
                if resource_id == rd.ResourceId.Null():
                    continue

                slot = uav.access.index
                info = {
                    "slot": slot,
                    "name": name_map.get(slot, ""),
                    "resource_id": str(resource_id),
                }
                info.update(self._get_resource_details(resource_id, texture_map, buffer_map))

                descriptor = uav.descriptor
                self._try_copy_attr(info, descriptor, "first_element", "firstMip")
                self._try_copy_attr(info, descriptor, "num_elements", "numMips")
                resources.append(info)
        except Exception as exc:
            resources.append({"error": str(exc)})

        return resources

    def _get_stage_cbuffers(self, pipe, stage, reflection, texture_map, buffer_map):
        cbuffers = []
        try:
            if not reflection:
                return cbuffers

            for index, cb in enumerate(reflection.constantBlocks):
                slot = self._get_cbuffer_slot(cb, index)
                info = {
                    "slot": slot,
                    "name": cb.name,
                    "byte_size": cb.byteSize,
                    "variable_count": len(cb.variables) if cb.variables else 0,
                }

                bind = self._try_get_constant_buffer(pipe, stage, slot, index)
                if bind is not None:
                    descriptor = getattr(bind, "descriptor", bind)
                    resource_id = self._get_descriptor_resource(descriptor)
                    if resource_id != rd.ResourceId.Null():
                        info["resource_id"] = str(resource_id)
                        info.update(self._get_resource_details(resource_id, texture_map, buffer_map))
                    self._try_copy_attr(info, descriptor, "byte_offset", "byteOffset")
                    self._try_copy_attr(info, descriptor, "bound_byte_size", "byteSize")

                cbuffers.append(info)
        except Exception as exc:
            cbuffers.append({"error": str(exc)})

        return cbuffers

    def _build_timing_map(self, controller, include_timings):
        if not include_timings:
            return False, {}, None

        try:
            counters = controller.EnumerateCounters()
            if rd.GPUCounter.EventGPUDuration not in counters:
                return False, {}, "GPU timing counters not supported on this capture"

            counter_results = controller.FetchCounters([rd.GPUCounter.EventGPUDuration])
            target_counter = int(rd.GPUCounter.EventGPUDuration)
            timing_map = {}
            for counter_result in counter_results:
                if counter_result.counter != target_counter:
                    continue
                timing_map[counter_result.eventId] = counter_result.value.d * 1000.0
            return True, timing_map, None
        except Exception as exc:
            return False, {}, str(exc)

    def _passes_filters(
        self,
        action,
        action_name,
        pass_name,
        marker_stack,
        marker_filter,
        exclude_markers,
        event_id_min,
        event_id_max,
    ):
        if event_id_min is not None and action.eventId < event_id_min:
            return False
        if event_id_max is not None and action.eventId > event_id_max:
            return False

        haystacks = marker_stack[:] + [action_name, pass_name]
        if marker_filter and not self._any_contains(haystacks, marker_filter):
            return False

        if exclude_markers:
            for exclude_marker in exclude_markers:
                if self._any_contains(haystacks, exclude_marker):
                    return False

        return True

    def _resolve_pass_name(self, action_name, marker_stack):
        for marker in reversed(marker_stack):
            if self._is_logical_pass_name(marker):
                return marker

        if self._is_logical_pass_name(action_name):
            return action_name

        if marker_stack:
            return marker_stack[-1]
        return action_name

    def _is_logical_pass_name(self, value):
        if not value:
            return False

        text = str(value)
        if "." not in text:
            return False

        technical_prefixes = (
            "ExecuteIndirect(",
            "ID3D",
            "[",
        )
        for prefix in technical_prefixes:
            if text.startswith(prefix):
                return False

        return True

    def _get_action_name(self, action, structured_file):
        custom_name = getattr(action, "customName", "")
        if custom_name:
            return custom_name
        return action.GetName(structured_file)

    def _get_dispatch_dimensions(self, action):
        dimensions = getattr(action, "dispatchDimension", None)
        if dimensions is not None:
            try:
                return [int(dimensions[0]), int(dimensions[1]), int(dimensions[2])]
            except Exception:
                pass

        names = [
            ("dispatchX", "dispatchY", "dispatchZ"),
            ("dispatchDimensionX", "dispatchDimensionY", "dispatchDimensionZ"),
        ]
        for x_name, y_name, z_name in names:
            if hasattr(action, x_name):
                return [
                    int(getattr(action, x_name)),
                    int(getattr(action, y_name)),
                    int(getattr(action, z_name)),
                ]

        return None

    def _build_resource_maps(self, controller):
        texture_map = {}
        buffer_map = {}
        try:
            for texture in controller.GetTextures():
                texture_map[str(texture.resourceId)] = texture
        except Exception:
            pass
        try:
            for buffer in controller.GetBuffers():
                buffer_map[str(buffer.resourceId)] = buffer
        except Exception:
            pass
        return texture_map, buffer_map

    def _get_resource_details(self, resource_id, texture_map, buffer_map):
        details = {}
        try:
            resource_name = self.ctx.GetResourceName(resource_id)
            if resource_name:
                details["resource_name"] = resource_name
        except Exception:
            pass

        resource_key = str(resource_id)
        texture = texture_map.get(resource_key)
        if texture is not None:
            details["resource_type"] = "texture"
            details["width"] = texture.width
            details["height"] = texture.height
            details["depth"] = texture.depth
            details["array_size"] = texture.arraysize
            details["mip_levels"] = texture.mips
            details["dimension"] = str(texture.type)
            details["msaa_samples"] = texture.msSamp
            try:
                details["format"] = str(texture.format.Name())
            except Exception:
                details["format"] = str(texture.format)
            return details

        buffer = buffer_map.get(resource_key)
        if buffer is not None:
            details["resource_type"] = "buffer"
            details["length"] = buffer.length
            return details

        return details

    def _build_resource_name_map(self, reflection, reflection_attr):
        name_map = {}
        if not reflection:
            return name_map

        try:
            reflected_resources = getattr(reflection, reflection_attr)
            for reflected_resource in reflected_resources:
                bind_number = getattr(reflected_resource, "fixedBindNumber", None)
                if bind_number is None:
                    bind_number = getattr(reflected_resource, "bindPoint", None)
                if bind_number is not None:
                    name_map[bind_number] = reflected_resource.name
        except Exception:
            pass

        return name_map

    def _get_descriptor_resource(self, descriptor):
        resource_id = getattr(descriptor, "resource", None)
        if resource_id is None:
            resource_id = getattr(descriptor, "resourceId", rd.ResourceId.Null())
        return resource_id

    def _get_cbuffer_slot(self, cbuffer, fallback_index):
        slot = getattr(cbuffer, "bindPoint", None)
        if slot is None:
            slot = getattr(cbuffer, "fixedBindNumber", None)
        if slot is None:
            slot = fallback_index
        return slot

    def _try_get_constant_buffer(self, pipe, stage, slot, fallback_index):
        try:
            return pipe.GetConstantBlock(stage, slot, 0)
        except Exception:
            try:
                return pipe.GetConstantBlock(stage, fallback_index, 0)
            except Exception:
                return None

    def _try_copy_attr(self, target, source, target_name, source_name):
        try:
            target[target_name] = getattr(source, source_name)
        except Exception:
            pass

    def _any_contains(self, values, needle):
        needle_lower = str(needle).lower()
        for value in values:
            if needle_lower in str(value).lower():
                return True
        return False

    def _normalize_limit(self, max_passes):
        if max_passes is None:
            return None
        try:
            normalized = int(max_passes)
        except Exception:
            return 256
        if normalized <= 0:
            return None
        return normalized

    def _stage_name(self, stage):
        if stage == rd.ShaderStage.Vertex:
            return "vertex"
        if stage == rd.ShaderStage.Hull:
            return "hull"
        if stage == rd.ShaderStage.Domain:
            return "domain"
        if stage == rd.ShaderStage.Geometry:
            return "geometry"
        if stage == rd.ShaderStage.Pixel:
            return "pixel"
        if stage == rd.ShaderStage.Compute:
            return "compute"
        return str(stage)
