using EMF.Orchestration.Models;
using EMF.Security.Encryption.Envelope;
namespace EMF.Tests;

public sealed class ZipParentAllocationProfileTests
{
    [Fact]
    public void ExactFirstSliceArrayProfileAndMajorBufferCounts()
    {
        var profile = new ZipParentAllocationProfile();
        Assert.Equal(33554432, profile.MaximumPlaintextBytes);
        Assert.Equal(33554432, profile.MaximumDecodedCiphertextBytes);
        Assert.Equal(44739244, profile.MaximumEncodedCiphertextBytes);
        Assert.Equal(44767425, profile.MaximumProtectedSourceBytes);
        Assert.Equal(44767425, profile.MaximumRetainedEnvelopeBytes);
        Assert.Equal(44767425, profile.MaximumPhysicalPreparationBytes);
        Assert.Equal(111876289, profile.ReadMajorBufferBytes);
        Assert.Equal(123089282, profile.PreparationAfterCiphertextReleaseMajorBufferBytes);
        Assert.Equal(156643714, profile.ConservativeFourMajorBufferBytes);
        var request = profile.CreateReadRequest();
        Assert.Equal(44767425, request.MaximumStoredRepresentationBytes);
        Assert.Equal(33554432, request.MaximumReturnedContentBytes);
    }
    [Theory]
    [InlineData(1)] [InlineData(1024)] [InlineData(33554431)]
    public void DownwardConfigurationDerivesEveryRepresentation(long plain)
    {
        var profile = new ZipParentAllocationProfile(plain);
        Assert.Equal(plain, profile.MaximumPlaintextBytes);
        Assert.Equal(BoundedEncryptedEnvelopeCodec.MaximumProtectedBytes(plain), profile.MaximumProtectedSourceBytes);
        profile.ValidateProviderMaximum(profile.MaximumProtectedSourceBytes);
        Assert.Throws<InvalidOperationException>(() => profile.ValidateProviderMaximum(profile.MaximumProtectedSourceBytes - 1));
    }
    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(33554433)] [InlineData(long.MaxValue)]
    public void RejectsUpwardOrInvalidConfiguration(long plain) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ZipParentAllocationProfile(plain));
    [Fact]
    public void IncompatibleProviderCannotSilentlyReduceProfile()
    {
        var profile = new ZipParentAllocationProfile();
        Assert.Throws<InvalidOperationException>(() => profile.ValidateProviderMaximum(44767424));
        Assert.Equal(33554432, profile.MaximumPlaintextBytes);
        Assert.Throws<OverflowException>(() => BoundedEncryptedEnvelopeCodec.Base64Length(long.MaxValue));
    }
}
