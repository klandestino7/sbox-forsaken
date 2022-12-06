﻿using Sandbox;
using System;
using System.Collections.Generic;

namespace Facepunch.Forsaken;

public partial class Campfire : Deployable, IContextActionProvider, IHeatEmitter, ICookerEntity
{
	public float InteractionRange => 150f;
	public Color GlowColor => Color.White;
	public float GlowWidth => 0.4f;

	[Net] public CookingProcessor Processor { get; private set; }

	private ContextAction ExtinguishAction { get; set; }
	private ContextAction IgniteAction { get; set; }
	private ContextAction PickupAction { get; set; }
	private ContextAction OpenAction { get; set; }

	public float EmissionRadius => 100f;
	public float HeatToEmit => Processor.IsActive ? 20f : 0f;

	private PointLightEntity DynamicLight { get; set; }
	private Particles ParticleEffect { get; set; }

	public Campfire()
	{
		PickupAction = new( "pickup", "Pickup", "textures/ui/actions/pickup.png" );
		PickupAction.SetCondition( p => Processor.IsEmpty && !Processor.IsActive );

		OpenAction = new( "open", "Open", "textures/ui/actions/open.png" );

		IgniteAction = new( "ignore", "Ignite", "textures/ui/actions/ignite.png" );
		ExtinguishAction = new( "extinguish", "Extinguish", "textures/ui/actions/disable.png" );

		Tags.Add( "hover" );
	}

	public string GetContextName()
	{
		return "Campfire";
	}

	public void Open( ForsakenPlayer player )
	{
		UI.Cooking.Open( player, GetContextName(), this );
	}

	public IEnumerable<ContextAction> GetSecondaryActions( ForsakenPlayer player )
	{
		yield return OpenAction;
		yield return PickupAction;
	}

	public ContextAction GetPrimaryAction( ForsakenPlayer player )
	{
		if ( Processor.IsActive )
			return ExtinguishAction;
		else
			return IgniteAction;
	}

	public virtual void OnContextAction( ForsakenPlayer player, ContextAction action )
	{
		if ( action == OpenAction )
		{
			if ( IsServer )
			{
				Open( player );
			}
		}
		else if ( action == PickupAction )
		{
			if ( IsServer )
			{
				var item = InventorySystem.CreateItem<CampfireItem>();
				player.TryGiveItem( item );
				player.PlaySound( "inventory.move" );
				Delete();
			}
		}
		else if ( action == IgniteAction )
		{
			if ( IsServer )
			{
				Processor.Start();
			}
		}
		else if ( action == ExtinguishAction )
		{
			if ( IsServer )
			{
				Processor.Stop();
			}
		}
	}

	public override void Spawn()
	{
		SetModel( "models/campfire/campfire.vmdl" );
		SetupPhysicsFromModel( PhysicsMotionType.Keyframed );

		Processor = new();
		Processor.SetCooker( this );
		Processor.Interval = 5f;
		Processor.OnStarted += OnStarted;
		Processor.OnStopped += OnStopped;
		Processor.Fuel.Whitelist.Add( "fuel" );
		Processor.Input.Whitelist.Add( "cookable" );

		SphereTrigger.Attach( this, EmissionRadius );

		base.Spawn();
	}

	public override void ClientSpawn()
	{
		Processor.SetCooker( this );
		Processor.OnStarted += OnStarted;
		Processor.OnStopped += OnStopped;

		base.ClientSpawn();
	}

	protected override void OnDestroy()
	{
		DynamicLight?.Delete();
		DynamicLight = null;

		base.OnDestroy();
	}

	[Event.Tick.Client]
	private void ClientTick()
	{
		if ( DynamicLight.IsValid() )
		{
			UpdateDynamicLight();
		}
	}

	[Event.Tick.Server]
	private void ServerTick()
	{
		Processor.Process();
	}

	private void UpdateDynamicLight()
	{
		DynamicLight.Brightness = 0.1f + MathF.Sin( Time.Now * 4f ) * 0.02f;
		DynamicLight.Position = Position + Vector3.Up * 40f;
		DynamicLight.Position += new Vector3( MathF.Sin( Time.Now * 2f ) * 4f, MathF.Cos( Time.Now * 2f ) * 4f );
		DynamicLight.Range = 700f + MathF.Sin( Time.Now ) * 50f;
	}

	private void OnStarted()
	{
		if ( Host.IsServer ) return;

		if ( !DynamicLight.IsValid() )
		{
			DynamicLight = new();
			DynamicLight.SetParent( this );
			DynamicLight.EnableShadowCasting = true;
			DynamicLight.DynamicShadows = true;
			DynamicLight.Color = Color.Orange;

			UpdateDynamicLight();
		}

		if ( ParticleEffect == null )
		{
			ParticleEffect = Particles.Create( "particles/campfire/campfire.vpcf", this );
		}
	}

	private void OnStopped()
	{
		if ( Host.IsServer ) return;

		ParticleEffect?.Destroy();
		ParticleEffect = null;

		DynamicLight?.Delete();
		DynamicLight = null;
	}
}
