using System.Reflection;
using System.Runtime.InteropServices;

namespace NinOnlineAutoAssist.Tests;

public sealed class NativeInputLayoutTests
{
    [Fact]
    public void SendInputStructureKeepsNativeUnionSize()
    {
        var controller = Assembly.Load("NinOnlineAutoAssist.App")
            .GetType("NinOnlineAutoAssist.App.LiveInputController", throwOnError: true)!;
        var input = controller.GetNestedType("Input", BindingFlags.NonPublic)!;
        var union = controller.GetNestedType("InputUnion", BindingFlags.NonPublic)!;
        Assert.Equal(IntPtr.Size == 8 ? 32 : 24, Marshal.SizeOf(union));
        Assert.Equal(IntPtr.Size == 8 ? 40 : 28, Marshal.SizeOf(input));
    }
}
