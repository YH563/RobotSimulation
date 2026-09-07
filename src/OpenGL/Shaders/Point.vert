#version 330 core

layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec4 aColor;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;
uniform float uPointSize;

out vec4 v_color;

void main()
{
    v_color = aColor;
    gl_Position = uProjection * uView * uModel * vec4(aPosition, 1.0);
    gl_PointSize = uPointSize;
}
