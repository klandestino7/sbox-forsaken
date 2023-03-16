﻿using Sandbox;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Facepunch.Forsaken;

public partial class ForsakenGame : GameManager
{
	public static ForsakenGame Entity => Current as ForsakenGame;
	public static string UniqueSaveId => Entity?.InternalSaveId ?? string.Empty;

	[ConVar.Replicated( "fsk.isometric" )]
	public static bool Isometric { get; set; } = true;

	[ConVar.Server( "fsk.autosave", Saved = true )]
	public static bool ShouldAutoSave { get; set; } = true;

	[ConVar.Server( "fsk.pvp" )]
	public static bool EnablePvP { get; set; } = true;

	private TimeUntil NextDespawnItems { get; set; }
	private TimeUntil NextAutoSave { get; set; }
	private IsometricCamera IsometricCamera { get; set; }
	private TopDownCamera TopDownCamera { get; set; }
	private bool HasLoadedWorld { get; set; }

	[Net] private string InternalSaveId { get; set; }

	public ForsakenGame() : base()
	{

	}

	[ConCmd.Server]
	private static async void CreateWalkabilityMap()
	{
		if ( ConsoleSystem.Caller.Pawn is ForsakenPlayer pl )
		{
			var gridX = 100;
			var gridY = 100;
			var cellSize = 30f;
			var agentRadius = 8f;
			var height = new float[gridX * gridY];
			var grid = new byte[gridX * gridY];
			var worldSize = new Vector3( gridX * cellSize, gridY * cellSize );
			var agentCapsule = new Capsule( Vector3.Zero, Vector3.Up * 80f, agentRadius );

			await GameTask.RunInThreadAsync( () =>
			{
				for ( var x = 0; x < gridX; x++ )
				{
					for ( var y = 0; y < gridY; y++ )
					{
						var position = new Vector3( x * cellSize, y * cellSize, 0f ) - worldSize * 0.5f;
						var idx = y * gridY + x;

						var trace = Trace.Ray( position + Vector3.Up * 5000f, position )
							.WorldAndEntities()
							.Size( agentRadius )
							.Run();

						if ( trace.Hit && trace.Normal.Angle( Vector3.Up ) <= 45f )
						{
							var liftedPosition = trace.EndPosition + Vector3.Up * 4f;
							var sweep = Trace.Capsule( agentCapsule, liftedPosition, liftedPosition )
								.WorldAndEntities()
								.Run();

							if ( sweep.Hit || sweep.StartedSolid )
								grid[idx] = 1;
							else
								grid[idx] = 0;
						}
						else
						{
							grid[idx] = 1;
						}

						height[idx] = trace.EndPosition.z;
					}
				}

				Log.Info( "Walkability map generated." );
			} );

			for ( var x = 0; x < gridX; x++ )
			{
				for ( var y = 0; y < gridY; y++ )
				{
					var position = new Vector3( x * cellSize, y * cellSize, 0f ) - worldSize * 0.5f;
					var idx = y * gridY + x;

					if ( grid[idx] == 1 )
						DebugOverlay.Sphere( position.WithZ( height[idx] ), 4f, Color.Red, 3f );
					else
						DebugOverlay.Sphere( position.WithZ( height[idx] ), 4f, Color.Green, 3f );
				}
			}
		}
	}

	public override void LoadSavedGame( SavedGame save )
	{
		Log.Info( "[Forsaken] Loading world..." );

		using var s = new MemoryStream( save.Data );
		using var r = new BinaryReader( s );

		PersistenceSystem.LoadAll( r );
	}

	public override void Spawn()
	{
		InventorySystem.Initialize();
		base.Spawn();
	}

	public override void ClientSpawn()
	{
		InventorySystem.Initialize();

		ItemTag.Register( "deployable", "Deployable", ItemColors.Deployable );
		ItemTag.Register( "consumable", "Consumable", ItemColors.Consumable );
		ItemTag.Register( "tool", "Tool", ItemColors.Tool );

		Game.RootPanel?.Delete( true );
		Game.RootPanel = new UI.Hud();

		IsometricCamera = new();
		TopDownCamera = new();

		base.ClientSpawn();
	}

	public override void ClientJoined( IClient client )
	{
		InventorySystem.ClientJoined( client );

		var pawn = All.OfType<ForsakenPlayer>()
			.Where( p => p.SteamId == client.SteamId )
			.FirstOrDefault();

		if ( !pawn.IsValid() )
		{
			pawn = new ForsakenPlayer();
			pawn.MakePawnOf( client );
			pawn.Respawn();
		}
		else
		{
			pawn.MakePawnOf( client );
		}

		PersistenceSystem.Load( pawn );

		base.ClientJoined( client );
	}

