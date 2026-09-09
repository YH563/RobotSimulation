#version 330 core

layout (location = 0) in vec3 aPosition;

uniform mat4 uAxesModel;          // World "rotation + translation" matrix of the axis (parent Scale removed).
uniform mat4 uView;
uniform mat4 uProjection;
uniform vec3 uViewPos;

// "Constant screen size" control for the axes:
uniform vec3  uAxesOrigin;        // Axis origin (the node's world position).
uniform float uAxesRefLocalLen;   // Total arrow length in local space along the axis (to isotropically scale geometry to the target length).
uniform float uAxesRatio;         // Desired axis length = uAxesRatio × camera-to-origin distance, then clamped by min/max.
uniform float uAxesMinLength;     // Minimum world length.
uniform float uAxesMaxLength;     // Maximum world length.

void main()
{
    // Use the "scaleless" model matrix first: off contains only rotation, no parent scale.
    vec4 worldR = uAxesModel * vec4(aPosition, 1.0);
    vec3 off = worldR.xyz - uAxesOrigin;

    // Isotropic scale: stretch/shrink the whole arrow uniformly to the target length while preserving shape.
    float targetLen = clamp(uAxesRatio * length(uViewPos - uAxesOrigin),
                            uAxesMinLength, uAxesMaxLength);
    float s = targetLen / uAxesRefLocalLen;

    vec3 finalPos = uAxesOrigin + off * s;
    gl_Position = uProjection * uView * vec4(finalPos, 1.0);
}
