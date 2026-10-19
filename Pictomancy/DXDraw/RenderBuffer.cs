using SharpDX;
using SharpDX.Direct3D11;
using System.Runtime.CompilerServices;
using Buffer = SharpDX.Direct3D11.Buffer;

namespace Pictomancy.DXDraw;

// base class for large buffers
// there are two flavours with slightly different performance characteristics and update methods:
// - 'dynamic' - suitable for buffers that are updated frequenty - once per frame or more often
//   these are created with Dynamic usage + cpu Write access and mapped with WriteDiscard flag
// - 'static' - suitable for buffers that are updated infrequenty
//   these are created with Default usage + no cpu access, builder creates temporary Staging buffer, maps it, then copies over to main resource
internal class RenderBuffer<T> : IDisposable where T : unmanaged
{
    public class Builder : IDisposable
    {
        private RenderContext _ctx = null!;
        private readonly RenderBuffer<T> _buffer;
        // Map 出來的寫入起點，Open 到 Dispose 之間有效；直接寫指標，不再每次 Map 包一個 DataStream。
        private nint _data;
        private Buffer? _staging; // only for non-dynamic

        public int CurElements => _buffer.CurElements;

        internal Builder(RenderBuffer<T> buffer)
        {
            _buffer = buffer;
        }

        internal void Open(RenderContext ctx)
        {
            _ctx = ctx;
            var buffer = _buffer;
            buffer.CurElements = 0;
            if (buffer.Dynamic)
            {
                _data = ctx.Context.MapSubresource(buffer.Buffer, 0, MapMode.WriteDiscard, MapFlags.None).DataPointer;
            }
            else
            {
                _staging = new(ctx.Device, new()
                {
                    SizeInBytes = buffer.ElementSize * buffer.MaxElements,
                    Usage = ResourceUsage.Staging,
                    CpuAccessFlags = CpuAccessFlags.Write,
                });
                _data = ctx.Context.MapSubresource(_staging, 0, MapMode.Write, MapFlags.None).DataPointer;
            }
        }

        public void Dispose()
        {
            _data = 0;
            if (_buffer.Dynamic)
            {
                _ctx.Context.UnmapSubresource(_buffer.Buffer, 0);
            }
            else
            {
                _ctx.Context.UnmapSubresource(_staging!, 0);
                _ctx.Context.CopyResource(_staging!, _buffer.Buffer);
                _staging!.Dispose();
                _staging = null;
            }
        }

        public unsafe void Add(ref T item)
        {
            if (_buffer.CurElements >= _buffer.MaxElements)
                throw new IndexOutOfRangeException(_buffer.FriendlyName + " max buffer size of " + _buffer.MaxElements + " exceeded. Futher elements will not be displayed.");
            int index = _buffer.CurElements++;
            Unsafe.CopyBlockUnaligned((byte*)_data + (nint)index * sizeof(T), Unsafe.AsPointer(ref item), (uint)sizeof(T));
        }
        public void Add(T item) => Add(ref item);
    }

    public string FriendlyName { get; private set; }
    public bool Dynamic { get; init; }
    public int ElementSize { get; init; }
    public int MaxElements { get; init; }
    public int CurElements { get; private set; }
    public Buffer Buffer { get; init; }

    public unsafe RenderBuffer(string friendlyName, RenderContext ctx, int maxElements, BindFlags bindFlags, bool dynamic)
    {
        FriendlyName = friendlyName;
        dynamic = true; // TODO: figure why it doesn't work as expected..
        Dynamic = dynamic;
        ElementSize = sizeof(T);
        MaxElements = maxElements;
        Buffer = new(ctx.Device, new()
        {
            SizeInBytes = ElementSize * maxElements,
            Usage = dynamic ? ResourceUsage.Dynamic : ResourceUsage.Default,
            BindFlags = bindFlags,
            CpuAccessFlags = dynamic ? CpuAccessFlags.Write : CpuAccessFlags.None,
        });
    }

    public void Dispose()
    {
        Buffer.Dispose();
    }

    private Builder? _builder;

    // 同一個緩衝區同時只會有一個 Builder 開著（DXRenderer 每幀 Map 一次、EndFrame 才 Dispose），所以沿用同一個物件。
    public Builder Map(RenderContext ctx)
    {
        var builder = _builder ??= new Builder(this);
        builder.Open(ctx);
        return builder;
    }
}
