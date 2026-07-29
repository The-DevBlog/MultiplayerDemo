shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix, depth_draw_never;

uniform sampler2D nav_data : filter_nearest, repeat_disable;
uniform vec2 cell_count = vec2(1.0, 1.0);
uniform vec2 grid_origin = vec2(0.0, 0.0);
uniform vec2 grid_world_size = vec2(1.0, 1.0);

uniform bool show_grid = true;
uniform bool show_blocked = false;
uniform bool show_flow = false;
uniform bool show_target = false;

uniform vec4 grid_color : source_color = vec4(1.0, 1.0, 1.0, 0.35);
uniform vec4 blocked_color : source_color = vec4(1.0, 0.05, 0.05, 0.9);
uniform vec4 flow_color : source_color = vec4(0.1, 0.75, 1.0, 0.9);
uniform vec4 target_color : source_color = vec4(1.0, 0.9, 0.1, 1.0);

uniform float grid_line_width : hint_range(0.0005, 0.2, 0.0005) = 0.012;
uniform float marker_line_width : hint_range(0.001, 0.2, 0.001) = 0.025;

varying vec3 world_position;

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

float line_alpha(float distance_to_line, float width) {
	float aa = max(fwidth(distance_to_line), 0.0005);
	return 1.0 - smoothstep(width, width + aa, distance_to_line);
}

float segment_alpha(vec2 point, vec2 start, vec2 end, float width) {
	vec2 segment = end - start;
	float segment_len_sq = max(dot(segment, segment), 0.00001);
	float t = clamp(dot(point - start, segment) / segment_len_sq, 0.0, 1.0);
	vec2 closest = start + segment * t;
	return line_alpha(length(point - closest), width);
}

vec4 blend_layer(vec4 base_color, vec4 layer_color, float layer_alpha) {
	float alpha = clamp(layer_color.a * layer_alpha, 0.0, 1.0);
	vec3 rgb = mix(base_color.rgb, layer_color.rgb, alpha);
	return vec4(rgb, max(base_color.a, alpha));
}

float padded_cell_mask(vec2 cell_uv, float padding) {
	float left = step(padding, cell_uv.x);
	float right = step(cell_uv.x, 1.0 - padding);
	float top = step(padding, cell_uv.y);
	float bottom = step(cell_uv.y, 1.0 - padding);
	return left * right * top * bottom;
}

float blocked_x_alpha(vec2 cell_uv) {
	float padding_mask = padded_cell_mask(cell_uv, 0.18);
	float diagonal_a = line_alpha(abs(cell_uv.y - cell_uv.x) * 0.70710678, marker_line_width);
	float diagonal_b = line_alpha(abs(cell_uv.y - (1.0 - cell_uv.x)) * 0.70710678, marker_line_width);
	return max(diagonal_a, diagonal_b) * padding_mask;
}

float flow_arrow_alpha(vec2 cell_uv, vec2 direction) {
	float has_direction = step(0.5, length(direction));
	if (has_direction < 0.5) {
		return 0.0;
	}

	direction = normalize(direction);
	vec2 perpendicular = vec2(-direction.y, direction.x);
	vec2 center = vec2(0.5, 0.5);

	float arrow_length = 0.58;
	float head_length = 0.18;
	float head_width = 0.13;

	vec2 start = center - direction * arrow_length * 0.5;
	vec2 end = center + direction * arrow_length * 0.5;
	vec2 head_left = end - direction * head_length + perpendicular * head_width;
	vec2 head_right = end - direction * head_length - perpendicular * head_width;

	float shaft = segment_alpha(cell_uv, start, end, marker_line_width);
	float left = segment_alpha(cell_uv, end, head_left, marker_line_width);
	float right = segment_alpha(cell_uv, end, head_right, marker_line_width);
	return max(shaft, max(left, right));
}

float target_alpha(vec2 cell_uv) {
	vec2 center = vec2(0.5, 0.5);
	float radius = 0.30;
	vec2 top = center + vec2(0.0, -radius);
	vec2 right = center + vec2(radius, 0.0);
	vec2 bottom = center + vec2(0.0, radius);
	vec2 left = center + vec2(-radius, 0.0);

	float a = segment_alpha(cell_uv, top, right, marker_line_width);
	a = max(a, segment_alpha(cell_uv, right, bottom, marker_line_width));
	a = max(a, segment_alpha(cell_uv, bottom, left, marker_line_width));
	a = max(a, segment_alpha(cell_uv, left, top, marker_line_width));
	return a;
}

void fragment() {
	vec2 overlay_uv = (world_position.xz - grid_origin) / grid_world_size;
	if (overlay_uv.x < 0.0 || overlay_uv.y < 0.0 || overlay_uv.x > 1.0 || overlay_uv.y > 1.0) {
		discard;
	}

	vec2 scaled_uv = overlay_uv * cell_count;
	vec2 cell_uv = fract(scaled_uv);
	vec2 cell_index = clamp(floor(scaled_uv), vec2(0.0), cell_count - vec2(1.0));
	vec2 data_uv = (cell_index + vec2(0.5)) / cell_count;
	vec4 data = texture(nav_data, data_uv);

	vec4 color = vec4(0.0);

	if (show_grid) {
		vec2 distance_to_grid = min(cell_uv, 1.0 - cell_uv);
		float grid_alpha = max(
			line_alpha(distance_to_grid.x, grid_line_width),
			line_alpha(distance_to_grid.y, grid_line_width)
		);
		color = blend_layer(color, grid_color, grid_alpha);
	}

	if (show_blocked && data.r > 0.5) {
		color = blend_layer(color, blocked_color, blocked_x_alpha(cell_uv));
	}

	if (show_flow) {
		vec2 direction = round(data.gb * 2.0 - 1.0);
		color = blend_layer(color, flow_color, flow_arrow_alpha(cell_uv, direction));
	}

	if (show_target && data.a > 0.5) {
		color = blend_layer(color, target_color, target_alpha(cell_uv));
	}

	if (color.a <= 0.001) {
		discard;
	}

	ALBEDO = color.rgb;
	EMISSION = color.rgb * color.a;
	ALPHA = color.a;
}