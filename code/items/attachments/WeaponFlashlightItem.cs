﻿using Sandbox;
using System.IO;

namespace Facepunch.Forsaken;

public class WeaponFlashlightItem : AttachmentItem
{
	public override int AttachmentSlot => 0;
	public override string Description => "A flashlight. It looks like it mounts to a weapon.";
	public override string WorldModel => "models/attachments/flashlight/flashlight.vmdl";
	public override string UniqueId => "weapon_flashlight";
	public override string Icon => "textures/items/weapon_flashlight.png";
	public override string Name => "Weapon Flashlight";

	public override int StockStackSize => 1;
	public override int LootStackSize => 1;
	public override float StockChance => 0.1f;
	public override float LootChance => 0.03f;
	public override int SalvageCost => 50;
	public override bool IsPurchasable => true;
	public override bool IsLootable => true;

	protected ModelEntity AttachmentEntity { get; set; }
	protected Flashlight LightEntity { get; set; }
	protected bool IsEnabled { get; set; }

	public override void Write( BinaryWriter writer )
	{
		writer.Write( IsEnabled );

		base.Write( writer );
	}

	public override void Read( BinaryReader reader )
	{
		IsEnabled = reader.ReadBoolean();

		base.Read( reader );
	}

	public override void OnActiveStart( WeaponItem item, ForsakenPlayer player )
	{
		if ( item.Weapon.IsValid() )
			CreateEntity( item.Weapon );
		else
			DestroyEntity();
	}

	public override void OnActiveEnd( WeaponItem item, ForsakenPlayer player )
	{
		DestroyEntity();
	}

	public override void OnAttached( WeaponItem item )
	{
		if ( item.Weapon.IsValid() && item.Weapon.IsActive )
			CreateEntity( item.Weapon );
		else
			DestroyEntity();
	}

	public override void OnDetatched( WeaponItem item )
	{
		DestroyEntity();
	}

	public override void Simulate( IClient client )
	{
		if ( Game.IsServer && Input.Released( "flashlight" ) )
		{
			IsEnabled = !IsEnabled;
			IsDirty = true;
		}

		if ( LightEntity.IsValid() )
		{
			LightEntity.Enabled = IsEnabled;
			UpdateLights();
		}
	}

	private void UpdateLights()
	{
		if ( !IsEnabled || !AttachedTo.Weapon.IsValid() ) return;

		var mountPoint = AttachedTo.Weapon.GetAttachment( "laser" );

		if ( mountPoint.HasValue )
		{
			var position = mountPoint.Value.Position;
			var rotation = mountPoint.Value.Rotation;
			var trace = Trace.Ray( position, position + rotation.Forward * 20f )
				.WithoutTags( "trigger" )
				.Run();

			LightEntity.Position = trace.EndPosition - trace.Direction * 15f;
		}
	}

	private void DestroyEntity()
	{
		if ( Game.IsClient ) return;

		if ( AttachmentEntity.IsValid() )
		{
			AttachmentEntity.Delete();
			AttachmentEntity = null;
		}

		if ( LightEntity.IsValid() )
		{
			LightEntity.Delete();
			LightEntity = null;
		}
	}

	private void CreateEntity( Weapon weapon )
	{
		if ( Game.IsClient ) return;

		DestroyEntity();

		var mountPoint = weapon.GetAttachment( "laser" );
		if ( !mountPoint.HasValue ) return;

		AttachmentEntity = new ModelEntity( WorldModel );
		AttachmentEntity.SetParent( weapon );
		AttachmentEntity.Transform = mountPoint.Value;

		LightEntity = new();
		LightEntity.SetParent( weapon );
		LightEntity.Transform = mountPoint.Value;
		LightEntity.Enabled = IsEnabled;
	}
}
