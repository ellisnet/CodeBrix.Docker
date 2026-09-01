using System;
using System.Collections.Generic;
using CodeBrix.Docker;
using RedisSetupTool.DockerManagement.Models;

namespace RedisSetupTool.DockerManagement.Mapping;

/// <summary>Turns the containerised analysis tools' results into this library's DTOs.</summary>
internal static class AnalysisMapper
{
    internal static TrivyScanOptions ToScanOptions(ImageScanOptions options)
    {
        if (options is null)
        {
            return null;
        }

        var mapped = new TrivyScanOptions { IgnoreUnfixed = options.IgnoreUnfixed };
        foreach (var severity in options.Severities)
        {
            mapped.Severities.Add(severity);
        }

        if (options.TimeoutSeconds.HasValue)
        {
            mapped.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds.Value);
        }

        return mapped;
    }

    internal static ImageScanReport ToReport(TrivyScanResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var findings = new List<VulnerabilityInfo>();
        foreach (var vulnerability in result.Vulnerabilities ?? [])
        {
            findings.Add(new VulnerabilityInfo
            {
                Id = vulnerability.Id,
                PackageName = vulnerability.PkgName,
                InstalledVersion = vulnerability.InstalledVersion,
                FixedVersion = vulnerability.FixedVersion,
                Severity = vulnerability.Severity,
                Title = vulnerability.Title,
                Target = vulnerability.Target,
                HasFix = vulnerability.HasFix,
            });
        }

        return new ImageScanReport
        {
            ImageReference = result.ImageReference,
            Total = result.Total,
            CountBySeverity = result.CountBySeverity
                ?? new Dictionary<string, int>(StringComparer.Ordinal),
            Vulnerabilities = findings,
        };
    }

    internal static ImageEfficiencyReport ToReport(DiveAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var layers = new List<EfficiencyLayerInfo>();
        foreach (var layer in result.Layers ?? [])
        {
            layers.Add(new EfficiencyLayerInfo
            {
                Index = layer.Index,
                SizeBytes = layer.SizeBytes,
                Command = layer.Command,
                Digest = layer.Digest,
            });
        }

        return new ImageEfficiencyReport
        {
            ImageReference = result.ImageReference,
            EfficiencyScore = result.EfficiencyScore,
            WastedBytes = result.WastedBytes,
            WastedPercent = result.WastedPercent,
            TotalSizeBytes = result.TotalSizeBytes,
            Layers = layers,
        };
    }

    internal static DockerfileLintReport ToReport(HadolintResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var findings = new List<LintFindingInfo>();
        foreach (var finding in result.Findings ?? [])
        {
            findings.Add(new LintFindingInfo
            {
                Code = finding.Code,
                Level = finding.Level,
                Line = finding.Line,
                Column = finding.Column,
                Message = finding.Message,
            });
        }

        return new DockerfileLintReport
        {
            DockerfilePath = result.DockerfilePath,
            Total = result.Total,
            CountByLevel = result.CountByLevel ?? new Dictionary<string, int>(StringComparer.Ordinal),
            Findings = findings,
        };
    }

    internal static SlimOptions ToSlimOptions(ImageOptimizeOptions options)
    {
        if (options is null)
        {
            return null;
        }

        var mapped = new SlimOptions
        {
            OutputTag = options.OutputTag,
            ContinueAfterSeconds = options.ContinueAfterSeconds,
            Timeout = TimeSpan.FromMinutes(options.TimeoutMinutes),
        };

        foreach (var path in options.HttpProbePaths)
        {
            mapped.HttpProbePaths.Add(path);
        }

        return mapped;
    }

    internal static ImageOptimizeReport ToReport(SlimResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new ImageOptimizeReport
        {
            OriginalImage = result.OriginalImage,
            OptimizedImage = result.OptimizedImage,
            Succeeded = result.Succeeded,
            OriginalSizeBytes = result.OriginalSizeBytes,
            OptimizedSizeBytes = result.OptimizedSizeBytes,
            SizeReduction = result.SizeReduction,
            Output = result.Output ?? string.Empty,
        };
    }
}
