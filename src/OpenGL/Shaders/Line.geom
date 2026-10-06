#version 330 core

// Screen-space line expansion: every GL_LINES segment becomes a camera-facing quad whose on-screen
// width is uLineWidth pixels. This is the portable replacement for glLineWidth > 1, which drivers are
// free to clamp back to 1 in modern core profiles. The quad keeps a constant pixel width whatever the
// perspective, because the offset is applied after the perspective divide.
layout (lines) in;
layout (triangle_strip, max_vertices = 4) out;

uniform vec2 uViewportSize;   // Viewport size in pixels.
uniform float uLineWidth;     // Line width in pixels.
uniform float uNear;          // Near-plane distance, for clipping segments that cross the eye.

in vec4 v_color[];

out vec4 g_color;

void main()
{
    vec4 clip0 = gl_in[0].gl_Position;
    vec4 clip1 = gl_in[1].gl_Position;
    vec4 color0 = v_color[0];
    vec4 color1 = v_color[1];

    // Clip the segment against the near plane (w = uNear): at or behind the eye w is non-positive and the
    // perspective divide is undefined. Interpolating to w = uNear keeps the visible part (the GPU then
    // clips the expanded quad against the real frustum) and gives the screen direction a finite basis,
    // instead of dropping the whole segment the way a plain "w > 0" test would.
    bool in0 = clip0.w >= uNear;
    bool in1 = clip1.w >= uNear;
    if (!in0 && !in1)
        return;   // Entirely behind the near plane: nothing visible.

    if (in0 != in1)
    {
        float t = (uNear - clip0.w) / (clip1.w - clip0.w);
        vec4 crossing = mix(clip0, clip1, t);
        vec4 crossingColor = mix(color0, color1, t);
        if (in0)
        {
            clip1 = crossing;
            color1 = crossingColor;
        }
        else
        {
            clip0 = crossing;
            color0 = crossingColor;
        }
    }

    // Direction in pixel space (uViewportSize already carries the aspect ratio), so the normal below is
    // perpendicular on screen.
    vec2 pixel0 = (clip0.xy / clip0.w) * uViewportSize;
    vec2 pixel1 = (clip1.xy / clip1.w) * uViewportSize;
    vec2 dir = pixel1 - pixel0;
    dir = (dot(dir, dir) < 1e-8) ? vec2(1.0, 0.0) : normalize(dir);
    vec2 normal = vec2(-dir.y, dir.x);

    // Half-width offset in NDC: pixels -> NDC is a factor 2/size, and the half width is width/2.
    vec2 offset = normal * uLineWidth / uViewportSize;

    g_color = color0;
    gl_Position = vec4(clip0.xy + offset * clip0.w, clip0.zw);
    EmitVertex();
    gl_Position = vec4(clip0.xy - offset * clip0.w, clip0.zw);
    EmitVertex();

    g_color = color1;
    gl_Position = vec4(clip1.xy + offset * clip1.w, clip1.zw);
    EmitVertex();
    gl_Position = vec4(clip1.xy - offset * clip1.w, clip1.zw);
    EmitVertex();

    EndPrimitive();
}
