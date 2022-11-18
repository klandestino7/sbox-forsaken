﻿using Sandbox;
using System.Collections.Generic;

namespace Facepunch.Forsaken;

[GameResource( "Recipe", "recipe", "A crafting recipe to produce an item in Forsaken.", Icon = "auto_awesome" )]
public class RecipeResource : GameResource
{
	/// <summary>
	/// The unique id of the item to produce as an output.
	/// </summary>
	[Property] public string Output { get; set; }

	/// <summary>
	/// The producted quantity of the output item.
	/// </summary>
	[Property] public int StackSize { get; set; } = 1;

	/// <summary>
	/// The unique item ids and their quantities required to produce the output item.
	/// </summary>
	[Property] public Dictionary<string,int> Inputs { get; set; }
}