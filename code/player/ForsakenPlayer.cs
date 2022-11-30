﻿using System;
using System.Collections.Generic;
using System.Linq;
using Facepunch.Forsaken.UI;
using Sandbox;
using Sandbox.Component;

namespace Facepunch.Forsaken;

public partial class ForsakenPlayer : Player
{
	private class ActiveEffect
	{
		public ConsumableEffect Type { get; set; }
		public TimeUntil EndTime { get; set; }
		public float AmountGiven { get; set; }
	}

	public static ForsakenPlayer Me => Local.Pawn as ForsakenPlayer;

	private static string[] InvalidPlacementThoughts = new string[]
	{
		"It won't fit here, I should try elsewhere.",
		"Hmm... this doesn't go there.",
		"I can't place that here.",
		"It doesn't seem to go there.",
		"I should try to place it somewhere else."
	};

	private static string[] MissingItemsThoughts = new string[]
	{
		"I don't have the required items to do that.",
		"I seem to be missing some items for that.",
		"I don't have enough to do that."
	};

	[Net] public float Temperature { get; private set; }
	[Net] public float Calories { get; private set; }
	[Net] public float Hydration { get; private set; }
	[Net, Predicted] public float Stamina { get; private set; }
	[Net, Predicted] public bool IsOutOfBreath { get; private set; }
	[Net, Predicted] public ushort HotbarIndex { get; private set; }
	[Net] public TimedAction TimedAction { get; private set; }

	[Net] private NetInventoryContainer InternalBackpack { get; set; }
	public InventoryContainer Backpack => InternalBackpack.Value;

	[Net] private NetInventoryContainer InternalHotbar { get; set; }
	public InventoryContainer Hotbar => InternalHotbar.Value;

	[Net] private NetInventoryContainer InternalEquipment { get; set; }
	public InventoryContainer Equipment => InternalEquipment.Value;

	[ClientInput] public Vector3 CursorDirection { get; private set; }
	[ClientInput] public Vector3 CameraPosition { get; private set; }
	[ClientInput] public int ContextActionId { get; private set; }
	[ClientInput] public Entity HoveredEntity { get; private set; }
	[ClientInput] public string OpenContainerIds { get; private set; }
	[ClientInput] public string ChangeAmmoType { get; private set; }
	[ClientInput] public bool HasDialogOpen { get; private set; }

	public int MaxHealth => 100;
	public int MaxStamina => 100;
	public int MaxCalories => 300;
	public int MaxHydration => 200;

	public Dictionary<ArmorSlot, List<ArmorEntity>> Armor { get; private set; }
	public ProjectileSimulator Projectiles { get; private set; }
	public Vector2 Cursor { get; set; }
	public DamageInfo LastDamageTaken { get; private set; }

	[Net] private int StructureType { get; set; }

	private TimeUntil NextCalculateTemperature { get; set; }
	private float CalculatedTemperature { get; set; }
	private List<IHeatEmitter> HeatEmitters { get; set; } = new();
	private TimeSince TimeSinceBackpackOpen { get; set; }
	private bool IsBackpackToggleMode { get; set; }
	private Entity LastHoveredEntity { get; set; }
	private List<ActiveEffect> ActiveEffects { get; set; } = new();

	[ConCmd.Server( "fsk.player.structuretype" )]
	private static void SetStructureTypeCmd( int identity )
	{
		if ( ConsoleSystem.Caller.Pawn is ForsakenPlayer player )
		{
			player.StructureType = identity;
		}
	}

	[ConCmd.Server( "fsk.item.give" )]
	public static void GiveItemCmd( string itemId, int amount )
	{
		if ( ConsoleSystem.Caller.Pawn is not ForsakenPlayer player )
			return;

		var definition = InventorySystem.GetDefinition( itemId );
		var totalToGive = amount;
		var stacksToGive = totalToGive / definition.MaxStackSize;
		var remainder = totalToGive % definition.MaxStackSize;

		for ( var i = 0; i < stacksToGive; i++ )
		{
			var item = InventorySystem.CreateItem( itemId );
			item.StackSize = item.MaxStackSize;
			player.TryGiveItem( item );
		}

		if ( remainder > 0 )
		{
			var item = InventorySystem.CreateItem( itemId );
			item.StackSize = (ushort)remainder;
			player.TryGiveItem( item );
		}
	}

	public ForsakenPlayer() : base()
	{
		Projectiles = new( this );
	}

