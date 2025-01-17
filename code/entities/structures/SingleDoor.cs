﻿using Sandbox;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Conna.Inventory;

namespace Facepunch.Forsaken;

public abstract partial class SingleDoor : Structure, ICodeLockable
{
	public virtual StructureMaterial Material => StructureMaterial.Wood;
	public abstract Type ItemType { get; }

	public override Color GlowColor => IsAuthorized() ? Color.Green : Color.Red;

	private ContextAction PickupAction { get; set; }
	private ContextAction OpenAction { get; set; }
	private ContextAction CloseAction { get; set; }
	private ContextAction LockAction { get; set; }
	private ContextAction AuthorizeAction { get; set; }

	[Net] private IList<long> Authorized { get; set; } = new List<long>();
	[Net] public bool IsLocked { get; private set; }
	[Net] public bool IsOpen { get; private set; }

	private TimeSince LastOpenOrCloseTime { get; set; }
	private bool DoorOpensAway { get; set; }
	private Socket Socket { get; set; }

	public string Code { get; private set; }

	public SingleDoor()
	{
		PickupAction = new( "pickup", "Pickup", "textures/ui/actions/pickup.png" );
		PickupAction.SetCondition( p =>
		{
			return new ContextAction.Availability
			{
				IsAvailable = IsAuthorized( p )
			};
		} );

		CloseAction = new( "close", "Close", "textures/ui/actions/close_door.png" );
		CloseAction.SetCondition( p =>
		{
			return new ContextAction.Availability
			{
				IsAvailable = IsAuthorized( p )
			};
		} );

		OpenAction = new( "open", "Open", "textures/ui/actions/open_door.png" );
		OpenAction.SetCondition( p =>
		{
			return new ContextAction.Availability
			{
				IsAvailable = IsAuthorized( p )
			};
		} );

		LockAction = new( "lock", "Lock", "textures/items/code_lock.png" );
		LockAction.SetCondition( p =>
		{
			var isAvailable = p.HasItems<CodeLockItem>( 1 );

			return new ContextAction.Availability
			{
				IsAvailable = isAvailable,
				Message = isAvailable ? string.Empty : "Code Lock Required"
			};
		} );

		AuthorizeAction = new( "authorize", "Authorize", "textures/ui/actions/authorize.png" );
	}

	public bool ApplyLock( ForsakenPlayer player, string code )
	{
		if ( !player.HasItems<CodeLockItem>( 1 ) )
			return false;

		IsLocked = true;
		Code = code;

		player.TakeItems<CodeLockItem>( 1 );

		return true;
	}

	public void Authorize( ForsakenPlayer player )
	{
		if ( IsAuthorized( player ) ) return;
		Authorized.Add( player.SteamId );
	}

	public void Deauthorize( ForsakenPlayer player )
	{
		Authorized.Remove( player.SteamId );
	}

	public bool IsAuthorized( ForsakenPlayer player )
	{
		return Authorized.Contains( player.SteamId );
	}

	public bool IsAuthorized()
	{
		Game.AssertClient();
		return Authorized.Contains( Game.LocalClient.SteamId );
	}

	public override IEnumerable<ContextAction> GetSecondaryActions( ForsakenPlayer player )
	{
		if ( !IsLocked && IsAuthorized( player ) )
		{
			yield return LockAction;
		}

		yield return PickupAction;
	}

	public override ContextAction GetPrimaryAction( ForsakenPlayer player )
	{
		if ( IsLocked && !IsAuthorized( player ) )
			return AuthorizeAction;

		if ( IsOpen )
			return CloseAction;
		else
			return OpenAction;
	}

	public override string GetContextName()
	{
		return "Door";
	}

	public override void OnContextAction( ForsakenPlayer player, ContextAction action )
	{
		if ( Game.IsClient ) return;

		if ( action == OpenAction && IsAuthorized( player ) )
		{
			PlaySound( "door.single.open" );
			LastOpenOrCloseTime = 0f;
			EnableAllCollisions = false;
			IsOpen = true;

			var direction = (Position - player.Position).Normal;
			var dot = Rotation.Forward.Dot( direction );

			DoorOpensAway = dot > 0f;
		}
		else if ( action == CloseAction && IsAuthorized( player ) )
		{
			PlaySound( "door.single.close" );
			LastOpenOrCloseTime = 0f;
			EnableAllCollisions = false;
			IsOpen = false;
		}
		else if ( action == LockAction && IsAuthorized( player ) )
		{
			UI.LockScreen.OpenToLock( player, this );
		}
		else if ( action == AuthorizeAction )
		{
			UI.LockScreen.OpenToUnlock( player, this );
		}
		else if ( action == PickupAction && IsAuthorized( player ) )
		{
			if ( Game.IsServer )
			{
				Sound.FromScreen( To.Single( player ), "inventory.move" );

				var item = InventorySystem.CreateItem( ItemType );
				player.TryGiveItem( item );
				Delete();
			}
		}
	}

	public override void Spawn()
	{
		base.Spawn();
		Tags.Add( "hover", "solid", "door" );
	}

	public override void OnPlacedByPlayer( ForsakenPlayer player )
	{
		Authorize( player );
		base.OnPlacedByPlayer( player );
	}

	public override bool CanConnectTo( Socket socket )
	{
		return !FindInSphere( socket.Position, 32f )
			.OfType<Structure>()
			.Where( s => !s.Equals( this ) )
			.Where( s => s.Tags.Has( "door" ) )
			.Any();
	}

	public override void OnNewModel( Model model )
	{
		if ( Game.IsServer || IsClientOnly )
		{
			Socket?.Delete();

			Socket = AddSocket( "center" );
			Socket.ConnectAny.Add( "doorway" );
			Socket.Tags.Add( "door" );
		}

		base.OnNewModel( model );
	}

	public override void SerializeState( BinaryWriter writer )
	{
		writer.Write( IsOpen );
		writer.Write( IsLocked );
		writer.Write( DoorOpensAway );
		writer.Write( string.IsNullOrEmpty( Code ) ? "" : Code );
		writer.Write( Authorized.Count );

		foreach ( var id in Authorized )
		{
			writer.Write( id );
		}

		base.SerializeState( writer );
	}

	public override void DeserializeState( BinaryReader reader )
	{
		IsOpen = reader.ReadBoolean();
		IsLocked = reader.ReadBoolean();
		DoorOpensAway = reader.ReadBoolean();
		Code = reader.ReadString();

		var count = reader.ReadInt32();

		for ( var i = 0; i < count; i++ )
		{
			var id = reader.ReadInt64();
			Authorized.Add( id );
		}

		base.DeserializeState( reader );
	}

	private bool LastEnableAllCollisions { get; set; }

	[Event.Tick.Server]
	protected virtual void Tick()
	{
		if ( !Socket.IsValid() ) return;

		var parent = Socket.Connection;
		if ( !parent.IsValid() ) return;

		var targetRotation = IsOpen ? parent.Rotation.RotateAroundAxis( Vector3.Up, DoorOpensAway ? -90f : 90f ) : parent.Rotation;
		LocalRotation = Rotation.Slerp( LocalRotation, targetRotation, Time.Delta * 8f );

		var enableAllCollisions = (LocalRotation.Distance( targetRotation ) <= 1f && LastOpenOrCloseTime > 0.2f);
		EnableAllCollisions = enableAllCollisions;

		if ( LastEnableAllCollisions != enableAllCollisions )
		{
			// This is kinda meh.
			LastEnableAllCollisions = enableAllCollisions;
			Navigation.Update( Position, 256f );
		}
	}
}
