namespace Chiko.WirelessControl.Core.Models;

public sealed record ConnectResult(
    string ModelName,
    string SerialNumber,
    string ProgramVersion);
