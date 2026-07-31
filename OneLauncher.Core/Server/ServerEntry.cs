using OneLauncher.Core.Helper.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace OneLauncher.Core.Server;

public class ServerEntry(Guid id, string instanceId, ServerInfo serverInfo, string name, string? description)
{
    public Guid Id { get; init; } = id;

    public string InstanceId { get; init; } = instanceId;

    public ServerInfo ServerInfo { get; init; } = serverInfo;

    public string Name { get; set; } = name;

    public string? Description { get; set; } = description;
}