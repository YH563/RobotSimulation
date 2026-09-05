#version 330 core

uniform samplerCube uSkybox;

in vec3 v_uvw;

out vec4 out_color;

void main()
{
    out_color = texture(uSkybox, v_uvw);
}
