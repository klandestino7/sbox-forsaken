﻿using Sandbox;
using System.Linq;

namespace Facepunch.Forsaken;

[Title( "Foundation" )]
[Description( "The most fundamental building block. Walls, doors and windows can be attached to it." )]
public partial class Foundation : Structure
{
	public override bool RequiresSocket => false;

	public override void Spawn()
	{
		base.Spawn();

		SetModel( "models/structures/foundation.vmdl" );
		SetupPhysicsFromModel( PhysicsMotionType.Keyframed );
	}

	public override void OnNewModel( Model model )
	{
		if ( IsServer )
		{
			AddSocket( "forward" );
			AddSocket( "backward" );
			AddSocket( "left" );
			AddSocket( "right" );
		}

		base.OnNewModel( model );
	}

	public override bool LocateSocket( Vector3 target, out Socket socket )
	{
		var nearest = FindInSphere( target, 64f ).OfType<Foundation>();

		if ( nearest.Any() )
		{
			var structures = nearest.OrderBy( s => OrderStructureByDistance( target, s ) );

			foreach ( var structure in structures )
			{
				var orderedSockets = structure.Sockets
					.Where( s => s.Structures.Count == 0 )
					.OrderBy( a => OrderSocketByDistance( target, a ) );

				socket = orderedSockets.FirstOrDefault();

				if ( socket.IsValid() )
				{
					return true;
				}
			}
		}

		socket = null;
		return false;;
	}
}