	public ForsakenPlayer( Client client ) : this()
	{
		client.Pawn = this;

		CreateInventories();

		CraftingQueue = new List<CraftingQueueEntry>();
		HotbarIndex = 0;
		Armor = new();
	}

	public void SetAmmoType( string uniqueId )
	{
		Assert.NotNull( uniqueId );
		ChangeAmmoType = uniqueId;
	}

	public void SetStructureType( TypeDescription type )
	{
		Host.AssertClient();
		Assert.NotNull( type );
		SetStructureTypeCmd( type.Identity );
	}

	[ClientRpc]
	public void ResetCursor()
	{
		Cursor = new Vector2( 0.5f, 0.5f );
	}

	public IEnumerable<Client> GetChatRecipients()
	{
		var clientsNearby = FindInSphere( Position, 4000f )
			.OfType<ForsakenPlayer>()
			.Select( p => p.Client );

		foreach ( var client in clientsNearby )
		{
			yield return client;
		}
	}

	public void ReduceStamina( float amount )
	{
		Stamina = Math.Max( Stamina - amount, 0f );
	}

	public void SetContextAction( ContextAction action )
	{
		ContextActionId = action.Hash;
	}

	public InventoryItem GetActiveHotbarItem()
	{
		return Hotbar.GetFromSlot( HotbarIndex );
	}

	public void GainStamina( float amount )
	{
		Stamina = Math.Min( Stamina + amount, 100f );
	}

	public void StartTimedAction( string title, Vector3 origin, float duration, Action callback )
	{
		TimedAction = new();
		TimedAction.Start( title, origin, duration, callback );
	}

	public void CancelTimedAction()
	{
		TimedAction = null;
	}

	public void AddEffect( ConsumableEffect effect )
	{
		if ( effect.Duration > 0f )
		{
			var instance = new ActiveEffect()
			{
				EndTime = effect.Duration,
				AmountGiven = 0f,
				Type = effect
			};

			ActiveEffects.Add( instance );
		}
		else
		{
			if ( effect.Target == ConsumableType.Calories )
				Calories = Math.Clamp( Calories + effect.Amount, 0f, MaxCalories );
			else if ( effect.Target == ConsumableType.Hydration )
				Hydration = Math.Clamp( Hydration + effect.Amount, 0f, MaxHydration );
			else if ( effect.Target == ConsumableType.Health )
				Health = Math.Clamp( Health + effect.Amount, 0f, MaxHealth );
			else if ( effect.Target == ConsumableType.Stamina )
				Stamina = Math.Clamp( Stamina + effect.Amount, 0f, MaxStamina );
		}
	}

	public override void Spawn()
	{
		SetModel( "models/citizen/citizen.vmdl" );

		NextCalculateTemperature = 0f;

		base.Spawn();
	}

	public override void BuildInput()
	{
		base.BuildInput();

		var storage = UI.Storage.Current;
		var cooking = UI.Cooking.Current;

		if ( cooking.IsOpen && cooking.Cooker.IsValid() )
			OpenContainerIds = cooking.Cooker.Processor.GetContainerIdString();
		else if ( storage.IsOpen && storage.Container.IsValid() )
			OpenContainerIds = storage.Container.InventoryId.ToString();
		else
			OpenContainerIds = string.Empty;

		HasDialogOpen = UI.Dialog.IsActive();

		if ( Input.StopProcessing ) return;

		var mouseDelta = Input.MouseDelta / new Vector2( Screen.Width, Screen.Height );

		if ( !Mouse.Visible )
		{
			Cursor += (mouseDelta * 20f * Time.Delta);
			Cursor = Cursor.Clamp( 0f, 1f );
		}

		ActiveChild?.BuildInput();

		CursorDirection = Screen.GetDirection( Screen.Size * Cursor );
		CameraPosition = CurrentView.Position;

		var plane = new Plane( Position, Vector3.Up );
		var trace = plane.Trace( new Ray( EyePosition, CursorDirection ), true );

		if ( trace.HasValue )
		{
			var direction = (trace.Value - Position).Normal;
			ViewAngles = direction.EulerAngles;
		}

		var startPosition = CameraPosition;
		var endPosition = CameraPosition + CursorDirection * 1000f;
		var cursor = Trace.Ray( startPosition, endPosition )
			.EntitiesOnly()
			.Size( 16f )
			.Run();

		var visible = Trace.Ray( EyePosition, cursor.EndPosition )
			.Ignore( this )
			.Ignore( ActiveChild )
			.Run();

		if ( visible.Entity == cursor.Entity || visible.Fraction > 0.9f )
			HoveredEntity = cursor.Entity;
		else
			HoveredEntity = null;
	}

