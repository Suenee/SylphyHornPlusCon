using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using SylphyHorn.Services;
using SylphyHorn.Services.Mouse;
using Xunit;

namespace SylphyHorn.Tests
{
	public sealed class MouseShortcutStateTests
	{
		[Fact]
		public void ButtonDownPublishesExistingButtonsBeforeAddingCurrentButton()
		{
			var state = new MouseShortcutState();
			Assert.True(ProcessDown(state, Keys.LButton, out var first));
			Assert.Empty(first);

			Assert.True(ProcessDown(state, Keys.RButton, out var second));
			Assert.Equal(new[] { Keys.LButton }, second);
		}

		[Fact]
		public void DuplicateButtonDownDoesNotDuplicateThePressedSet()
		{
			var state = new MouseShortcutState();
			ProcessDown(state, Keys.LButton, out _);
			ProcessDown(state, Keys.LButton, out var duplicate);
			ProcessDown(state, Keys.RButton, out var following);

			Assert.Equal(new[] { Keys.LButton }, duplicate);
			Assert.Equal(new[] { Keys.LButton }, following);
		}

		[Fact]
		public void ButtonUpRemovesCurrentButtonBeforePublishing()
		{
			var state = new MouseShortcutState();
			ProcessDown(state, Keys.LButton, out _);
			ProcessDown(state, Keys.RButton, out _);

			Assert.True(ProcessUp(state, Keys.LButton, out var modifiers));
			Assert.Equal(new[] { Keys.RButton }, modifiers);
		}

		[Theory]
		[InlineData(Keys.LButton)]
		[InlineData(Keys.RButton)]
		[InlineData(Keys.MButton)]
		[InlineData(Keys.XButton1)]
		[InlineData(Keys.XButton2)]
		public void EverySupportedButtonCanTransitionDownAndUp(Keys keyCode)
		{
			var state = new MouseShortcutState();

			Assert.True(ProcessDown(state, keyCode, out var downModifiers));
			Assert.Empty(downModifiers);
			Assert.True(ProcessUp(state, keyCode, out var upModifiers));
			Assert.Empty(upModifiers);
		}

		[Fact]
		public void ButtonUpPublishesEvenWhenTheButtonWasNotPressed()
		{
			var state = new MouseShortcutState();

			Assert.True(ProcessUp(state, Keys.LButton, out var modifiers));
			Assert.Empty(modifiers);
		}

		[Fact]
		public void WheelRequiresAPressedButtonAndIsAddedAfterPublishing()
		{
			var state = new MouseShortcutState();
			Assert.False(ProcessWheel(state, Stroke.WheelDown, out _));

			ProcessDown(state, Keys.LButton, out _);
			Assert.True(ProcessWheel(state, Stroke.WheelDown, out var wheelModifiers));
			Assert.Equal(new[] { Keys.LButton }, wheelModifiers);

			ProcessDown(state, Keys.RButton, out var following);
			Assert.Equal(new[] { Keys.LButton, (Keys)Stroke.WheelDown }, following.OrderBy(key => key));
		}

		[Fact]
		public void ButtonUpPublishesWheelThenRemovesItBeforeTheNextTransition()
		{
			var state = new MouseShortcutState();
			ProcessDown(state, Keys.LButton, out _);
			ProcessWheel(state, Stroke.WheelUp, out _);

			ProcessUp(state, Keys.LButton, out var upModifiers);
			Assert.Equal(new[] { (Keys)Stroke.WheelUp }, upModifiers);

			ProcessDown(state, Keys.RButton, out var following);
			Assert.Empty(following);
		}

		[Theory]
		[InlineData(Keys.None)]
		[InlineData(Keys.Cancel)]
		[InlineData(Keys.A)]
		public void InvalidButtonsDoNotPublishOrMutateState(Keys keyCode)
		{
			var state = new MouseShortcutState();
			var publications = 0;

			var downHandled = true;
			var upHandled = true;
			Assert.False(state.TryProcessButtonDown(keyCode, Publish, ref downHandled));
			Assert.False(state.TryProcessButtonUp(keyCode, Publish, ref upHandled));
			Assert.True(downHandled);
			Assert.True(upHandled);
			Assert.Equal(0, publications);

			ProcessDown(state, Keys.LButton, out var modifiers);
			Assert.Empty(modifiers);

			bool Publish(Keys _, ICollection<Keys> __)
			{
				publications++;
				return true;
			}
		}

