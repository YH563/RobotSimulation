using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace RobotSimulation.OpenGL.Resources;

/// <summary>
/// A compiled+linked GLSL shader program. The vertex/fragment sources are compiled and linked in the
/// constructor (a compile or link error throws); uniform locations are looked up lazily and cached, and
/// calls for uniforms the program does not declare are silently ignored.
/// </summary>
public class ShaderProgram : IDisposable
{
    private readonly GL _gl;
    private readonly uint _handle;
    private readonly Dictionary<string, int> _uniformLocations = new();
    private bool _disposed = false;

    /// <summary>Compiles and links a program from GLSL source text.</summary>
    /// <param name="gl">The GL facade this program is created on.</param>
    /// <param name="vertexSrc">Vertex shader source.</param>
    /// <param name="fragmentSrc">Fragment shader source.</param>
    /// <exception cref="Exception">The vertex or fragment shader failed to compile, or linking failed.</exception>
    public ShaderProgram(GL gl, string vertexSrc, string fragmentSrc)
    {
        _gl = gl;

        // Compile the vertex shader.
        uint vertex = _gl.CreateShader(ShaderType.VertexShader);
        _gl.ShaderSource(vertex, vertexSrc);
        _gl.CompileShader(vertex);
        CheckShaderError(vertex, "VERTEX");

        // Compile the fragment shader.
        uint fragment = _gl.CreateShader(ShaderType.FragmentShader);
        _gl.ShaderSource(fragment, fragmentSrc);
        _gl.CompileShader(fragment);
        CheckShaderError(fragment, "FRAGMENT");

        // Link the program.
        _handle = _gl.CreateProgram();
        _gl.AttachShader(_handle, vertex);
        _gl.AttachShader(_handle, fragment);
        _gl.LinkProgram(_handle);
        CheckProgramError();

        // Clean up the intermediate shader objects.
        _gl.DetachShader(_handle, vertex);
        _gl.DetachShader(_handle, fragment);
        _gl.DeleteShader(vertex);
        _gl.DeleteShader(fragment);
    }

    /// <summary>Binds this program as the one subsequent draw calls use.</summary>
    public void Use() => _gl.UseProgram(_handle);

    // ---- Uniform writes: the location is queried once and cached; subsequent set calls do not re-query GetUniformLocation ----

    /// <summary>Writes a single int uniform.</summary>
    public void SetUniform(string name, int value) => SetIfValid(name, loc => _gl.Uniform1(loc, value));

    /// <summary>Writes a single float uniform.</summary>
    public void SetUniform(string name, float value) => SetIfValid(name, loc => _gl.Uniform1(loc, value));

    /// <summary>Writes a float array uniform (float[] / float[n]); an empty array is a no-op.</summary>
    public void SetUniform(string name, float[] values)
    {
        if (values.Length == 0) return;
        int loc = GetLocation(name);
        if (loc == -1) return;
        unsafe
        {
            fixed (float* ptr = values)
                _gl.Uniform1(loc, (uint)values.Length, ptr);
        }
    }

    /// <summary>Writes an int array uniform (int[] / int[n]); an empty array is a no-op.</summary>
    public void SetUniform(string name, int[] values)
    {
        if (values.Length == 0) return;
        int loc = GetLocation(name);
        if (loc == -1) return;
        unsafe
        {
            fixed (int* ptr = values)
                _gl.Uniform1(loc, (uint)values.Length, ptr);
        }
    }

    /// <summary>Writes a 2-component float uniform.</summary>
    public void SetUniform(string name, Vector2 value) => SetIfValid(name, loc => _gl.Uniform2(loc, value));

    /// <summary>Writes a 3-component float uniform.</summary>
    public void SetUniform(string name, Vector3 value) => SetIfValid(name, loc => _gl.Uniform3(loc, value));

    /// <summary>Writes a Vector3 array uniform (vec3[n]); an empty array is a no-op.</summary>
    public void SetUniform(string name, Vector3[] values)
    {
        if (values.Length == 0) return;
        int loc = GetLocation(name);
        if (loc == -1) return;
        unsafe
        {
            fixed (Vector3* ptr = values)
                _gl.Uniform3(loc, (uint)values.Length, (float*)ptr);
        }
    }

    /// <summary>Writes a 4-component float uniform.</summary>
    public void SetUniform(string name, Vector4 value) => SetIfValid(name, loc => _gl.Uniform4(loc, value));

    /// <summary>
    /// Writes a 4×4 matrix uniform. It is uploaded with <c>transpose = false</c> on purpose: see the
    /// comment inside for why that keeps System.Numerics' row-major/row-vector convention consistent
    /// with GLSL's column-major expectation.
    /// </summary>
    public void SetUniform(string name, Matrix4x4 value)
    {
        // Keep transpose = false:
        // System.Numerics.Matrix4x4 is row-major with a row-vector convention (v' = v·M).
        // Passing it as false makes GLSL interpret row-major as column-major, giving M^T;
        // the column-vector transform M^T·p equals the row-vector transform p·M, so geometry and
        // normal matrices stay consistent.
        int loc = GetLocation(name);
        if (loc == -1) return;
        unsafe { _gl.UniformMatrix4(loc, 1, false, (float*)&value); }
    }

    private void SetIfValid(string name, Action<int> setter)
    {
        int loc = GetLocation(name);
        if (loc != -1) setter(loc);
    }

    private int GetLocation(string name)
    {
        if (_uniformLocations.TryGetValue(name, out int cached))
            return cached;

        int loc = _gl.GetUniformLocation(_handle, name);
        _uniformLocations[name] = loc;
        return loc;
    }

    /// <summary>Checks a shader for compile errors.</summary>
    private void CheckShaderError(uint shader, string stage)
    {
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status != (int)GLEnum.True)
            throw new Exception($"{stage} shader error: {_gl.GetShaderInfoLog(shader)}");
    }

    /// <summary>Checks the program for link errors.</summary>
    private void CheckProgramError()
    {
        _gl.GetProgram(_handle, ProgramPropertyARB.LinkStatus, out int status);
        if (status != (int)GLEnum.True)
            throw new Exception($"Program link error: {_gl.GetProgramInfoLog(_handle)}");
    }

    /// <summary>Deletes the GL program and clears the cached uniform locations; safe to call more than once.</summary>
    public void Dispose()
    {
        if (_disposed || _handle == 0) return;
        _gl.DeleteProgram(_handle);
        _uniformLocations.Clear();
        _disposed = true;
    }
}
