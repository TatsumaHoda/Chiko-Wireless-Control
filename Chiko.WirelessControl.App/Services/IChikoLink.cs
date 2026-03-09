using System;
using System.IO;

namespace Chiko.WirelessControl.App.Services;

public interface IChikoLink : IAsyncDisposable
{
    Stream Stream { get; }
}
