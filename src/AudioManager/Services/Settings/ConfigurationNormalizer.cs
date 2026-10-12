using AudioManager.Models;

namespace AudioManager.Services.Settings;

/// <summary>
/// Repairs structurally valid but semantically broken configuration so the rest of the
/// application can rely on its invariants (unique channels, single owner per process, valid bindings).
/// </summary>
public static class ConfigurationNormalizer
{
    private const int MaxChannels = 6;

    public static bool Normalize(MixerConfiguration configuration)
    {
        var changed = false;

        configuration.MidiBindings ??= [];
        configuration.KeyboardBindings ??= [];
        changed |= NormalizeChannels(configuration);
        changed |= NormalizeAssignments(configuration);
        changed |= NormalizeMidiBindings(configuration);
        changed |= NormalizeKeyboardBindings(configuration);
        return changed;
    }

    private static bool NormalizeChannels(MixerConfiguration configuration)
    {
        var changed = configuration.Channels is null;
        var source = configuration.Channels ?? [];
        var defaults = MixerConfiguration.CreateDefaultChannels();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<AudioChannelConfig>();

        foreach (var channel in source)
        {
            if (channel is null || string.IsNullOrWhiteSpace(channel.Id) || !seen.Add(channel.Id))
            {
                changed = true;
                continue;
            }

            var template = defaults.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, channel.Id, StringComparison.OrdinalIgnoreCase));

            if (template is not null && channel.Role != template.Role)
            {
                channel.Role = template.Role;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(channel.Name))
            {
                channel.Name = template?.Name ?? "Channel";
                changed = true;
            }

            if (float.IsNaN(channel.Volume) || float.IsInfinity(channel.Volume))
            {
                channel.Volume = 1f;
                changed = true;
            }
            else if (channel.Volume is < 0f or > 1f)
            {
                channel.Volume = Math.Clamp(channel.Volume, 0f, 1f);
                changed = true;
            }

            channel.AssignedProcesses ??= [];
            result.Add(channel);
        }

        foreach (var template in defaults)
        {
            if (seen.Add(template.Id))
            {
                result.Add(template);
                changed = true;
            }
        }

        // The UI is built around the six well-known channels; keep those when trimming.
        if (result.Count > MaxChannels)
        {
            var keep = result
                .OrderBy(channel => defaults.Any(d => string.Equals(d.Id, channel.Id, StringComparison.OrdinalIgnoreCase)) ? 0 : 1)
                .Take(MaxChannels)
                .ToHashSet();
            result = result.Where(keep.Contains).ToList();
            changed = true;
        }

        configuration.Channels = result;
        return changed;
    }

    private static bool NormalizeAssignments(MixerConfiguration configuration)
    {
        var changed = false;
        var owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var channel in configuration.Channels)
        {
            var cleaned = new List<string>();
            foreach (var raw in channel.AssignedProcesses)
            {
                var processName = raw?.Trim();
                var valid = channel.Role == AudioChannelRole.VirtualOutput &&
                            !string.IsNullOrWhiteSpace(processName) &&
                            !processName.StartsWith("pid-", StringComparison.OrdinalIgnoreCase) &&
                            owners.Add(processName);

                if (valid)
                {
                    cleaned.Add(processName!);
                }

                if (!valid || !string.Equals(raw, processName, StringComparison.Ordinal))
                {
                    changed = true;
                }
            }

            channel.AssignedProcesses = cleaned;
        }

        return changed;
    }

    private static bool NormalizeMidiBindings(MixerConfiguration configuration)
    {
        var knownChannels = new HashSet<string>(configuration.Channels.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);
        var kept = new List<MidiBinding>();
        var seen = new HashSet<(MidiBindingKind, int, int)>();

        // Later bindings win, matching how SaveBindingAsync replaces older ones.
        foreach (var binding in Enumerable.Reverse(configuration.MidiBindings))
        {
            if (binding is null || !knownChannels.Contains(binding.ChannelId))
            {
                continue;
            }

            binding.MidiChannel = Math.Clamp(binding.MidiChannel, 1, 16);
            binding.ControllerOrNote = Math.Clamp(binding.ControllerOrNote, 0, 127);
            if (float.IsNaN(binding.Step) || binding.Step <= 0f)
            {
                binding.Step = 0.02f;
            }

            if (seen.Add((binding.Kind, binding.MidiChannel, binding.ControllerOrNote)))
            {
                kept.Add(binding);
            }
        }

        kept.Reverse();
        var changed = kept.Count != configuration.MidiBindings.Count;
        configuration.MidiBindings = kept;
        return changed;
    }

    private static bool NormalizeKeyboardBindings(MixerConfiguration configuration)
    {
        var knownChannels = new HashSet<string>(configuration.Channels.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);
        var kept = new List<KeyboardBinding>();
        var seen = new HashSet<int>();

        foreach (var binding in Enumerable.Reverse(configuration.KeyboardBindings))
        {
            if (binding is null || binding.VirtualKey <= 0 || !knownChannels.Contains(binding.ChannelId))
            {
                continue;
            }

            if (seen.Add(binding.VirtualKey))
            {
                kept.Add(binding);
            }
        }

        kept.Reverse();
        var changed = kept.Count != configuration.KeyboardBindings.Count;
        configuration.KeyboardBindings = kept;
        return changed;
    }
}
