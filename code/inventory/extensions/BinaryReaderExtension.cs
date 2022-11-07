﻿using Sandbox;
using System.IO;

namespace Facepunch.Forsaken;

public static class BinaryReaderExtension
{
	public static InventoryItem ReadInventoryItem( this BinaryReader buffer )
	{
		var className = buffer.ReadString();

		if ( !string.IsNullOrEmpty( className ) )
		{
			var stackSize = buffer.ReadUInt16();
			var itemId = buffer.ReadUInt64();
			var slotId = buffer.ReadUInt16();

			var instance = InventorySystem.CreateItem( className, itemId );

			if ( instance != null )
			{
				instance.StackSize = stackSize;
				instance.SlotId = slotId;
				instance.Read( buffer );
			}

			return instance;
		}
		else
		{
			return null;
		}
	}

	public static InventoryContainer ReadInventoryContainer( this BinaryReader buffer )
	{
		var inventoryId = buffer.ReadUInt64();
		var slotLimit = buffer.ReadUInt16();
		var entityId = buffer.ReadInt32();

		var container = InventorySystem.Find( inventoryId );
		var entity = Entity.FindByIndex( entityId );

		if ( !entity.IsValid() )
		{
			Log.Error( "Unable to read an inventory container with an unscoped entity!" );
			return null;
		}

		if ( container == null )
		{
			container = new InventoryContainer( entity );
			container.SetSlotLimit( slotLimit );
			InventorySystem.Register( container, inventoryId );
		}
		else
		{
			container.SetSlotLimit( slotLimit );
		}

		for ( var i = 0; i < slotLimit; i++ )
		{
			var isValid = buffer.ReadBoolean();

			if ( isValid )
			{
				container.ItemList[i] = buffer.ReadInventoryItem();
			}
		}

		return container;
	}

}