﻿using Sandbox;

namespace Facepunch.Forsaken;

[Title( "Doorway" )]
[Description( "Can have a door placed inside. Must be placed on a foundation." )]
public partial class Doorway : Structure
{
	public override void Spawn()
	{
		SetModel( "models/structures/doorway.vmdl" );

		base.Spawn();
	}
}