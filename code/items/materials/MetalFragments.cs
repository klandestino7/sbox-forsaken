﻿namespace Facepunch.Forsaken;

public class RefinedMetal : InventoryItem
{
	public override Color Color => ItemColors.Material;
	public override string Name => "Metal Fragments";
	public override string UniqueId => "metal_fragments";
	public override string Description => "Fragments of metal. Usually obtained by smelting metal ore.";
	public override ushort MaxStackSize => 10;
	public override string Icon => "textures/items/metal_fragments.png";
}