		[Fact]
		public void NonWheelStrokeDoesNotPublishOrMutateState()
		{
			var state = new MouseShortcutState();
			ProcessDown(state, Keys.LButton, out _);
			var publications = 0;

			var handled = true;
			Assert.False(state.TryProcessWheel(
				Stroke.LeftDown,
				Keys.LButton,
				(_, __) => { publications++; return true; },
				ref handled));
			Assert.True(handled);
			Assert.Equal(0, publications);

			ProcessDown(state, Keys.RButton, out var following);
			Assert.Equal(new[] { Keys.LButton }, following);
		}

		[Fact]
		public void ClearRemovesButtonsAndWheelState()
		{
			var state = new MouseShortcutState();
			ProcessDown(state, Keys.LButton, out _);
			ProcessWheel(state, Stroke.WheelDown, out _);

			state.Clear();

			Assert.False(ProcessWheel(state, Stroke.WheelUp, out _));
			ProcessDown(state, Keys.RButton, out var modifiers);
			Assert.Empty(modifiers);
		}

		[Fact]
		public void HandledResultComesFromThePublication()
		{
			var state = new MouseShortcutState();

			var downHandled = false;
			Assert.True(state.TryProcessButtonDown(Keys.LButton, (_, __) => true, ref downHandled));
			Assert.True(downHandled);
			var wheelHandled = false;
			Assert.True(state.TryProcessWheel(Stroke.WheelDown, (Keys)Stroke.WheelDown, (_, __) => true, ref wheelHandled));
			Assert.True(wheelHandled);
			var upHandled = false;
			Assert.True(state.TryProcessButtonUp(Keys.LButton, (_, __) => true, ref upHandled));
			Assert.True(upHandled);
		}

		[Theory]
		[InlineData(MouseTransition.Down)]
		[InlineData(MouseTransition.Up)]
		[InlineData(MouseTransition.Wheel)]
		public void PublicationExceptionPreventsFollowingMutation(MouseTransition transition)
		{
			var state = new MouseShortcutState();
			if (transition != MouseTransition.Down) ProcessDown(state, Keys.LButton, out _);
			if (transition == MouseTransition.Up) ProcessWheel(state, Stroke.WheelDown, out _);

			Assert.Throws<InvalidOperationException>(() => ProcessWithException(state, transition));

			ProcessDown(state, Keys.RButton, out var following);
			var expected = transition == MouseTransition.Wheel
				? new[] { Keys.LButton }
				: transition == MouseTransition.Up
					? new[] { (Keys)Stroke.WheelDown }
					: Array.Empty<Keys>();
			Assert.Equal(expected, following.OrderBy(key => key));
		}

		[Theory]
		[InlineData(MouseTransition.Down, false)]
		[InlineData(MouseTransition.Down, true)]
		[InlineData(MouseTransition.Up, false)]
		[InlineData(MouseTransition.Up, true)]
		[InlineData(MouseTransition.Wheel, false)]
		[InlineData(MouseTransition.Wheel, true)]
		public void PublicationResultIsWrittenThroughCallerHandledStorage(MouseTransition transition, bool publishedHandled)
		{
			var state = new MouseShortcutState();
			if (transition != MouseTransition.Down) ProcessDown(state, Keys.LButton, out _);
			var handled = !publishedHandled;

			var processed = Process(state, transition, (_, __) => publishedHandled, ref handled);

			Assert.True(processed);
			Assert.Equal(publishedHandled, handled);
		}

		[Theory]
		[InlineData(MouseTransition.Down)]
		[InlineData(MouseTransition.Up)]
		[InlineData(MouseTransition.Wheel)]
		public void PublicationExceptionDoesNotChangeCallerHandledStorage(MouseTransition transition)
		{
			var state = new MouseShortcutState();
			if (transition != MouseTransition.Down) ProcessDown(state, Keys.LButton, out _);
			var handled = true;

			Assert.Throws<InvalidOperationException>(() =>
				Process(state, transition, (_, __) => throw new InvalidOperationException("synthetic"), ref handled));

			Assert.True(handled);
		}

