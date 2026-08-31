using Godot;
using ImGuiNET;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace WorldMapStudio;

internal sealed class ImGuiRenderer : IDisposable
{
    private readonly RenderingDevice _renderingDevice;
    private readonly Color[] _clearColors = [new(0f, 0f, 0f, 0f)];
    private readonly Rid _shader;
    private readonly Rid _pipeline;
    private readonly Rid _sampler;
    private readonly long _vertexFormat;
    private readonly Dictionary<Rid, Rid> _framebuffers = [];
    private readonly Dictionary<IntPtr, Rid> _uniformSets = [];
    private readonly HashSet<IntPtr> _usedTextures = [];

    // Godot texture Rid -> its RD-backing texture Rid, from RenderingServer.TextureGetRdTexture. That
    // mapping is stable for as long as the texture is (font atlas, viewport image), so without this
    // cache every single ImGui draw command paid for a fresh native round-trip to recompute the exact
    // same answer it got last frame — one of two redundant per-draw-command native calls in this file
    // that dominated a session's baseline frame cost regardless of what the 3D scene held.
    private readonly Dictionary<IntPtr, Rid> _rdTextureCache = [];
    private readonly HashSet<IntPtr> _usedOriginalTextures = [];
    private readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Create();
    private readonly float[] _scale = new float[2];
    private readonly float[] _translate = new float[2];
    private readonly byte[] _pushConstantBuffer = new byte[16];
    private readonly Godot.Collections.Array<Rid> _sourceBuffers = [];
    private readonly long[] _vertexOffsets = new long[3];
    private readonly Godot.Collections.Array<RDUniform> _uniformArray = [];
    private readonly Rect2 _zeroRect = new(Vector2.Zero, Vector2.Zero);

    private Rid _indexBuffer;
    private int _indexBufferSize;
    private Rid _vertexBuffer;
    private int _vertexBufferSize;

    public ImGuiRenderer()
    {
        _renderingDevice = RenderingServer.GetRenderingDevice()
            ?? throw new InvalidOperationException("RenderingDevice is unavailable. Use a Forward+/Mobile renderer, not Compatibility.");

        RDShaderFile shaderFile = ResourceLoader.Load<RDShaderFile>("res://src/Utilities/ImGui/ImGuiShader.glsl");
        _shader = _renderingDevice.ShaderCreateFromSpirV(shaderFile.GetSpirV());
        if (!_shader.IsValid)
        {
            throw new InvalidOperationException("Failed to create ImGui shader.");
        }

        uint vertexStride = (uint)Marshal.SizeOf<ImDrawVert>();
        using RDVertexAttribute position = new()
        {
            Location = 0,
            Format = RenderingDevice.DataFormat.R32G32Sfloat,
            Stride = vertexStride,
            Offset = 0,
        };
        using RDVertexAttribute uv = new()
        {
            Location = 1,
            Format = RenderingDevice.DataFormat.R32G32Sfloat,
            Stride = vertexStride,
            Offset = sizeof(float) * 2,
        };
        using RDVertexAttribute color = new()
        {
            Location = 2,
            Format = RenderingDevice.DataFormat.R8G8B8A8Unorm,
            Stride = vertexStride,
            Offset = sizeof(float) * 4,
        };

        _vertexFormat = _renderingDevice.VertexFormatCreate([position, uv, color]);

        using RDPipelineColorBlendStateAttachment blendAttachment = new()
        {
            EnableBlend = true,
            SrcColorBlendFactor = RenderingDevice.BlendFactor.SrcAlpha,
            DstColorBlendFactor = RenderingDevice.BlendFactor.OneMinusSrcAlpha,
            ColorBlendOp = RenderingDevice.BlendOperation.Add,
            SrcAlphaBlendFactor = RenderingDevice.BlendFactor.One,
            DstAlphaBlendFactor = RenderingDevice.BlendFactor.OneMinusSrcAlpha,
            AlphaBlendOp = RenderingDevice.BlendOperation.Add,
        };
        using RDPipelineColorBlendState blendState = new()
        {
            BlendConstant = Colors.Transparent,
        };
        blendState.Attachments.Add(blendAttachment);

        using RDPipelineRasterizationState rasterizationState = new()
        {
            FrontFace = RenderingDevice.PolygonFrontFace.CounterClockwise,
        };
        using RDAttachmentFormat attachmentFormat = new()
        {
            Format = RenderingDevice.DataFormat.R8G8B8A8Unorm,
            Samples = RenderingDevice.TextureSamples.Samples1,
            UsageFlags = (uint)RenderingDevice.TextureUsageBits.ColorAttachmentBit,
        };

        long framebufferFormat = _renderingDevice.FramebufferFormatCreate([attachmentFormat]);
        _pipeline = _renderingDevice.RenderPipelineCreate(
            _shader,
            framebufferFormat,
            _vertexFormat,
            RenderingDevice.RenderPrimitive.Triangles,
            rasterizationState,
            new RDPipelineMultisampleState(),
            new RDPipelineDepthStencilState(),
            blendState);

        using RDSamplerState samplerState = new()
        {
            MinFilter = RenderingDevice.SamplerFilter.Linear,
            MagFilter = RenderingDevice.SamplerFilter.Linear,
            MipFilter = RenderingDevice.SamplerFilter.Linear,
            RepeatU = RenderingDevice.SamplerRepeatMode.Repeat,
            RepeatV = RenderingDevice.SamplerRepeatMode.Repeat,
            RepeatW = RenderingDevice.SamplerRepeatMode.Repeat,
        };
        _sampler = _renderingDevice.SamplerCreate(samplerState);

        _sourceBuffers.Resize(3);
        _uniformArray.Resize(1);
    }

