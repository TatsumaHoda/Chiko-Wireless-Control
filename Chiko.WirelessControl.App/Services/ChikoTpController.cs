namespace Chiko.WirelessControl.App.Services;

public interface IChikoTpController
{
    Task<ChikoCommCodec.S4Status> ReadS4Async(CancellationToken ct);
    Task SetRunAsync(bool run, CancellationToken ct);
    Task SetLevelAsync(int level1to15, CancellationToken ct);
    Task ClearErrorAsync(CancellationToken ct);
}

public sealed class ChikoTpController : IChikoTpController, IAsyncDisposable
{
    private readonly ChikoLinkSession _session;

    public ChikoTpController(IChikoLink link)
    {
        _session = new ChikoLinkSession(link);
    }

    public ValueTask DisposeAsync() => _session.DisposeAsync();

    public async Task<ChikoCommCodec.S4Status> ReadS4Async(CancellationToken ct)
    {
        var resp = await _session.SendAndReceiveAsync("RS40000", ct);
        return ChikoCommCodec.ParseS4FromFullResponse(resp);
    }

    public async Task SetRunAsync(bool run, CancellationToken ct)
    {
        // RUN=1 / STOP=0 （10進4桁→LLHH）
        var data4 = ChikoCommCodec.EncodeDec4_LLHH(run ? 1 : 0);
        await _session.SendAndReceiveAsync("W01" + data4, ct);
    }

    public async Task SetLevelAsync(int level1to15, CancellationToken ct)
    {
        if (level1to15 < 1 || level1to15 > 15)
            throw new ArgumentOutOfRangeException(nameof(level1to15));

        // Lvは10進：0015 -> 1500
        var data4 = ChikoCommCodec.EncodeDec4_LLHH(level1to15);
        await _session.SendAndReceiveAsync("W02" + data4, ct);
    }

    public async Task ClearErrorAsync(CancellationToken ct)
    {
        // 0001 -> 0100
        var data4 = ChikoCommCodec.EncodeDec4_LLHH(1);
        await _session.SendAndReceiveAsync("W80" + data4, ct);
    }
}
