using System.Buffers.Binary;
using Xunit;

namespace OpenClicky.Input.Tests;

public class RawInputClassifierTests
{
    [Theory]
    [InlineData(PhysicalUserActivityCategory.Mouse, 0, false)]
    [InlineData(PhysicalUserActivityCategory.Mouse, 1, true)]
    [InlineData(PhysicalUserActivityCategory.Mouse, 1024, true)]
    [InlineData(PhysicalUserActivityCategory.Keyboard, 0, true)]
    [InlineData(PhysicalUserActivityCategory.Keyboard, 1, false)]
    public void ScreenChangeClassificationIgnoresPointerMotionAndKeyRelease(PhysicalUserActivityCategory category, ushort flags, bool expected)
    {
        var offset = 8 + IntPtr.Size * 2;
        var input = new byte[offset + 32];
        BinaryPrimitives.WriteUInt16LittleEndian(input.AsSpan(offset + (category == PhysicalUserActivityCategory.Mouse ? 4 : 2)), flags);
        Assert.Equal(expected, RawInputClassifier.MayChangeScreen(input, category));
        Assert.False(RawInputClassifier.MayChangeScreen(input.AsSpan(0, offset), category));
    }

    [Theory]
    [InlineData(0, PhysicalUserActivityCategory.Mouse)]
    [InlineData(1, PhysicalUserActivityCategory.Keyboard)]
    public void HardwareDeviceHeader_IsClassified(uint type, PhysicalUserActivityCategory expected)
    {
        var header = Header(type, device: 42);

        Assert.True(RawInputClassifier.TryClassifyHeader(header, out var category));
        Assert.Equal(expected, category);
    }

    [Fact]
    public void ZeroDeviceHandle_IsIgnoredAsSyntheticInput() =>
        Assert.False(RawInputClassifier.TryClassifyHeader(Header(1, device: 0), out _));

    [Theory]
    [InlineData(2)]
    [InlineData(99)]
    public void OtherRawInputTypes_AreIgnored(uint type) =>
        Assert.False(RawInputClassifier.TryClassifyHeader(Header(type, device: 42), out _));

    [Fact]
    public void TruncatedOrInvalidDeclaredHeader_IsRejected()
    {
        Assert.False(RawInputClassifier.TryClassifyHeader(new byte[7], out _));
        var header = Header(0, device: 42);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4, 4), 8);
        Assert.False(RawInputClassifier.TryClassifyHeader(header, out _));
    }

    private static byte[] Header(uint type, long device)
    {
        var bytes = new byte[8 + IntPtr.Size * 2];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), type);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), (uint)bytes.Length);
        if (IntPtr.Size == 8) BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(8, 8), device);
        else BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8, 4), (int)device);
        return bytes;
    }
}