    public void InitViewport(Rid viewportRid)
    {
        RenderingServer.ViewportSetClearMode(viewportRid, RenderingServer.ViewportClearMode.Never);
    }

    public void Render(Rid viewportRid, ImDrawDataPtr drawData)
    {
        if (drawData.CmdListsCount == 0 || drawData.DisplaySize.X <= 0 || drawData.DisplaySize.Y <= 0)
        {
            return;
        }

        ReplaceTextureRids(drawData);
        RenderDrawData(GetFramebuffer(viewportRid), drawData);
        FreeUnusedTextures();
    }

    public void Dispose()
    {
        foreach (Rid uniformSet in _uniformSets.Values)
        {
            if (_renderingDevice.UniformSetIsValid(uniformSet))
            {
                _renderingDevice.FreeRid(uniformSet);
            }
        }

        _uniformSets.Clear();

        foreach (Rid framebuffer in _framebuffers.Values)
        {
            if (_renderingDevice.FramebufferIsValid(framebuffer))
            {
                _renderingDevice.FreeRid(framebuffer);
            }
        }

        _framebuffers.Clear();

        if (_pipeline.IsValid)
        {
            _renderingDevice.FreeRid(_pipeline);
        }

        if (_sampler.IsValid)
        {
            _renderingDevice.FreeRid(_sampler);
        }

        if (_shader.IsValid)
        {
            _renderingDevice.FreeRid(_shader);
        }

        if (_indexBuffer.IsValid)
        {
            _renderingDevice.FreeRid(_indexBuffer);
        }

        if (_vertexBuffer.IsValid)
        {
            _renderingDevice.FreeRid(_vertexBuffer);
        }
    }

