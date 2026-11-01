using System.Runtime.CompilerServices;

// The behaviour scenarios grab and release items through VrInteractable's own grab events (HandDriver), so the game's
// chain behind a grab runs without a VR backend.
[assembly: InternalsVisibleTo("Gothic.Tests.Behaviour")]