		[Fact]
		public void PublicationsShareTheLiveButtonCollectionAndEventArgsObserveLaterMutation()
		{
			var state = new MouseShortcutState();
			ICollection<Keys> firstCollection = null;
			ShortcutKeyPressedEventArgs firstArgs = null;
			var handled = false;

			Assert.True(state.TryProcessButtonDown(
				Keys.LButton,
				(key, keys) =>
				{
					firstCollection = keys;
					firstArgs = new ShortcutKeyPressedEventArgs(key, keys);
					Assert.Empty(keys);
					return false;
				},
				ref handled));

			Assert.Contains(Keys.LButton, firstCollection);
			Assert.Same(firstCollection, firstArgs.ShortcutKey.ModifiersInternal);
			Assert.Contains(Keys.LButton, firstArgs.ShortcutKey.ModifiersInternal);

			Assert.True(state.TryProcessButtonDown(
				Keys.RButton,
				(_, keys) => { Assert.Same(firstCollection, keys); return false; },
				ref handled));
		}

		[Fact]
		public void ButtonUpCleansWheelFromTheSameLiveCollectionOnlyAfterPublicationReturns()
		{
			var state = new MouseShortcutState();
			ProcessDown(state, Keys.LButton, out _);
			ICollection<Keys> liveCollection = null;
			var handled = false;
			state.TryProcessWheel(
				Stroke.WheelUp,
				(Keys)Stroke.WheelUp,
				(_, keys) => { liveCollection = keys; return false; },
				ref handled);
			Assert.Contains((Keys)Stroke.WheelUp, liveCollection);

			state.TryProcessButtonUp(
				Keys.LButton,
				(_, keys) =>
				{
					Assert.Same(liveCollection, keys);
					Assert.Contains((Keys)Stroke.WheelUp, keys);
					return false;
				},
				ref handled);

			Assert.DoesNotContain((Keys)Stroke.WheelUp, liveCollection);
		}

		private static bool ProcessDown(MouseShortcutState state, Keys keyCode, out Keys[] modifiers)
		{
			Keys[] captured = null;
			var handled = false;
			var processed = state.TryProcessButtonDown(
				keyCode,
				(_, keys) => { captured = keys.OrderBy(key => key).ToArray(); return false; },
				ref handled);
			modifiers = captured;
			return processed;
		}

		private static bool ProcessUp(MouseShortcutState state, Keys keyCode, out Keys[] modifiers)
		{
			Keys[] captured = null;
			var handled = false;
			var processed = state.TryProcessButtonUp(
				keyCode,
				(_, keys) => { captured = keys.OrderBy(key => key).ToArray(); return false; },
				ref handled);
			modifiers = captured;
			return processed;
		}

		private static bool ProcessWheel(MouseShortcutState state, Stroke stroke, out Keys[] modifiers)
		{
			Keys[] captured = null;
			var handled = false;
			var processed = state.TryProcessWheel(
				stroke,
				(Keys)stroke,
				(_, keys) => { captured = keys.OrderBy(key => key).ToArray(); return false; },
				ref handled);
			modifiers = captured;
			return processed;
		}

		private static bool Process(
			MouseShortcutState state,
			MouseTransition transition,
			Func<Keys, ICollection<Keys>, bool> publish,
			ref bool handled)
		{
			switch (transition)
			{
				case MouseTransition.Down:
					return state.TryProcessButtonDown(Keys.LButton, publish, ref handled);
				case MouseTransition.Up:
					return state.TryProcessButtonUp(Keys.LButton, publish, ref handled);
				case MouseTransition.Wheel:
					return state.TryProcessWheel(Stroke.WheelDown, (Keys)Stroke.WheelDown, publish, ref handled);
				default:
					throw new ArgumentOutOfRangeException(nameof(transition));
			}
		}

		private static void ProcessWithException(MouseShortcutState state, MouseTransition transition)
		{
			bool Publish(Keys _, ICollection<Keys> modifiers)
			{
				if (transition == MouseTransition.Up)
				{
					Assert.Contains((Keys)Stroke.WheelDown, modifiers);
				}

				throw new InvalidOperationException("synthetic");
			}

			var handled = false;
			Process(state, transition, Publish, ref handled);
		}

		public enum MouseTransition
		{
			Down,
			Up,
			Wheel,
		}
	}
}
