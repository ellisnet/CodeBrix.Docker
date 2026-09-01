using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CodeBrix.Docker.Tests;

[Collection(DockerTestCollection.Name)]
public sealed class VolumeTests(DockerTestFixture fixture)
{
    private DockerClient Client => fixture.Client;

    [Fact]
    public async Task VolumeMount_CarriesDataFromOneContainerToTheNext()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var volumeName = fixture.NewName("vol");
        string createdVolume = null;
        string writerId = null;
        string readerId = null;

        try
        {
            createdVolume = await Client.Volumes.CreateAsync(volumeName,
                DockerTestFixture.TestLabelFilter, cancellation.Token);
            Assert.Equal(volumeName, createdVolume);

            var writer = fixture.Spec("volwriter", "alpine:latest", "sh", "-c",
                "echo persisted-by-writer > /data/message.txt");
            writer.Mounts.Add(MountSpec.Volume(volumeName, "/data"));
            writerId = await Client.Containers.RunAsync(writer, cancellation.Token);
            Assert.Equal(0, await Client.Containers.WaitForExitAsync(writerId, cancellation.Token));

            var reader = fixture.Spec("volreader", "alpine:latest", "cat", "/data/message.txt");
            reader.Mounts.Add(MountSpec.Volume(volumeName, "/data", readOnly: true));
            readerId = await Client.Containers.RunAsync(reader, cancellation.Token);
            Assert.Equal(0, await Client.Containers.WaitForExitAsync(readerId, cancellation.Token));

            var logs = await Client.Containers.GetLogsAsync(readerId, cancellationToken: cancellation.Token);
            Assert.Contains("persisted-by-writer", logs.Stdout, StringComparison.Ordinal);

            var mounts = (await Client.Containers.InspectAsync(readerId, cancellation.Token)).Mounts;
            Assert.NotNull(mounts);
            Assert.Contains(mounts, mount => mount.Name == volumeName);

            var inspect = await Client.Volumes.InspectAsync(volumeName, cancellation.Token);
            Assert.Equal(volumeName, inspect.Name);
            Assert.Equal("local", inspect.Driver);
            Assert.Equal(DockerTestFixture.LabelValue, inspect.Labels?[DockerTestFixture.LabelName]);
            Assert.False(string.IsNullOrWhiteSpace(inspect.Mountpoint));

            var listed = await Client.Volumes.ListAsync(DockerTestFixture.TestLabelFilter, cancellation.Token);
            Assert.Contains(listed, volume => volume.Name == volumeName);
            Assert.All(listed, volume =>
                Assert.Equal(DockerTestFixture.LabelValue, volume.Labels?[DockerTestFixture.LabelName]));

            await Client.Containers.RemoveAsync(readerId, force: true, removeVolumes: false,
                cancellation.Token);
            readerId = null;
            await Client.Containers.RemoveAsync(writerId, force: true, removeVolumes: false,
                cancellation.Token);
            writerId = null;

            await Client.Volumes.RemoveAsync(volumeName, force: false, cancellation.Token);
            createdVolume = null;

            var remaining = await Client.Volumes.ListAsync(DockerTestFixture.TestLabelFilter,
                cancellation.Token);
            Assert.DoesNotContain(remaining, volume => volume.Name == volumeName);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(readerId);
            await fixture.RemoveContainerQuietlyAsync(writerId);
            await fixture.RemoveVolumeQuietlyAsync(createdVolume);
        }
    }

    [Fact]
    public async Task TmpfsMount_IsWritableAndStartsEmptyForEveryContainer()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        string firstId = null;
        string secondId = null;

        try
        {
            var first = fixture.Spec("tmpfswriter", "alpine:latest", "sh", "-c",
                "echo scratch > /scratch/file.txt; ls /scratch; grep ' /scratch ' /proc/mounts");
            first.Mounts.Add(MountSpec.Tmpfs("/scratch", ResourceLimits.Megabytes(16)));
            firstId = await Client.Containers.RunAsync(first, cancellation.Token);
            Assert.Equal(0, await Client.Containers.WaitForExitAsync(firstId, cancellation.Token));

            var firstLogs = await Client.Containers.GetLogsAsync(firstId, cancellationToken: cancellation.Token);
            Assert.Contains("file.txt", firstLogs.Stdout, StringComparison.Ordinal);
            Assert.Contains("tmpfs", firstLogs.Stdout, StringComparison.Ordinal);

            // A tmpfs lives and dies with its container: a fresh one sees an empty directory.
            var second = fixture.Spec("tmpfsreader", "alpine:latest", "sh", "-c",
                "ls -A /scratch | wc -l");
            second.Mounts.Add(MountSpec.Tmpfs("/scratch", ResourceLimits.Megabytes(16)));
            secondId = await Client.Containers.RunAsync(second, cancellation.Token);
            Assert.Equal(0, await Client.Containers.WaitForExitAsync(secondId, cancellation.Token));

            var secondLogs = await Client.Containers.GetLogsAsync(secondId,
                cancellationToken: cancellation.Token);
            Assert.Equal("0", secondLogs.Stdout.Trim());
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(firstId);
            await fixture.RemoveContainerQuietlyAsync(secondId);
        }
    }

    [Fact]
    public async Task CreateAsync_WithoutAName_CreatesAnAnonymousVolume()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        string volumeName = null;

        try
        {
            volumeName = await Client.Volumes.CreateAsync(name: null,
                DockerTestFixture.TestLabelFilter, cancellation.Token);

            Assert.False(string.IsNullOrWhiteSpace(volumeName));

            var inspect = await Client.Volumes.InspectAsync(volumeName, cancellation.Token);
            Assert.Equal(volumeName, inspect.Name);
            Assert.Equal(DockerTestFixture.LabelValue, inspect.Labels?[DockerTestFixture.LabelName]);
        }
        finally
        {
            await fixture.RemoveVolumeQuietlyAsync(volumeName);
        }
    }
}
