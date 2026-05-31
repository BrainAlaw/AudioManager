using AudioManager.Models;

namespace AudioManager.ViewModels;

public sealed record EndpointOptionViewModel(string Id, string Name, AudioEndpointKind Kind);
