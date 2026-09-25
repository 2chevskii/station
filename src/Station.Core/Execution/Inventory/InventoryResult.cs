namespace Station.Core.Execution.Inventory;

public sealed record InventoryResult(
    bool Succeeded,
    string Message,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<AvailableComponent> Components
);
