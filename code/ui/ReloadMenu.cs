﻿using Sandbox;
using Sandbox.UI;
using System.Linq;
using Conna.Inventory;

namespace Facepunch.Forsaken.UI;

[StyleSheet( "/ui/RadialMenu.scss" )]
public partial class ReloadMenu : RadialMenu
{
	public static ReloadMenu Current { get; private set; }

	public override string Button => "reload";
	public override float OpenDelay => 0.3f;

	public ReloadMenu()
	{
		Current = this;
	}

	public override void Populate()
	{
		var player = ForsakenPlayer.Me;

		if ( !player.IsValid() )
			return;

		var weapon = player.ActiveChild as Weapon;
		if ( !weapon.IsValid() ) return;

		if ( !weapon.WeaponItem.IsValid() )
			return;

		if ( weapon.WeaponItem.AmmoType == AmmoType.None )
			return;

		var items = ForsakenPlayer.Me.FindItems<AmmoItem>()
			.Where( i => i.AmmoType == weapon.WeaponItem.AmmoType )
			.Select( i => i.UniqueId )
			.Distinct();

		foreach ( var id in items )
		{
			var definition = InventorySystem.GetDefinition( id ) as AmmoItem;

			if ( definition.IsValid() )
			{
				AddItem( definition.Name, definition.Description, definition.Icon, () => Select( definition.UniqueId ) );
			}
		}

		base.Populate();
	}

	protected override bool ShouldOpen()
	{
		if ( !ForsakenPlayer.Me.IsValid() )
			return false;

		var weaponItem = ForsakenPlayer.Me.GetActiveHotbarItem() as WeaponItem;
		if ( !weaponItem.IsValid() )
			return false;

		if ( weaponItem.AmmoType == AmmoType.None )
			return false;

		return true;
	}

	private void Select( string uniqueId )
	{
		var type = TypeLibrary.GetType( uniqueId );
		ForsakenPlayer.Me.SetAmmoType( uniqueId );
	}
}
