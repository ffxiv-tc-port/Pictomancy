namespace Pictomancy.DXDraw;

// device + deferred context
internal class RenderContext : IDisposable
{
    public SharpDX.Direct3D11.Device Device { get; private set; }
    public SharpDX.Direct2D1.Device Device2 { get; private set; }

    public SharpDX.Direct3D11.DeviceContext Context { get; private set; }
    //public SharpDX.Direct2D1.DeviceContext Context2 { get; private set; }

    public unsafe RenderContext()
    {
        // 🔴 Device.Instance() 宣告為 [StaticAddress(..., isPointer: true)]，產生的程式碼回傳的是
        //    *ppInstance——只有「靜態槽位址」為 null 時才擲例外，槽裡的值本身可以合法為 null。
        //    直接 -> 解參考等於對位址 0 取值，會產生 AccessViolationException；那在 .NET Core 屬
        //    corrupted-state exception，try/catch 攔不到，直接把遊戲帶走。
        //    這裡改成 fail-closed：取不到裝置就擲可攔截的受控例外，由呼叫端停用繪製。
        var device = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Device.Instance();
        if (device == null)
            throw new InvalidOperationException("[Pictomancy] Graphics Device instance is null; cannot create render context.");
        if (device->D3D11Forwarder == null)
            throw new InvalidOperationException("[Pictomancy] Graphics Device has no D3D11Forwarder; cannot create render context.");

        Device = new((nint)device->D3D11Forwarder);
        Context = new(Device);

        try
        {
            /*
            SharpDX.DXGI.Device dxgiDev = Device.QueryInterfaceOrNull<SharpDX.DXGI.Device>();
            if (dxgiDev != null)
            {
                Device2 = new(dxgiDev);
            }
            */
            using (var dxgiDevice = Device.QueryInterface<SharpDX.DXGI.Device>())
            {
                Device2 = new(dxgiDevice);
            }
        }
        catch (Exception ex)
        {
            //PictoService.Log.Error(ex, "Failed to create D2D device");
        }

        //Context2 = new(Device2, SharpDX.Direct2D1.DeviceContextOptions.None);
    }

    public void Dispose()
    {
        Context.Dispose();
        //Context2.Dispose();
    }

    public void Execute()
    {
        using var cmds = Context.FinishCommandList(true);
        Device.ImmediateContext.ExecuteCommandList(cmds, true);
        Context.ClearState();
    }
}
