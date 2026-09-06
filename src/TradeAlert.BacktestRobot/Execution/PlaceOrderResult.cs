using cAlgo.API;

namespace TradeAlert.BacktestRobot.Execution;

public readonly struct PlaceOrderResult
{
    public bool Success { get; init; }
    public PendingOrder? Order { get; init; }
    public Position? Position { get; init; }
    public string? Error { get; init; }

    public static PlaceOrderResult Failed(string? error) => new() { Success = false, Error = error };
    public static PlaceOrderResult Ok(PendingOrder? order) => new() { Success = true, Order = order };
    public static PlaceOrderResult OkPosition(Position? position) => new() { Success = true, Position = position };
}
