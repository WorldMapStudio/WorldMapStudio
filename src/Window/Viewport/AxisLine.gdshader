shader_type spatial;
render_mode blend_mix, cull_disabled, unshaded;

// A single infinite line through the world origin along Godot's up (Y) axis, completing the
// ground grid's own X/Z axis lines. It is drawn on a large quad that stays centred on the origin
// horizontally and is kept yawed to face the camera each frame (see ViewportWindow), so from any
// angle it still reads as a crisp, constant-width screen-space line, using the exact same
// anti-aliasing and radial fade as the grid's axis lines.
//
// The color is set at runtime by ViewportWindow from the project's AxisConvention, so it always
// reads as the user's own axis (red = user X, green = user Y, blue = user Z). The default below
// matches Godot's own axes (the identity convention): world Y = user Y (green).

uniform vec3 axis_color : source_color = vec3(0.18, 0.5, 0.18);
uniform float fade_start = 18.0;
uniform float fade_end = 90.0;
uniform float line_opacity = 1.0;

varying vec3 world_pos;
varying float local_x;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	// The quad's local X is already the exact signed lateral offset from the line (the quad is
	// rigid and always faces the camera, so local X maps linearly onto a world direction
	// perpendicular to the view). Unlike length(world_pos.xz), it is smooth (no cusp) through
	// zero, so fwidth() of it gives a stable, non-flickering derivative right at the line.
	local_x = VERTEX.x;
}

// Anti-aliased coverage of a single axis line sitting at coord == 0, identical to the ground
// grid's axis_coverage(): fwidth() is taken of the raw signed coord (never abs()), which is what
// keeps the derivative well-behaved and the line from flickering/dashing near coord == 0.
float axis_coverage(float coord, float thickness) {
	float aa = fwidth(coord) * thickness;
	return 1.0 - clamp(abs(coord) / aa, 0.0, 1.0);
}

void fragment() {
	float alpha = axis_coverage(local_x, 1.5);

	float dist = distance(world_pos, CAMERA_POSITION_WORLD);
	float fade = 1.0 - smoothstep(fade_start, fade_end, dist);

	alpha *= fade * line_opacity;
	if (alpha <= 0.001) {
		discard;
	}

	ALBEDO = axis_color;
	ALPHA = alpha;
}
