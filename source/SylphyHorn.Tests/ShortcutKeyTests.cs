using System.Collections.Generic;
using System.Windows.Forms;
using SylphyHorn.Serialization;
using SylphyHorn.Services;
using Xunit;

namespace SylphyHorn.Tests
{
	public class ShortcutKeyTests
	{
		[Fact]
		public void EqualityIgnoresModifierOrder()
		{
			var left = new ShortcutKey(Keys.D, Keys.ControlKey, Keys.Menu);
			var right = new ShortcutKey(Keys.D, Keys.Menu, Keys.ControlKey);

			Assert.True(left == right);
			Assert.True(right == left);
			Assert.Equal(left, right);
		}

		[Fact]
		public void EqualValuesHaveEqualHashCodes()
		{
			var left = new ShortcutKey(Keys.D, Keys.ControlKey, Keys.Menu);
			var right = new ShortcutKey(Keys.D, Keys.Menu, Keys.ControlKey);

			Assert.Equal(left.GetHashCode(), right.GetHashCode());
			Assert.Single(new HashSet<ShortcutKey> { left, right });
		}

		[Fact]
		public void EqualityDistinguishesKeyAndModifierMultiset()
		{
			var value = new ShortcutKey(Keys.D, Keys.ControlKey, Keys.Menu);
			var duplicateControl = new ShortcutKey(Keys.D, Keys.ControlKey, Keys.ControlKey);
			var controlAndMenu = new ShortcutKey(Keys.D, Keys.ControlKey, Keys.Menu);

			Assert.NotEqual(value, new ShortcutKey(Keys.F4, Keys.ControlKey, Keys.Menu));
			Assert.NotEqual(value, new ShortcutKey(Keys.D, Keys.ControlKey));
			Assert.False(duplicateControl == controlAndMenu);
			Assert.False(controlAndMenu == duplicateControl);
		}

		[Fact]
		public void EqualityIsAnEquivalenceRelationWithDuplicateModifiers()
		{
			var first = new ShortcutKey(Keys.D, Keys.ControlKey, Keys.ControlKey, Keys.Menu);
			var second = new ShortcutKey(Keys.D, Keys.Menu, Keys.ControlKey, Keys.ControlKey);
			var third = new ShortcutKey(Keys.D, Keys.ControlKey, Keys.Menu, Keys.ControlKey);
			var sameAsFirst = first;

			Assert.True(first == sameAsFirst);
			Assert.True(first == second);
			Assert.True(second == first);
			Assert.True(second == third);
			Assert.True(first == third);
			Assert.Equal(first.GetHashCode(), second.GetHashCode());
			Assert.Equal(second.GetHashCode(), third.GetHashCode());
		}

		[Fact]
		public void NullAndEmptyModifiersAreEquivalentForNonNoneKey()
		{
			var withNull = new ShortcutKey(Keys.D, (Keys[])null);
			var withEmpty = new ShortcutKey(Keys.D);

			Assert.Equal(withEmpty, withNull);
			Assert.Equal(withEmpty.GetHashCode(), withNull.GetHashCode());
		}

		[Fact]
		public void HashCodeReflectsKeyAndModifierValuesForSelectedInputs()
		{
			var baseline = new ShortcutKey(Keys.D, Keys.ControlKey);

			Assert.NotEqual(baseline.GetHashCode(), new ShortcutKey(Keys.F4, Keys.ControlKey).GetHashCode());
			Assert.NotEqual(baseline.GetHashCode(), new ShortcutKey(Keys.D, Keys.Menu).GetHashCode());
		}

		[Fact]
		public void EqualLongModifierSequencesHaveEqualHashCodes()
		{
			var first = new ShortcutKey(
				Keys.D,
				Keys.LControlKey,
				Keys.RControlKey,
				Keys.LShiftKey,
				Keys.RShiftKey,
				Keys.LMenu,
				Keys.RMenu,
				Keys.LWin,
				Keys.RWin);
			var second = new ShortcutKey(
				Keys.D,
				Keys.RWin,
				Keys.LWin,
				Keys.RMenu,
				Keys.LMenu,
				Keys.RShiftKey,
				Keys.LShiftKey,
				Keys.RControlKey,
				Keys.LControlKey);

			Assert.Equal(first, second);
			Assert.Equal(first.GetHashCode(), second.GetHashCode());
		}

		[Fact]
		public void DefaultValueEqualsNone()
		{
			var value = default(ShortcutKey);

			Assert.Equal(ShortcutKey.None, value);
			Assert.Equal(ShortcutKey.None.GetHashCode(), value.GetHashCode());
		}

		[Fact]
		public void ToStringUsesStableModifierOrder()
		{
			var value = new ShortcutKey(Keys.D, Keys.ShiftKey, Keys.ControlKey);

			Assert.Equal("ShiftKey + ControlKey + D", value.ToString());
		}

		[Theory]
		[InlineData(Keys.LMenu)]
		[InlineData(Keys.LControlKey)]
		[InlineData(Keys.LShiftKey)]
		[InlineData(Keys.LWin)]
		[InlineData(Keys.RMenu)]
		[InlineData(Keys.RControlKey)]
		[InlineData(Keys.RShiftKey)]
		[InlineData(Keys.RWin)]
		public void IsModifyKeyRecognizesLeftAndRightModifiers(Keys key)
		{
			Assert.True(key.IsModifyKey());
		}

		[Theory]
		[InlineData(Keys.None)]
		[InlineData(Keys.A)]
		[InlineData(Keys.Enter)]
		[InlineData(Keys.Tab)]
		public void IsModifyKeyRejectsOrdinaryKeys(Keys key)
		{
			Assert.False(key.IsModifyKey());
		}

		[Fact]
		public void SerializableShortcutRoundTripsKeyAndModifiers()
		{
			var value = new ShortcutKey(Keys.D, Keys.ControlKey, Keys.Menu);

			var serialized = value.ToSerializable();
			Assert.Equal(new[] { (int)Keys.D, (int)Keys.ControlKey, (int)Keys.Menu }, serialized);

			var roundTrip = serialized.ToShortcutKey();

			Assert.Equal(value, roundTrip);
			Assert.Equal(Keys.D, roundTrip.Key);
			Assert.Equal(new[] { Keys.ControlKey, Keys.Menu }, roundTrip.Modifiers);
			Assert.Equal(value.GetHashCode(), roundTrip.GetHashCode());
		}

		[Fact]
		public void SerializationPreservesModifierOrderAndDuplicates()
		{
			var value = new ShortcutKey(Keys.D, Keys.Menu, Keys.ControlKey, Keys.Menu);

			var serialized = value.ToSerializable();

			Assert.Equal(
				new[] { (int)Keys.D, (int)Keys.Menu, (int)Keys.ControlKey, (int)Keys.Menu },
				serialized);
			var deserialized = serialized.ToShortcutKey();
			Assert.Equal(Keys.D, deserialized.Key);
			Assert.Equal(new[] { Keys.Menu, Keys.ControlKey, Keys.Menu }, deserialized.Modifiers);
		}

		[Fact]
		public void NullAndEmptySerializableValuesProduceNone()
		{
			Assert.Equal(ShortcutKey.None, ((IList<int>)null).ToShortcutKey());
			Assert.Equal(ShortcutKey.None, new int[0].ToShortcutKey());
		}

		[Fact]
		public void NoneSerializesAsEmptyList()
		{
			Assert.Empty(ShortcutKey.None.ToSerializable());
		}
	}
}
