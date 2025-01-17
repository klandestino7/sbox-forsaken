﻿using System.Collections.Generic;
using Conna.Inventory;

namespace Facepunch.Forsaken;

public class MetalFragments : InventoryItem
{
	public override Color Color => ItemColors.Material;
	public override string Name => "Metal Fragments";
	public override string UniqueId => "metal_fragments";
	public override string Description => "Fragments of metal. Usually obtained by smelting metal ore.";
	public override ushort MaxStackSize => 500;
	public override string Icon => "textures/items/metal_fragments.png";

	protected override void BuildTags( HashSet<string> tags )
	{
		tags.Add( "material" );

		base.BuildTags( tags );
	}
}
