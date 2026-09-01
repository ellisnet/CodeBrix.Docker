using System;
using System.Collections.Generic;
using CodeBrix.Docker;
using RedisSetupTool.DockerManagement.Models;

namespace RedisSetupTool.DockerManagement.Mapping;

/// <summary>Turns CodeBrix.Docker image types into this library's DTOs.</summary>
internal static class ImageMapper
{
    internal static ImageInfo ToInfo(ImageSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return new ImageInfo
        {
            Id = summary.Id,
            ShortId = summary.ShortId,
            DisplayName = summary.DisplayName,
            RepoTags = summary.RepoTags ?? [],
            RepoDigests = summary.RepoDigests ?? [],
            Created = summary.Created,
            SizeBytes = summary.Size,
            SharedSizeBytes = summary.SharedSize,
            Labels = summary.Labels ?? new Dictionary<string, string>(StringComparer.Ordinal),
            ContainerCount = summary.Containers,
            IsDangling = summary.IsDangling,
        };
    }

    internal static ImageDetail ToDetail(ImageInspectResult inspect)
    {
        ArgumentNullException.ThrowIfNull(inspect);

        var config = inspect.Config;

        return new ImageDetail
        {
            Id = inspect.Id,
            ShortId = inspect.ShortId,
            DisplayName = inspect.DisplayName,
            RepoTags = inspect.RepoTags ?? [],
            RepoDigests = inspect.RepoDigests ?? [],
            Parent = inspect.Parent,
            Comment = inspect.Comment,
            Created = inspect.Created,
            Author = inspect.Author,
            Architecture = inspect.Architecture,
            Os = inspect.Os,
            SizeBytes = inspect.Size,
            LayerCount = inspect.LayerCount,
            Layers = inspect.RootFs?.Layers ?? [],
            Env = config?.Env ?? [],
            Cmd = config?.Cmd ?? [],
            Entrypoint = config?.Entrypoint ?? [],
            WorkingDir = config?.WorkingDir,
            User = config?.User,
            Labels = config?.Labels ?? new Dictionary<string, string>(StringComparer.Ordinal),
        };
    }

    internal static ImageLayerInfo ToLayer(ImageHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new ImageLayerInfo
        {
            Id = entry.Id,
            Created = entry.Created,
            CreatedBy = entry.CreatedBy,
            SizeBytes = entry.Size,
            Comment = entry.Comment,
            Tags = entry.Tags ?? [],
            IsEmptyLayer = entry.IsEmptyLayer,
        };
    }

    internal static ImageBuildSpec ToBuildSpec(ImageBuildRequest request, IProgress<string> output)
    {
        ArgumentNullException.ThrowIfNull(request);

        var spec = new ImageBuildSpec
        {
            ContextDirectory = request.ContextDirectory,
            DockerfilePath = request.DockerfilePath,
            Target = request.Target,
            Pull = request.Pull,
            NoCache = request.NoCache,
            Output = output,
        };

        foreach (var tag in request.Tags)
        {
            spec.Tags.Add(tag);
        }

        foreach (var argument in request.BuildArgs)
        {
            spec.BuildArgs[argument.Key] = argument.Value;
        }

        foreach (var label in request.Labels)
        {
            spec.Labels[label.Key] = label.Value;
        }

        return spec;
    }

    internal static ImageBuildOutcome ToBuildOutcome(ImageBuildResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new ImageBuildOutcome
        {
            ImageId = result.ImageId,
            ShortImageId = ContainerMapper.Shorten(result.ImageId),
            Tags = result.Tags ?? [],
            Output = result.Output ?? string.Empty,
        };
    }
}
