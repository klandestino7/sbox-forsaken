﻿using Sandbox;
using System.Collections.Generic;
using Conna.Inventory;

namespace Facepunch.Forsaken;

public class ConsumableItem : ResourceItem<ConsumableResource, ConsumableItem>, ILootSpawnerItem, IConsumableItem, ICookableItem, IPurchasableItem, IRecyclableItem
{
	public override Color Color => ItemColors.Consumable;
	public override ushort DefaultStackSize => (ushort)(Resource?.DefaultStackSize ?? 1);
	public override ushort MaxStackSize => (ushort)(Resource?.MaxStackSize ?? 1);
	public override string PrimaryUseHint => "Consume";

	public virtual int StockStackSize => Resource?.StockStackSize.GetValue().CeilToInt() ?? default;
	public virtual int LootStackSize => Resource?.LootStackSize.GetValue().CeilToInt() ?? default;
	public virtual float StockChance => Resource?.StockChance ?? default;
	public virtual float LootChance => Resource?.LootChance ?? default;
	public virtual int SalvageCost => Resource?.SalvageCost ?? default;
	public virtual bool IsPurchasable => Resource?.IsPurchasable ?? default;
	public virtual bool IsLootable => Resource?.IsLootable ?? default;
	public virtual bool OncePerContainer => Resource?.OncePerContainer ?? default;
	public virtual string ConsumeSound => Resource?.ConsumeSound ?? default;
	public virtual string ConsumeEffect => Resource?.ConsumeEffect ?? default;
	public virtual string ActivateSound => Resource?.ActivateSound ?? default;
	public virtual float ActivateDelay => Resource?.ActivateDelay ?? default;
	public virtual List<ConsumableEffect> Effects => Resource?.Effects ?? default;
	public virtual string CookedItemId => Resource?.CookedItemId ?? default;
	public virtual int CookedQuantity => Resource?.CookedQuantity ?? default;
	public virtual bool IsCookable => Resource?.IsCookable ?? default;
	public virtual Dictionary<string, int> RecycleOutput => Resource?.RecycleOutput ?? default;
	public virtual float BaseComponentReturn => Resource?.BaseComponentReturn ?? 0.5f;
	public virtual bool IsRecyclable => Resource?.IsRecyclable ?? default;

	public async void Consume( ForsakenPlayer player )
	{
		StackSize--;

		using ( Prediction.Off() )
		{
			if ( !string.IsNullOrEmpty( ConsumeSound ) )
			{
				player.PlaySound( ConsumeSound );
			}
		}

		await GameTask.DelaySeconds( ActivateDelay );

		if ( !player.IsValid() )
			return;

		if ( !string.IsNullOrEmpty( ActivateSound ) )
		{
			player.PlaySound( ActivateSound );
		}

		if ( !string.IsNullOrEmpty( ConsumeEffect ) )
		{
			var effect = Particles.Create( ConsumeEffect, player );
			effect.AutoDestroy( 3f );
			effect.SetEntity( 0, player );
		}

		if ( player.LifeState == LifeState.Alive )
		{
			OnActivated( player );
		}
	}

	public virtual void OnActivated( ForsakenPlayer player )
	{
		foreach ( var effect in Effects )
		{
			player.AddEffect( effect );
		}
	}

	public override bool CanStackWith( InventoryItem other )
	{
		return true;
	}

	protected override void BuildTags( HashSet<string> tags )
	{
		tags.Add( "consumable" );

		if ( IsCookable )
			tags.Add( "cookable" );

		base.BuildTags( tags );
	}
}
