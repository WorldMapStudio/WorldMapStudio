shader_type spatial;
render_mode blend_mix, cull_disabled, unshaded;

// An infinite ground grid, à la an empty Blender scene. The grid lives in world
// space, so the plane it is drawn on can freely follow the camera without the
// lines appearing to slide. The whole thing fades out radially around the
// camera, so the fade always stays centred on wherever you are looking from.
//
// The two axis line colors are set at runtime by ViewportWindow from the project's
// AxisConvention, so they always read as the user's own axes (red = user X, green = user Y,
// blue = user Z) no matter how those are mapped onto Godot's X/Y/Z. The defaults below match
// Godot's own axes (the identity convention): world X = user X (red), world Z = user Z (blue).

uniform vec3 line_color : source_color = vec3(0.36, 0.36, 0.36);
uniform vec3 major_line_color : source_color = vec3(0.46, 0.46, 0.46);
uniform vec3 x_axis_color : source_color = vec3(0.55, 0.18, 0.18);
uniform vec3 z_axis_color : source_color = vec3(0.18, 0.32, 0.6);
uniform float cell_size = 1.0;
uniform float major_every = 10.0;
uniform float fade_start = 18.0;
uniform float fade_end = 90.0;
uniform float grid_opacity = 1.0;

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

// Screen-space anti-aliased coverage of grid lines at the given spacing.
float grid_coverage(vec2 coord, float spacing, float thickness) {
	vec2 c = coord / spacing;
	vec2 aa = fwidth(c) * thickness;
	vec2 lines = abs(fract(c - 0.5) - 0.5) / aa;
	return 1.0 - clamp(min(lines.x, lines.y), 0.0, 1.0);
}

// Anti-aliased coverage of a single axis line sitting at coord == 0.
float axis_coverage(float coord, float thickness) {
	float aa = fwidth(coord) * thickness;
	return 1.0 - clamp(abs(coord) / aa, 0.0, 1.0);
}

void fragment() {
	vec2 coord = world_pos.xz;

	float minor = grid_coverage(coord, cell_size, 1.0);
	float major = grid_coverage(coord, cell_size * major_every, 1.5);

	vec3 col = mix(line_color, major_line_color, major);
	float alpha = max(minor, major);

	// The X axis runs along world X, so it is the line where Z == 0, and vice versa.
	float x_axis = axis_coverage(coord.y, 1.5);
	float z_axis = axis_coverage(coord.x, 1.5);
	col = mix(col, x_axis_color, x_axis);
	col = mix(col, z_axis_color, z_axis);
	alpha = max(alpha, max(x_axis, z_axis));

	// Radial fade centred on the camera, so the fade follows it everywhere.
	float dist = distance(world_pos, CAMERA_POSITION_WORLD);
	float fade = 1.0 - smoothstep(fade_start, fade_end, dist);

	alpha *= fade * grid_opacity;
	if (alpha <= 0.001) {
		discard;
	}

	ALBEDO = col;
	ALPHA = alpha;
}
