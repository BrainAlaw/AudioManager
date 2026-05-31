using VirtualMixer.Models;

namespace VirtualMixer.ViewModels;

public sealed record EndpointOptionViewModel(string Id, string Name, AudioEndpointKind Kind);