	public override void Respawn()
	{
		Controller = new MoveController
		{
			SprintSpeed = 200f,
			WalkSpeed = 100f
		};

		CameraMode = new TopDownCamera();
		Animator = new PlayerAnimator();

		EnableAllCollisions = true;
		EnableDrawing = true;
		LifeState = LifeState.Alive;
		Calories = 100f;
		Hydration = 30f;
		Stamina = 100f;
		Health = 100f;
		Velocity = Vector3.Zero;
		WaterLevel = 0f;

		CreateHull();
		GiveInitialItems();
		InitializeWeapons();
		ResetCursor();

		base.Respawn();
	}

	public override void StartTouch( Entity other )
	{
		var emitter = other.FindParentOfType<IHeatEmitter>();

		if ( emitter.IsValid() && !HeatEmitters.Contains( emitter ) )
		{
			HeatEmitters.Add( emitter );
		}

		base.StartTouch( other );
	}

	public override void EndTouch( Entity other )
	{
		var emitter = other.FindParentOfType<IHeatEmitter>();

		if ( other is not null )
		{
			HeatEmitters.Remove( emitter );
		}

		base.EndTouch( other );
	}

	public override void TakeDamage( DamageInfo info )
	{
		if ( info.Attacker is ForsakenPlayer )
		{
			if ( info.Hitbox.HasTag( "head" ) )
			{
				info.Damage *= 2f;
			}

			using ( Prediction.Off() )
			{
				var particles = Particles.Create( "particles/gameplay/player/taken_damage/taken_damage.vpcf", info.Position );
				particles.SetForward( 0, info.Force.Normal );
			}

			if ( info.Flags.HasFlag( DamageFlags.Blunt ) )
			{
				ApplyAbsoluteImpulse( info.Force );
			}
		}

		LastDamageTaken = info;
		base.TakeDamage( info );
	}

	public override void OnKilled()
	{
		BecomeRagdollOnServer( LastDamageTaken.Force, LastDamageTaken.BoneIndex );

		EnableAllCollisions = false;
		EnableDrawing = false;
		Controller = null;

		ClearCraftingQueue();

		var itemsToDrop = FindItems<InventoryItem>().Where( i => i.DropOnDeath );

		foreach ( var item in itemsToDrop )
		{
			var entity = new ItemEntity();
			entity.Position = WorldSpaceBounds.Center + Vector3.Up * 64f;
			entity.SetItem( item );
			entity.ApplyLocalImpulse( Vector3.Random * 100f );
		}

		var weapons = Children.OfType<Weapon>().ToArray();

		foreach ( var weapon in weapons )
		{
			weapon.Delete();
		}

		base.OnKilled();
	}

	public override void FrameSimulate( Client cl )
	{
		SimulateConstruction();
		SimulateDeployable();

		base.FrameSimulate( cl );
	}

	public override void Simulate( Client client )
	{
		base.Simulate( client );

		if ( Stamina <= 10f )
			IsOutOfBreath = true;
		else if ( IsOutOfBreath && Stamina >= 25f )
			IsOutOfBreath = false;

		Projectiles.Simulate();

		if ( IsServer )
		{
			SimulateNeeds();
			SimulateTimedAction();
		}
		
		SimulateCrafting();
		SimulateOpenContainers();

		if ( !HasDialogOpen )
		{
			if ( SimulateContextActions() )
				return;
		}

		SimulateAmmoType();
		SimulateHotbar();
		SimulateInventory();
		SimulateConstruction();
		SimulateDeployable();
		SimulateActiveChild( client, ActiveChild );
	}

	[Event.Tick.Server]
	protected virtual void ServerTick()
	{
		if ( NextCalculateTemperature )
		{
			CalculatedTemperature = TimeSystem.Temperature;
			CalculatedTemperature += Equipment.FindItems<ArmorItem>().Sum( i => i.TemperatureModifier );
			CalculatedTemperature += HeatEmitters.Sum( e =>
			{
				var distanceFraction = 1f - ((1f / e.EmissionRadius) * Position.Distance( e.Position ));
				return e.HeatToEmit * distanceFraction;
			} );

			NextCalculateTemperature = 1f;
		}

		Temperature = Temperature.LerpTo( CalculatedTemperature, Time.Delta * 2f );

		for ( var i = ActiveEffects.Count - 1; i >= 0; i-- )
		{
			var effect = ActiveEffects[i];
			var ticksPerSecond = (1f / Time.Delta) * effect.Type.Duration;
			var amountToGive = effect.Type.Amount / ticksPerSecond;

			if ( effect.Type.Target == ConsumableType.Calories )
				Calories = Math.Clamp( Calories + amountToGive, 0f, MaxCalories );
			else if ( effect.Type.Target == ConsumableType.Hydration )
				Hydration = Math.Clamp( Hydration + amountToGive, 0f, MaxHydration );
			else if ( effect.Type.Target == ConsumableType.Health )
				Health = Math.Clamp( Health + amountToGive, 0f, MaxHealth );
			else if ( effect.Type.Target == ConsumableType.Stamina )
				Stamina = Math.Clamp( Stamina + amountToGive, 0f, MaxStamina );

			if ( effect.EndTime )
			{
				ActiveEffects.RemoveAt( i );
			}
		}
	}

