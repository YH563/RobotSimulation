using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace RobotSimulation.OpenGL.Resources;

public class ShaderProgram : IDisposable
{
    private readonly GL _gl;
    private readonly uint _handle;
    private readonly Dictionary<string, int> _uniformLocations = new();
    private bool _disposed = false;

    public ShaderProgram(GL gl, string vertexSrc, string fragmentSrc)
    {
        _gl = gl;

        // 编译顶点着色器
        uint vertex = _gl.CreateShader(ShaderType.VertexShader);
        _gl.ShaderSource(vertex, vertexSrc);
        _gl.CompileShader(vertex);
        CheckShaderError(vertex, "VERTEX");

        // 编译片段着色器
        uint fragment = _gl.CreateShader(ShaderType.FragmentShader);
        _gl.ShaderSource(fragment, fragmentSrc);
        _gl.CompileShader(fragment);
        CheckShaderError(fragment, "FRAGMENT");

        // 链接程序
        _handle = _gl.CreateProgram();
        _gl.AttachShader(_handle, vertex);
        _gl.AttachShader(_handle, fragment);
        _gl.LinkProgram(_handle);
        CheckProgramError();

        // 清除中间着色器对象
        _gl.DetachShader(_handle, vertex);
        _gl.DetachShader(_handle, fragment);
        _gl.DeleteShader(vertex);
        _gl.DeleteShader(fragment);
    }

    public void Use() => _gl.UseProgram(_handle);

    // ---- Uniform 写入：location 只查询一次并缓存，之后的 set 调用不再重复 GetUniformLocation ----

    public void SetUniform(string name, int value) => SetIfValid(name, loc => _gl.Uniform1(loc, value));

    public void SetUniform(string name, float value) => SetIfValid(name, loc => _gl.Uniform1(loc, value));

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

    public void SetUniform(string name, Vector2 value) => SetIfValid(name, loc => _gl.Uniform2(loc, value));

    public void SetUniform(string name, Vector3 value) => SetIfValid(name, loc => _gl.Uniform3(loc, value));

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

    public void SetUniform(string name, Vector4 value) => SetIfValid(name, loc => _gl.Uniform4(loc, value));

    public void SetUniform(string name, Matrix4x4 value)
    {
        // 保持 transpose = false：
        // System.Numerics.Matrix4x4 采用行主序存储 + 行向量约定（v' = v·M）。
        // 以 false 直接传入时，GLSL 将行主序解释为列主序，得到 M^T；
        // 列向量变换 M^T·p 与行向量变换 p·M 结果一致，几何与法线矩阵自洽。
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

    /// <summary>检查着色器编译错误。</summary>
    private void CheckShaderError(uint shader, string stage)
    {
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status != (int)GLEnum.True)
            throw new Exception($"{stage} shader error: {_gl.GetShaderInfoLog(shader)}");
    }

    /// <summary>检查程序链接错误。</summary>
    private void CheckProgramError()
    {
        _gl.GetProgram(_handle, ProgramPropertyARB.LinkStatus, out int status);
        if (status != (int)GLEnum.True)
            throw new Exception($"Program link error: {_gl.GetProgramInfoLog(_handle)}");
    }

    public void Dispose()
    {
        if (_disposed || _handle == 0) return;
        _gl.DeleteProgram(_handle);
        _uniformLocations.Clear();
        _disposed = true;
    }
}
