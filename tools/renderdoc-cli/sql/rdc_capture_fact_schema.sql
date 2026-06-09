-- PostgreSQL schema for importing RenderDoc .rdc capture data.
-- Current model keeps capture, draw_event, texture, model, and
-- event_resource_binding.

DROP TABLE IF EXISTS event_unity_object CASCADE;
DROP TABLE IF EXISTS event_gpu_counter CASCADE;
DROP TABLE IF EXISTS event_pipeline_state CASCADE;
DROP TABLE IF EXISTS event_resource_usage CASCADE;
DROP TABLE IF EXISTS gpu_resource CASCADE;
DROP TABLE IF EXISTS frame_event CASCADE;
DROP TABLE IF EXISTS capture_file CASCADE;

CREATE TABLE IF NOT EXISTS capture (
    capture_id INTEGER PRIMARY KEY,
    project_name TEXT,
    build_id TEXT,
    scene_name TEXT,
    frame_index INTEGER,
    rdc_file_path TEXT,
    graphics_api TEXT,
    platform TEXT,
    resolution_width INTEGER,
    resolution_height INTEGER,
    captured_at TEXT
);

CREATE TABLE IF NOT EXISTS draw_event (
    event_pk INTEGER PRIMARY KEY,
    capture_id INTEGER NOT NULL,
    event_id INTEGER NOT NULL,
    parent_event_id INTEGER,
    event_name TEXT,
    marker_path TEXT,
    drawcall_type TEXT,

    index_count INTEGER,
    instance_count INTEGER,
    primitive_count INTEGER,

    render_target TEXT,
    depth_target TEXT,
    gpu_us DOUBLE PRECISION,

    FOREIGN KEY (capture_id) REFERENCES capture(capture_id)
);

-- Importer writes only named Texture resources. Anonymous fallback names such as
-- ResourceId::346 are filtered out before insertion.
CREATE TABLE IF NOT EXISTS texture (
    texture_pk INTEGER PRIMARY KEY,
    capture_id INTEGER NOT NULL,
    rdc_texture_id TEXT NOT NULL,

    texture_name TEXT,

    byte_size INTEGER,

    width INTEGER,
    height INTEGER,
    depth INTEGER,
    mip_count INTEGER,
    array_size INTEGER,
    format TEXT,

    FOREIGN KEY (capture_id) REFERENCES capture(capture_id)
);

CREATE TABLE IF NOT EXISTS model (
    model_pk INTEGER PRIMARY KEY,
    capture_id INTEGER NOT NULL,
    rdc_model_id TEXT NOT NULL,

    model_name TEXT,
    vertex_buffer_ids TEXT,
    index_buffer_id TEXT,

    index_count INTEGER,
    vertex_count INTEGER,
    instance_count INTEGER,
    primitive_count INTEGER,

    FOREIGN KEY (capture_id) REFERENCES capture(capture_id)
);

-- Unity-side prefab facts imported from unity_prefab_signatures.sqlite.
-- These rows map a prefab asset to the model names and texture names observed
-- in Unity, so database queries can resolve prefab names before aggregating
-- RenderDoc model costs.
CREATE TABLE IF NOT EXISTS prefab (
    prefab_pk INTEGER PRIMARY KEY,
    capture_id INTEGER NOT NULL,
    prefab_name TEXT NOT NULL,
    prefab_path TEXT,
    prefab_guid TEXT,
    source TEXT,

    FOREIGN KEY (capture_id) REFERENCES capture(capture_id)
);

CREATE TABLE IF NOT EXISTS prefab_model (
    prefab_model_pk INTEGER PRIMARY KEY,
    prefab_pk INTEGER NOT NULL,
    model_name TEXT NOT NULL,
    model_pk INTEGER,
    lod_index INTEGER,
    prefab_inner_path TEXT,
    scene_instance_path TEXT,
    scene_renderer_path TEXT,

    FOREIGN KEY (prefab_pk) REFERENCES prefab(prefab_pk) ON DELETE CASCADE,
    FOREIGN KEY (model_pk) REFERENCES model(model_pk)
);

CREATE TABLE IF NOT EXISTS prefab_texture (
    prefab_texture_pk INTEGER PRIMARY KEY,
    prefab_pk INTEGER NOT NULL,
    texture_name TEXT NOT NULL,
    texture_pk INTEGER,

    FOREIGN KEY (prefab_pk) REFERENCES prefab(prefab_pk) ON DELETE CASCADE,
    FOREIGN KEY (texture_pk) REFERENCES texture(texture_pk)
);

-- Importer writes only Texture bindings. The row links one event, one texture,
-- and, when available, the model drawn by that event.
CREATE TABLE IF NOT EXISTS event_resource_binding (
    binding_id INTEGER PRIMARY KEY,
    event_pk INTEGER NOT NULL,
    texture_pk INTEGER NOT NULL,
    model_pk INTEGER,

    FOREIGN KEY (event_pk) REFERENCES draw_event(event_pk),
    FOREIGN KEY (texture_pk) REFERENCES texture(texture_pk),
    FOREIGN KEY (model_pk) REFERENCES model(model_pk)
);

CREATE INDEX IF NOT EXISTS idx_capture_rdc_file_path
    ON capture(rdc_file_path);

CREATE UNIQUE INDEX IF NOT EXISTS idx_draw_event_capture_event
    ON draw_event(capture_id, event_id);

CREATE INDEX IF NOT EXISTS idx_draw_event_marker_path
    ON draw_event(capture_id, marker_path);

CREATE UNIQUE INDEX IF NOT EXISTS idx_texture_capture_texture_id
    ON texture(capture_id, rdc_texture_id);

CREATE INDEX IF NOT EXISTS idx_texture_name
    ON texture(capture_id, texture_name);

CREATE UNIQUE INDEX IF NOT EXISTS idx_model_capture_model_id
    ON model(capture_id, rdc_model_id);

CREATE INDEX IF NOT EXISTS idx_model_name
    ON model(capture_id, model_name);

CREATE INDEX IF NOT EXISTS idx_prefab_name
    ON prefab(capture_id, prefab_name);

CREATE INDEX IF NOT EXISTS idx_prefab_path
    ON prefab(capture_id, prefab_path);

CREATE INDEX IF NOT EXISTS idx_prefab_model_name
    ON prefab_model(model_name);

CREATE INDEX IF NOT EXISTS idx_prefab_model_model
    ON prefab_model(model_pk);

CREATE INDEX IF NOT EXISTS idx_prefab_texture_name
    ON prefab_texture(texture_name);

CREATE INDEX IF NOT EXISTS idx_prefab_texture_texture
    ON prefab_texture(texture_pk);

CREATE INDEX IF NOT EXISTS idx_event_resource_binding_event
    ON event_resource_binding(event_pk);

CREATE INDEX IF NOT EXISTS idx_event_resource_binding_texture
    ON event_resource_binding(texture_pk);

CREATE INDEX IF NOT EXISTS idx_event_resource_binding_model
    ON event_resource_binding(model_pk);