	protected override void OnDestroy()
	{
		if ( IsServer )
		{
			InventorySystem.Remove( Hotbar, true );
			InventorySystem.Remove( Backpack, true );
			InventorySystem.Remove( Equipment, true );
		}

		base.OnDestroy();
	}

	private void SimulateTimedAction()
	{
		if ( TimedAction is null ) return;

		if ( !InputDirection.IsNearZeroLength )
		{
			CancelTimedAction();
			return;
		}

		if ( TimedAction.EndTime )
		{
			TimedAction.OnFinished?.Invoke();
			TimedAction = null;
		}
	}

	private void SimulateNeeds()
	{
		var baseReduction = 0.02f;
		var calorieReduction = baseReduction;
		var hydrationReduction = baseReduction;

		if ( Velocity.Length > 0f )
		{
			var movementReduction = Velocity.Length.Remap( 0f, 300f, 0f, 0.075f );
			calorieReduction += movementReduction;
			hydrationReduction += movementReduction;
		}

		calorieReduction *= Temperature.Remap( -20, 0f, 4f, 2f );
		hydrationReduction *= Temperature.Remap( 0f, 40f, 0.5f, 4f );

		Calories = Math.Max( Calories - calorieReduction * Time.Delta, 0f );
		Hydration = Math.Max( Hydration - hydrationReduction * Time.Delta, 0f );
	}

	private void SimulateAmmoType()
	{
		if ( IsServer )
		{
			if ( string.IsNullOrEmpty( ChangeAmmoType ) )
				return;

			var weapon = ActiveChild as Weapon;

			if ( weapon.IsValid() )
			{
				var definition = InventorySystem.GetDefinition( ChangeAmmoType ) as AmmoItem;

				if ( definition.IsValid() )
				{
					weapon.SetAmmoItem( definition );
					weapon.Reload();
				}
			}
		}

		ChangeAmmoType = string.Empty;
	}

	private void SimulateOpenContainers()
	{
		if ( IsClient ) return;

		var viewer = Client.Components.Get<InventoryViewer>();
		viewer.ClearContainers();

		if ( string.IsNullOrEmpty( OpenContainerIds ) ) return;

		var split = OpenContainerIds.Split( ',' );

		foreach ( var id in split )
		{
			if ( ulong.TryParse( id, out var value ) )
			{
				var container = InventorySystem.Find( value );

				if ( container.IsValid() )
					viewer.AddContainer( container );
			}
		}
	}

	private bool SimulateContextActions()
	{
		var actions = HoveredEntity as IContextActionProvider;

		if ( IsClient )
		{
			if ( actions.IsValid() )
			{
				var glow = HoveredEntity.Components.GetOrCreate<Glow>();
				glow.Enabled = true;
				glow.Width = actions.GlowWidth;

				if ( Position.Distance( actions.Position ) <= actions.InteractionRange )
					glow.Color = actions.GlowColor;
				else
					glow.Color = Color.Gray;
			}

			if ( LastHoveredEntity.IsValid() && LastHoveredEntity != HoveredEntity )
			{
				var glow = LastHoveredEntity.Components.GetOrCreate<Glow>();
				glow.Enabled = false;
			}

			LastHoveredEntity = HoveredEntity;
		}

		var actionId = ContextActionId;

		if ( IsClient )
		{
			ContextActionId = 0;
		}

		if ( actions.IsValid() && Position.Distance( actions.Position ) <= actions.InteractionRange )
		{
			if ( actionId != 0 )
			{
				var allActions = IContextActionProvider.GetAllActions( actions );
				var action = allActions.Where( a => a.IsAvailable( this ) && a.Hash == actionId ).FirstOrDefault();

				if ( action.IsValid() )
				{
					actions.OnContextAction( this, action );
				}
			}

			return true;
		}

		return false;
	}

