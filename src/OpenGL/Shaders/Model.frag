#version 330 core

in vec2 v_uv;
in vec3 v_normal;
in vec3 v_tangent;
in vec3 v_worldPos;

#define MAX_LIGHTS 8

uniform int uLightCount;
uniform vec3 uLightColors[MAX_LIGHTS];
uniform vec3 uLightPositions[MAX_LIGHTS];
uniform vec3 uLightDirections[MAX_LIGHTS];
uniform int  uLightTypes[MAX_LIGHTS];
uniform float uLightIntensities[MAX_LIGHTS];

uniform vec3 uViewPos;
uniform vec3 uAmbientColor;   // Scene ambient light (passed each frame by the Renderer from Scene.Settings).
uniform float uHighlightMix;     // Highlight blend factor (0 = none; >0 blends the final color toward uHighlightColor).
uniform vec3 uHighlightColor;    // Target color of the highlight blend (click-selection feedback).
uniform vec4 uBaseColor;

uniform sampler2D uAlbedo;
uniform sampler2D uNormal;
uniform int uHasAlbedo;
uniform int uHasNormal;

out vec4 out_color;

void main()
{
    // 1. Diffuse color.
    vec4 albedo = uHasAlbedo == 1 ? texture(uAlbedo, v_uv) : uBaseColor;
    if (albedo.a < 0.1) discard;

    // 2. Normal (transformed via TBN to world space when a normal map is present).
    vec3 normal = normalize(v_normal);
    if (uHasNormal == 1) {
        vec3 tangentNormal = texture(uNormal, v_uv).xyz * 2.0 - 1.0;
        vec3 T = normalize(v_tangent);
        vec3 N = normalize(v_normal);
        T = normalize(T - dot(T, N) * N);
        vec3 B = cross(N, T);
        normal = normalize(mat3(T, B, N) * tangentNormal);
    }

    vec3 viewDir = normalize(uViewPos - v_worldPos);

    // 3. Per-light (Blinn-Phong).
    vec3 result = vec3(0.0);
    for (int i = 0; i < uLightCount; i++)
    {
        vec3 lightColor = uLightColors[i] * uLightIntensities[i];

        vec3 lightDir;
        if (uLightTypes[i] == 1)
            lightDir = normalize(-uLightDirections[i]);
        else
            lightDir = normalize(uLightPositions[i] - v_worldPos);

        float diff = max(dot(normal, lightDir), 0.0);
        vec3 halfDir = normalize(lightDir + viewDir);
        float spec = pow(max(dot(normal, halfDir), 0.0), 32.0);
        result += lightColor * (diff + spec * 0.5);
    }

    // Ambient light comes from the scene settings (so shadowed faces are not pure black; independent of lights).
    vec3 lit = (uAmbientColor + result) * albedo.rgb;

    // Highlight: blend the final color toward HighlightColor (works with or without textures; selection feedback).
    lit = mix(lit, uHighlightColor, uHighlightMix);

    out_color = vec4(lit, albedo.a);
}
