-- Example queries for the RenderDoc .rdc import schema.

-- Capture list.
SELECT
    capture_id,
    project_name,
    build_id,
    scene_name,
    frame_index,
    graphics_api,
    platform,
    resolution_width,
    resolution_height,
    captured_at,
    rdc_file_path
FROM capture
ORDER BY captured_at DESC, capture_id DESC;

-- Draw/event rows under one marker path.
SELECT
    event_pk,
    event_id,
    parent_event_id,
    event_name,
    marker_path,
    drawcall_type,
    index_count,
    instance_count,
    primitive_count,
    render_target,
    depth_target,
    gpu_us
FROM draw_event
WHERE capture_id = :capture_id
  AND marker_path LIKE :marker_prefix || '%'
ORDER BY event_id;

-- Instance/model GPU cost. This deduplicates binding rows so one event is
-- counted once per model_name, even when the event has multiple textures or
-- multiple RenderDoc model buffer resources for the same mesh name.
WITH model_events AS (
    SELECT DISTINCT
        m.model_name,
        d.event_pk,
        d.event_id,
        d.gpu_us
    FROM event_resource_binding AS b
    JOIN draw_event AS d
      ON d.event_pk = b.event_pk
    JOIN model AS m
      ON m.model_pk = b.model_pk
    WHERE d.capture_id = :capture_id
      AND b.model_pk IS NOT NULL
)
SELECT
    model_name,
    COUNT(*) AS event_count,
    SUM(COALESCE(gpu_us, 0.0)) AS total_gpu_us,
    AVG(gpu_us) AS avg_gpu_us,
    MAX(gpu_us) AS max_gpu_us
FROM model_events
GROUP BY model_name
ORDER BY total_gpu_us DESC, event_count DESC;

-- Texture bindings for one event, including the model relationship when available.
SELECT
    d.event_id,
    d.event_name,
    d.gpu_us,
    m.model_name,
    t.rdc_texture_id,
    t.texture_name,
    t.width,
    t.height,
    t.format,
    t.byte_size
FROM event_resource_binding AS b
JOIN draw_event AS d
  ON d.event_pk = b.event_pk
JOIN texture AS t
  ON t.texture_pk = b.texture_pk
LEFT JOIN model AS m
  ON m.model_pk = b.model_pk
WHERE d.capture_id = :capture_id
  AND d.event_id = :event_id
ORDER BY t.rdc_texture_id, m.model_name;

-- Textures used by each model.
SELECT
    m.model_name,
    t.texture_name,
    COUNT(*) AS event_binding_count
FROM event_resource_binding AS b
JOIN model AS m
  ON m.model_pk = b.model_pk
JOIN texture AS t
  ON t.texture_pk = b.texture_pk
JOIN draw_event AS d
  ON d.event_pk = b.event_pk
WHERE d.capture_id = :capture_id
GROUP BY
    m.model_name,
    t.texture_name
ORDER BY event_binding_count DESC, m.model_name, t.texture_name;

-- Models that use a specific texture.
SELECT
    m.model_name,
    t.texture_name,
    COUNT(DISTINCT d.event_id) AS event_count,
    SUM(COALESCE(d.gpu_us, 0.0)) AS gpu_us_sum
FROM event_resource_binding AS b
JOIN model AS m
  ON m.model_pk = b.model_pk
JOIN texture AS t
  ON t.texture_pk = b.texture_pk
JOIN draw_event AS d
  ON d.event_pk = b.event_pk
WHERE d.capture_id = :capture_id
  AND t.texture_name = :texture_name
GROUP BY m.model_name, t.texture_name
ORDER BY gpu_us_sum DESC, event_count DESC;

-- Texture usage fan-out.
SELECT
    t.rdc_texture_id,
    t.texture_name,
    COUNT(*) AS event_binding_count
FROM event_resource_binding AS b
JOIN texture AS t
  ON t.texture_pk = b.texture_pk
JOIN draw_event AS d
  ON d.event_pk = b.event_pk
WHERE d.capture_id = :capture_id
GROUP BY
    t.rdc_texture_id,
    t.texture_name
ORDER BY event_binding_count DESC, t.texture_name;