    private void RenderDrawData(Rid framebuffer, ImDrawDataPtr drawData)
    {
        if (!framebuffer.IsValid)
        {
            return;
        }

        int vertexSize = Marshal.SizeOf<ImDrawVert>();
        _scale[0] = 2.0f / drawData.DisplaySize.X;
        _scale[1] = 2.0f / drawData.DisplaySize.Y;
        _translate[0] = -1.0f - drawData.DisplayPos.X * _scale[0];
        _translate[1] = -1.0f - drawData.DisplayPos.Y * _scale[1];

        Buffer.BlockCopy(_scale, 0, _pushConstantBuffer, 0, 8);
        Buffer.BlockCopy(_translate, 0, _pushConstantBuffer, 8, 8);

        EnsureBuffers(drawData, vertexSize);
        SetupBuffers(drawData, vertexSize);

        long drawList = _renderingDevice.DrawListBegin(
            framebuffer,
            RenderingDevice.DrawFlags.ClearAll,
            _clearColors,
            1f,
            0,
            _zeroRect);

        _renderingDevice.DrawListBindRenderPipeline(drawList, _pipeline);
        _renderingDevice.DrawListSetPushConstant(drawList, _pushConstantBuffer, (uint)_pushConstantBuffer.Length);

        int globalIndexOffset = 0;
        int globalVertexOffset = 0;
        for (int listIndex = 0; listIndex < drawData.CmdListsCount; listIndex++)
        {
            ImDrawListPtr commandList = drawData.CmdListsRange[listIndex];

            // VtxOffset is the same for every command in a list unless ImGui had to split it (past
            // 64k vertices, rare for editor UI), so the vertex array it describes only needs rebuilding
            // when that offset actually changes — not once per draw command. Recreating it every
            // command was a second native RenderingDevice round-trip per command, on top of the index
            // array below, for no behavioural difference in the overwhelmingly common case.
            Rid vertexArray = default;
            long boundVertexOffset = -1;

            for (int commandIndex = 0; commandIndex < commandList.CmdBuffer.Size; commandIndex++)
            {
                ImDrawCmdPtr drawCommand = commandList.CmdBuffer[commandIndex];
                if (drawCommand.ElemCount == 0 || !_uniformSets.TryGetValue(drawCommand.TextureId, out Rid uniformSet))
                {
                    continue;
                }

                Rid indexArray = _renderingDevice.IndexArrayCreate(
                    _indexBuffer,
                    (uint)(drawCommand.IdxOffset + globalIndexOffset),
                    drawCommand.ElemCount);

                long vertexOffset = (drawCommand.VtxOffset + globalVertexOffset) * vertexSize;
                if (vertexOffset != boundVertexOffset)
                {
                    if (vertexArray.IsValid)
                    {
                        _renderingDevice.FreeRid(vertexArray);
                    }

                    _sourceBuffers[0] = _sourceBuffers[1] = _sourceBuffers[2] = _vertexBuffer;
                    _vertexOffsets[0] = _vertexOffsets[1] = _vertexOffsets[2] = vertexOffset;
                    vertexArray = _renderingDevice.VertexArrayCreate(
                        (uint)commandList.VtxBuffer.Size,
                        _vertexFormat,
                        _sourceBuffers,
                        _vertexOffsets);
                    boundVertexOffset = vertexOffset;
                }

                Rect2 clipRect = new(
                    drawCommand.ClipRect.X,
                    drawCommand.ClipRect.Y,
                    drawCommand.ClipRect.Z - drawCommand.ClipRect.X,
                    drawCommand.ClipRect.W - drawCommand.ClipRect.Y);
                clipRect.Position -= drawData.DisplayPos.ToGodotVector2();

                _renderingDevice.DrawListBindUniformSet(drawList, uniformSet, 0);
                _renderingDevice.DrawListBindIndexArray(drawList, indexArray);
                _renderingDevice.DrawListBindVertexArray(drawList, vertexArray);
                _renderingDevice.DrawListEnableScissor(drawList, clipRect);
                _renderingDevice.DrawListDraw(drawList, true, 1);

                _renderingDevice.FreeRid(indexArray);
            }

            if (vertexArray.IsValid)
            {
                _renderingDevice.FreeRid(vertexArray);
            }

            globalIndexOffset += commandList.IdxBuffer.Size;
            globalVertexOffset += commandList.VtxBuffer.Size;
        }

        _renderingDevice.DrawListEnd();
    }

    private void EnsureBuffers(ImDrawDataPtr drawData, int vertexSize)
    {
        if (_indexBufferSize < drawData.TotalIdxCount)
        {
            if (_indexBuffer.IsValid)
            {
                _renderingDevice.FreeRid(_indexBuffer);
            }

            _indexBuffer = _renderingDevice.IndexBufferCreate(
                (uint)drawData.TotalIdxCount,
                RenderingDevice.IndexBufferFormat.Uint16);
            _indexBufferSize = drawData.TotalIdxCount;
        }

        if (_vertexBufferSize < drawData.TotalVtxCount)
        {
            if (_vertexBuffer.IsValid)
            {
                _renderingDevice.FreeRid(_vertexBuffer);
            }

            _vertexBuffer = _renderingDevice.VertexBufferCreate((uint)(drawData.TotalVtxCount * vertexSize));
            _vertexBufferSize = drawData.TotalVtxCount;
        }
    }