	public override void MoveToSpawnpoint( Entity pawn )
	{
		if ( pawn is ForsakenPlayer player && player.Bedroll.IsValid() )
		{
			player.Position = player.Bedroll.Position + Vector3.Up * 10f;
			return;
		}

		base.MoveToSpawnpoint( pawn );
	}

	public override void ClientDisconnect( IClient client, NetworkDisconnectionReason reason )
	{
		if ( client.Pawn is ForsakenPlayer player )
		{
			PersistenceSystem.Save( player );
		}

		InventorySystem.ClientDisconnected( client );

		base.ClientDisconnect( client, reason );
	}

	public override bool CanHearPlayerVoice( IClient source, IClient receiver )
	{
		if ( !source.IsValid() || !receiver.IsValid() ) return false;

		var a = source.Pawn as ForsakenPlayer;
		var b = source.Pawn as ForsakenPlayer;

		if ( !a.IsValid() || !b.IsValid() ) return false;

		return a.Position.Distance( b.Position ) <= 2000f;
	}

	public override void OnVoicePlayed( IClient cl )
	{
		cl.Voice.WantsStereo = false;
		base.OnVoicePlayed( cl );
	}

	public override void PostLevelLoaded()
	{
		Game.WorldEntity.Tags.Add( "world" );

		{
			var spawner = new LimitedSpawner();
			spawner.SetType<WoodPickup>();
			spawner.UseNavMesh = true;
			spawner.MaxTotal = 400;
			spawner.MinPerSpawn = 200;
			spawner.MaxPerSpawn = 300;
			spawner.Interval = 120f;
		}

		{
			var spawner = new LimitedSpawner();
			spawner.SetType<StonePickup>();
			spawner.UseNavMesh = true;
			spawner.MaxTotal = 300;
			spawner.MinPerSpawn = 100;
			spawner.MaxPerSpawn = 200;
			spawner.Interval = 180f;
		}

		{
			var spawner = new LimitedSpawner();
			spawner.SetType<MetalOrePickup>();
			spawner.UseNavMesh = true;
			spawner.MaxTotal = 200;
			spawner.MinPerSpawn = 100;
			spawner.MaxPerSpawn = 150;
			spawner.Interval = 300f;
		}

		{
			var spawner = new LimitedSpawner();
			spawner.SetType<PlantFiberPickup>();
			spawner.UseNavMesh = true;
			spawner.MaxTotal = 250;
			spawner.MinPerSpawn = 150;
			spawner.MaxPerSpawn = 200;
			spawner.Interval = 120f;
		}

		{
			var spawner = new LimitedSpawner();
			spawner.SetType<Deer>();
			spawner.UseNavMesh = true;
			spawner.MaxTotal = 20;
			spawner.MinPerSpawn = 1;
			spawner.MaxPerSpawn = 10;
			spawner.Interval = 120f;
		}

		{
			var spawner = new LimitedSpawner();
			spawner.SetType<Undead>();
			spawner.UseNavMesh = true;
			spawner.MaxTotal = 20;
			spawner.MinPerSpawn = 1;
			spawner.MaxPerSpawn = 10;
			spawner.Interval = 120f;
		}

		NextDespawnItems = 30f;
		HasLoadedWorld = true;
		NextAutoSave = 60f;

		base.PostLevelLoaded();
	}

	[Event.Tick.Server]
	private void ServerTick()
	{
		if ( HasLoadedWorld && NextAutoSave && ShouldAutoSave )
		{
			Log.Info( "[Forsaken] Saving world..." );
			PersistenceSystem.SaveAll();
			NextAutoSave = 60f;
		}

		if ( HasLoadedWorld && NextDespawnItems )
		{
			var items = All.OfType<ItemEntity>();

			foreach ( var item in items )
			{
				if ( item.TimeSinceSpawned >= 1800f )
				{
					item.Delete();
				}
			}

			NextDespawnItems = 30f;
		}

		InternalSaveId = PersistenceSystem.UniqueId;
	}

	[Event.Client.Frame]
	private void OnFrame()
	{
		if ( Isometric )
			IsometricCamera?.Update();
		else
			TopDownCamera?.Update();
	}
}
