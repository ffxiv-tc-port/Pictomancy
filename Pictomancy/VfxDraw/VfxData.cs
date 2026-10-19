using System.Runtime.InteropServices;

namespace SourOmen.Structs;

// 不透明型別：實例指標的偏移改由 VfxFunctions 從遊戲碼推導（寫死的欄位刻意拿掉，避免繞過推導）。
[StructLayout(LayoutKind.Explicit, Size = 0x1E0)]
public struct VfxData
{
}
