shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled;

uniform float AgentRadius : hint_range(0.1, 2.0, 0.05) = 0.65;
uniform float AvoidanceRadius : hint_range(0.5, 10.0, 0.1) = 5.0;

void fragment() {
	vec2 local_position = (UV - vec2(0.5)) * 2.0 * AvoidanceRadius;
	float radius = length(local_position);
	float edge_width = max(fwidth(radius) * 1.5, 0.01);
	float disk = 1.0 - smoothstep(AvoidanceRadius - edge_width, AvoidanceRadius, radius);
	float avoidance_ring = 1.0 - smoothstep(
		edge_width,
		edge_width * 2.0,
		abs(radius - AvoidanceRadius)
	);
	float agent_ring = 1.0 - smoothstep(
		edge_width,
		edge_width * 2.0,
		abs(radius - AgentRadius)
	);
	float agent_fill = 1.0 - smoothstep(AgentRadius - edge_width, AgentRadius, radius);

	vec3 avoidance_color = vec3(0.1, 0.75, 1.0);
	vec3 agent_color = vec3(1.0, 0.55, 0.08);
	float agent_weight = max(agent_ring, agent_fill * 0.35);

	ALBEDO = mix(avoidance_color, agent_color, agent_weight);
	ALPHA = disk * (0.035 + avoidance_ring * 0.55 + agent_ring * 0.8 + agent_fill * 0.08);

	if (ALPHA < 0.01) {
		discard;
	}
}
