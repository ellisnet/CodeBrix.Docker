using SilverAssertions;
using Xunit;

namespace CodeBrix.Docker.Tests;

/// <summary>
/// Tests for the parts of <see cref="AnalysisOperations"/> that can be checked without starting a tool
/// container: the default tool image references, the argument list handed to Slim, and image-reference
/// splitting.
/// </summary>
/// <remarks>
/// These need no daemon and run in milliseconds, which matters because the tool tiers they guard are
/// either slow (Trivy downloads a vulnerability database) or opt-in (Slim runs only when
/// <c>CODEBRIX_DOCKER_TEST_SLIM=1</c> is set). Without them a wrong default image reference would only
/// be noticed by a test almost nobody runs.
/// </remarks>
public sealed class AnalysisOperationsTests
{
    /// <summary>Creates the operation group. Building a client does not open a connection.</summary>
    private static AnalysisOperations CreateAnalysis()
    {
        var client = DockerClient.Create();
        return client.Analysis;
    }

    // ---------------------------------------------------------------------------------------
    // Default tool images
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void SlimImage_DefaultsToTheMaintainedMintImage()
    {
        //Arrange
        // dslim/slim stopped receiving builds at 1.40.11 (February 2024). That build negotiates Docker
        // Engine API 1.24, which Docker 25 and later refuse outright, so the retired image cannot
        // optimize anything on a current daemon. mintoolkit/mint is the renamed continuation and takes
        // the identical command line.
        var analysis = CreateAnalysis();

        //Act
        var image = analysis.SlimImage;

        //Assert
        image.Should().Be("mintoolkit/mint:latest");
        image.Should().NotContain("dslim");
    }

    [Fact]
    public void TrivyImage_DefaultsToTheOfficialAquaSecurityImage()
    {
        //Arrange
        var analysis = CreateAnalysis();

        //Act
        var image = analysis.TrivyImage;

        //Assert
        image.Should().Be("aquasec/trivy:latest");
    }

    [Fact]
    public void DiveImage_DefaultsToTheUpstreamDiveImage()
    {
        //Arrange
        var analysis = CreateAnalysis();

        //Act
        var image = analysis.DiveImage;

        //Assert
        image.Should().Be("wagoodman/dive:latest");
    }

    [Fact]
    public void HadolintImage_DefaultsToTheUpstreamHadolintImage()
    {
        //Arrange
        var analysis = CreateAnalysis();

        //Act
        var image = analysis.HadolintImage;

        //Assert
        image.Should().Be("hadolint/hadolint:latest");
    }

    // ---------------------------------------------------------------------------------------
    // Slim argument shape
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void BuildSlimCommand_WithoutHttpProbePaths_DisablesProbingEntirely()
    {
        //Arrange
        var options = new SlimOptions { ContinueAfterSeconds = 10 };

        //Act
        var command = AnalysisOperations.BuildSlimCommand("nginx:alpine", "optimized:latest", options);

        //Assert
        command.Should().Equal("build", "--target", "nginx:alpine", "--tag", "optimized:latest",
            "--continue-after=10", "--http-probe=false");
    }

    [Fact]
    public void BuildSlimCommand_WithHttpProbePaths_PassesOneProbeArgumentPerPath()
    {
        //Arrange
        var options = new SlimOptions { ContinueAfterSeconds = 5 };
        options.HttpProbePaths.Add("/");
        options.HttpProbePaths.Add("/health");

        //Act
        var command = AnalysisOperations.BuildSlimCommand("nginx:alpine", "optimized:latest", options);

        //Assert
        command.Should().Equal("build", "--target", "nginx:alpine", "--tag", "optimized:latest",
            "--continue-after=5", "--http-probe-cmd", "/", "--http-probe-cmd", "/health");
        command.Should().NotContain("--http-probe=false");
    }

    // ---------------------------------------------------------------------------------------
    // Image reference splitting
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("alpine:3.19", "alpine", "3.19")]
    [InlineData("alpine", "alpine", "latest")]
    [InlineData("aquasec/trivy:latest", "aquasec/trivy", "latest")]
    [InlineData("mintoolkit/mint", "mintoolkit/mint", "latest")]
    [InlineData("registry:5000/team/app:2.1", "registry:5000/team/app", "2.1")]
    public void SplitReference_SeparatesTheNameFromTheTag(string reference, string expectedName,
        string expectedTag)
    {
        //Arrange
        // POST /images/create wants the name and the tag as separate query parameters.

        //Act
        var (name, tag) = AnalysisOperations.SplitReference(reference);

        //Assert
        name.Should().Be(expectedName);
        tag.Should().Be(expectedTag);
    }

    [Fact]
    public void SplitReference_ForAPortQualifiedRegistryWithoutATag_DoesNotMistakeThePortForATag()
    {
        //Arrange
        // The colon belongs to the registry port, not to a tag, because a slash follows it.
        const string reference = "registry:5000/team/app";

        //Act
        var (name, tag) = AnalysisOperations.SplitReference(reference);

        //Assert
        name.Should().Be("registry:5000/team/app");
        tag.Should().Be("latest");
    }

    [Fact]
    public void SplitReference_ForADigestPinnedReference_ReturnsTheDigest()
    {
        //Arrange
        const string digest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        //Act
        var (name, tag) = AnalysisOperations.SplitReference($"alpine@{digest}");

        //Assert
        name.Should().Be("alpine");
        tag.Should().Be(digest);
    }
}
