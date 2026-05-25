/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025-2026 Linus Björnstam
 *
 */
using System.Runtime.InteropServices;

#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value

namespace Map;

[StructLayout(LayoutKind.Sequential)]
public abstract class NodeBase
{
    public ulong Meta;
    public ulong Map;
    
}