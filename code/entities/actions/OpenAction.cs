﻿namespace Facepunch.Forsaken;

public class OpenAction : ContextAction
{
	public override string Name => "Open";
	public override string Icon => "textures/ui/armor_slot_head.png";

	public OpenAction()
	{

	}

	public OpenAction( IContextActionProvider owner )
	{
		Provider = owner;
	}

	public override void Select( ForsakenPlayer player )
	{
		if ( IsServer && Provider is StorageCrate crate )
		{
			crate.Open( player );
		}
	}
}