	private void SimulateDeployable()
	{
		if ( GetActiveHotbarItem() is not DeployableItem deployable )
		{
			Deployable.ClearGhost();
			return;
		}

		var startPosition = CameraPosition;
		var endPosition = CameraPosition + CursorDirection * 1000f;
		var trace = Trace.Ray( startPosition, endPosition )
			.WithAnyTags( deployable.ValidTags )
			.Run();

		var model = Model.Load( deployable.Model );
		var collision = Trace.Box( model.PhysicsBounds, trace.EndPosition, trace.EndPosition ).Run();
		var isPositionValid = !collision.Hit && deployable.CanPlaceOn( trace.Entity );

		if ( IsClient )
		{
			var ghost = Deployable.GetOrCreateGhost( model );
			ghost.RenderColor = isPositionValid ? Color.Cyan.WithAlpha( 0.5f ) : Color.Red.WithAlpha( 0.5f );

			if ( !isPositionValid )
			{
				var cursor = Trace.Ray( startPosition, endPosition )
					.WorldOnly()
					.Run();

				ghost.Position = cursor.EndPosition;
			}
			else
			{
				ghost.Position = trace.EndPosition;
			}

			ghost.ResetInterpolation();
		}

		if ( Input.Released( InputButton.PrimaryAttack ) )
		{
			if ( IsServer )
			{
				if ( isPositionValid )
				{
					var entity = TypeLibrary.Create<Deployable>( deployable.Deployable );
					entity.Position = trace.EndPosition;
					entity.ResetInterpolation();
					deployable.Remove();
				}
				else
				{
					Thoughts.Show( To.Single( this ), Rand.FromArray( InvalidPlacementThoughts ) );
				}
			}
		}
	}

	private void SimulateConstruction()
	{
		if ( GetActiveHotbarItem() is not ToolboxItem )
		{
			Structure.ClearGhost();
			return;
		}

		var structureType = TypeLibrary.GetDescriptionByIdent( StructureType );

		if ( structureType == null )
		{
			Structure.ClearGhost();
			return;
		}

		var trace = Trace.Ray( CameraPosition, CameraPosition + CursorDirection * 1000f )
			.WorldOnly()
			.Run();

		if ( !trace.Hit )
		{
			Structure.ClearGhost();
			return;
		}

		if ( IsClient )
		{
			var ghost = Structure.GetOrCreateGhost( structureType );
			var renderColor = Color.Cyan.WithAlpha( 0.5f );

			var match = ghost.LocateSocket( trace.EndPosition );

			if ( match.IsValid )
			{
				ghost.SnapToSocket( match );
			}
			else
			{
				ghost.Position = trace.EndPosition;
				ghost.ResetInterpolation();

				if ( ghost.RequiresSocket || !ghost.IsValidPlacement( ghost.Position, trace.Normal ) )
					renderColor = Color.Red.WithAlpha( 0.5f );
			}

			if ( !Structure.CanAfford( this, structureType ) )
			{
				renderColor = Color.Red.WithAlpha( 0.5f );
			}

			ghost.RenderColor = renderColor;
		}

		if ( Prediction.FirstTime && Input.Released( InputButton.PrimaryAttack ) )
		{
			if ( IsServer )
			{
				if ( Structure.CanAfford( this, structureType ) )
				{
					var structure = structureType.Create<Structure>();

					if ( structure.IsValid() )
					{
						var isValid = false;
						var match = structure.LocateSocket( trace.EndPosition );

						if ( match.IsValid )
						{
							structure.SnapToSocket( match );
							structure.OnConnected( match.Ours, match.Theirs );
							match.Ours.Connect( match.Theirs );
							isValid = true;
						}
						else if ( !structure.RequiresSocket )
						{
							structure.Position = trace.EndPosition;
							isValid = structure.IsValidPlacement( structure.Position, trace.Normal );
						}

						if ( !isValid )
						{
							Thoughts.Show( To.Single( this ), "invalid_placement", Rand.FromArray( InvalidPlacementThoughts ) );
							structure.Delete();
						}
						else
						{
							var costs = Structure.GetCostsFor( structureType );

							foreach ( var kv in costs )
							{
								TakeItems( kv.Key, kv.Value );
							}
						}
					}
				}
				else
				{
					Thoughts.Show( To.Single( this ), "missing_items", Rand.FromArray( MissingItemsThoughts ) );
				}
			}

			Structure.ClearGhost();
		}
	}
}
