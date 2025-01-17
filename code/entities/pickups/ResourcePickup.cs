﻿using System;
using Sandbox;
using System.Collections.Generic;
using Conna.Inventory;

namespace Facepunch.Forsaken;

public abstract partial class ResourcePickup : ModelEntity, IContextActionProvider, ILimitedSpawner
{
	public float InteractionRange => 100f;
	public Color GlowColor => Color.White;
	public bool AlwaysGlow => false;

	private ContextAction HarvestAction { get; set; }

	public abstract string GatherSound { get; }
	public abstract string ModelPath { get; }
	public abstract Type ItemType { get; }
	public abstract int StackSize { get; }

	public ResourcePickup()
	{
		HarvestAction = new( "harvest", "Harvest", "textures/ui/actions/harvest.png" );
	}

	public IEnumerable<ContextAction> GetSecondaryActions( ForsakenPlayer player )
	{
		yield break;
	}

	public ContextAction GetPrimaryAction( ForsakenPlayer player )
	{
		return HarvestAction;
	}

	public virtual void Despawn()
	{

	}

	public virtual string GetContextName()
	{
		return "Resource";
	}

	public virtual void OnContextAction( ForsakenPlayer player, ContextAction action )
	{
		if ( action == HarvestAction )
		{
			if ( Game.IsServer )
			{
				var timedAction = new TimedActionInfo( OnHarvested );

				timedAction.SoundName = GatherSound;
				timedAction.Title = "Harvesting";
				timedAction.Origin = Position;
				timedAction.Duration = 2f;
				timedAction.Icon = "textures/ui/actions/harvest.png";

				player.StartTimedAction( timedAction );
			}
		}
	}

	public override void OnNewModel( Model model )
	{
		SetupPhysicsFromModel( PhysicsMotionType.Keyframed );

		if ( !PhysicsBody.IsValid() )
		{
			SetupPhysicsFromSphere( PhysicsMotionType.Keyframed, Vector3.Zero, 16f );
		}

		base.OnNewModel( model );
	}

	public override void Spawn()
	{
		SetModel( ModelPath );

		Tags.Add( "hover", "solid", "passplayers" );

		base.Spawn();
	}

	private void OnHarvested( ForsakenPlayer player )
	{
		if ( !IsValid ) return;

		var item = InventorySystem.CreateItem( ItemType );
		item.StackSize = (ushort)StackSize;

		var remaining = player.TryGiveItem( item );

		if ( remaining < StackSize )
		{
			Sound.FromScreen( To.Single( player ), "inventory.move" );
		}

		if ( remaining == StackSize ) return;

		if ( remaining > 0 )
		{
			var entity = InventorySystem.CreateItemEntity( item );
			entity.Position = Position;
		}

		Delete();
	}
}
