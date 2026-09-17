namespace DeploySeal.Nop.Core.Inventory;

/// <summary>
/// One installed platform plugin as the DeploySeal inventory records it (install contract §6):
/// the platform's stable identifier, its display name, its version and whether it is
/// installed/enabled right now.
/// </summary>
public sealed record InventoryItem(string SystemName, string Name, string Version, bool Enabled);
