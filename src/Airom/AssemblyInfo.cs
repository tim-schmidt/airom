// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Runtime.CompilerServices;

// ScreenBuffer is an implementation detail of the terminal layer, but its
// clipping and change tracking are worth testing directly rather than only
// through a screen.
[assembly: InternalsVisibleTo("Airom.Tests")]