    private void SetupBuffers(ImDrawDataPtr drawData, int vertexSize)
    {
        int indexBufferBytes = drawData.TotalIdxCount * sizeof(ushort);
        byte[] indexBuffer = _bufferPool.Rent(indexBufferBytes);
        int vertexBufferBytes = drawData.TotalVtxCount * vertexSize;
        byte[] vertexBuffer = _bufferPool.Rent(vertexBufferBytes);

        int globalIndexOffset = 0;
        int globalVertexOffset = 0;
        for (int listIndex = 0; listIndex < drawData.CmdListsCount; listIndex++)
        {
            ImDrawListPtr commandList = drawData.CmdListsRange[listIndex];
            int vertexBytes = commandList.VtxBuffer.Size * vertexSize;
            Marshal.Copy(commandList.VtxBuffer.Data, vertexBuffer, globalVertexOffset, vertexBytes);
            globalVertexOffset += vertexBytes;

            int indexBytes = commandList.IdxBuffer.Size * sizeof(ushort);
            Marshal.Copy(commandList.IdxBuffer.Data, indexBuffer, globalIndexOffset, indexBytes);
            globalIndexOffset += indexBytes;

            for (int commandIndex = 0; commandIndex < commandList.CmdBuffer.Size; commandIndex++)
            {
                ImDrawCmdPtr drawCommand = commandList.CmdBuffer[commandIndex];
                if (drawCommand.TextureId == IntPtr.Zero)
                {
                    continue;
                }

                Rid textureRid = ConstructRid((ulong)drawCommand.TextureId);
                if (!_renderingDevice.TextureIsValid(textureRid))
                {
                    continue;
                }

                _usedTextures.Add(drawCommand.TextureId);
                if (_uniformSets.TryGetValue(drawCommand.TextureId, out Rid existingUniformSet))
                {
                    if (_renderingDevice.UniformSetIsValid(existingUniformSet))
                    {
                        continue;
                    }

                    _uniformSets.Remove(drawCommand.TextureId);
                }

                using RDUniform uniform = new()
                {
                    Binding = 0,
                    UniformType = RenderingDevice.UniformType.SamplerWithTexture,
                };
                uniform.AddId(_sampler);
                uniform.AddId(textureRid);
                _uniformArray[0] = uniform;
                Rid uniformSet = _renderingDevice.UniformSetCreate(_uniformArray, _shader, 0);
                if (_renderingDevice.UniformSetIsValid(uniformSet))
                {
                    _uniformSets[drawCommand.TextureId] = uniformSet;
                }
            }
        }

        _renderingDevice.BufferUpdate(_indexBuffer, 0, (uint)indexBufferBytes, indexBuffer);
        _renderingDevice.BufferUpdate(_vertexBuffer, 0, (uint)vertexBufferBytes, vertexBuffer);
        _bufferPool.Return(indexBuffer);
        _bufferPool.Return(vertexBuffer);
    }

    private void FreeUnusedTextures()
    {
        List<IntPtr> staleTextures = [];
        foreach (IntPtr textureId in _uniformSets.Keys)
        {
            if (!_usedTextures.Contains(textureId))
            {
                staleTextures.Add(textureId);
            }
        }

        foreach (IntPtr textureId in staleTextures)
        {
            Rid uniformSet = _uniformSets[textureId];
            if (_renderingDevice.UniformSetIsValid(uniformSet))
            {
                _renderingDevice.FreeRid(uniformSet);
            }

            _uniformSets.Remove(textureId);
        }

        _usedTextures.Clear();
    }

    private Rid GetFramebuffer(Rid viewportRid)
    {
        if (_framebuffers.TryGetValue(viewportRid, out Rid framebuffer) && _renderingDevice.FramebufferIsValid(framebuffer))
        {
            return framebuffer;
        }

        Rid viewportTexture = RenderingServer.TextureGetRdTexture(RenderingServer.ViewportGetTexture(viewportRid));
        framebuffer = _renderingDevice.FramebufferCreate([viewportTexture]);
        _framebuffers[viewportRid] = framebuffer;
        return framebuffer;
    }

    private void ReplaceTextureRids(ImDrawDataPtr drawData)
    {
        _usedOriginalTextures.Clear();

        for (int listIndex = 0; listIndex < drawData.CmdListsCount; listIndex++)
        {
            ImDrawListPtr commandList = drawData.CmdListsRange[listIndex];
            for (int commandIndex = 0; commandIndex < commandList.CmdBuffer.Size; commandIndex++)
            {
                ImDrawCmdPtr drawCommand = commandList.CmdBuffer[commandIndex];
                if (drawCommand.TextureId == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr originalId = drawCommand.TextureId;
                _usedOriginalTextures.Add(originalId);

                if (!_rdTextureCache.TryGetValue(originalId, out Rid rdTexture) || !_renderingDevice.TextureIsValid(rdTexture))
                {
                    rdTexture = RenderingServer.TextureGetRdTexture(ConstructRid((ulong)originalId));
                    _rdTextureCache[originalId] = rdTexture;
                }

                drawCommand.TextureId = (IntPtr)rdTexture.Id;
            }
        }

        PruneStaleTextureMappings();
    }

    // Same shape as FreeUnusedTextures below: drop cache entries for textures no ImGui draw command
    // referenced this frame (e.g. a closed asset-picker thumbnail), so a long session's churn through
    // many distinct textures cannot grow this cache without bound.
    private void PruneStaleTextureMappings()
    {
        if (_rdTextureCache.Count <= _usedOriginalTextures.Count)
        {
            return;
        }

        List<IntPtr> stale = [];
        foreach (IntPtr id in _rdTextureCache.Keys)
        {
            if (!_usedOriginalTextures.Contains(id))
            {
                stale.Add(id);
            }
        }

        foreach (IntPtr id in stale)
        {
            _rdTextureCache.Remove(id);
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern Rid ConstructRid(ulong id);
}

internal static class ImGuiVectorExtensions
{
    public static Vector2 ToGodotVector2(this System.Numerics.Vector2 vector)
    {
        return new Vector2(vector.X, vector.Y);
    }
}
